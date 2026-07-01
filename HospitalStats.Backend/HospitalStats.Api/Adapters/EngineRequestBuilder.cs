using HospitalStats.Api.Abstractions;
using HospitalStats.Api.Data;
using HospitalStats.Api.Models;
using HospitalStats.Api.Services;
using HospitalStats.QueryEngine;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Adapters;

/// <summary>
/// Adapts EF Core QueryConfig entities into a standalone QueryEngineRequest
/// that the engine can execute without any database or ASP.NET dependencies.
/// </summary>
public class EngineRequestBuilder
{
    private readonly AppDbContext _db;
    private readonly DataSourceService _dsService;
    private readonly SystemSettingsService _settingsService;
    private readonly ICurrentUserContext _userContext;

    public EngineRequestBuilder(AppDbContext db, DataSourceService dsService,
        SystemSettingsService settingsService, ICurrentUserContext userContext)
    {
        _db = db;
        _dsService = dsService;
        _settingsService = settingsService;
        _userContext = userContext;
    }

    public async Task<QueryEngineRequest> BuildAsync(int configId,
        Dictionary<string, string> filters, int page, int pageSize)
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
        if (!config.IsEnabled)
            throw new InvalidOperationException("该查询配置已禁用");

        var ds = config.MainTable.DataSource;
        var connStr = _dsService.Decrypt(ds.ConnectionString);
        var charSetOverride = ds.CharSetOverride;

        var queryTimeout = await _settingsService.GetIntAsync("QueryTimeoutSeconds", 120);
        var maxRowCount = await _settingsService.GetIntAsync("MaxRowCount", 50000);
        var historyLimit = await _settingsService.GetIntAsync("HistoryLimit", 50000);

        return new QueryEngineRequest
        {
            ConnectionString = connStr,
            CharSetOverride = charSetOverride,
            MainTable = MapTable(config.MainTable),
            Fields = config.Fields.OrderBy(f => f.SortOrder).Select(MapField).ToList(),
            Filters = config.Filters.OrderBy(f => f.SortOrder).Select(MapFilter).ToList(),
            Joins = config.Joins.OrderBy(j => j.SortOrder).Select(MapJoin).ToList(),
            RawSql = config.RawSql,
            GroupByColumn = config.GroupByColumn,
            SortColumn = config.SortColumn,
            SortDirection = config.SortDirection,
            Page = page,
            PageSize = pageSize,
            FilterValues = filters,
            ContextValues = _userContext.GetContextValues(),
            Options = new EngineOptions
            {
                QueryTimeoutSeconds = queryTimeout,
                MaxRowCount = maxRowCount,
                HistoryLimit = historyLimit
            }
        };
    }

    private static EngineTableDef MapTable(MetaTable? t)
    {
        if (t == null) return new EngineTableDef();
        return new EngineTableDef
        {
            TableName = t.TableName ?? "",
            SchemaName = t.SchemaName,
            Alias = string.IsNullOrEmpty(t.Alias) ? t.TableName : t.Alias,
            Columns = t.Columns?.Select(c => new EngineColumnDef
            {
                ColumnName = c.ColumnName ?? "",
                DataType = c.DataType
            }).ToList() ?? new List<EngineColumnDef>()
        };
    }

    private static EngineFieldDef MapField(QueryField f)
    {
        var col = f.MetaColumn;
        var ta = col?.MetaTable?.Alias ?? col?.MetaTable?.TableName ?? "T";
        return new EngineFieldDef
        {
            ColumnName = col?.ColumnName ?? "",
            Alias = !string.IsNullOrEmpty(f.Alias) ? f.Alias
                : !string.IsNullOrEmpty(col?.Alias) ? col.Alias
                : col?.ColumnName,
            AggregateFunc = f.AggregateFunc,
            SortOrder = f.SortOrder,
            TableAlias = ta,
            DataType = col?.DataType
        };
    }

    private static EngineFilterDef MapFilter(QueryFilter f)
    {
        var col = f.MetaColumn;
        var ta = col?.MetaTable?.Alias ?? col?.MetaTable?.TableName ?? "T";
        return new EngineFilterDef
        {
            Id = f.Id,
            ColumnName = col?.ColumnName ?? "",
            TableAlias = ta,
            DataType = col?.DataType ?? "",
            Operator = f.Operator,
            DefaultValue = f.DefaultValue,
            IsContextFilter = f.IsContextFilter,
            ContextKey = f.ContextKey,
            SortOrder = f.SortOrder
        };
    }

    private static EngineJoinDef MapJoin(QueryJoin j)
    {
        var leftCol = j.LeftMetaColumn;
        var rightCol = j.RightMetaColumn;
        return new EngineJoinDef
        {
            JoinType = j.JoinType ?? "LEFT",
            JoinTable = MapTable(j.JoinTable),
            LeftColumnName = leftCol?.ColumnName ?? "",
            LeftTableAlias = leftCol?.MetaTable?.Alias ?? leftCol?.MetaTable?.TableName ?? "T",
            RightColumnName = rightCol?.ColumnName ?? "",
            RightTableAlias = rightCol?.MetaTable?.Alias ?? j.JoinTable?.Alias ?? j.JoinTable?.TableName ?? "T",
            LeftDateTrunc = j.LeftDateTrunc,
            SortOrder = j.SortOrder
        };
    }

    public async Task<DistinctValuesRequest> BuildDistinctValuesAsync(int configId, int filterId)
    {
        var config = await _db.QueryConfigs
            .Include(q => q.MainTable).ThenInclude(t => t!.DataSource)
            .Include(q => q.Filters).ThenInclude(f => f.MetaColumn).ThenInclude(c => c!.MetaTable)
            .FirstOrDefaultAsync(q => q.Id == configId);

        if (config?.MainTable?.DataSource == null)
            throw new ArgumentException("查询配置无效");
        if (!config.IsEnabled)
            throw new InvalidOperationException("该查询配置已禁用");

        var filter = config.Filters.FirstOrDefault(f => f.Id == filterId);
        var col = filter?.MetaColumn;
        if (col == null) return new DistinctValuesRequest();

        var ds = config.MainTable.DataSource;
        var tableAlias = col.MetaTable?.Alias ?? col.MetaTable?.TableName ?? "T";

        return new DistinctValuesRequest
        {
            ConnectionString = _dsService.Decrypt(ds.ConnectionString),
            CharSetOverride = ds.CharSetOverride,
            SchemaName = col.MetaTable?.SchemaName ?? "HOSPITAL",
            TableName = col.MetaTable?.TableName ?? "",
            TableAlias = tableAlias,
            ColumnName = col.ColumnName ?? "",
            DataType = col.DataType
        };
    }
}
