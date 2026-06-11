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
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly QueryExecutionService _executor;
    private readonly SystemSettingsService _settings;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(AppDbContext db, QueryExecutionService executor,
        SystemSettingsService settings, ILogger<DashboardController> logger)
    {
        _db = db;
        _executor = executor;
        _settings = settings;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<DashboardCardDto>>> GetDashboard(
        [FromQuery] string? dateFrom = null,
        [FromQuery] string? dateTo = null)
    {
        var cards = await _db.DashboardCards
            .Include(d => d.QueryConfig).ThenInclude(q => q.Filters).ThenInclude(f => f.MetaColumn)
            .Where(d => d.IsEnabled)
            .OrderBy(d => d.SortOrder)
            .ToListAsync();

        var result = new List<DashboardCardDto>();

        foreach (var card in cards)
        {
            var dto = new DashboardCardDto
            {
                Id = card.Id,
                Title = card.Title,
                QueryConfigId = card.QueryConfigId,
                QueryConfigName = card.QueryConfig?.Name,
                DisplayType = card.DisplayType,
                Icon = card.Icon,
                Color = card.Color,
                Unit = card.Unit,
                SortOrder = card.SortOrder,
                Width = card.Width,
                IsEnabled = card.IsEnabled,
                CompareMode = card.CompareMode,
                DecimalPlaces = card.DecimalPlaces
            };

            // execute bound query to get data
            if (card.QueryConfigId != null)
            {
            try
            {
                var filterDict = new Dictionary<string, string>();
                var configFilters = card.QueryConfig?.Filters ?? new List<QueryFilter>();
                _logger.LogInformation("Dashboard card '{Title}': dateFrom={From} dateTo={To} filters={Count}",
                    card.Title, dateFrom, dateTo, configFilters.Count);
                var dateColsStr = await _settings.GetAsync("DashboardDateColumns",
                    "VISIT_DATE,BILLING_DATE_TIME,DISCHARGE_DATE_TIME,PRESC_DATE");
                var dateCols = dateColsStr.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).ToArray();
                if (!string.IsNullOrEmpty(dateFrom))
                {
                    var gteFilter = configFilters.FirstOrDefault(f =>
                        f.MetaColumn != null && dateCols.Contains(f.MetaColumn.ColumnName) && f.Operator == "GTE");
                    if (gteFilter != null) filterDict[gteFilter.Id.ToString()] = dateFrom;
                    else _logger.LogWarning("Dashboard card '{Title}': no GTE date filter found for dateFrom", card.Title);
                }
                if (!string.IsNullOrEmpty(dateTo))
                {
                    var ltFilter = configFilters.FirstOrDefault(f =>
                        f.MetaColumn != null && dateCols.Contains(f.MetaColumn.ColumnName) && f.Operator == "LT");
                    if (ltFilter != null) filterDict[ltFilter.Id.ToString()] = dateTo;
                    else _logger.LogWarning("Dashboard card '{Title}': no LT date filter found for dateTo", card.Title);
                }

                var queryResult = await _executor.ExecuteAsync(
                    card.QueryConfigId.Value,
                    filterDict,
                    1, 100, recordHistory: false);

                if (card.DisplayType == "number" && queryResult.Rows.Count > 0)
                {
                    var firstRow = queryResult.Rows[0];
                    var columns = queryResult.Columns.Count > 0
                        ? queryResult.Columns
                        : firstRow.Keys.ToList();
                    var value = firstRow.GetValueOrDefault(columns[0])?.ToString() ?? "-";

                    // Compare mode: calculate previous period
                    double? changePct = null;
                    string? compareLabel = null;
                    if (!string.IsNullOrEmpty(card.CompareMode) && !string.IsNullOrEmpty(dateFrom) && !string.IsNullOrEmpty(dateTo))
                    {
                        var prevFilterDict = BuildCompareFilterDict(card.CompareMode, dateFrom, dateTo, configFilters, dateCols);
                        if (prevFilterDict != null)
                        {
                            try
                            {
                                var prevResult = await _executor.ExecuteAsync(card.QueryConfigId.Value, prevFilterDict, 1, 1, recordHistory: false);
                                if (prevResult.Rows.Count > 0)
                                {
                                    var prevRow = prevResult.Rows[0];
                                    var prevColumns = prevResult.Columns.Count > 0 ? prevResult.Columns : prevRow.Keys.ToList();
                                    var prevValueStr = prevRow.GetValueOrDefault(prevColumns[0])?.ToString() ?? "0";
                                    if (double.TryParse(value, out var cur) && double.TryParse(prevValueStr, out var prev) && prev != 0)
                                    {
                                        changePct = Math.Round((cur - prev) / prev * 100, 1);
                                    }
                                    compareLabel = card.CompareMode == "mom" ? "环比" : "同比";
                                }
                            }
                            catch { /* compare query failed, just show main value */ }
                        }
                    }

                    dto.Data = new
                    {
                        value,
                        compareLabel,
                        changePct,
                        rows = queryResult.Rows.Take(20),
                        columns,
                        total = queryResult.Total
                    };
                }
                else
                {
                    dto.Data = new
                    {
                        rows = queryResult.Rows,
                        columns = queryResult.Columns,
                        total = queryResult.Total
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Dashboard card {CardTitle} (config {ConfigId}) query failed", card.Title, card.QueryConfigId);
                dto.Data = new { error = "查询失败" };
            }
            }
            else
            {
                dto.Data = new { info = "未配置查询" };
            }

            result.Add(dto);
        }

        return result;
    }

    private static Dictionary<string, string>? BuildCompareFilterDict(
        string compareMode, string dateFrom, string dateTo,
        List<QueryFilter> configFilters, string[] dateCols)
    {
        if (!DateTime.TryParse(dateFrom, out var from) || !DateTime.TryParse(dateTo, out var to))
            return null;
        var span = to - from;
        DateTime prevFrom, prevTo;
        if (compareMode == "mom")
        {
            prevTo = from.AddDays(-1);
            prevFrom = prevTo.AddDays(-span.TotalDays);
        }
        else if (compareMode == "yoy")
        {
            prevFrom = from.AddYears(-1);
            prevTo = to.AddYears(-1);
        }
        else return null;

        var dict = new Dictionary<string, string>();
        var gteFilter = configFilters.FirstOrDefault(f =>
            f.MetaColumn != null && dateCols.Contains(f.MetaColumn.ColumnName) && f.Operator == "GTE");
        if (gteFilter != null) dict[gteFilter.Id.ToString()] = prevFrom.ToString("yyyy-MM-dd");
        var ltFilter = configFilters.FirstOrDefault(f =>
            f.MetaColumn != null && dateCols.Contains(f.MetaColumn.ColumnName) && f.Operator == "LT");
        if (ltFilter != null) dict[ltFilter.Id.ToString()] = prevTo.ToString("yyyy-MM-dd");
        return dict.Count > 0 ? dict : null;
    }

    // ===== Card CRUD =====

    [HttpGet("cards")]
    public async Task<ActionResult<List<DashboardCardDto>>> GetCards()
    {
        var cards = await _db.DashboardCards
            .Include(d => d.QueryConfig)
            .OrderBy(d => d.SortOrder)
            .ToListAsync();

        return cards.Select(c => new DashboardCardDto
        {
            Id = c.Id,
            Title = c.Title,
            QueryConfigId = c.QueryConfigId,
            QueryConfigName = c.QueryConfig?.Name,
            DisplayType = c.DisplayType,
            Icon = c.Icon,
            Color = c.Color,
            Unit = c.Unit,
            SortOrder = c.SortOrder,
            Width = c.Width,
            IsEnabled = c.IsEnabled,
            CompareMode = c.CompareMode,
            DecimalPlaces = c.DecimalPlaces
        }).ToList();
    }

    [HttpPost("cards")]
    public async Task<ActionResult<DashboardCardDto>> CreateCard([FromBody] DashboardCardSaveRequest req)
    {
        var entity = new DashboardCard
        {
            Title = req.Title,
            QueryConfigId = req.QueryConfigId,
            DisplayType = req.DisplayType,
            Icon = req.Icon,
            Color = req.Color,
            Unit = req.Unit,
            SortOrder = req.SortOrder,
            Width = req.Width,
            IsEnabled = req.IsEnabled,
            CompareMode = req.CompareMode,
            DecimalPlaces = req.DecimalPlaces
        };
        _db.DashboardCards.Add(entity);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCards), new DashboardCardDto
        {
            Id = entity.Id,
            Title = entity.Title,
            QueryConfigId = entity.QueryConfigId,
            DisplayType = entity.DisplayType,
            Icon = entity.Icon,
            Color = entity.Color,
            Unit = entity.Unit,
            SortOrder = entity.SortOrder,
            Width = entity.Width,
            IsEnabled = entity.IsEnabled,
            CompareMode = entity.CompareMode,
            DecimalPlaces = entity.DecimalPlaces
        });
    }

    [HttpPut("cards/{id}")]
    public async Task<IActionResult> UpdateCard(int id, [FromBody] DashboardCardSaveRequest req)
    {
        var entity = await _db.DashboardCards.FindAsync(id);
        if (entity == null) return NotFound();

        entity.Title = req.Title;
        entity.QueryConfigId = req.QueryConfigId;
        entity.DisplayType = req.DisplayType;
        entity.Icon = req.Icon;
        entity.Color = req.Color;
        entity.Unit = req.Unit;
        entity.SortOrder = req.SortOrder;
        entity.Width = req.Width;
        entity.IsEnabled = req.IsEnabled;
        entity.CompareMode = req.CompareMode;
        entity.DecimalPlaces = req.DecimalPlaces;

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("cards/{id}")]
    public async Task<IActionResult> DeleteCard(int id)
    {
        var entity = await _db.DashboardCards.FindAsync(id);
        if (entity == null) return NotFound();
        _db.DashboardCards.Remove(entity);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPut("cards/order")]
    public async Task<IActionResult> UpdateOrder([FromBody] DashboardCardOrderRequest req)
    {
        for (int i = 0; i < req.CardIds.Count; i++)
        {
            var card = await _db.DashboardCards.FindAsync(req.CardIds[i]);
            if (card != null) card.SortOrder = i;
        }
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
