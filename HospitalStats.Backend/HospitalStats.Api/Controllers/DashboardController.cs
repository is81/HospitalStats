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

    public DashboardController(AppDbContext db, QueryExecutionService executor)
    {
        _db = db;
        _executor = executor;
    }

    [HttpGet]
    public async Task<ActionResult<List<DashboardCardDto>>> GetDashboard()
    {
        var cards = await _db.DashboardCards
            .Include(d => d.QueryConfig)
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
                IsEnabled = card.IsEnabled
            };

            // execute bound query to get data
            if (card.QueryConfigId != null)
            {
            try
            {
                var queryResult = await _executor.ExecuteAsync(
                    card.QueryConfigId.Value,
                    new Dictionary<string, string>(),
                    1, 100);

                if (card.DisplayType == "number" && queryResult.Rows.Count > 0)
                {
                    var firstRow = queryResult.Rows[0];
                    // 优先用配置字段的列名，RawSql场景则从行键提取
                    var columns = queryResult.Columns.Count > 0
                        ? queryResult.Columns
                        : firstRow.Keys.ToList();
                    dto.Data = new
                    {
                        value = firstRow.GetValueOrDefault(columns[0])?.ToString() ?? "-",
                        rows = queryResult.Rows.Take(20),
                        columns = columns,
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
            catch
            {
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
            IsEnabled = c.IsEnabled
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
            IsEnabled = req.IsEnabled
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
            IsEnabled = entity.IsEnabled
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
