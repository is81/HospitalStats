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
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.Api.Services;

public class QueryExecutionService
{
    private readonly AppDbContext _db;
    private readonly DataSourceService _dsService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<QueryExecutionService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public QueryExecutionService(AppDbContext db, DataSourceService dsService,
        IMemoryCache cache, ILogger<QueryExecutionService> logger,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _dsService = dsService;
        _cache = cache;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<QueryResult> ExecuteAsync(int configId, Dictionary<string, string> filters,
        int page = 1, int? pageSize = null)
    {
        var config = await _db.QueryConfigs
            .Include(q => q.MainTable).ThenInclude(t => t!.DataSource)
            .Include(q => q.Fields).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Filters).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Joins).ThenInclude(j => j.JoinTable)
            .Include(q => q.Joins).ThenInclude(j => j.LeftMetaColumn).ThenInclude(c => c!.MetaTable)
            .Include(q => q.Joins).ThenInclude(j => j.RightMetaColumn).ThenInclude(c => c!.MetaTable)
            .FirstOrDefaultAsync(q => q.Id == configId);

        if (config?.MainTable?.DataSource == null)
            throw new ArgumentException("查询配置无效");

        var ds = config.MainTable.DataSource;
        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var charSetOverride = ds.CharSetOverride;

        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        // resolve context filter values from JWT claims
        var contextValues = ResolveContextValues();

        // build SQL
        var (countSql, countParams) = BuildCountSql(config, filters, contextValues);
        var (dataSql, dataParams) = BuildDataSql(config, page, pageSize ?? config.PageSize ?? 50, filters, contextValues);
        var allParams = MergeParams(countParams, dataParams, filters);

        _logger.LogInformation("Count SQL: {Sql}", countSql);
        _logger.LogInformation("Data SQL: {Sql}", dataSql);


        // caching check (include context values in cache key)
        var cacheKey = BuildCacheKey(configId, filters, page, pageSize, contextValues);
        if (_cache.TryGetValue(cacheKey, out QueryResult? cached))
            return cached!;

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // execute count (only pass count params to avoid Oracle unbound param errors)
        var countDp = new DynamicParameters();
        foreach (var (k, v) in countParams) countDp.Add(k, v);
        foreach (var (k, v) in filters)
        {
            var paramName = $"p_f_{k}";
            if (countSql.Contains($":{paramName}"))
                countDp.Add(paramName, v);
        }

        int total;
        try
        {
            total = await conn.ExecuteScalarAsync<int>(countSql, countDp);
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
            rows = await conn.QueryAsync(dataSql, allParams);
        }
        catch (OracleException ex)
        {
            throw new InvalidOperationException(
                $"Data query failed: {ex.Message}. SQL: {dataSql}", ex);
        }

        sw.Stop();

