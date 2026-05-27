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
public class MetaController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly MetaScannerService _scanner;
    private readonly ILogger<MetaController> _logger;

    public MetaController(AppDbContext db, MetaScannerService scanner, ILogger<MetaController> logger)
    {
        _db = db;
        _scanner = scanner;
        _logger = logger;
    }

    // ===== BizDomain =====

    [HttpGet("domains")]
    public async Task<ActionResult<List<BizDomainDto>>> GetDomains([FromQuery] int? dataSourceId)
    {
        return await _db.BizDomains.OrderBy(d => d.SortOrder).Select(d => new BizDomainDto
        {
            Id = d.Id,
            Name = d.Name,
            Description = d.Description,
            SortOrder = d.SortOrder,
            TableCount = dataSourceId.HasValue
                ? d.Tables.Count(t => t.DataSourceId == dataSourceId.Value)
                : 0
        }).ToListAsync();
    }

    [HttpPost("domains")]
    public async Task<ActionResult<BizDomainDto>> CreateDomain([FromBody] BizDomainCreateRequest req)
    {
        var entity = new BizDomain
        {
            Name = req.Name,
            Description = req.Description,
            SortOrder = req.SortOrder
        };
        _db.BizDomains.Add(entity);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(GetDomains), new BizDomainDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Description = entity.Description,
            SortOrder = entity.SortOrder
        });
    }

    [HttpPut("domains/{id}")]
    public async Task<IActionResult> UpdateDomain(int id, [FromBody] BizDomainCreateRequest req)
    {
        var entity = await _db.BizDomains.FindAsync(id);
        if (entity == null) return NotFound();
        entity.Name = req.Name;
        entity.Description = req.Description;
        entity.SortOrder = req.SortOrder;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("domains/{id}")]
    public async Task<IActionResult> DeleteDomain(int id)
    {
        var entity = await _db.BizDomains.FindAsync(id);
        if (entity == null) return NotFound();
        _db.BizDomains.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ===== Tables =====

    [HttpGet("datasources/{dsId}/tables")]
    public async Task<ActionResult<List<MetaTableDto>>> GetTables(int dsId,
        [FromQuery] int? domainId, [FromQuery] string? search)
    {
        var query = _db.MetaTables
            .Include(t => t.BizDomain)
            .Where(t => t.DataSourceId == dsId);

        if (domainId.HasValue)
            query = query.Where(t => t.BizDomainId == domainId.Value);

        if (!string.IsNullOrEmpty(search))
            query = query.Where(t => t.TableName.Contains(search)
                || (t.Alias != null && t.Alias.Contains(search)));

        return await query.OrderBy(t => t.TableName).Select(t => new MetaTableDto
        {
            Id = t.Id,
            DataSourceId = t.DataSourceId,
            BizDomainId = t.BizDomainId,
            BizDomainName = t.BizDomain != null ? t.BizDomain.Name : null,
            TableName = t.TableName,
            SchemaName = t.SchemaName,
            Alias = t.Alias,
            Description = t.Description,
            IsEnabled = t.IsEnabled,
            IsView = t.IsView,
            ColumnCount = t.Columns.Count,
            UpdatedAt = t.UpdatedAt
        }).ToListAsync();
    }

    [HttpPut("tables/{id}")]
    public async Task<IActionResult> UpdateTable(int id, [FromBody] MetaTableUpdateRequest req)
    {
        var entity = await _db.MetaTables.FindAsync(id);
        if (entity == null) return NotFound();
        entity.Alias = req.Alias;
        entity.Description = req.Description;
        entity.BizDomainId = req.BizDomainId;
        entity.IsEnabled = req.IsEnabled;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ===== Columns =====

    [HttpGet("tables/{tableId}/columns")]
    public async Task<ActionResult<List<MetaColumnDto>>> GetColumns(int tableId)
    {
        return await _db.MetaColumns
            .Where(c => c.MetaTableId == tableId)
            .OrderBy(c => c.SortOrder)
            .Select(c => new MetaColumnDto
            {
                Id = c.Id,
                MetaTableId = c.MetaTableId,
                ColumnName = c.ColumnName,
                DataType = c.DataType,
                DataLength = c.DataLength,
                DataPrecision = c.DataPrecision,
                DataScale = c.DataScale,
                Nullable = c.Nullable,
                Alias = c.Alias,
                Comment = c.Comment,
                IsQueryField = c.IsQueryField,
                IsFilterField = c.IsFilterField,
                IsDisplayField = c.IsDisplayField,
                SortOrder = c.SortOrder
            }).ToListAsync();
    }

    [HttpPut("columns/{id}")]
    public async Task<IActionResult> UpdateColumn(int id, [FromBody] MetaColumnUpdateRequest req)
    {
        var entity = await _db.MetaColumns.FindAsync(id);
        if (entity == null) return NotFound();
        entity.Alias = req.Alias;
        entity.IsQueryField = req.IsQueryField;
        entity.IsFilterField = req.IsFilterField;
        entity.IsDisplayField = req.IsDisplayField;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    // ===== Scan =====

    [HttpPost("datasources/{dsId}/scan")]
    public async Task<ActionResult<ScanResult>> Scan(int dsId, [FromQuery] string? schema)
    {
        try
        {
            var result = await _scanner.ScanAsync(dsId, schema);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata scan failed for datasource {DsId}", dsId);
            return BadRequest(new { message = $"扫描失败: {ex.Message}" });
        }
    }
}
