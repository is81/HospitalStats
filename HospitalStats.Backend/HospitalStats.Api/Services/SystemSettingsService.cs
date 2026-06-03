using HospitalStats.Api.Data;
using HospitalStats.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Services;

public class SystemSettingsService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _cacheAt = DateTime.MinValue;
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromSeconds(30);

    public SystemSettingsService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public virtual async Task<string> GetAsync(string key, string defaultValue = "")
    {
        var map = await GetMapAsync();
        return map.TryGetValue(key, out var val) ? val : defaultValue;
    }

    public virtual async Task<int> GetIntAsync(string key, int defaultValue = 0)
    {
        var val = await GetAsync(key);
        return int.TryParse(val, out var n) ? n : defaultValue;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        return new Dictionary<string, string>(await GetMapAsync(), StringComparer.OrdinalIgnoreCase);
    }

    public async Task SetAsync(string key, string value)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var setting = await db.SystemSettings.FindAsync(key);
        if (setting == null)
        {
            db.SystemSettings.Add(new SystemSetting { Key = key, Value = value });
        }
        else
        {
            setting.Value = value;
        }
        await db.SaveChangesAsync();
        _cacheAt = DateTime.MinValue; // force refresh
    }

    private async Task<Dictionary<string, string>> GetMapAsync()
    {
        if (_cache.Count > 0 && DateTime.UtcNow - _cacheAt < _cacheTtl)
            return _cache;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        _cache = await db.SystemSettings.ToDictionaryAsync(s => s.Key, s => s.Value, StringComparer.OrdinalIgnoreCase);
        _cacheAt = DateTime.UtcNow;
        return _cache;
    }
}
