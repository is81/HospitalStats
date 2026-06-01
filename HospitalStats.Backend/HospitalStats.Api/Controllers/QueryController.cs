using HospitalStats.Api.Data;
using HospitalStats.Api.DTOs;
using HospitalStats.Api.Models;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class QueryController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly SqlParsingService _sqlParser;
    private readonly ILogger<QueryController> _logger;

    public QueryController(AppDbContext db, SqlParsingService sqlParser, ILogger<QueryController> logger)
    {
        _db = db;
        _sqlParser = sqlParser;
        _logger = logger;
    }

    // ===== Menu Tree =====

    [HttpGet("menus")]
    public async Task<ActionResult<List<MenuDto>>> GetMenus()
    {
        var all = await _db.Menus
            .Include(m => m.QueryConfig)
            .OrderBy(m => m.SortOrder)
            .ToListAsync();

        // filter by user role permissions
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userIdClaim != null && int.TryParse(userIdClaim, out var userId))
        {
            var roleIds = await _db.UserRoles
                .Where(ur => ur.UserId == userId)
                .Select(ur => ur.RoleId)
                .ToListAsync();

            if (roleIds.Count == 0)
            {
                // no roles → no menu access
                return new List<MenuDto>();
            }

            var allowedMenuIds = await _db.RoleMenus
                .Where(rm => roleIds.Contains(rm.RoleId))
                .Select(rm => rm.MenuId)
                .Distinct()
                .ToListAsync();

            // if roles have menu assignments, filter; otherwise allow all (admin)
            if (allowedMenuIds.Count > 0)
            {
                var allowedSet = new HashSet<int>(allowedMenuIds);
                // also include ancestor menus for tree structure
                foreach (var menu in all)
                {
                    if (allowedSet.Contains(menu.Id))
                    {
                        var parent = menu.ParentId;
                        while (parent != null)
                        {
                            allowedSet.Add(parent.Value);
                            var p = all.FirstOrDefault(m => m.Id == parent.Value);
                            parent = p?.ParentId;
                        }
                    }
                }
                all = all.Where(m => allowedSet.Contains(m.Id)).ToList();
            }
        }

        var dtos = all.Select(ToMenuDto).ToList();
        return BuildTree(dtos, null);
    }

    [HttpGet("menus/{id}")]
    public async Task<ActionResult<MenuDto>> GetMenu(int id)
    {
        var m = await _db.Menus.Include(x => x.QueryConfig).FirstOrDefaultAsync(x => x.Id == id);
        if (m == null) return NotFound();
        return ToMenuDto(m);
    }

    [HttpPost("menus")]
    public async Task<ActionResult<MenuDto>> CreateMenu([FromBody] MenuSaveRequest req)
    {
        var entity = new Menu
        {
            ParentId = req.ParentId,
            Name = req.Name,
            Icon = req.Icon,
            SortOrder = req.SortOrder,
            QueryConfigId = req.QueryConfigId,
            IsEnabled = req.IsEnabled
        };
        _db.Menus.Add(entity);
        await _db.SaveChangesAsync();

        // Auto-assign new menu to all existing roles so admins don't need to
        // manually update role permissions for each new menu.
        var roleIds = await _db.Roles.Select(r => r.Id).ToListAsync();
        foreach (var roleId in roleIds)
        {
            _db.RoleMenus.Add(new RoleMenu { RoleId = roleId, MenuId = entity.Id });
        }
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetMenu), new { id = entity.Id }, ToMenuDto(entity));
    }

    [HttpPut("menus/{id}")]
    public async Task<IActionResult> UpdateMenu(int id, [FromBody] MenuSaveRequest req)
    {
        var entity = await _db.Menus.FindAsync(id);
        if (entity == null) return NotFound();
        entity.ParentId = req.ParentId;
        entity.Name = req.Name;
        entity.Icon = req.Icon;
        entity.SortOrder = req.SortOrder;
        entity.QueryConfigId = req.QueryConfigId;
        entity.IsEnabled = req.IsEnabled;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("menus/{id}")]
    public async Task<IActionResult> DeleteMenu(int id)
    {
        var entity = await _db.Menus.FindAsync(id);
        if (entity == null) return NotFound();

        var children = await _db.Menus.Where(m => m.ParentId == id).ToListAsync();
        foreach (var c in children) c.ParentId = entity.ParentId;

        _db.Menus.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ===== QueryConfig CRUD =====

    [HttpGet("configs")]
    public async Task<ActionResult<List<QueryConfigDto>>> GetConfigs([FromQuery] int? dsId)
    {
        var query = _db.QueryConfigs.Include(q => q.MainTable).AsQueryable();
        if (dsId.HasValue)
            query = query.Where(q => q.MainTable != null && q.MainTable.DataSourceId == dsId.Value);

        var configs = await query.OrderBy(q => q.Name).ToListAsync();
        return configs.Select(c => new QueryConfigDto
        {
            Id = c.Id,
            Name = c.Name,
            MainTableId = c.MainTableId,
            MainTableName = c.MainTable?.Alias ?? c.MainTable?.TableName,
            DisplayType = c.DisplayType,
            UpdatedAt = c.UpdatedAt,
            IsEnabled = c.IsEnabled
        }).ToList();
    }

    [HttpGet("configs/{id}")]
    public async Task<ActionResult<QueryConfigDto>> GetConfig(int id)
    {
        var c = await _db.QueryConfigs
            .Include(q => q.MainTable)
            .Include(q => q.Fields).ThenInclude(f => f.MetaColumn).ThenInclude(c2 => c2!.MetaTable)
            .Include(q => q.Filters).ThenInclude(f => f.MetaColumn)
            .Include(q => q.Joins).ThenInclude(j => j.JoinTable)
            .Include(q => q.Joins).ThenInclude(j => j.LeftMetaColumn)
            .Include(q => q.Joins).ThenInclude(j => j.RightMetaColumn)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (c == null) return NotFound();
        return ToConfigDto(c);
    }

    [HttpPost("configs")]
    public async Task<ActionResult<QueryConfigDto>> CreateConfig([FromBody] QueryConfigSaveRequest req)
    {
        var entity = new QueryConfig
        {
            Name = req.Name,
            MainTableId = req.MainTableId,
            DisplayType = req.DisplayType,
            AggregateType = req.AggregateType,
            AggregateColumn = req.AggregateColumn,
            GroupByColumn = req.GroupByColumn,
            SortColumn = req.SortColumn,
            SortDirection = req.SortDirection,
            PageSize = req.PageSize,
            IsEnabled = req.IsEnabled,
            RawSql = req.RawSql,
            OriginalSql = req.OriginalSql
        };

        SaveChildren(entity, req);
        _db.QueryConfigs.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetConfig), new { id = entity.Id },
            ToConfigDto(entity));
    }

    [HttpPut("configs/{id}")]
    public async Task<ActionResult<QueryConfigDto>> UpdateConfig(int id, [FromBody] QueryConfigSaveRequest req)
    {
        var entity = await _db.QueryConfigs
            .Include(q => q.Fields)
            .Include(q => q.Filters)
            .Include(q => q.Joins)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (entity == null) return NotFound();

        entity.Name = req.Name;
        entity.MainTableId = req.MainTableId;
        entity.DisplayType = req.DisplayType;
        entity.AggregateType = req.AggregateType;
        entity.AggregateColumn = req.AggregateColumn;
        entity.GroupByColumn = req.GroupByColumn;
        entity.SortColumn = req.SortColumn;
        entity.SortDirection = req.SortDirection;
        entity.PageSize = req.PageSize;
        entity.IsEnabled = req.IsEnabled;
        entity.RawSql = req.RawSql;
        entity.OriginalSql = req.OriginalSql;
        entity.UpdatedAt = DateTime.UtcNow;

        _db.QueryFields.RemoveRange(entity.Fields);
        _db.QueryFilters.RemoveRange(entity.Filters);
        _db.QueryJoins.RemoveRange(entity.Joins);

        SaveChildren(entity, req);
        await _db.SaveChangesAsync();

        return ToConfigDto(entity);
    }

    [HttpDelete("configs/{id}")]
    public async Task<IActionResult> DeleteConfig(int id)
    {
        var entity = await _db.QueryConfigs.FindAsync(id);
        if (entity == null) return NotFound();

        // clear references from menus
        var menus = await _db.Menus.Where(m => m.QueryConfigId == id).ToListAsync();
        foreach (var m in menus) m.QueryConfigId = null;

        _db.QueryConfigs.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ===== SQL Import =====

    [HttpPost("configs/parse-sql")]
    public async Task<ActionResult<SqlParseResponse>> ParseSql([FromBody] SqlParseRequest request)
    {
        try
        {
            var result = await _sqlParser.ParseAsync(request.Sql);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "SQL parse argument error");
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SQL parse failed");
            return BadRequest(new { message = $"SQL 解析失败: {ex.Message}" });
        }
    }

    // ===== helpers =====

    private void SaveChildren(QueryConfig entity, QueryConfigSaveRequest req)
    {
        entity.Fields = req.Fields.Select(f => new QueryField
        {
            MetaColumnId = f.MetaColumnId,
            Alias = f.Alias,
            SortOrder = f.SortOrder,
            AggregateFunc = f.AggregateFunc
        }).ToList();

        entity.Filters = req.Filters.Select(f => new QueryFilter
        {
            MetaColumnId = f.MetaColumnId,
            Operator = f.Operator,
            DefaultValue = f.DefaultValue,
            IsRequired = f.IsRequired,
            ControlType = f.ControlType,
            Label = f.Label,
            SortOrder = f.SortOrder,
            IsContextFilter = f.IsContextFilter,
            ContextKey = f.ContextKey
        }).ToList();

        entity.Joins = req.Joins.Select(j => new QueryJoin
        {
            JoinTableId = j.JoinTableId,
            JoinType = j.JoinType,
            LeftMetaColumnId = j.LeftMetaColumnId,
            RightMetaColumnId = j.RightMetaColumnId,
            SortOrder = j.SortOrder,
            LeftDateTrunc = j.LeftDateTrunc
        }).ToList();
    }

    private static MenuDto ToMenuDto(Menu m) => new()
    {
        Id = m.Id,
        ParentId = m.ParentId,
        Name = m.Name,
        Icon = m.Icon,
        SortOrder = m.SortOrder,
        QueryConfigId = m.QueryConfigId,
        QueryConfigName = m.QueryConfig?.Name,
        IsEnabled = m.IsEnabled
    };

    private static List<MenuDto> BuildTree(List<MenuDto> all, int? parentId)
    {
        return all.Where(m => m.ParentId == parentId)
            .Select(m =>
            {
                m.Children = BuildTree(all, m.Id);
                return m;
            }).ToList();
    }

    private static QueryConfigDto ToConfigDto(QueryConfig c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        MainTableId = c.MainTableId,
        MainTableName = c.MainTable?.Alias ?? c.MainTable?.TableName,
        DisplayType = c.DisplayType,
        AggregateType = c.AggregateType,
        AggregateColumn = c.AggregateColumn,
        GroupByColumn = c.GroupByColumn,
        SortColumn = c.SortColumn,
        SortDirection = c.SortDirection,
        PageSize = c.PageSize,
        IsEnabled = c.IsEnabled,
        UpdatedAt = c.UpdatedAt,
        RawSql = c.RawSql,
        OriginalSql = c.OriginalSql,
        Fields = c.Fields.OrderBy(f => f.SortOrder).Select(f => new QueryFieldDto
        {
            Id = f.Id,
            MetaColumnId = f.MetaColumnId,
            ColumnName = f.MetaColumn?.ColumnName,
            ColumnAlias = f.MetaColumn?.Alias,
            TableName = f.MetaColumn?.MetaTable?.Alias ?? f.MetaColumn?.MetaTable?.TableName,
            Alias = f.Alias,
            SortOrder = f.SortOrder,
            AggregateFunc = f.AggregateFunc
        }).ToList(),
        Filters = c.Filters.OrderBy(f => f.SortOrder).Select(f => new QueryFilterDto
        {
            Id = f.Id,
            MetaColumnId = f.MetaColumnId,
            ColumnName = f.MetaColumn?.ColumnName,
            ColumnAlias = f.MetaColumn?.Alias,
            Operator = f.Operator,
            DefaultValue = f.DefaultValue,
            IsRequired = f.IsRequired,
            ControlType = f.ControlType,
            Label = f.Label,
            SortOrder = f.SortOrder,
            IsContextFilter = f.IsContextFilter,
            ContextKey = f.ContextKey
        }).ToList(),
        Joins = c.Joins.OrderBy(j => j.SortOrder).Select(j => new QueryJoinDto
        {
            Id = j.Id,
            JoinTableId = j.JoinTableId,
            JoinTableName = j.JoinTable?.Alias ?? j.JoinTable?.TableName,
            JoinType = j.JoinType,
            LeftMetaColumnId = j.LeftMetaColumnId,
            LeftColumnName = j.LeftMetaColumn?.ColumnName,
            RightMetaColumnId = j.RightMetaColumnId,
            RightColumnName = j.RightMetaColumn?.ColumnName,
            SortOrder = j.SortOrder,
            LeftDateTrunc = j.LeftDateTrunc
        }).ToList()
    };
}
