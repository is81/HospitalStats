using HospitalStats.Api.Data;
using HospitalStats.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace HospitalStats.Api.Tests;

public class SystemSettingsServiceTests : IDisposable
{
    private static readonly InMemoryDatabaseRoot _dbRoot = new();
    private readonly ServiceProvider _provider;
    private readonly SystemSettingsService _service;
    private readonly string _dbName;

    public SystemSettingsServiceTests()
    {
        _dbName = $"test_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(opts =>
            opts.UseInMemoryDatabase(_dbName, _dbRoot));
        _provider = services.BuildServiceProvider();

        // Ensure schema
        using (var scope = _provider.CreateScope())
        {
            using var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        }

        _service = new SystemSettingsService(_provider.GetRequiredService<IServiceScopeFactory>());
    }

    public void Dispose() => _provider.Dispose();

    // ===== GetAsync =====

    [Fact]
    public async Task GetAsync_MissingKey_ReturnsDefault()
    {
        var val = await _service.GetAsync("NonExistent", "fallback");
        Assert.Equal("fallback", val);
    }

    [Fact]
    public async Task SetAsync_Then_GetAsync_ReturnsSetValue()
    {
        await _service.SetAsync("MaxRowCount", "50000");
        var val = await _service.GetAsync("MaxRowCount");
        Assert.Equal("50000", val);
    }

    [Fact]
    public async Task SetAsync_Twice_UpdatesValue()
    {
        await _service.SetAsync("Key", "old");
        await _service.SetAsync("Key", "updated");
        var val = await _service.GetAsync("Key");
        Assert.Equal("updated", val);
    }

    // ===== GetIntAsync =====

    [Theory]
    [InlineData(100)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(50000)]
    public async Task SetAsync_Then_GetIntAsync_ReturnsParsedInt(int expected)
    {
        await _service.SetAsync("Timeout", expected.ToString());
        var result = await _service.GetIntAsync("Timeout", 999);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task GetIntAsync_MissingKey_ReturnsDefault()
    {
        var result = await _service.GetIntAsync("NotFound", 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SetAsync_NonNumeric_Then_GetIntAsync_ReturnsDefault()
    {
        await _service.SetAsync("BadValue", "abc");
        var result = await _service.GetIntAsync("BadValue", 99);
        Assert.Equal(99, result);
    }

    // ===== GetAllAsync =====

    [Fact]
    public async Task GetAllAsync_ReturnsAllSetKeys()
    {
        await _service.SetAsync("K1", "V1");
        await _service.SetAsync("K2", "V2");

        var all = await _service.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("V1", all["K1"]);
        Assert.Equal("V2", all["K2"]);
    }

    [Fact]
    public async Task GetAllAsync_Empty_ReturnsEmptyDict()
    {
        var all = await _service.GetAllAsync();
        Assert.Empty(all);
    }

    // ===== Cache behavior =====

    [Fact]
    public async Task SetAsync_RefreshesCacheForGetAll()
    {
        await _service.SetAsync("CacheKey", "initial");
        var v1 = await _service.GetAsync("CacheKey");
        Assert.Equal("initial", v1);

        await _service.SetAsync("CacheKey", "refreshed");
        var v2 = await _service.GetAsync("CacheKey");

        Assert.Equal("refreshed", v2);
    }

    // ===== Direct cross-scope persistence (diagnostic) =====

    [Fact]
    public async Task Direct_CrossScope_Persistence()
    {
        using (var s1 = _provider.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var db1 = s1.ServiceProvider.GetRequiredService<AppDbContext>();
            db1.SystemSettings.Add(new HospitalStats.Api.Models.SystemSetting { Key = "Diag", Value = "OK" });
            await db1.SaveChangesAsync();
        }

        using (var s2 = _provider.GetRequiredService<IServiceScopeFactory>().CreateScope())
        {
            var db2 = s2.ServiceProvider.GetRequiredService<AppDbContext>();
            var entry = await db2.SystemSettings.FindAsync("Diag");
            Assert.NotNull(entry);
            Assert.Equal("OK", entry!.Value);
        }
    }
}
