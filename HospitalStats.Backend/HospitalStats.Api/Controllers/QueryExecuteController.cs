using HospitalStats.Api.Data;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/query-execute")]
[Authorize]
public class QueryExecuteController : ControllerBase
{
    private readonly QueryExecutionService _executor;
    private readonly AppDbContext _db;
    private readonly ILogger<QueryExecuteController> _logger;

    public QueryExecuteController(QueryExecutionService executor, AppDbContext db, ILogger<QueryExecuteController> logger)
    {
        _executor = executor;
        _db = db;
        _logger = logger;
    }

    [HttpGet("{configId}/filter-options/{filterId}")]
    public async Task<ActionResult<List<string>>> GetFilterOptions(int configId, int filterId)
    {
        try
        {
            var values = await _executor.GetDistinctValuesAsync(configId, filterId);
            return Ok(values);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get filter options for config {ConfigId} filter {FilterId}", configId, filterId);
            return BadRequest(new { message = $"获取筛选选项失败: {ex.Message}" });
        }
    }

    [HttpPost("{configId}")]
    public async Task<ActionResult<QueryResult>> Execute(
        int configId,
        [FromBody] QueryExecuteRequest request)
    {
        try
        {
            var result = await _executor.ExecuteAsync(
                configId,
                request.Filters ?? new Dictionary<string, string>(),
                request.Page ?? 1,
                request.PageSize);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid argument for config {ConfigId}", configId);
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Query execution failed for config {ConfigId}", configId);
            return BadRequest(new { message = $"查询执行失败: {ex.Message}" });
        }
    }

    [HttpPost("{configId}/export")]
    public async Task<IActionResult> ExportExcel(
        int configId,
        [FromBody] QueryExecuteRequest request)
    {
        try
        {
            var bytes = await _executor.ExportExcelAsync(
                configId,
                request.Filters ?? new Dictionary<string, string>());

            return File(bytes,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                $"query_result_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excel export failed for config {ConfigId}", configId);
            return BadRequest(new { message = $"导出失败: {ex.Message}" });
        }
    }
    [HttpGet("history")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult> GetHistory([FromQuery] int limit = 20)
    {
        var history = await _db.QueryHistories
            .OrderByDescending(h => h.ExecutedAt)
            .Take(Math.Min(limit, 100))
            .Select(h => new
            {
                h.Id,
                h.QueryConfigId,
                h.QueryConfigName,
                h.ExecutedAt,
                h.RowCount,
                h.ElapsedMs
            })
            .ToListAsync();

        return Ok(history);
    }
}

public class QueryExecuteRequest
{
    public Dictionary<string, string>? Filters { get; set; }
    public int? Page { get; set; } = 1;
    public int? PageSize { get; set; }
}