        var columns = new List<string>();
        var colDisplayMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in config.Fields.OrderBy(f => f.SortOrder))
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var sqlAlias = col.ColumnName ?? "";
            var displayName = !string.IsNullOrEmpty(field.Alias) ? field.Alias
                : !string.IsNullOrEmpty(col.Alias) ? col.Alias
                : col.ColumnName ?? "";
            colDisplayMap[sqlAlias] = displayName;
            columns.Add(displayName);
        }

        // convert to list of dictionaries
        var resultRows = new List<Dictionary<string, object?>>();
        foreach (var row in rows)
        {
            var dict = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in (IDictionary<string, object?>)row)
            {
                var val = prop.Value;
                // US7ASCII encoding fix: if value is string and charSet is overridden
                if (val is string strVal && charSetOverride != null)
                {
                    val = ConvertEncoding(strVal, charSetOverride);
                }
                // Remap from SQL column name to display name
                var key = colDisplayMap.TryGetValue(prop.Key, out var display) ? display : prop.Key;
                dict[key] = val;
            }
            resultRows.Add(dict);
        }

        // Diagnose encoding on first row if garbled text detected
        if (resultRows.Count > 0)
        {
            var firstRow = resultRows[0];
            foreach (var (key, val) in firstRow)
            {
                if (val is string s && s.Any(c => c > 127 && c < 256))
                {
                    DiagnoseEncoding(s, _logger);
                    break;
                }
            }
        }

        // Fallback: if no fields configured (RawSql), extract columns from result keys
        if (columns.Count == 0 && resultRows.Count > 0)
        {
            columns = resultRows[0].Keys.Where(k => !k.Equals("RN", StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (columns.Count == 0 && resultRows.Count > 0)
        {
            columns = resultRows[0].Keys.Where(k => !k.Equals("RN", StringComparison.OrdinalIgnoreCase)).ToList();
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
        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var tableAlias = GetTableAlias(col.MetaTable);
        var schema = col.MetaTable?.SchemaName ?? "HOSPITAL";
        var table = col.MetaTable?.TableName ?? "";

        var sql = $"SELECT DISTINCT \"{tableAlias}\".\"{col.ColumnName}\" " +
                  $"FROM \"{schema}\".\"{table}\" \"{tableAlias}\" " +
                  $"ORDER BY \"{tableAlias}\".\"{col.ColumnName}\"";

        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();
        var values = await conn.QueryAsync<string>(sql);
        return values.Where(v => v != null).ToList();
    }

    // ===== SQL Builders =====

    internal static string SanitizeRawSql(string? rawSql)
    {
        if (string.IsNullOrEmpty(rawSql)) return "";
        return rawSql.TrimEnd(';').TrimEnd();
    }

    /// <summary>Returns true when the user has touched at least one filter on the preview
    /// page (key present in the dictionary, regardless of value). When this is true we
    /// fall back to config-based SQL so user filter actions actually take effect.</summary>
    internal static bool HasUserFilterInput(QueryConfig config, Dictionary<string, string> userFilters)
    {
        foreach (var filter in config.Filters)
        {
            if (userFilters.ContainsKey(filter.Id.ToString()))
                return true;
        }
        return false;
    }

    internal (string Sql, Dictionary<string, object?> Params) BuildCountSql(
        QueryConfig config, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues)
    {
        var rawSql = SanitizeRawSql(config.RawSql);
        if (!string.IsNullOrEmpty(rawSql) && !HasUserFilterInput(config, userFilters))
        {
            // still need context filters even in raw SQL mode
            var ctxWhere = BuildWhereClause(config, new Dictionary<string, string>(), contextValues);
            if (!string.IsNullOrEmpty(ctxWhere))
            {
                return ($"SELECT COUNT(*) FROM ({rawSql}) \"_cnt\" WHERE {ctxWhere}",
                    new Dictionary<string, object?>());
            }
            return ($"SELECT COUNT(*) FROM ({rawSql}) \"_cnt\"",
                new Dictionary<string, object?>());
        }

        var sb = new StringBuilder();
        sb.Append("SELECT COUNT(*) FROM ");
        AppendFromClause(sb, config);
        AppendWhereClause(sb, config, userFilters, contextValues);
        return (sb.ToString(), new Dictionary<string, object?>());
    }

    internal (string Sql, Dictionary<string, object?> Params) BuildDataSql(
        QueryConfig config, int page, int pageSize, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues)
    {
        string innerSql;
        var rawSql = SanitizeRawSql(config.RawSql);
        if (!string.IsNullOrEmpty(rawSql) && !HasUserFilterInput(config, userFilters))
        {
            innerSql = rawSql;
            // apply context filters by appending WHERE to the raw SQL
            var ctxWhere = BuildWhereClause(config, new Dictionary<string, string>(), contextValues);
            if (!string.IsNullOrEmpty(ctxWhere))
            {
                innerSql = $"SELECT * FROM ({rawSql}) \"_ctx\" WHERE {ctxWhere}";
            }
        }
        else
        {
            var selectClause = BuildSelectClause(config);
            var fromClause = BuildFromClause(config);
            var whereClause = BuildWhereClause(config, userFilters, contextValues);
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
        if (!string.IsNullOrEmpty(rawSql) && !HasUserFilterInput(config, userFilters))
        {
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

    internal static string BuildSelectClause(QueryConfig config)
    {
        var parts = new List<string>();
        foreach (var field in config.Fields.OrderBy(f => f.SortOrder))
        {
            var col = field.MetaColumn;
            if (col == null) continue;
            var colExpr = QualifyColumn(col);

            if (!string.IsNullOrEmpty(field.AggregateFunc))
                colExpr = $"{field.AggregateFunc}({colExpr})";

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

                var leftTableAlias = leftCol?.MetaTable?.Alias;
                // If left column is from a join table, use its assigned alias
                if (leftTableAlias != null && leftCol?.MetaTable != null)
                {
                    var leftTableId = leftCol.MetaTable.Id;
                    if (joinAliasMap.TryGetValue(leftTableId, out var mapped))
                        leftTableAlias = mapped;
                }
                var leftAlias = leftTableAlias ?? mainAlias;
                var leftFull = $"\"{leftAlias}\".\"{leftCol?.ColumnName}\"";
                var rightFull = $"\"{alias}\".\"{rightCol?.ColumnName}\"";

                onParts.Add($"{leftFull} = {rightFull}");
            }

            var joinType = first.JoinType;
            from += $"\n  {joinType} JOIN \"{first.JoinTable?.SchemaName}\".\"{first.JoinTable?.TableName}\" \"{alias}\"";
            from += $" ON {string.Join(" AND ", onParts)}";
        }

        return from;
    }

    internal string BuildWhereClause(QueryConfig config, Dictionary<string, string> userFilters,
        Dictionary<string, string> contextValues)
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

            var colExpr = QualifyColumn(col);
            var paramName = $"p_f_{filter.Id}";
            var isDate = "DATE".Equals(col.DataType, StringComparison.OrdinalIgnoreCase);
            parts.Add(OperatorToSql(colExpr, filter.Operator, paramName, isDate));

            // store resolved value into userFilters so MergeParams picks it up
            // context filters always overwrite user input
            if (filter.IsContextFilter || !userFilters.ContainsKey(filter.Id.ToString()))
                userFilters[filter.Id.ToString()] = effectiveValue;
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

                var leftTableAlias = leftCol?.MetaTable?.Alias;
                if (leftTableAlias != null && leftCol?.MetaTable != null)
                {
                    var leftTableId = leftCol.MetaTable.Id;
                    if (joinAliasMap.TryGetValue(leftTableId, out var mapped))
                        leftTableAlias = mapped;
                }
                var leftAlias = leftTableAlias ?? mainAlias;
                var leftFull = $"\"{leftAlias}\".\"{leftCol?.ColumnName}\"";
                var rightFull = $"\"{alias}\".\"{rightCol?.ColumnName}\"";

                onParts.Add($"{leftFull} = {rightFull}");
            }

            var joinType = first.JoinType;
            sb.Append($"\n  {joinType} JOIN \"{first.JoinTable?.SchemaName}\".\"{first.JoinTable?.TableName}\" \"{alias}\"");
            sb.Append($" ON {string.Join(" AND ", onParts)}");
        }
    }

    internal void AppendWhereClause(StringBuilder sb, QueryConfig config,
        Dictionary<string, string> userFilters, Dictionary<string, string> contextValues)
    {
        var where = BuildWhereClause(config, userFilters, contextValues);
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
        Dictionary<string, string> userFilters)
    {
        var dp = new DynamicParameters();
        foreach (var (k, v) in countParams) dp.Add(k, v);
        foreach (var (k, v) in dataParams) dp.Add(k, v);

        foreach (var (k, v) in userFilters)
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
        int page, int? pageSize, Dictionary<string, string> contextValues)
    {
        var raw = $"q_{configId}_p{page}_s{pageSize}_";
        foreach (var (k, v) in filters.OrderBy(x => x.Key))
            raw += $"{k}={v}_";
        foreach (var (k, v) in contextValues.OrderBy(x => x.Key))
            raw += $"ctx_{k}={v}_";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return $"query_{Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    /// <summary>
    /// Fix garbled Chinese text from Oracle US7ASCII databases.
    /// Oracle stores multi-byte Chinese as individual bytes; when retrieved via
    /// ODP.NET without NLS_LANG, each byte is interpreted as a Latin-1 character.
    /// We recover raw bytes via ISO-8859-1 and re-decode with the real encoding.
    /// </summary>
    internal static string ConvertEncoding(string input, string sourceEncoding)
    {
        if (string.IsNullOrEmpty(input)) return input;
        try
        {
            var latin1 = Encoding.GetEncoding("iso-8859-1");
            var rawBytes = latin1.GetBytes(input);
            var srcEnc = Encoding.GetEncoding(sourceEncoding);
            var result = srcEnc.GetString(rawBytes);
            // Only return the result if it actually changed and looks valid
            if (result != input && result.Any(c => c > 127))
                return result;
            return input;
        }
        catch
        {
            return input;
        }
    }

    internal static string? DiagnoseEncoding(string input, ILogger logger)
    {
        if (string.IsNullOrEmpty(input) || input.All(c => c < 128))
            return null;

        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var rawBytes = latin1.GetBytes(input);
        var hex = Convert.ToHexString(rawBytes.Take(20).ToArray());
        logger.LogWarning("Encoding diagnosis: input len={Len}, first 20 bytes hex={Hex}", input.Length, hex);

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
