using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.QueryEngine;

public class QueryEngine : IQueryEngine
{
    private readonly ILogger<QueryEngine> _logger;

    private static readonly HashSet<string> _sqlKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "WHERE", "GROUP", "ORDER", "HAVING", "ON", "AND", "OR", "SET",
        "LEFT", "RIGHT", "INNER", "OUTER", "FULL", "CROSS", "JOIN",
        "SELECT", "FROM", "UNION", "INSERT", "UPDATE", "DELETE",
        "BY", "AS", "IN", "NOT", "NULL", "IS", "LIKE", "BETWEEN",
        "DISTINCT", "ALL", "ANY", "SOME", "EXISTS", "CASE", "WHEN",
        "THEN", "ELSE", "END", "WITH", "START", "CONNECT",
        "COUNT", "SUM", "AVG", "MAX", "MIN", "NVL", "DECODE",
        "TO_DATE", "TO_CHAR", "TRUNC", "ROWNUM", "RAWTOHEX",
        "UTL_RAW", "CAST", "HEXTORAW", "VALUE", "VALUES",
        "DESC", "ASC", "NULLS", "PRIMARY", "KEY", "INDEX",
        "CREATE", "ALTER", "DROP", "TABLE", "VIEW", "INTO"
    };

    public QueryEngine(ILogger<QueryEngine> logger)
    {
        _logger = logger;
    }

    // ===== Public API =====

    public async Task<QueryEngineResult> ExecuteAsync(QueryEngineRequest request, CancellationToken ct = default)
    {
        await EngineLicense.ValidateAsync();
        EngineLicense.ValidateInstance(request.ConnectionString);
        var useHexEncoding = !string.IsNullOrEmpty(request.CharSetOverride);

        using var conn = new OracleConnection(request.ConnectionString);
        await conn.OpenAsync(ct);

        var hasRawSql = !string.IsNullOrEmpty(SanitizeRawSql(request.RawSql));
        if (hasRawSql)
            ValidateRawSqlReadOnly(SanitizeRawSql(request.RawSql)!);

        var paramValues = new Dictionary<string, string>();
        var (countSql, countParams) = BuildCountSql(request, paramValues, hasRawSql);
        var (dataSql, dataParams, rawColAliasMap, rawHexSafeColumns) = BuildDataSql(
            request, paramValues, useHexEncoding, hasRawSql);
        var allParams = MergeParams(countParams, dataParams, paramValues);

        _logger.LogInformation("Count SQL: {Sql}", countSql);
        _logger.LogInformation("Data SQL: {Sql}", dataSql);

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // Execute count
        var countDp = new DynamicParameters();
        foreach (var (k, v) in countParams) countDp.Add(k, v);
        foreach (var (k, v) in paramValues)
        {
            var paramName = $"p_f_{k}";
            if (countSql.Contains($":{paramName}"))
                countDp.Add(paramName, v);
        }

        int total;
        try
        {
            total = await conn.ExecuteScalarAsync<int>(
                new CommandDefinition(countSql, countDp,
                    commandTimeout: request.Options.QueryTimeoutSeconds,
                    cancellationToken: ct));
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException(
                $"Count query failed: {ex.Message}. SQL: {countSql}", ex);
        }

        if (total > request.Options.MaxRowCount)
            throw new InvalidOperationException(
                $"结果超过{request.Options.MaxRowCount}行，请调整筛选条件或限制");

        // Execute data
        IEnumerable<dynamic> rows;
        try
        {
            rows = await conn.QueryAsync(
                new CommandDefinition(dataSql, allParams,
                    commandTimeout: request.Options.QueryTimeoutSeconds,
                    cancellationToken: ct));
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException(
                $"Data query failed: {ex.Message}. SQL: {dataSql}", ex);
        }

        sw.Stop();

        // Build column display map and hex-column set
        var colDisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hexColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in request.Fields.OrderBy(f => f.SortOrder))
        {
            var sqlAlias = field.ColumnName;
            var displayName = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.ColumnName;
            colDisplayMap[sqlAlias] = displayName;
            if (useHexEncoding && EncodingHelper.IsStringType(field.DataType))
                hexColumns.Add(sqlAlias);
        }
        foreach (var safe in rawHexSafeColumns)
            hexColumns.Add(safe);
        foreach (var (orig, safe) in rawColAliasMap)
            if (orig != safe)
                colDisplayMap[safe] = orig;

        // Convert to list of dictionaries with hex decoding
        var resultRows = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in (IDictionary<string, object?>)row)
            {
                var val = prop.Value;
                if (useHexEncoding && hexColumns.Contains(prop.Key) && val is string hexStr1 && !string.IsNullOrEmpty(hexStr1))
                {
                    val = EncodingHelper.DecodeHexString(hexStr1, request.CharSetOverride);
                }
                else if (useHexEncoding && val is string hexStr2 && !string.IsNullOrEmpty(hexStr2)
                    && hexStr2.Length % 2 == 0 && hexStr2.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                {
                    var decoded = EncodingHelper.DecodeHexString(hexStr2, request.CharSetOverride);
                    if (decoded != hexStr2 && !string.IsNullOrEmpty(decoded) && decoded.Any(c => c > 127))
                        val = decoded;
                }
                else if (useHexEncoding && val is string strVal && strVal.Any(c => c > 127 || c == '?'))
                {
                    var fixedVal = EncodingHelper.ConvertEncoding(strVal, request.CharSetOverride);
                    if (fixedVal != strVal)
                        val = fixedVal;
                }
                var key = colDisplayMap.TryGetValue(prop.Key, out var display) ? display : prop.Key;
                dict[key] = val;
            }
            resultRows.Add(dict);
        }

        // Build columns list
        var columns = new List<string>();
        if (hasRawSql)
        {
            var rawKeys = resultRows.Count > 0
                ? resultRows[0].Keys
                    .Where(k => !k.Equals("RN", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : ParseSelectAliases(SanitizeRawSql(request.RawSql)!);
            foreach (var key in rawKeys)
                columns.Add(colDisplayMap.TryGetValue(key, out var display) ? display : key);
        }
        else
        {
            foreach (var field in request.Fields.OrderBy(f => f.SortOrder))
            {
                var displayName = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.ColumnName;
                columns.Add(displayName);
            }
        }

        return new QueryEngineResult
        {
            Rows = resultRows,
            Columns = columns,
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
            ElapsedMs = sw.ElapsedMilliseconds
        };
    }

    public async Task<byte[]> ExportExcelAsync(QueryEngineRequest request, CancellationToken ct = default)
    {
        await EngineLicense.ValidateAsync();
        if (!EngineLicense.HasModule("export"))
            throw new InvalidOperationException("当前 License 不包含 Excel 导出功能。请升级至高级版。");
        request.Page = 1;
        request.PageSize = request.Options.MaxRowCount;
        var result = await ExecuteAsync(request, ct);
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("查询结果");

        for (int i = 0; i < result.Columns.Count; i++)
        {
            ws.Cell(1, i + 1).Value = result.Columns[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        for (int r = 0; r < result.Rows.Count; r++)
        {
            var row = result.Rows[r];
            for (int c = 0; c < result.Columns.Count; c++)
            {
                var colName = result.Columns[c];
                var val = row.GetValueOrDefault(colName);
                ws.Cell(r + 2, c + 1).Value = val?.ToString() ?? "";
            }
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        return ms.ToArray();
    }

    public async Task<List<string>> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken ct = default)
    {
        var useHexEncoding = !string.IsNullOrEmpty(request.CharSetOverride)
            && EncodingHelper.IsStringType(request.DataType);
        var tableAlias = request.TableAlias ?? request.TableName;
        var schema = request.SchemaName ?? "HOSPITAL";
        var colRef = $"\"{tableAlias}\".\"{request.ColumnName}\"";
        var colExpr = useHexEncoding
            ? $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({colRef})) as \"_v\""
            : colRef;
        var orderExpr = useHexEncoding ? "\"_v\"" : colRef;

        var sql = $"SELECT DISTINCT {colExpr} " +
                  $"FROM \"{schema}\".\"{request.TableName}\" \"{tableAlias}\" " +
                  $"ORDER BY {orderExpr}";

        using var conn = new OracleConnection(request.ConnectionString);
        await conn.OpenAsync(ct);
        var values = await conn.QueryAsync<string>(sql);
        return values
            .Where(v => v != null)
            .Select(v => useHexEncoding
                ? EncodingHelper.DecodeHexString(v!, request.CharSetOverride)
                : EncodingHelper.ConvertEncoding(v!, request.CharSetOverride))
            .ToList();
    }

    // ===== SQL builders =====

    internal static string SanitizeRawSql(string? rawSql)
    {
        if (string.IsNullOrEmpty(rawSql)) return "";
        return rawSql.TrimEnd(';').TrimEnd();
    }

    internal static void ValidateRawSqlReadOnly(string sql)
    {
        var t = Regex.Replace(sql.TrimStart(), @"^\s*--[^\n]*\n?", "", RegexOptions.Multiline).TrimStart();
        if (t.Length == 0) return;
        var firstWord = t.Length >= 6 ? t[..6] : t;
        if (firstWord.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            firstWord.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return;
        throw new InvalidOperationException("仅允许 SELECT 和 WITH 查询，不支持数据修改操作。");
    }

    internal static (string Operator, string Value) ParseFilterValue(
        string? rawFilterValue, string configOperator)
    {
        if (string.IsNullOrEmpty(rawFilterValue))
            return (configOperator, "");
        const string sep = "::";
        var idx = rawFilterValue.IndexOf(sep, StringComparison.Ordinal);
        if (idx >= 0)
            return (rawFilterValue[..idx], rawFilterValue[(idx + sep.Length)..]);
        return (configOperator, rawFilterValue);
    }

    internal static void RegisterParamValues(Dictionary<string, string> paramValues, string key,
        string op, string value, Func<string, string>? encode = null)
    {
        if (op is "IN" or "NOT IN")
        {
            var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                var p = parts[i].Trim().Trim('\'', '"', '(', ')');
                if (string.IsNullOrEmpty(p)) continue;
                paramValues[key + "_" + i] = encode != null ? encode(p) : p;
            }
        }
        else if (op is "BETWEEN" or "NOT BETWEEN")
        {
            var parts = value.Split(',', 2);
            var from = parts[0].Trim();
            var to = parts.Length > 1 ? parts[1].Trim() : "";
            paramValues[key + "_from"] = encode != null ? encode(from) : from;
            paramValues[key + "_to"] = encode != null ? encode(to) : to;
        }
        else
        {
            paramValues[key] = encode != null ? encode(value) : value;
        }
    }

    internal (string Sql, Dictionary<string, object?> Params) BuildCountSql(
        QueryEngineRequest request, Dictionary<string, string> paramValues, bool useRawSqlPath)
    {
        var rawSql = SanitizeRawSql(request.RawSql);
        if (useRawSqlPath)
        {
            var rawAliases = ExtractAliasesFromRawSql(rawSql);
            var isUnionCount = Regex.IsMatch(rawSql, @"\bUNION\s+(ALL\s+)?SELECT\b", RegexOptions.IgnoreCase);
            var innerSql = isUnionCount
                ? InjectWhereIntoUnionSql(rawSql, request, paramValues)
                : InjectWhereIntoRawSql(rawSql,
                    BuildOuterWhereForRawSql(request, paramValues, rawAliases));
            if (!string.IsNullOrEmpty(request.CharSetOverride))
                innerSql = EncodingHelper.HexEncodeInlineLiterals(innerSql, request.CharSetOverride);
            return ($"SELECT COUNT(*) FROM ({innerSql}) \"_cnt\"",
                new Dictionary<string, object?>());
        }

        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ");
        AppendFromClause(sb, request);
        AppendWhereClause(sb, request, paramValues);
        return (sb.ToString(), new Dictionary<string, object?>());
    }

    internal (string Sql, Dictionary<string, object?> Params,
        Dictionary<string, string> ColAliasMap, HashSet<string> HexSafeColumns) BuildDataSql(
        QueryEngineRequest request, Dictionary<string, string> paramValues,
        bool useHexEncoding, bool useRawSqlPath)
    {
        string innerSql;
        var rawSqlColAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var rawHexSafeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rawSql = SanitizeRawSql(request.RawSql);

        if (useRawSqlPath)
        {
            var rawAliases = ExtractAliasesFromRawSql(rawSql);
            var isUnion = Regex.IsMatch(rawSql, @"\bUNION\s+(ALL\s+)?SELECT\b", RegexOptions.IgnoreCase);
            innerSql = isUnion
                ? InjectWhereIntoUnionSql(rawSql, request, paramValues)
                : InjectWhereIntoRawSql(rawSql,
                    BuildOuterWhereForRawSql(request, paramValues, rawAliases));

            if (useHexEncoding)
            {
                innerSql = EncodingHelper.HexEncodeInlineLiterals(innerSql, request.CharSetOverride!);
                var (hexSql, hexAliasMap, hexSafeCols) = HexEncodeRawSqlColumns(
                    innerSql, request, rawAliases);
                innerSql = hexSql;
                rawSqlColAliasMap = hexAliasMap;
                rawHexSafeColumns = hexSafeCols;
            }
        }
        else
        {
            var selectClause = BuildSelectClause(request, useHexEncoding);
            var fromClause = BuildFromClause(request);
            var whereClause = BuildWhereClause(request, paramValues);
            var groupBy = BuildGroupBy(request);
            var orderBy = BuildOrderBy(request);

            innerSql = $"SELECT {selectClause} FROM {fromClause}";
            if (!string.IsNullOrEmpty(whereClause))
                innerSql += $" WHERE {whereClause}";
            if (!string.IsNullOrEmpty(groupBy))
                innerSql += $" GROUP BY {groupBy}";
            if (!string.IsNullOrEmpty(orderBy))
                innerSql += $" ORDER BY {orderBy}";
        }

        var startRow = (request.Page - 1) * request.PageSize + 1;
        var endRow = request.Page * request.PageSize;

        string outerCols;
        if (useRawSqlPath)
            outerCols = "*";
        else
        {
            var cols = request.Fields.OrderBy(f => f.SortOrder)
                .Select(f => $"\"{f.ColumnName}\"")
                .ToList();
            outerCols = string.Join(", ", cols);
        }

        var paginatedSql = $"SELECT {outerCols} FROM (SELECT t.*, ROWNUM rn FROM ({innerSql}) t WHERE ROWNUM <= :p_endRow) WHERE rn >= :p_startRow";

        var extraParams = new Dictionary<string, object?>
        {
            ["p_endRow"] = endRow,
            ["p_startRow"] = startRow
        };
        return (paginatedSql, extraParams, rawSqlColAliasMap, rawHexSafeColumns);
    }

    // ===== Select / From / Where / Group / Order builders =====

    internal static string BuildSelectClause(QueryEngineRequest request, bool useHexEncoding = false)
    {
        var parts = new List<string>();
        foreach (var field in request.Fields.OrderBy(f => f.SortOrder))
        {
            var colExpr = $"\"{field.TableAlias}\".\"{field.ColumnName}\"";
            if (!string.IsNullOrEmpty(field.AggregateFunc))
                colExpr = $"{field.AggregateFunc}({colExpr})";
            if (useHexEncoding && EncodingHelper.IsStringType(field.DataType))
                colExpr = $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({colExpr}))";
            var label = field.ColumnName;
            parts.Add($"{colExpr} AS \"{label}\"");
        }
        if (parts.Count == 0)
            throw new InvalidOperationException("没有配置查询字段");
        return string.Join(", ", parts);
    }

    internal static string BuildFromClause(QueryEngineRequest request)
    {
        var mainAlias = GetTableAlias(request.MainTable);
        var from = $"\"{request.MainTable.SchemaName}\".\"{request.MainTable.TableName}\" \"{mainAlias}\"";

        var grouped = request.Joins
            .OrderBy(j => j.SortOrder)
            .GroupBy(j => j.JoinTable.TableName + j.JoinTable.SchemaName);

        var joinAliasMap = new Dictionary<string, string>();
        foreach (var group in grouped)
        {
            var first = group.First();
            var baseAlias = GetTableAlias(first.JoinTable);
            var alias = baseAlias;
            var suffix = 2;
            while (joinAliasMap.Values.Any(v => v == alias))
            {
                alias = $"{baseAlias}_{suffix}";
                suffix++;
            }
            var groupKey = first.JoinTable.TableName + first.JoinTable.SchemaName;
            joinAliasMap[groupKey] = alias;

            var onParts = new List<string>();
            foreach (var join in group)
            {
                var leftFull = $"\"{join.LeftTableAlias}\".\"{join.LeftColumnName}\"";
                var rightFull = $"\"{alias}\".\"{join.RightColumnName}\"";
                if (join.LeftDateTrunc)
                {
                    leftFull = $"TRUNC({leftFull})";
                    rightFull = $"TRUNC({rightFull})";
                }
                onParts.Add($"{leftFull} = {rightFull}");
            }

            from += $"\n  {first.JoinType} JOIN \"{first.JoinTable.SchemaName}\".\"{first.JoinTable.TableName}\" \"{alias}\"";
            from += $" ON {string.Join(" AND ", onParts)}";
        }

        return from;
    }

    internal string BuildWhereClause(QueryEngineRequest request,
        Dictionary<string, string> paramValues)
    {
        return BuildFilterParts(request, paramValues, filter =>
        {
            var tableAlias = !string.IsNullOrEmpty(filter.TableAlias)
                ? filter.TableAlias
                : GetTableAlias(request.MainTable);
            return $"\"{tableAlias}\".\"{filter.ColumnName}\"";
        });
    }

    internal static string BuildGroupBy(QueryEngineRequest request)
    {
        if (string.IsNullOrEmpty(request.GroupByColumn)) return "";
        return QuoteQualifiedName(request.GroupByColumn);
    }

    internal static string BuildOrderBy(QueryEngineRequest request)
    {
        if (string.IsNullOrEmpty(request.SortColumn)) return "";

        var dir = request.SortDirection ?? "ASC";
        var label = ResolveSortLabel(request);
        if (!string.IsNullOrEmpty(label))
            return $"\"{label}\" {dir}";

        if (request.SortColumn.Any(c => c > 127))
            return "";
        return $"{QuoteQualifiedName(request.SortColumn)} {dir}";
    }

    internal static string? ResolveSortLabel(QueryEngineRequest request)
    {
        var sortCol = request.SortColumn;
        if (string.IsNullOrEmpty(sortCol)) return null;
        foreach (var field in request.Fields)
        {
            var displayAlias = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.ColumnName;
            if (sortCol.Equals($"{field.TableAlias}.{field.ColumnName}", StringComparison.OrdinalIgnoreCase) ||
                sortCol.Equals($"{field.TableAlias}.{displayAlias}", StringComparison.OrdinalIgnoreCase))
                return field.ColumnName;
        }

        var lastDot = sortCol.LastIndexOf('.');
        var colPart = lastDot >= 0 ? sortCol[(lastDot + 1)..] : sortCol;
        foreach (var field in request.Fields)
        {
            var displayAlias = !string.IsNullOrEmpty(field.Alias) ? field.Alias : field.ColumnName;
            if (colPart.Equals(field.ColumnName, StringComparison.OrdinalIgnoreCase) ||
                colPart.Equals(displayAlias, StringComparison.OrdinalIgnoreCase))
                return field.ColumnName;
        }
        return null;
    }

    internal static string QuoteQualifiedName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0) return $"\"{name}\"";
        return $"\"{name[..lastDot]}\".\"{name[(lastDot + 1)..]}\"";
    }

    internal static string GetTableAlias(EngineTableDef? table)
    {
        return !string.IsNullOrEmpty(table?.Alias) ? table!.Alias : (table?.TableName ?? "T");
    }

    // ===== Filter / WHERE builders =====

    internal string BuildFilterParts(QueryEngineRequest request,
        Dictionary<string, string> paramValues,
        Func<EngineFilterDef, string?> qualifyCol)
    {
        var parts = new List<string>();
        foreach (var filter in request.Filters.OrderBy(f => f.SortOrder))
        {
            string? effectiveValue;
            if (filter.IsContextFilter && !string.IsNullOrEmpty(filter.ContextKey))
            {
                if (!request.ContextValues.TryGetValue(filter.ContextKey, out effectiveValue) ||
                    string.IsNullOrEmpty(effectiveValue))
                {
                    _logger.LogWarning(
                        "Context filter {FilterId} skipped: ContextKey '{ContextKey}' has no value",
                        filter.Id, filter.ContextKey);
                    continue;
                }
            }
            else
            {
                _ = request.FilterValues.TryGetValue(filter.Id.ToString(), out var userVal);
                effectiveValue = userVal ?? filter.DefaultValue;
                if (string.IsNullOrEmpty(effectiveValue)) continue;
            }

            var (effectiveOp, effectiveVal) = ParseFilterValue(effectiveValue, filter.Operator);
            if (string.IsNullOrEmpty(effectiveVal)) continue;

            var qualified = qualifyCol(filter);
            var isDate = "DATE".Equals(filter.DataType, StringComparison.OrdinalIgnoreCase);
            var paramName = $"p_f_{filter.Id}";

            if (!string.IsNullOrEmpty(request.CharSetOverride)
                && EncodingHelper.IsStringType(filter.DataType)
                && EncodingHelper.ContainsNonAscii(effectiveVal))
            {
                var colExpr = $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({qualified}))";
                var hexValue = EncodingHelper.EncodeNonAsciiValue(effectiveVal, effectiveOp, request.CharSetOverride);
                parts.Add(effectiveOp is "IN" or "NOT IN"
                    ? BuildInClause(colExpr, effectiveOp, paramName, effectiveVal, isDate)
                    : OperatorToSql(colExpr, effectiveOp, paramName, isDate));
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal,
                    (v) => EncodingHelper.EncodeNonAsciiValue(v, effectiveOp, request.CharSetOverride));
            }
            else
            {
                parts.Add(effectiveOp is "IN" or "NOT IN"
                    ? BuildInClause(qualified, effectiveOp, paramName, effectiveVal, isDate)
                    : OperatorToSql(qualified, effectiveOp, paramName, isDate));
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal, null);
            }
        }
        return string.Join(" AND ", parts);
    }

    internal string BuildOuterWhereForRawSql(QueryEngineRequest request,
        Dictionary<string, string> paramValues,
        Dictionary<string, string>? rawAliases = null,
        bool isUnionContext = false)
    {
        return BuildFilterParts(request, paramValues, filter =>
        {
            var colName = filter.ColumnName;
            var tableName = filter.TableAlias;
            if (!string.IsNullOrEmpty(tableName) && rawAliases != null)
            {
                if (rawAliases.TryGetValue(tableName, out var alias))
                    return $"{alias}.\"{colName}\"";
                if (isUnionContext)
                    return null;
            }
            return $"\"{colName}\"";
        });
    }

    internal static string OperatorToSql(string col, string op, string param, bool isDate = false)
    {
        var val = isDate ? $"TO_DATE(:{param}, 'YYYY-MM-DD')" : $":{param}";
        return op.ToUpperInvariant() switch
        {
            "EQ" => $"{col} = {val}",
            "NE" => $"{col} != {val}",
            "GT" => $"{col} > {val}",
            "GTE" => $"{col} >= {val}",
            "LT" => $"{col} < {val}",
            "LTE" => $"{col} <= {val}",
            "LIKE" => $"{col} LIKE :{param}",
            "NOT LIKE" => $"{col} NOT LIKE :{param}",
            "IN" => $"{col} IN (:{param})",
            "NOT IN" => $"{col} NOT IN (:{param})",
            "BETWEEN" => isDate
                ? $"{col} BETWEEN TO_DATE(:{param}_from, 'YYYY-MM-DD') AND TO_DATE(:{param}_to, 'YYYY-MM-DD')"
                : $"{col} BETWEEN :{param}_from AND :{param}_to",
            "NOT BETWEEN" => isDate
                ? $"{col} NOT BETWEEN TO_DATE(:{param}_from, 'YYYY-MM-DD') AND TO_DATE(:{param}_to, 'YYYY-MM-DD')"
                : $"{col} NOT BETWEEN :{param}_from AND :{param}_to",
            _ => $"{col} = {val}"
        };
    }

    internal static string BuildInClause(string col, string op, string paramPrefix, string value, bool isDate)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var cleanParts = new List<string>(parts.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            var p = parts[i].Trim().Trim('\'', '"', '(', ')');
            if (string.IsNullOrEmpty(p)) continue;
            cleanParts.Add(p);
        }
        var args = new List<string>(cleanParts.Count);
        for (int i = 0; i < cleanParts.Count; i++)
        {
            var p = paramPrefix + "_" + i;
            args.Add(isDate ? $"TO_DATE(:{p}, 'YYYY-MM-DD')" : $":{p}");
        }
        var list = string.Join(", ", args);
        return op == "NOT IN" ? $"{col} NOT IN ({list})" : $"{col} IN ({list})";
    }

    internal static DynamicParameters MergeParams(
        Dictionary<string, object?> countParams,
        Dictionary<string, object?> dataParams,
        Dictionary<string, string> paramValues)
    {
        var dp = new DynamicParameters();
        foreach (var (k, v) in countParams) dp.Add(k, v);
        foreach (var (k, v) in dataParams) dp.Add(k, v);
        foreach (var (k, v) in paramValues)
            dp.Add($"p_f_{k}", v);
        return dp;
    }

    // ===== Raw SQL manipulation =====

    internal static Dictionary<string, string> ExtractAliasesFromRawSql(string rawSql)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fromMatch = Regex.Match(rawSql, @"\bFROM\b", RegexOptions.IgnoreCase);
        var scanPart = fromMatch.Success ? rawSql[fromMatch.Index..] : rawSql;
        foreach (Match m in Regex.Matches(scanPart,
            @"(?:\bFROM\b|\bJOIN\b|,)\s*(?:(\w+)\.)?(\w+)(?:\s+(""?\w+""?))?\b",
            RegexOptions.IgnoreCase))
        {
            var tableName = m.Groups[2].Value;
            if (_sqlKeywords.Contains(tableName)) continue;
            string alias;
            if (m.Groups[3].Success)
                alias = m.Groups[3].Value.Trim('"');
            else
                alias = tableName;
            if (_sqlKeywords.Contains(alias)) continue;
            if (!map.ContainsKey(tableName))
                map[tableName] = alias;
        }
        return map;
    }

    internal static string InjectWhereIntoRawSql(string rawSql, string whereClause)
    {
        if (string.IsNullOrEmpty(whereClause)) return rawSql;
        var groupByMatch = Regex.Match(rawSql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase);
        var orderByMatch = Regex.Match(rawSql, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase);
        int insertPos = rawSql.Length;
        if (groupByMatch.Success) insertPos = groupByMatch.Index;
        else if (orderByMatch.Success) insertPos = orderByMatch.Index;
        var hasWhere = Regex.IsMatch(rawSql[..insertPos], @"\bWHERE\b", RegexOptions.IgnoreCase);
        var keyword = hasWhere ? " AND " : " WHERE ";
        return rawSql[..insertPos].TrimEnd() + keyword + whereClause + " " + rawSql[insertPos..].TrimStart();
    }

    internal static List<string> SplitUnionBranches(string sql)
    {
        var branches = new List<string>();
        var unionPattern = new Regex(@"\bUNION\s+(ALL\s+)?(?=\s*SELECT\b)", RegexOptions.IgnoreCase);
        var matches = unionPattern.Matches(sql);
        if (matches.Count == 0) { branches.Add(sql); return branches; }
        int lastEnd = 0;
        foreach (Match m in matches)
        {
            branches.Add(sql[lastEnd..m.Index].Trim());
            lastEnd = m.Index + m.Length;
        }
        branches.Add(sql[lastEnd..].Trim());
        return branches;
    }

    internal string InjectWhereIntoUnionSql(string sql, QueryEngineRequest request,
        Dictionary<string, string> paramValues)
    {
        var branches = SplitUnionBranches(sql);
        _logger.LogInformation("UNION split: {Count} branches", branches.Count);
        if (request.Filters.Count == 0) return sql;
        var injected = new List<string>(branches.Count);
        for (int b = 0; b < branches.Count; b++)
        {
            var branchAliases = ExtractAliasesFromRawSql(branches[b]);
            var branchWhere = BuildOuterWhereForRawSql(request, paramValues,
                branchAliases, isUnionContext: true);
            injected.Add(InjectWhereIntoRawSql(branches[b], branchWhere));
        }
        return string.Join("\nUNION ALL\n", injected);
    }

    internal static (string Sql, Dictionary<string, string> AliasMap, HashSet<string> HexSafeColumns)
        HexEncodeRawSqlColumns(string rawSql, QueryEngineRequest request,
        Dictionary<string, string> rawAliases)
    {
        var aliases = ParseSelectAliases(rawSql);
        if (aliases.Count == 0) return (rawSql, new Dictionary<string, string>(), new HashSet<string>());

        if (Regex.IsMatch(rawSql, @"\bUNION\s+(ALL\s+)?SELECT\b", RegexOptions.IgnoreCase))
        {
            var branches = SplitUnionBranches(rawSql);
            for (int i = 1; i < branches.Count; i++)
            {
                var branchAliases = ParseSelectAliases(branches[i]);
                if (branchAliases.Count != aliases.Count)
                    System.Diagnostics.Debug.WriteLine(
                        $"UNION column count mismatch: branch 0 has {aliases.Count}, branch {i} has {branchAliases.Count}");
            }
        }

        var stringCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in request.Fields)
        {
            if (EncodingHelper.IsStringType(field.DataType))
            {
                stringCols.Add(field.ColumnName);
                if (!string.IsNullOrEmpty(field.Alias))
                    stringCols.Add(field.Alias);
            }
        }

        var innerAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var outerAliasMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool hasNonAsciiAlias = false;
        int nonAsciiIdx = 0;
        for (int i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            if (EncodingHelper.ContainsNonAscii(alias))
            {
                innerAliasMap[alias] = "_c" + nonAsciiIdx;
                outerAliasMap[alias] = "_cx" + nonAsciiIdx;
                nonAsciiIdx++;
                hasNonAsciiAlias = true;
            }
            else
            {
                innerAliasMap[alias] = alias;
                outerAliasMap[alias] = alias;
            }
        }

        var innerSql = hasNonAsciiAlias
            ? RewriteRawSqlSelectAliases(rawSql, innerAliasMap)
            : rawSql;

        if (hasNonAsciiAlias)
            innerSql = RewriteClauseAliases(innerSql, innerAliasMap);

        var rawSelectExprs = SplitRawSelectColumns(rawSql);
        var wrapped = new List<string>();
        var hasHex = false;
        var hexSafeColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < aliases.Count; i++)
        {
            var alias = aliases[i];
            var innerAlias = innerAliasMap[alias];
            var outerAlias = outerAliasMap[alias];
            bool isString = stringCols.Contains(alias);

            if (!isString && request.MainTable.Columns != null && i < rawSelectExprs.Count)
            {
                var colName = ExtractColumnName(rawSelectExprs[i]);
                if (colName != null)
                {
                    isString = request.MainTable.Columns.Any(c =>
                        c.ColumnName.Equals(colName, StringComparison.OrdinalIgnoreCase)
                        && EncodingHelper.IsStringType(c.DataType));
                    if (!isString)
                    {
                        foreach (var join in request.Joins)
                        {
                            if (join.JoinTable.Columns != null &&
                                join.JoinTable.Columns.Any(c =>
                                    c.ColumnName.Equals(colName, StringComparison.OrdinalIgnoreCase)
                                    && EncodingHelper.IsStringType(c.DataType)))
                            {
                                isString = true;
                                break;
                            }
                        }
                    }
                }
            }

            if (isString)
            {
                wrapped.Add($"RAWTOHEX(UTL_RAW.CAST_TO_RAW(\"{innerAlias}\")) AS \"{outerAlias}\"");
                hasHex = true;
                hexSafeColumns.Add(outerAlias);
            }
            else
            {
                wrapped.Add($"\"{innerAlias}\" AS \"{outerAlias}\"");
            }
        }

        if (!hasHex && !hasNonAsciiAlias)
            return (rawSql, outerAliasMap, hexSafeColumns);
        return ($"SELECT {string.Join(", ", wrapped)} FROM ({innerSql}) \"_raw\"", outerAliasMap, hexSafeColumns);
    }

    internal static string RewriteRawSqlSelectAliases(string rawSql,
        Dictionary<string, string> aliasToSafe)
    {
        var isUnion = Regex.IsMatch(rawSql, @"\bUNION\s+(ALL\s+)?SELECT\b", RegexOptions.IgnoreCase);
        if (!isUnion)
            return RewriteOneSelectAliases(rawSql, aliasToSafe);
        var branches = SplitUnionBranches(rawSql);
        var rewritten = branches.Select(b => RewriteOneSelectAliases(b, aliasToSafe)).ToList();
        return string.Join("\nUNION ALL\n", rewritten);
    }

    internal static string RewriteOneSelectAliases(string sql,
        Dictionary<string, string> aliasToSafe)
    {
        var selectMatch = Regex.Match(sql,
            @"\bSELECT\s+(.+?)\s+\bFROM\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!selectMatch.Success) return sql;
        var selectPart = selectMatch.Groups[1].Value;
        var selectStart = selectMatch.Groups[1].Index;
        var selectEnd = selectStart + selectPart.Length;
        var columns = SplitSelectColumns(selectPart);
        var rewritten = new List<string>();
        foreach (var colExpr in columns)
            rewritten.Add(ReplaceNonAsciiAlias(colExpr, aliasToSafe));
        var newSelect = string.Join(", ", rewritten);
        return sql[..selectStart] + newSelect + sql[selectEnd..];
    }

    internal static List<string> SplitSelectColumns(string selectPart)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        for (int i = 0; i < selectPart.Length; i++)
        {
            if (selectPart[i] == '(') depth++;
            else if (selectPart[i] == ')') depth--;
            else if (selectPart[i] == ',' && depth == 0)
            {
                parts.Add(selectPart[start..i].Trim());
                start = i + 1;
            }
        }
        var last = selectPart[start..].Trim();
        if (!string.IsNullOrEmpty(last)) parts.Add(last);
        return parts;
    }

    internal static string ReplaceNonAsciiAlias(string colExpr,
        Dictionary<string, string> aliasToSafe)
    {
        var trimmed = colExpr.TrimEnd();
        var asQuotedMatch = Regex.Match(trimmed,
            @"\b(AS)\s+""([^""]+)""\s*$", RegexOptions.IgnoreCase);
        if (asQuotedMatch.Success)
        {
            var alias = asQuotedMatch.Groups[2].Value;
            if (EncodingHelper.ContainsNonAscii(alias) && aliasToSafe.TryGetValue(alias, out var safe))
                return Regex.Replace(trimmed, @"\b(AS)\s+""[^""]+""\s*$",
                    $"$1 \"{safe}\"", RegexOptions.IgnoreCase);
            return colExpr;
        }
        var quotedMatch = Regex.Match(trimmed, @"""([^""]+)""\s*$");
        if (quotedMatch.Success)
        {
            var alias = quotedMatch.Groups[1].Value;
            if (EncodingHelper.ContainsNonAscii(alias) && aliasToSafe.TryGetValue(alias, out var safe))
                return Regex.Replace(trimmed, @"""[^""]+""\s*$", $"\"{safe}\"");
            return colExpr;
        }
        var bareMatch = Regex.Match(trimmed, @"\s+(\w[\w-]*)\s*$");
        if (bareMatch.Success)
        {
            var alias = bareMatch.Groups[1].Value;
            if (EncodingHelper.ContainsNonAscii(alias) && aliasToSafe.TryGetValue(alias, out var safe))
                return Regex.Replace(trimmed,
                    @"\s+" + Regex.Escape(alias) + @"\s*$", $" \"{safe}\"");
        }
        return colExpr;
    }

    internal static string RewriteClauseAliases(string sql,
        Dictionary<string, string> aliasToSafe)
    {
        var replacements = aliasToSafe
            .Where(kv => !string.Equals(kv.Key, kv.Value, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (replacements.Count == 0) return sql;

        var orderByMatch = Regex.Match(sql, @"\bORDER\s+BY\b", RegexOptions.IgnoreCase);
        var groupByMatch = Regex.Match(sql, @"\bGROUP\s+BY\b", RegexOptions.IgnoreCase);

        var suffixStart = sql.Length;
        if (groupByMatch.Success) suffixStart = Math.Min(suffixStart, groupByMatch.Index);
        if (orderByMatch.Success) suffixStart = Math.Min(suffixStart, orderByMatch.Index);
        if (suffixStart == sql.Length) return sql;

        var prefix = sql[..suffixStart];
        var suffix = sql[suffixStart..];

        foreach (var (oldAlias, newAlias) in replacements)
        {
            suffix = Regex.Replace(suffix,
                $@"""{Regex.Escape(oldAlias)}""",
                $"\"{newAlias}\"",
                RegexOptions.IgnoreCase);
            suffix = Regex.Replace(suffix,
                $@"\b{Regex.Escape(oldAlias)}\b",
                $"\"{newAlias}\"",
                RegexOptions.IgnoreCase);
        }

        return prefix + suffix;
    }

    internal static List<string> SplitRawSelectColumns(string rawSql)
    {
        var result = new List<string>();
        var selectMatch = Regex.Match(rawSql,
            @"\bSELECT\s+(.+?)\s+\bFROM\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!selectMatch.Success) return result;
        return SplitSelectColumns(selectMatch.Groups[1].Value);
    }

    internal static string? ExtractColumnName(string colExpr)
    {
        var trimmed = colExpr.Trim();
        trimmed = Regex.Replace(trimmed, @"\s+""[^""]*""\s*$", "");
        trimmed = Regex.Replace(trimmed, @"\s+AS\s+""[^""]*""\s*$", "", RegexOptions.IgnoreCase);
        var bareAliasMatch = Regex.Match(trimmed, @"\s+(\w+)\s*$");
        if (bareAliasMatch.Success)
        {
            var potentialAlias = bareAliasMatch.Groups[1].Value;
            if (!_sqlKeywords.Contains(potentialAlias) && !trimmed.Contains('('))
                trimmed = trimmed[..bareAliasMatch.Index].Trim();
        }
        var lastDot = trimmed.LastIndexOf('.');
        var colName = lastDot >= 0 ? trimmed[(lastDot + 1)..].Trim() : trimmed;
        if (colName.Length > 0 && colName.All(c => char.IsLetterOrDigit(c) || c == '_'))
            return colName;
        return null;
    }

    internal static List<string> ParseSelectAliases(string rawSql)
    {
        var aliases = new List<string>();
        var selectMatch = Regex.Match(rawSql,
            @"\bSELECT\s+(.+?)\s+\bFROM\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!selectMatch.Success) return aliases;

        var selectPart = selectMatch.Groups[1].Value;
        int depth = 0, start = 0;
        var parts = new List<string>();
        for (int i = 0; i < selectPart.Length; i++)
        {
            if (selectPart[i] == '(') depth++;
            else if (selectPart[i] == ')') depth--;
            else if (selectPart[i] == ',' && depth == 0)
            {
                parts.Add(selectPart[start..i].Trim());
                start = i + 1;
            }
        }
        parts.Add(selectPart[start..].Trim());

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            string? alias = null;
            var asMatch = Regex.Match(trimmed, @"\bAS\s+""?(\w+)""?\s*$", RegexOptions.IgnoreCase);
            if (asMatch.Success) alias = asMatch.Groups[1].Value;

            if (alias == null)
            {
                var qm = Regex.Match(trimmed, @"""(\w+)""\s*$");
                if (qm.Success) alias = qm.Groups[1].Value;
            }

            if (alias == null)
            {
                var pm = Regex.Match(trimmed, @"\)\s+(\w+)\s*$");
                if (pm.Success) alias = pm.Groups[1].Value;
            }

            if (alias == null)
            {
                var bareMatch = Regex.Match(trimmed, @"(?:^|\.)(\w+)\s*$");
                if (bareMatch.Success && !trimmed.Contains('('))
                    alias = bareMatch.Groups[1].Value;
            }

            if (alias == null && trimmed.Contains('('))
                alias = Regex.Replace(trimmed, @"\s+", "").ToUpperInvariant();

            if (!string.IsNullOrEmpty(alias))
                aliases.Add(alias.ToUpperInvariant());
        }
        return aliases;
    }

    // ===== Append helpers =====

    internal static void AppendFromClause(StringBuilder sb, QueryEngineRequest request)
    {
        sb.Append($"\"{request.MainTable.SchemaName}\".\"{request.MainTable.TableName}\"");
        var mainAlias = GetTableAlias(request.MainTable);
        sb.Append($" \"{mainAlias}\"");

        var grouped = request.Joins
            .OrderBy(j => j.SortOrder)
            .GroupBy(j => j.JoinTable.TableName + j.JoinTable.SchemaName);

        var joinAliasMap = new Dictionary<string, string>();
        foreach (var group in grouped)
        {
            var first = group.First();
            var baseAlias = GetTableAlias(first.JoinTable);
            var alias = baseAlias;
            var suffix = 2;
            while (joinAliasMap.Values.Any(v => v == alias))
            {
                alias = $"{baseAlias}_{suffix}";
                suffix++;
            }
            var groupKey = first.JoinTable.TableName + first.JoinTable.SchemaName;
            joinAliasMap[groupKey] = alias;

            var onParts = new List<string>();
            foreach (var join in group)
            {
                var leftFull = $"\"{join.LeftTableAlias}\".\"{join.LeftColumnName}\"";
                var rightFull = $"\"{alias}\".\"{join.RightColumnName}\"";
                if (join.LeftDateTrunc)
                {
                    leftFull = $"TRUNC({leftFull})";
                    rightFull = $"TRUNC({rightFull})";
                }
                onParts.Add($"{leftFull} = {rightFull}");
            }

            sb.Append($"\n  {first.JoinType} JOIN \"{first.JoinTable.SchemaName}\".\"{first.JoinTable.TableName}\" \"{alias}\"");
            sb.Append($" ON {string.Join(" AND ", onParts)}");
        }
    }

    internal void AppendWhereClause(StringBuilder sb, QueryEngineRequest request,
        Dictionary<string, string> paramValues)
    {
        var where = BuildWhereClause(request, paramValues);
        if (!string.IsNullOrEmpty(where))
            sb.Append($" WHERE {where}");
    }

    // ===== Cache key builder (for use by callers) =====

    public static string BuildCacheKey(int configId, Dictionary<string, string> filters,
        int page, int pageSize, Dictionary<string, string> contextValues, DateTime configUpdatedAt)
    {
        var raw = $"q_{configId}_p{page}_s{pageSize}_v{configUpdatedAt:yyyyMMddHHmmss}_";
        foreach (var (k, v) in filters.OrderBy(x => x.Key))
            raw += $"{k}={v}_";
        foreach (var (k, v) in contextValues.OrderBy(x => x.Key))
            raw += $"ctx_{k}={v}_";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"query_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
