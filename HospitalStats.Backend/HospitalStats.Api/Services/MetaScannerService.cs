using System.Data;
using Dapper;
using HospitalStats.Api.Data;
using HospitalStats.Api.Models;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.Api.Services;

public class MetaScannerService
{
    private readonly AppDbContext _db;
    private readonly DataSourceService _dsService;
    private readonly ILogger<MetaScannerService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public MetaScannerService(AppDbContext db, DataSourceService dsService,
        ILogger<MetaScannerService> logger, IServiceScopeFactory scopeFactory)
    {
        _db = db;
        _dsService = dsService;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    public async Task<ScanResult> ScanAsync(int dataSourceId, string? schemaFilter = null)
    {
        var ds = await _db.DataSources.FindAsync(dataSourceId);
        if (ds == null) throw new ArgumentException("数据源不存在");

        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var charSetOverride = ds.CharSetOverride;
        var schema = (schemaFilter ?? ds.Schema ?? GetDefaultSchema(connStr)).ToUpperInvariant();

        var result = new ScanResult { Schema = schema };

        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        // detect charset
        var charSetInfo = await DetectCharSetAsync(conn);
        ds.CharSetInfo = charSetInfo;
        await _db.SaveChangesAsync();

        // scan tables
        var tables = await ScanTablesAsync(conn, schema, charSetOverride);
        // scan views
        var views = await ScanViewsAsync(conn, schema, charSetOverride);

        result.TablesFound = tables.Count;
        result.ViewsFound = views.Count;

        var allItems = tables.Concat(views).ToList();

        // scan columns and persist
        var created = 0;
        var updated = 0;

        foreach (var item in allItems)
        {
            var existing = await _db.MetaTables
                .FirstOrDefaultAsync(t => t.DataSourceId == dataSourceId
                    && t.SchemaName == item.SchemaName
                    && t.TableName == item.TableName);

            if (existing != null)
            {
                existing.IsView = item.IsView;
                existing.UpdatedAt = DateTime.UtcNow;
                updated++;
                await ScanColumnsAsync(conn, existing, charSetInfo, charSetOverride);
            }
            else
            {
                item.DataSourceId = dataSourceId;
                _db.MetaTables.Add(item);
                await _db.SaveChangesAsync();
                created++;
                await ScanColumnsAsync(conn, item, charSetInfo, charSetOverride);
            }
        }

        await _db.SaveChangesAsync();

        result.Created = created;
        result.Updated = updated;

        // probe Chinese data for each VARCHAR2 column if US7ASCII
        // run in background — can take minutes for large schemas (one query per column),
        // and this only adds optional comments, never blocks scanning
        var isUs7Ascii = charSetInfo.Contains("US7ASCII", StringComparison.OrdinalIgnoreCase);
        if (isUs7Ascii)
        {
            var dsIdCapture = dataSourceId;
            var charSetCapture = charSetInfo;
            var connStrCapture = connStr;
            _logger.LogInformation("US7ASCII detected, probing Chinese data encoding in background");
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    using var probeConn = new OracleConnection(connStrCapture);
                    await probeConn.OpenAsync();
                    await ProbeChineseEncodingAsync(probeConn, db, dsIdCapture, charSetCapture);
                    _logger.LogInformation("Chinese encoding probe completed for datasource {DsId}", dsIdCapture);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Chinese encoding probe failed for datasource {DsId}", dsIdCapture);
                }
            });
        }

        return result;
    }

    private async Task<List<MetaTable>> ScanTablesAsync(OracleConnection conn, string schema, string? charSetOverride)
    {
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride);
        var commentExpr = useHexEncoding
            ? "RAWTOHEX(UTL_RAW.CAST_TO_RAW(COMMENTS)) as COMMENTS"
            : "COMMENTS";
        var sql = $"SELECT OWNER, TABLE_NAME, {commentExpr} FROM ALL_TAB_COMMENTS " +
                  "WHERE OWNER = :p_schema AND TABLE_TYPE = 'TABLE' ORDER BY TABLE_NAME";
        var tables = await conn.QueryAsync<OracleTable>(sql, new { p_schema = schema });

        return tables.Select(t => new MetaTable
        {
            TableName = t.TABLE_NAME,
            SchemaName = t.OWNER,
            Description = useHexEncoding
                ? QueryExecutionService.DecodeHexString(t.COMMENTS ?? "", charSetOverride)
                : QueryExecutionService.ConvertEncoding(t.COMMENTS ?? "", charSetOverride),
            IsView = false
        }).ToList();
    }

    private async Task<List<MetaTable>> ScanViewsAsync(OracleConnection conn, string schema, string? charSetOverride)
    {
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride);
        var commentExpr = useHexEncoding
            ? "RAWTOHEX(UTL_RAW.CAST_TO_RAW(COMMENTS)) as COMMENTS"
            : "COMMENTS";
        var views = await conn.QueryAsync<OracleTable>(
            $@"SELECT OWNER, TABLE_NAME, {commentExpr}
              FROM ALL_TAB_COMMENTS
              WHERE OWNER = :p_schema AND TABLE_TYPE = 'VIEW'
              ORDER BY TABLE_NAME",
            new { p_schema = schema });

        return views.Select(t => new MetaTable
        {
            TableName = t.TABLE_NAME,
            SchemaName = t.OWNER,
            Description = useHexEncoding
                ? QueryExecutionService.DecodeHexString(t.COMMENTS ?? "", charSetOverride)
                : QueryExecutionService.ConvertEncoding(t.COMMENTS ?? "", charSetOverride),
            IsView = true
        }).ToList();
    }

    private async Task ScanColumnsAsync(OracleConnection conn, MetaTable table, string charSetInfo, string? charSetOverride)
    {
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride);
        var commentExpr = useHexEncoding
            ? "RAWTOHEX(UTL_RAW.CAST_TO_RAW(cc.COMMENTS)) as COMMENTS"
            : "cc.COMMENTS";
        var columns = await conn.QueryAsync<OracleColumn>(
            $@"SELECT c.COLUMN_NAME, c.DATA_TYPE, c.DATA_LENGTH,
                     c.DATA_PRECISION, c.DATA_SCALE, c.NULLABLE,
                     {commentExpr}, c.COLUMN_ID
              FROM ALL_TAB_COLUMNS c
              LEFT JOIN ALL_COL_COMMENTS cc
                ON cc.OWNER = c.OWNER
                AND cc.TABLE_NAME = c.TABLE_NAME
                AND cc.COLUMN_NAME = c.COLUMN_NAME
              WHERE c.OWNER = :p_schema AND c.TABLE_NAME = :p_table
              ORDER BY c.COLUMN_ID",
            new { p_schema = table.SchemaName, p_table = table.TableName });

        foreach (var col in columns)
        {
            var comments = useHexEncoding
                ? QueryExecutionService.DecodeHexString(col.COMMENTS ?? "", charSetOverride)
                : QueryExecutionService.ConvertEncoding(col.COMMENTS ?? "", charSetOverride);
            var existing = await _db.MetaColumns
                .FirstOrDefaultAsync(c => c.MetaTableId == table.Id
                    && c.ColumnName == col.COLUMN_NAME);

            if (existing != null)
            {
                // keep user-edited fields, update system fields only
                existing.DataType = col.DATA_TYPE;
                existing.DataLength = col.DATA_LENGTH;
                existing.DataPrecision = col.DATA_PRECISION;
                existing.DataScale = col.DATA_SCALE;
                existing.Nullable = col.NULLABLE == "Y";
                existing.Comment = comments;
                if (!string.IsNullOrEmpty(comments))
                {
                    existing.Alias = comments;
                }
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                var newCol = new MetaColumn
                {
                    MetaTableId = table.Id,
                    ColumnName = col.COLUMN_NAME,
                    DataType = col.DATA_TYPE,
                    DataLength = col.DATA_LENGTH,
                    DataPrecision = col.DATA_PRECISION,
                    DataScale = col.DATA_SCALE,
                    Nullable = col.NULLABLE == "Y",
                    Comment = comments,
                    Alias = comments,
                    SortOrder = col.COLUMN_ID
                };
                _db.MetaColumns.Add(newCol);
            }
        }

        await _db.SaveChangesAsync();
    }

    private async Task<string> DetectCharSetAsync(OracleConnection conn)
    {
        var nls = await conn.QueryAsync<(string P, string V)>(
            "SELECT PARAMETER, VALUE FROM NLS_DATABASE_PARAMETERS " +
            "WHERE PARAMETER IN ('NLS_CHARACTERSET','NLS_NCHAR_CHARACTERSET')");
        return string.Join("; ", nls.Select(x => $"{x.P}={x.V}"));
    }

    private async Task ProbeChineseEncodingAsync(OracleConnection conn,
        AppDbContext db, int dataSourceId, string charSetInfo)
    {
        var tables = await db.MetaTables
            .Where(t => t.DataSourceId == dataSourceId)
            .ToListAsync();

        var foundChinese = false;

        foreach (var table in tables)
        {
            var varcharCols = await db.MetaColumns
                .Where(c => c.MetaTableId == table.Id
                    && (c.DataType == "VARCHAR2" || c.DataType == "CHAR"))
                .ToListAsync();

            if (varcharCols.Count == 0) continue;

            foreach (var col in varcharCols)
            {
                try
                {
                    var sample = await conn.QueryFirstOrDefaultAsync<string>(
                        $"SELECT \"{col.ColumnName}\" FROM \"{table.SchemaName}\".\"{table.TableName}\" " +
                        $"WHERE \"{col.ColumnName}\" IS NOT NULL AND ROWNUM = 1");

                    if (!string.IsNullOrEmpty(sample) && ContainsHighBytes(sample))
                    {
                        foundChinese = true;
                        col.Comment = (col.Comment ?? "") + " [含中文字节]";
                        await db.SaveChangesAsync();
                    }
                }
                catch
                {
                    // skip problematic columns
                }
            }
        }

        // Auto-set CharSetOverride to GBK for US7ASCII databases with Chinese content
        if (foundChinese)
        {
            var ds = await db.DataSources.FindAsync(dataSourceId);
            if (ds != null && string.IsNullOrEmpty(ds.CharSetOverride))
            {
                ds.CharSetOverride = "GBK";
                await db.SaveChangesAsync();
                _logger.LogInformation("Auto-set CharSetOverride=GBK for datasource {DsId}", dataSourceId);
            }
        }
    }

    private static bool ContainsHighBytes(string s)
    {
        foreach (var c in s)
        {
            if (c > 127) return true;
        }
        return false;
    }

    private static string GetDefaultSchema(string connStr)
    {
        foreach (var part in connStr.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("User", StringComparison.OrdinalIgnoreCase))
            {
                var eqIdx = trimmed.IndexOf('=');
                if (eqIdx >= 0)
                    return trimmed.Substring(eqIdx + 1).Trim().ToUpperInvariant();
            }
        }
        return string.Empty;
    }

    // Oracle query result types
    private class OracleTable
    {
        public string OWNER { get; set; } = string.Empty;
        public string TABLE_NAME { get; set; } = string.Empty;
        public string? COMMENTS { get; set; }
    }

    private class OracleColumn
    {
        public string COLUMN_NAME { get; set; } = string.Empty;
        public string DATA_TYPE { get; set; } = string.Empty;
        public int? DATA_LENGTH { get; set; }
        public int? DATA_PRECISION { get; set; }
        public int? DATA_SCALE { get; set; }
        public string NULLABLE { get; set; } = string.Empty;
        public string? COMMENTS { get; set; }
        public int? COLUMN_ID { get; set; }
    }
}

public class ScanResult
{
    public string Schema { get; set; } = string.Empty;
    public int TablesFound { get; set; }
    public int ViewsFound { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
}
