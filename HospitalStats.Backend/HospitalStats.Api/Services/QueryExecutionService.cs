using System.Data;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Dapper;
using HospitalStats.Api.Data;
using HospitalStats.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.Api.Services;

public class QueryExecutionService
{
    private readonly AppDbContext _db;
    private readonly DataSourceService _dsService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<QueryExecutionService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly int _queryTimeoutSeconds;

    public QueryExecutionService(AppDbContext db, DataSourceService dsService,
        IMemoryCache cache, ILogger<QueryExecutionService> logger,
        IHttpContextAccessor httpContextAccessor, IConfiguration config)
    {
        _db = db;
        _dsService = dsService;
        _cache = cache;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        var timeoutStr = config["QueryTimeoutSeconds"];
        _queryTimeoutSeconds = int.TryParse(timeoutStr, out var t) ? t : 120;
    }

    public async Task<QueryResult> ExecuteAsync(int configId, Dictionary<string, string> filters,
        int page = 1, int? pageSize = null)
    {
        var config = await _db.QueryConfigs
            .Include(q => q.MainTable).ThenInclude(t => t!.DataSource)
            .Include(q => q.MainTable).ThenInclude(t => t!.Columns)
            .Include(q => q.Fields).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Filters).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Joins).ThenInclude(j => j.JoinTable)
            .Include(q => q.Joins).ThenInclude(j => j.JoinTable!.Columns)
            .Include(q => q.Joins).ThenInclude(j => j.LeftMetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Joins).ThenInclude(j => j.RightMetaColumn).ThenInclude(c => c!.MetaTable)
            .FirstOrDefaultAsync(q => q.Id == configId);

        if (config?.MainTable?.DataSource == null)
            throw new ArgumentException("查询配置无效");

        var ds = config.MainTable.DataSource;
        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var charSetOverride = ds.CharSetOverride;

        // For US7ASCII databases, wrap string columns with RAWTOHEX(UTL_RAW.CAST_TO_RAW())
        // to preserve raw bytes through Oracle's lossy character set conversion.
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride);

        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        // resolve context filter values from JWT claims
        var contextValues = ResolveContextValues();

        var hasRawSql = !string.IsNullOrEmpty(SanitizeRawSql(config.RawSql));
        if (hasRawSql)
            ValidateRawSqlReadOnly(SanitizeRawSql(config.RawSql)!);
        // Both Count and Data use rawSql when available. Filters are injected into
        // rawSql via InjectWhereIntoRawSql. For US7ASCII, the hex-encoding wrapper
        // (HexEncodeRawSqlColumns) handles string column encoding for Data SQL.
        // The config path is only a fallback when there is no rawSql at all.
        var useRawSqlForCount = hasRawSql;
        var useRawSqlForData = hasRawSql;

        // build SQL — paramValues holds final parameter values (hex-encoded or original),
        // separate from filters which stays as original user input for cache key + path decisions
        var paramValues = new Dictionary<string, string>();
        var (countSql, countParams) = BuildCountSql(config, filters, contextValues, paramValues, useRawSqlForCount, charSetOverride);
        var (dataSql, dataParams) = BuildDataSql(config, page, pageSize ?? config.PageSize ?? 50,
            filters, contextValues, paramValues, useHexEncoding, useRawSqlForData, charSetOverride);
        var allParams = MergeParams(countParams, dataParams, paramValues);

        _logger.LogInformation("Count SQL: {Sql}", countSql);
        _logger.LogInformation("Data SQL: {Sql}", dataSql);


        // caching check (include context values in cache key)
        var cacheKey = BuildCacheKey(configId, filters, page, pageSize, contextValues, config.UpdatedAt);
        if (_cache.TryGetValue(cacheKey, out QueryResult? cached))
            return cached!;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // execute count (only pass count params to avoid Oracle unbound param errors)
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
            total = await conn.ExecuteScalarAsync<int>(new CommandDefinition(countSql, countDp, commandTimeout: _queryTimeoutSeconds));
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException(
                $"Count query failed: {ex.Message}. SQL: {countSql}", ex);
        }

        // execute data
        IEnumerable<dynamic> rows;
        try
        {
            rows = await conn.QueryAsync(new CommandDefinition(dataSql, allParams, commandTimeout: _queryTimeoutSeconds));
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException(
                $"Data query failed: {ex.Message}. SQL: {dataSql}", ex);
        }

        sw.Stop();

        var colDisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hexColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in config.Fields.OrderBy(f => f.SortOrder))
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var sqlAlias = col.ColumnName ?? "";
            var displayName = !string.IsNullOrEmpty(field.Alias) ? field.Alias
                : !string.IsNullOrEmpty(col.Alias) ? col.Alias
                : col.ColumnName ?? "";
            colDisplayMap[sqlAlias] = displayName;
            if (useHexEncoding && IsStringType(col.DataType))
                hexColumns.Add(sqlAlias);
        }

        // convert to list of dictionaries
        var resultRows = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in (IDictionary<string, object?>)row)
            {
                var val = prop.Value;
                if (useHexEncoding && hexColumns.Contains(prop.Key) && val is string hexStr1 && !string.IsNullOrEmpty(hexStr1))
                {
                    val = DecodeHexString(hexStr1, charSetOverride);
                }
                else if (useHexEncoding && val is string hexStr2 && !string.IsNullOrEmpty(hexStr2)
                    && hexStr2.Length % 2 == 0 && hexStr2.All(c => (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                {
                    // Fallback: decode any hex-like string even if not in hexColumns (RawSql-only configs)
                    var decoded = DecodeHexString(hexStr2, charSetOverride);
                    if (decoded != hexStr2 && !string.IsNullOrEmpty(decoded))
                        val = decoded;
                }
                else if (useHexEncoding && val is string strVal && strVal.Any(c => c > 127 || c == '?'))
                {
                    var fixedVal = ConvertEncoding(strVal, charSetOverride);
                    if (fixedVal != strVal)
                        val = fixedVal;
                }
                // Remap from SQL column name to display name using configured alias
                var key = colDisplayMap.TryGetValue(prop.Key, out var display) ? display : prop.Key;
                dict[key] = val;
            }
            resultRows.Add(dict);
        }

        // Build columns list: remap SQL column names to display names via configured aliases
        var columns = new List<string>();
        if (useRawSqlForData)
        {
            var rawKeys = resultRows.Count > 0
                ? resultRows[0].Keys
                    .Where(k => !k.Equals("RN", StringComparison.OrdinalIgnoreCase))
                    .ToList()
                : ParseSelectAliases(SanitizeRawSql(config.RawSql)!);

            foreach (var key in rawKeys)
            {
                columns.Add(colDisplayMap.TryGetValue(key, out var display) ? display : key);
            }
        }
        else
        {
            foreach (var field in config.Fields.OrderBy(f => f.SortOrder))
            {
                var col = field.MetaColumn;
                if (col == null) continue;
                var displayName = !string.IsNullOrEmpty(field.Alias) ? field.Alias
                    : !string.IsNullOrEmpty(col.Alias) ? col.Alias
                    : col.ColumnName ?? "";
                columns.Add(displayName);
            }
        }

        var result = new QueryResult
        {
            Rows = resultRows,
            Columns = columns,
            Total = total,
            Page = page,
            PageSize = pageSize ?? config.PageSize ?? 50,
            ElapsedMs = sw.ElapsedMilliseconds
        };

        // cache for 2 minutes
        _cache.Set(cacheKey, result, TimeSpan.FromMinutes(2));

        return result;
    }

    public async Task<byte[]> ExportExcelAsync(int configId, Dictionary<string, string> filters)
    {
        var result = await ExecuteAsync(configId, filters, 1, 50000);
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var ws = workbook.Worksheets.Add("查询结果");

        // header
        for (int i = 0; i < result.Columns.Count; i++)
        {
            ws.Cell(1, i + 1).Value = result.Columns[i];
            ws.Cell(1, i + 1).Style.Font.Bold = true;
        }

        // data
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

    public async Task<List<string>> GetDistinctValuesAsync(int configId, int filterId)
    {
        var config = await _db.QueryConfigs
            .Include(q => q.MainTable).ThenInclude(t => t!.DataSource)
            .Include(q => q.Filters).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .FirstOrDefaultAsync(q => q.Id == configId);

        if (config?.MainTable?.DataSource == null)
            throw new ArgumentException("查询配置无效");

        var filter = config.Filters.FirstOrDefault(f => f.Id == filterId);
        var col = filter?.MetaColumn;
        if (col == null) return new List<string>();

        var ds = config.MainTable.DataSource;
        var charSetOverride = ds.CharSetOverride;
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride) && IsStringType(col.DataType);
        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var tableAlias = GetTableAlias(col.MetaTable);
        var schema = col.MetaTable?.SchemaName ?? "HOSPITAL";
        var table = col.MetaTable?.TableName ?? "";
        var colRef = $"\"{tableAlias}\".\"{col.ColumnName}\"";
        var colExpr = useHexEncoding
            ? $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({colRef})) as \"_v\""
            : $"{colRef}";
        var orderExpr = useHexEncoding ? "\"_v\"" : colRef;

        var sql = $"SELECT DISTINCT {colExpr} " +
                  $"FROM \"{schema}\".\"{table}\" \"{tableAlias}\" " +
                  $"ORDER BY {orderExpr}";

        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();
        var values = await conn.QueryAsync<string>(sql);
        return values
            .Where(v => v != null)
            .Select(v => useHexEncoding
                ? DecodeHexString(v!, charSetOverride)
                : ConvertEncoding(v!, charSetOverride))
            .ToList();
    }

    // ===== SQL Builders =====

    internal static string SanitizeRawSql(string? rawSql)
    {
        if (string.IsNullOrEmpty(rawSql)) return "";
        return rawSql.TrimEnd(';').TrimEnd();
    }

    internal static void ValidateRawSqlReadOnly(string sql)
    {
        var t = sql.TrimStart();
        if (t.Length == 0) return;
        // Oracle: first keyword must be SELECT or WITH (CTE)
        var firstWord = t.Length >= 6 ? t[..6] : t;
        if (firstWord.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
            firstWord.StartsWith("WITH", StringComparison.OrdinalIgnoreCase))
            return;
        throw new InvalidOperationException("仅允许 SELECT 和 WITH 查询，不支持数据修改操作。");
    }

    /// <summary>Returns true when the user has changed at least one filter from its default
    /// value on the preview page. When this is true we fall back to config-based SQL so
    /// user filter actions actually take effect. When false (all filters at defaults),
    /// the raw SQL is used directly, preserving the original query semantics including
    /// character encoding of filter values.</summary>
    /// <summary>
    /// Parse a filter value that may contain an operator override prefix.
    /// Format: "operator::value" (e.g. "NOT LIKE::%头痛%") or plain "value" for backward compatibility.
    /// When no operator prefix is found, the config's default operator is used.
    /// </summary>
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

    /// <summary>
    /// Register parameter value(s) for a filter, splitting comma-separated values
    /// for BETWEEN/NOT BETWEEN operators into _from and _to parameters.
    /// </summary>
    internal static void RegisterParamValues(Dictionary<string, string> paramValues, string key,
        string op, string value, Func<string, string>? encode = null)
    {
        if (op is "BETWEEN" or "NOT BETWEEN")
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

    internal static bool HasUserFilterInput(QueryConfig config, Dictionary<string, string> userFilters)
    {
        foreach (var filter in config.Filters)
        {
            if (filter.IsContextFilter) continue;
            if (userFilters.TryGetValue(filter.Id.ToString(), out var userVal))
            {
                var defaultVal = filter.DefaultValue ?? "";
                if (!string.Equals(userVal, defaultVal, StringComparison.Ordinal))
                    return true;
            }
        }
        return false;
    }

    internal (string Sql, Dictionary<string, object?> Params) BuildCountSql(
        QueryConfig config, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues,
        Dictionary<string, string> paramValues,
        bool useRawSqlPath,
        string? charSetOverride = null)
    {
        var rawSql = SanitizeRawSql(config.RawSql);
        if (useRawSqlPath)
        {
            var rawAliases = ExtractAliasesFromRawSql(rawSql);
var rawWhere = BuildOuterWhereForRawSql(config, userFilters, contextValues, paramValues, charSetOverride, rawAliases);
            var innerSql = InjectWhereIntoRawSql(rawSql, rawWhere);
            // Hex-encode inline Chinese literals in JOIN / subquery conditions
            if (!string.IsNullOrEmpty(charSetOverride))
                innerSql = HexEncodeInlineLiterals(innerSql, charSetOverride);
            return ($"SELECT COUNT(*) FROM ({innerSql}) \"_cnt\"",
                new Dictionary<string, object?>());
        }

        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ");
        AppendFromClause(sb, config);
        AppendWhereClause(sb, config, userFilters, contextValues, paramValues, charSetOverride);
        return (sb.ToString(), new Dictionary<string, object?>());
    }

    internal (string Sql, Dictionary<string, object?> Params) BuildDataSql(
        QueryConfig config, int page, int pageSize, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues, Dictionary<string, string> paramValues,
        bool useHexEncoding, bool useRawSqlPath, string? charSetOverride = null)
    {
        string innerSql;
        var rawSql = SanitizeRawSql(config.RawSql);
        if (useRawSqlPath)
        {
            var rawAliases = ExtractAliasesFromRawSql(rawSql);
var rawWhere = BuildOuterWhereForRawSql(config, userFilters, contextValues, paramValues, charSetOverride, rawAliases);
            innerSql = InjectWhereIntoRawSql(rawSql, rawWhere);

            // For US7ASCII: hex-encode inline non-ASCII string literals in the raw
            // SQL (JOIN conditions, subqueries, etc.) before other hex wrapping.
            if (useHexEncoding)
            {
                innerSql = HexEncodeInlineLiterals(innerSql, charSetOverride!);
                innerSql = HexEncodeRawSqlColumns(innerSql, config, rawAliases);
            }
        }
        else
        {
            var selectClause = BuildSelectClause(config, useHexEncoding);
            var fromClause = BuildFromClause(config);
            var whereClause = BuildWhereClause(config, userFilters, contextValues, paramValues, charSetOverride);
            var groupBy = BuildGroupBy(config);
            var orderBy = BuildOrderBy(config);

            innerSql = $"SELECT {selectClause} FROM {fromClause}";
            if (!string.IsNullOrEmpty(whereClause))
                innerSql += $" WHERE {whereClause}";
            if (!string.IsNullOrEmpty(groupBy))
                innerSql += $" GROUP BY {groupBy}";
            if (!string.IsNullOrEmpty(orderBy))
                innerSql += $" ORDER BY {orderBy}";
        }

        // Oracle 10g ROWNUM pagination (explicit columns to avoid t.* column name loss)
        var startRow = (page - 1) * pageSize + 1;
        var endRow = page * pageSize;

        string outerCols;
        if (useRawSqlPath)
        {
            // Use * for rawSql — the raw SQL defines its own output columns,
            // which may not match configured field names (e.g., columns wrapped
            // in functions, aggregates without aliases, implicit aliases).
            outerCols = "*";
        }
        else
        {
            var cols = config.Fields.OrderBy(f => f.SortOrder)
                .Select(f => $"\"{f.MetaColumn?.ColumnName ?? "COL"}\"")
                .ToList();
            outerCols = string.Join(", ", cols);
        }

        var paginatedSql = $"SELECT {outerCols} FROM (SELECT t.*, ROWNUM rn FROM ({innerSql}) t WHERE ROWNUM <= :p_endRow) WHERE rn >= :p_startRow";

        var extraParams = new Dictionary<string, object?>
        {
            ["p_endRow"] = endRow,
            ["p_startRow"] = startRow
        };
        return (paginatedSql, extraParams);
    }

    internal static bool IsStringType(string? dataType) => dataType?.ToUpperInvariant() switch
    {
        "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "CLOB" or "NCLOB"
            or "VARCHAR" or "NVARCHAR" or "LONG" => true,
        _ => false
    };

    internal static bool ContainsNonAscii(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.Any(c => c > 127);
    }

    /// <summary>
    /// Encode a filter value into a hex string suitable for comparison against
    /// RAWTOHEX(UTL_RAW.CAST_TO_RAW(...)) output. LIKE wildcards (% and _) are
    /// preserved as-is for LIKE/NOT LIKE operators; all other characters are
    /// converted to their byte representation in the source encoding and hex-encoded.
    /// The result is pure ASCII and survives US7ASCII transport without corruption.
    /// </summary>
    internal static string EncodeNonAsciiValue(string value, string op, string? sourceEncoding)
    {
        var isLike = op.Equals("LIKE", StringComparison.OrdinalIgnoreCase) ||
                     op.Equals("NOT LIKE", StringComparison.OrdinalIgnoreCase);

        var enc = Encoding.GetEncoding(sourceEncoding ?? "gbk");
        var sb = new StringBuilder();

        foreach (var ch in value)
        {
            if (isLike && (ch == '%' || ch == '_'))
            {
                sb.Append(ch);
            }
            else
            {
                var bytes = enc.GetBytes(new[] { ch });
                foreach (var b in bytes)
                    sb.Append(b.ToString("X2"));
            }
        }

        return sb.ToString();
    }

    internal static string QualifyColumn(MetaColumn col)
    {
        var alias = !string.IsNullOrEmpty(col.MetaTable?.Alias)
            ? col.MetaTable!.Alias
            : col.MetaTable?.TableName ?? "T";
        return $"\"{alias}\".\"{col.ColumnName}\"";
    }

    internal static string GetTableAlias(MetaTable? table)
    {
        return !string.IsNullOrEmpty(table?.Alias) ? table!.Alias : (table?.TableName ?? "T");
    }

    internal static string BuildSelectClause(QueryConfig config, bool useHexEncoding = false)
    {
        var parts = new List<string>();
        foreach (var field in config.Fields.OrderBy(f => f.SortOrder))
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var colExpr = QualifyColumn(col);

            if (!string.IsNullOrEmpty(field.AggregateFunc))
                colExpr = $"{field.AggregateFunc}({colExpr})";

            // Wrap string columns with RAWTOHEX(UTL_RAW.CAST_TO_RAW()) to
            // preserve raw bytes through Oracle's lossy charset conversion.
            if (useHexEncoding && IsStringType(col.DataType))
                colExpr = $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({colExpr}))";

            // Use column name as SQL alias — ASCII-safe for Oracle 10g
            var label = col.ColumnName ?? "COL";
            parts.Add($"{colExpr} AS \"{label}\"");
        }

        if (parts.Count == 0)
            throw new InvalidOperationException("没有配置查询字段");

        return string.Join(", ", parts);
    }

    internal static string BuildFromClause(QueryConfig config)
    {
        var mainAlias = GetTableAlias(config.MainTable);
        var from = $"\"{config.MainTable?.SchemaName}\".\"{config.MainTable?.TableName}\" \"{mainAlias}\"";

        // Group joins by table to merge ON conditions for the same table
        var grouped = config.Joins
            .OrderBy(j => j.SortOrder)
            .GroupBy(j => j.JoinTableId);

        var joinAliasMap = new Dictionary<int, string>();
        foreach (var group in grouped)
        {
            var first = group.First();
            var baseAlias = GetTableAlias(first.JoinTable);
            // Deduplicate alias if same table appears in multiple groups
            var alias = baseAlias;
            var suffix = 2;
            while (joinAliasMap.Values.Any(v => v == alias))
            {
                alias = $"{baseAlias}_{suffix}";
                suffix++;
            }
            joinAliasMap[group.Key] = alias;

            var onParts = new List<string>();
            foreach (var join in group)
            {
                var leftCol = join.LeftMetaColumn;
                var rightCol = join.RightMetaColumn;

                var leftAlias = mainAlias;
                if (leftCol?.MetaTable != null)
                {
                    var leftTableId = leftCol.MetaTable.Id;
                    if (leftTableId == config.MainTable?.Id)
                        leftAlias = mainAlias;
                    else if (joinAliasMap.TryGetValue(leftTableId, out var mapped))
                        leftAlias = mapped;
                }
                var leftFull = $"\"{leftAlias}\".\"{leftCol?.ColumnName}\"";
                var rightFull = $"\"{alias}\".\"{rightCol?.ColumnName}\"";
                if (join.LeftDateTrunc)
                {
                    leftFull = $"TRUNC({leftFull})";
                    rightFull = $"TRUNC({rightFull})";
                }

                onParts.Add($"{leftFull} = {rightFull}");
            }

            var joinType = first.JoinType;
            from += $"\n  {joinType} JOIN \"{first.JoinTable?.SchemaName}\".\"{first.JoinTable?.TableName}\" \"{alias}\"";
            from += $" ON {string.Join(" AND ", onParts)}";
        }

        return from;
    }

    /// <summary>Extracts table-alias mappings from the FROM/JOIN clauses of a raw SQL.</summary>
    internal static Dictionary<string, string> ExtractAliasesFromRawSql(string rawSql)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Match: [schema.]table_name alias  or  [schema.]table_name "alias"
        foreach (Match m in Regex.Matches(rawSql,
            @"(?:(\w+)\s*\.\s*)?(\w+)\s+(""?\w+""?)\b",
            RegexOptions.IgnoreCase))
        {
            var tableName = m.Groups[2].Value;
            var alias = m.Groups[3].Value.Trim('"');
            if (!map.ContainsKey(tableName))
                map[tableName] = alias;
        }
        return map;
    }

    /// <summary>Injects filter WHERE clause into rawSql before GROUP BY / ORDER BY,
    /// rather than wrapping it as an outer subquery. This prevents ORA-00904
    /// when filter columns are used inside functions in the SELECT list and are
    /// not themselves output columns.</summary>
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

    /// <summary>
    /// Wrap rawSql as a subquery, hex-encoding string output columns in the outer SELECT.
    /// Parses output column aliases from the rawSql SELECT, matches them against
    /// configured string-type MetaColumns, and wraps matching columns with RAWTOHEX.
    /// </summary>
    internal static string HexEncodeRawSqlColumns(string rawSql, QueryConfig config,
        Dictionary<string, string> rawAliases)
    {
        var aliases = ParseSelectAliases(rawSql);
        if (aliases.Count == 0) return rawSql;

        // Build set of string column names from configured Fields.
        // If no Fields are configured (RawSql-only mode), derive from MainTable MetaColumns.
        var stringCols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in config.Fields)
        {
            var col = field.MetaColumn;
            if (col == null || !IsStringType(col.DataType)) continue;
            stringCols.Add(col.ColumnName ?? "");
            if (!string.IsNullOrEmpty(col.Alias))
                stringCols.Add(col.Alias);
        }

        // Fallback for RawSql-only configs: look up string columns from MainTable + JoinTables
        if (stringCols.Count == 0 && config.MainTable != null)
        {
            var allMetaCols = new List<MetaColumn>();
            if (config.MainTable.Columns != null && config.MainTable.Columns.Count > 0)
                allMetaCols.AddRange(config.MainTable.Columns);
            foreach (var join in config.Joins)
            {
                if (join.JoinTable?.Columns != null)
                    allMetaCols.AddRange(join.JoinTable.Columns);
            }

            foreach (var col in allMetaCols)
            {
                if (!IsStringType(col.DataType)) continue;
                stringCols.Add(col.ColumnName ?? "");
                if (!string.IsNullOrEmpty(col.Alias))
                    stringCols.Add(col.Alias);
            }
        }

        var wrapped = new List<string>();
        var hasHex = false;
        foreach (var alias in aliases)
        {
            if (stringCols.Contains(alias))
            {
                wrapped.Add($"RAWTOHEX(UTL_RAW.CAST_TO_RAW(\"{alias}\")) AS \"{alias}\"");
                hasHex = true;
            }
            else
            {
                wrapped.Add($"\"{alias}\"");
            }
        }

        if (!hasHex) return rawSql;
        return $"SELECT {string.Join(", ", wrapped)} FROM ({rawSql}) \"_raw\"";
    }

    /// <summary>
    /// Replace non-ASCII characters in SQL string literals with their hex-encoded
    /// equivalents, so they survive US7ASCII Oracle transport. Only modifies
    /// content inside single-quoted strings; SQL keywords and identifiers are untouched.
    /// </summary>
    internal static string HexEncodeInlineLiterals(string sql, string sourceEncoding)
    {
        var sb = new StringBuilder();
        var literalStart = -1;
        var depth = 0;
        var contentBuf = new StringBuilder();

        for (int i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch == '\'')
            {
                if (depth == 0)
                {
                    depth = 1;
                    literalStart = i;
                    contentBuf.Clear();
                }
                else if (i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    // Oracle escaped quote: ''
                    contentBuf.Append("''");
                    i++; // skip second quote
                }
                else
                {
                    // End of string literal
                    var content = contentBuf.ToString();
                    if (ContainsNonAscii(content))
                    {
                        var enc = Encoding.GetEncoding(sourceEncoding);
                        var hex = Convert.ToHexString(enc.GetBytes(content));
                        sb.Append($"UTL_RAW.CAST_TO_VARCHAR2(HEXTORAW('{hex}'))");
                    }
                    else
                    {
                        sb.Append('\'');
                        sb.Append(content);
                        sb.Append('\'');
                    }
                    depth = 0;
                }
            }
            else if (depth > 0)
            {
                contentBuf.Append(ch);
            }
            else
            {
                sb.Append(ch);
            }
        }

        // Unclosed string — append rest as-is
        if (depth > 0 && literalStart >= 0)
            sb.Append(sql[literalStart..]);

        return sb.ToString();
    }

    /// <summary>Parse output column aliases from rawSql's SELECT clause.
    /// Handles: "func()alias", "col AS alias", "col \"alias\"", "tbl.col", "col".</summary>
    internal static List<string> ParseSelectAliases(string rawSql)
    {
        var aliases = new List<string>();
        var selectMatch = Regex.Match(rawSql,
            @"\bSELECT\s+(.+?)\s+\bFROM\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!selectMatch.Success) return aliases;

        var selectPart = selectMatch.Groups[1].Value;
        // Split by comma, respecting parentheses depth
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
        parts.Add(selectPart[start..].Trim());

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Extract alias: try patterns from most specific to least
            string? alias = null;

            // 1. "expr AS alias" or "expr AS \"alias\""
            var asMatch = Regex.Match(trimmed, @"\bAS\s+""?(\w+)""?\s*$", RegexOptions.IgnoreCase);
            if (asMatch.Success) alias = asMatch.Groups[1].Value;

            // 2. "expr \"alias\"" (quoted alias after space)
            if (alias == null)
            {
                var qm = Regex.Match(trimmed, @"""(\w+)""\s*$");
                if (qm.Success) alias = qm.Groups[1].Value;
            }

            // 3. "func(...)alias" or "expr )alias" — alias directly after closing paren
            if (alias == null)
            {
                var pm = Regex.Match(trimmed, @"\)(\w+)\s*$");
                if (pm.Success) alias = pm.Groups[1].Value;
            }

            // 4. "tbl.col" or "col" — bare column reference (no parens, no AS)
            if (alias == null)
            {
                // Extract the last dot-separated identifier
                var bareMatch = Regex.Match(trimmed, @"(?:^|\.)(\w+)\s*$");
                if (bareMatch.Success && !trimmed.Contains('('))
                    alias = bareMatch.Groups[1].Value;
            }

            // 5. Function expression without alias (e.g. "sum(a.costs)")
            // Oracle auto-names the column by uppercasing the expression.
            if (alias == null && trimmed.Contains('('))
            {
                // Remove extra whitespace to match Oracle's column naming
                alias = Regex.Replace(trimmed, @"\s+", "").ToUpperInvariant();
            }

            if (!string.IsNullOrEmpty(alias))
                aliases.Add(alias.ToUpperInvariant());
        }
        return aliases;
    }

    /// <summary>Build WHERE clause for raw SQL mode. Applies filters (normal + context)
    /// using unqualified column names because rawSql subquery columns have no schema prefix.
    /// Filter values are written into <paramref name="userFilters"/> so MergeParams picks them up.
    ///
    /// When <paramref name="charSetOverride"/> is set (US7ASCII database), non-ASCII string
    /// filter values are hex-encoded and the column reference is wrapped with
    /// RAWTOHEX(UTL_RAW.CAST_TO_RAW(...)). Both sides become pure ASCII so the comparison
    /// survives Oracle's lossy charset conversion. LIKE wildcards (% and _) are preserved.</summary>
    internal string BuildOuterWhereForRawSql(QueryConfig config,
        Dictionary<string, string> userFilters, Dictionary<string, string> contextValues,
        Dictionary<string, string> paramValues, string? charSetOverride = null,
        Dictionary<string, string>? rawAliases = null)
    {
        var parts = new List<string>();
        foreach (var filter in config.Filters.OrderBy(f => f.SortOrder))
        {
            var col = filter.MetaColumn;
            if (col == null) continue;

            string? effectiveValue;

            if (filter.IsContextFilter && !string.IsNullOrEmpty(filter.ContextKey))
            {
                if (!contextValues.TryGetValue(filter.ContextKey, out effectiveValue) ||
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
                _ = userFilters.TryGetValue(filter.Id.ToString(), out var userVal);
                effectiveValue = userVal ?? filter.DefaultValue;
                if (string.IsNullOrEmpty(effectiveValue)) continue;
            }

            var (effectiveOp, effectiveVal) = ParseFilterValue(effectiveValue, filter.Operator);
            if (string.IsNullOrEmpty(effectiveVal)) continue;

            // Qualify column with table alias from rawSql to avoid ORA-00918 ambiguity
            var colName = col.ColumnName ?? "COL";
            var tableName = col.MetaTable?.TableName;
            var qualified = colName;
            if (!string.IsNullOrEmpty(tableName) && rawAliases != null && rawAliases.TryGetValue(tableName, out var alias))
                qualified = $"{alias}.\"{colName}\"";
            else
                qualified = $"\"{colName}\"";

            var paramName = $"p_f_{filter.Id}";
            var isDate = "DATE".Equals(col.DataType, StringComparison.OrdinalIgnoreCase);

            // For US7ASCII databases: non-ASCII string values get corrupted through
            // ODP.NET parameter binding just like inline SQL text does. Work around
            // by hex-encoding both sides of the comparison — column with RAWTOHEX,
            // value with EncodeNonAsciiValue — so the entire expression is pure ASCII.
            if (!string.IsNullOrEmpty(charSetOverride)
                && IsStringType(col.DataType)
                && ContainsNonAscii(effectiveVal))
            {
                var colExpr = $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({qualified}))";
                var hexValue = EncodeNonAsciiValue(effectiveVal, effectiveOp, charSetOverride);
                _logger.LogInformation("Hex filter {FilterId}: original='{Orig}' op='{Op}' hex='{Hex}' col={Col}",
                    filter.Id, effectiveVal, effectiveOp, hexValue, colExpr);
                parts.Add(OperatorToSql(colExpr, effectiveOp, paramName, isDate));
                // Hex-encoded value must always overwrite — the original Chinese
                // text would get garbled through US7ASCII parameter transport.
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal,
                    (v) => EncodeNonAsciiValue(v, effectiveOp, charSetOverride));
            }
            else
            {
                var colExpr = qualified;
                parts.Add(OperatorToSql(colExpr, effectiveOp, paramName, isDate));
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal, null);
            }
        }
        return string.Join(" AND ", parts);
    }

    internal string BuildWhereClause(QueryConfig config, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues,
        Dictionary<string, string> paramValues, string? charSetOverride = null)
    {
        var parts = new List<string>();
        foreach (var filter in config.Filters.OrderBy(f => f.SortOrder))
        {
            var col = filter.MetaColumn;
            if (col == null) continue;

            string? effectiveValue;

            if (filter.IsContextFilter && !string.IsNullOrEmpty(filter.ContextKey))
            {
                // context filter: value from JWT claims, user cannot override
                if (!contextValues.TryGetValue(filter.ContextKey, out effectiveValue) ||
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
                // normal filter: value from user input or default
                _ = userFilters.TryGetValue(filter.Id.ToString(), out var userVal);
                effectiveValue = userVal ?? filter.DefaultValue;
                if (string.IsNullOrEmpty(effectiveValue)) continue;
            }

            var (effectiveOp, effectiveVal) = ParseFilterValue(effectiveValue, filter.Operator);
            if (string.IsNullOrEmpty(effectiveVal)) continue;

            var colExpr = QualifyColumn(col);
            var paramName = $"p_f_{filter.Id}";
            var isDate = "DATE".Equals(col.DataType, StringComparison.OrdinalIgnoreCase);

            // For US7ASCII: hex-encode both sides so comparison survives transport.
            if (!string.IsNullOrEmpty(charSetOverride)
                && IsStringType(col.DataType)
                && ContainsNonAscii(effectiveVal))
            {
                colExpr = $"RAWTOHEX(UTL_RAW.CAST_TO_RAW({colExpr}))";
                var hexValue = EncodeNonAsciiValue(effectiveVal, effectiveOp, charSetOverride);
                _logger.LogInformation("Hex filter {FilterId}: original='{Orig}' op='{Op}' hex='{Hex}' col={Col}",
                    filter.Id, effectiveVal, effectiveOp, hexValue, colExpr);
                parts.Add(OperatorToSql(colExpr, effectiveOp, paramName, isDate));
                // Hex-encoded value must always overwrite — the original Chinese
                // text would get garbled through US7ASCII parameter transport.
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal,
                    (v) => EncodeNonAsciiValue(v, effectiveOp, charSetOverride));
            }
            else
            {
                parts.Add(OperatorToSql(colExpr, effectiveOp, paramName, isDate));
                RegisterParamValues(paramValues, filter.Id.ToString(), effectiveOp, effectiveVal, null);
            }
        }
        return string.Join(" AND ", parts);
    }

    internal static string BuildGroupBy(QueryConfig config)
    {
        if (string.IsNullOrEmpty(config.GroupByColumn)) return "";
        return QuoteQualifiedName(config.GroupByColumn);
    }

    internal static string QuoteQualifiedName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var lastDot = name.LastIndexOf('.');
        if (lastDot < 0) return $"\"{name}\"";
        return $"\"{name[..lastDot]}\".\"{name[(lastDot + 1)..]}\"";
    }

    internal static string BuildOrderBy(QueryConfig config)
    {
        if (string.IsNullOrEmpty(config.SortColumn)) return "";

        var label = ResolveSortLabel(config);
        var dir = config.SortDirection ?? "ASC";
        if (!string.IsNullOrEmpty(label))
            return $"\"{label}\" {dir}";

        // SortColumn not found in fields — if it contains non-ASCII, skip ORDER BY
        if (config.SortColumn.Any(c => c > 127))
            return "";

        return $"{QuoteQualifiedName(config.SortColumn)} {dir}";
    }

    internal static string? ResolveSortLabel(QueryConfig config)
    {
        var sortCol = config.SortColumn;
        if (string.IsNullOrEmpty(sortCol)) return null;

        foreach (var field in config.Fields)
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var ta = GetTableAlias(col.MetaTable);
            var displayAlias = !string.IsNullOrEmpty(field.Alias) ? field.Alias
                : !string.IsNullOrEmpty(col.Alias) ? col.Alias
                : col.ColumnName ?? "";
            if (sortCol.Equals($"{ta}.{col.ColumnName}", StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(displayAlias) &&
                 sortCol.Equals($"{ta}.{displayAlias}", StringComparison.OrdinalIgnoreCase)))
            {
                return col.ColumnName;
            }
        }

        // Second pass: match just the column part (e.g. "OUTP_MR.病人ID" → match "病人ID" against display alias)
        var lastDot = sortCol.LastIndexOf('.');
        var colPart = lastDot >= 0 ? sortCol[(lastDot + 1)..] : sortCol;
        foreach (var field in config.Fields)
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var displayAlias = !string.IsNullOrEmpty(field.Alias) ? field.Alias
                : !string.IsNullOrEmpty(col.Alias) ? col.Alias
                : col.ColumnName ?? "";
            if (colPart.Equals(col.ColumnName ?? "", StringComparison.OrdinalIgnoreCase) ||
                colPart.Equals(displayAlias, StringComparison.OrdinalIgnoreCase))
            {
                return col.ColumnName;
            }
        }

        return null;
    }

    internal static void AppendFromClause(StringBuilder sb, QueryConfig config)
    {
        sb.Append($"\"{config.MainTable?.SchemaName}\".\"{config.MainTable?.TableName}\"");
        var mainAlias = GetTableAlias(config.MainTable);
        sb.Append($" \"{mainAlias}\"");

        var grouped = config.Joins
            .OrderBy(j => j.SortOrder)
            .GroupBy(j => j.JoinTableId);

        var joinAliasMap = new Dictionary<int, string>();
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
            joinAliasMap[group.Key] = alias;

            var onParts = new List<string>();
            foreach (var join in group)
            {
                var leftCol = join.LeftMetaColumn;
                var rightCol = join.RightMetaColumn;

                var leftAlias = mainAlias;
                if (leftCol?.MetaTable != null)
                {
                    var leftTableId = leftCol.MetaTable.Id;
                    if (leftTableId == config.MainTable?.Id)
                        leftAlias = mainAlias;
                    else if (joinAliasMap.TryGetValue(leftTableId, out var mapped))
                        leftAlias = mapped;
                }
                var leftFull = $"\"{leftAlias}\".\"{leftCol?.ColumnName}\"";
                var rightFull = $"\"{alias}\".\"{rightCol?.ColumnName}\"";
                if (join.LeftDateTrunc)
                {
                    leftFull = $"TRUNC({leftFull})";
                    rightFull = $"TRUNC({rightFull})";
                }

                onParts.Add($"{leftFull} = {rightFull}");
            }

            var joinType = first.JoinType;
            sb.Append($"\n  {joinType} JOIN \"{first.JoinTable?.SchemaName}\".\"{first.JoinTable?.TableName}\" \"{alias}\"");
            sb.Append($" ON {string.Join(" AND ", onParts)}");
        }
    }

    internal void AppendWhereClause(StringBuilder sb, QueryConfig config,
        Dictionary<string, string> userFilters, Dictionary<string, string> contextValues,
        Dictionary<string, string> paramValues, string? charSetOverride = null)
    {
        var where = BuildWhereClause(config, userFilters, contextValues, paramValues, charSetOverride);
        if (!string.IsNullOrEmpty(where))
            sb.Append($" WHERE {where}");
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

    internal static DynamicParameters MergeParams(
        Dictionary<string, object?> countParams,
        Dictionary<string, object?> dataParams,
        Dictionary<string, string> paramValues)
    {
        var dp = new DynamicParameters();
        foreach (var (k, v) in countParams) dp.Add(k, v);
        foreach (var (k, v) in dataParams) dp.Add(k, v);

        foreach (var (k, v) in paramValues)
        {
            dp.Add($"p_f_{k}", v);
        }

        return dp;
    }

    internal Dictionary<string, string> ResolveContextValues()
    {
        var values = new Dictionary<string, string>();
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return values;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            values["UserId"] = userId;

        var deptName = user.FindFirst("dept_name")?.Value;
        if (!string.IsNullOrEmpty(deptName))
            values["DeptName"] = deptName;

        return values;
    }

    internal static string BuildCacheKey(int configId, Dictionary<string, string> filters,
        int page, int? pageSize, Dictionary<string, string> contextValues, DateTime configUpdatedAt)
    {
        var raw = $"q_{configId}_p{page}_s{pageSize}_v{configUpdatedAt:yyyyMMddHHmmss}_";
        foreach (var (k, v) in filters.OrderBy(x => x.Key))
            raw += $"{k}={v}_";
        foreach (var (k, v) in contextValues.OrderBy(x => x.Key))
            raw += $"ctx_{k}={v}_";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"query_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Decode a hex string produced by RAWTOHEX(UTL_RAW.CAST_TO_RAW()) back to
    /// readable text using the specified target encoding.
    /// </summary>
    internal static string DecodeHexString(string hex, string? targetEncoding)
    {
        if (string.IsNullOrEmpty(hex)) return hex;
        try
        {
            var bytes = Convert.FromHexString(hex);
            if (!string.IsNullOrEmpty(targetEncoding))
            {
                var enc = Encoding.GetEncoding(targetEncoding);
                return enc.GetString(bytes);
            }
            // Auto-detect: try common Chinese encodings
            foreach (var encName in new[] { "gbk", "gb2312", "gb18030" })
            {
                try
                {
                    var enc = Encoding.GetEncoding(encName);
                    var result = enc.GetString(bytes);
                    if (result.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                        return result;
                }
                catch { }
            }
            // Fallback: return hex string as-is
            return hex;
        }
        catch
        {
            return hex;
        }
    }

    /// <summary>
    /// Fix garbled Chinese text from Oracle US7ASCII databases.
    /// NLS_LANG=WE8ISO8859P1 ensures raw bytes pass through 1:1 as Latin-1 chars.
    /// We recover raw bytes via ISO-8859-1 and re-decode with the real encoding.
    /// </summary>
    internal static string ConvertEncoding(string input, string? sourceEncoding)
    {
        if (string.IsNullOrEmpty(input)) return input;
        try
        {
            var latin1 = Encoding.GetEncoding("iso-8859-1");
            var rawBytes = latin1.GetBytes(input);

            if (!string.IsNullOrEmpty(sourceEncoding))
            {
                var srcEnc = Encoding.GetEncoding(sourceEncoding);
                return srcEnc.GetString(rawBytes);
            }

            // Auto-detect: try common Chinese encodings, return first that produces Chinese
            foreach (var encName in new[] { "gbk", "gb2312", "gb18030" })
            {
                try
                {
                    var enc = Encoding.GetEncoding(encName);
                    var result = enc.GetString(rawBytes);
                    if (result.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                        return result;
                }
                catch { }
            }
            return input;
        }
        catch
        {
            return input;
        }
    }

    internal static string? DiagnoseEncoding(string input, ILogger logger)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var rawBytes = latin1.GetBytes(input);
        var hex = Convert.ToHexString(rawBytes.Take(20).ToArray());
        var hasHigh = rawBytes.Any(b => b > 127);
        logger.LogWarning("Encoding diagnosis: input={Input}, len={Len}, hasHighBytes={HasHigh}, first 20 hex={Hex}",
            input[..Math.Min(input.Length, 40)], input.Length, hasHigh, hex);

        // Try common Chinese encodings and log results
        foreach (var encName in new[] { "gb2312", "gbk", "gb18030", "big5", "utf-8" })
        {
            try
            {
                var enc = Encoding.GetEncoding(encName);
                var decoded = enc.GetString(rawBytes);
                var hasChinese = decoded.Any(c => c >= 0x4E00 && c <= 0x9FFF);
                logger.LogInformation("  Try {Enc}: hasChinese={HasCN}, sample={Sample}",
                    encName, hasChinese, decoded[..Math.Min(20, decoded.Length)]);
            }
            catch { }
        }
        return null;
    }
}

public class QueryResult
{
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public long ElapsedMs { get; set; }
}
