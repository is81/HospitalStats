using HospitalStats.Api.Adapters;
using HospitalStats.Api.Data;
using HospitalStats.QueryEngine;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Controllers;

[ApiController]
[Route("api/query-execute")]
[Authorize]
public class QueryExecuteController : ControllerBase
{
    private readonly IQueryEngine _engine;
    private readonly EngineRequestBuilder _requestBuilder;
    private readonly HistoryRecorder _historyRecorder;
    private readonly AppDbContext _db;
    private readonly ILogger<QueryExecuteController> _logger;

    public QueryExecuteController(IQueryEngine engine,
        EngineRequestBuilder requestBuilder,
        HistoryRecorder historyRecorder,
        AppDbContext db,
        ILogger<QueryExecuteController> logger)
    {
        _engine = engine;
        _requestBuilder = requestBuilder;
        _historyRecorder = historyRecorder;
        _db = db;
        _logger = logger;
    }

    [HttpGet("{configId}/filter-options/{filterId}")]
    public async Task<ActionResult<List<string>>> GetFilterOptions(int configId, int filterId)
    {
        try
        {
            var request = await _requestBuilder.BuildDistinctValuesAsync(configId, filterId);
            var values = await _engine.GetDistinctValuesAsync(request);
            return Ok(values);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get filter options for config {ConfigId} filter {FilterId}", configId, filterId);
            return BadRequest(new { message = $"获取筛选选项失败: {ex.Message}" });
        }
    }

    [HttpPost("{configId}")]
    public async Task<ActionResult<QueryEngineResult>> Execute(
        int configId,
        [FromBody] QueryExecuteRequest request)
    {
        try
        {
            var filters = request.Filters ?? new Dictionary<string, string>();
            var page = request.Page ?? 1;
            var pageSize = request.PageSize ?? 50;

            var engineRequest = await _requestBuilder.BuildAsync(configId, filters, page, pageSize);
            var result = await _engine.ExecuteAsync(engineRequest);

            // Record history (fire-and-forget, never blocks response)
            _historyRecorder.Record(configId,
                engineRequest.MainTable?.TableName ?? "",
                result.Total, result.ElapsedMs, filters);

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
            var filters = request.Filters ?? new Dictionary<string, string>();
            var engineRequest = await _requestBuilder.BuildAsync(configId, filters, 1, int.MaxValue);
            var bytes = await _engine.ExportExcelAsync(engineRequest);

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
            .Include(h => h.User)
            .OrderByDescending(h => h.ExecutedAt)
            .Take(Math.Min(limit, 100))
            .Select(h => new
            {
                h.Id,
                h.QueryConfigId,
                h.QueryConfigName,
                UserName = h.User != null ? h.User.DisplayName : "-",
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
