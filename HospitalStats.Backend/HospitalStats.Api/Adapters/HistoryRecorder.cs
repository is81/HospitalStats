using HospitalStats.Api.Abstractions;
using HospitalStats.Api.Data;
using HospitalStats.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HospitalStats.Api.Adapters;

/// <summary>
/// Records query execution history in a fire-and-forget manner.
/// Called by controllers AFTER the engine returns, so history recording
/// never blocks the query response.
/// </summary>
public class HistoryRecorder
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ICurrentUserContext _userContext;
    private readonly SystemSettingsService _settingsService;
    private readonly ILogger<HistoryRecorder> _logger;

    public HistoryRecorder(IServiceScopeFactory scopeFactory,
        ICurrentUserContext userContext,
        SystemSettingsService settingsService,
        ILogger<HistoryRecorder> logger)
    {
        _scopeFactory = scopeFactory;
        _userContext = userContext;
        _settingsService = settingsService;
        _logger = logger;
    }

    public void Record(int configId, string configName, int total, long elapsedMs,
        Dictionary<string, string> filters)
    {
        var captureConfigId = configId;
        var captureConfigName = configName;
        var captureTotal = total;
        var captureElapsed = elapsedMs;
        var captureFilters = filters.Count > 0
            ? System.Text.Json.JsonSerializer.Serialize(filters)
            : null;

        _ = Task.Run(async () =>
        {
            try
            {
                using var histScope = _scopeFactory.CreateScope();
                var histDb = histScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var ctxValues = _userContext.GetContextValues();
                ctxValues.TryGetValue("UserId", out var userIdClaim);
                int.TryParse(userIdClaim, out var uid);

                histDb.QueryHistories.Add(new Models.QueryHistory
                {
                    UserId = uid > 0 ? uid : null,
                    QueryConfigId = captureConfigId,
                    QueryConfigName = captureConfigName[..Math.Min(captureConfigName.Length, 200)],
                    FiltersJson = captureFilters,
                    ExecutedAt = DateTime.Now,
                    RowCount = captureTotal,
                    ElapsedMs = captureElapsed
                });
                await histDb.SaveChangesAsync();

                var histLimit = await _settingsService.GetIntAsync("HistoryLimit", 50000);
                var totalCount = await histDb.QueryHistories.CountAsync();
                if (totalCount > histLimit)
                {
                    var toDelete = await histDb.QueryHistories
                        .OrderBy(h => h.ExecutedAt)
                        .Take(totalCount - histLimit)
                        .ToListAsync();
                    histDb.QueryHistories.RemoveRange(toDelete);
                    await histDb.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Query history recording failed (non-critical)");
            }
        });
    }
}
