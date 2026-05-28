using System.Security.Cryptography;
using System.Text;
using Dapper;
using HospitalStats.Api.Data;
using HospitalStats.Api.DTOs;
using HospitalStats.Api.Models;
using Microsoft.EntityFrameworkCore;
using Oracle.ManagedDataAccess.Client;

namespace HospitalStats.Api.Services;

public class DataSourceService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public DataSourceService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<List<DataSourceDto>> GetAllAsync()
    {
        return await _db.DataSources
            .OrderBy(d => d.Name)
            .Select(d => ToDto(d))
            .ToListAsync();
    }

    public async Task<DataSourceDto?> GetByIdAsync(int id)
    {
        var entity = await _db.DataSources.FindAsync(id);
        return entity == null ? null : ToDto(entity);
    }

    public async Task<DataSourceDto> CreateAsync(DataSourceCreateRequest request)
    {
        var entity = new DataSource
        {
            Name = request.Name,
            DbType = request.DbType,
            ConnectionString = Encrypt(request.ConnectionString),
            Schema = request.Schema?.ToUpperInvariant(),
            CharSetOverride = request.CharSetOverride
        };
        _db.DataSources.Add(entity);
        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<DataSourceDto?> UpdateAsync(int id, DataSourceUpdateRequest request)
    {
        var entity = await _db.DataSources.FindAsync(id);
        if (entity == null) return null;

        entity.Name = request.Name;
        entity.DbType = request.DbType;
        entity.ConnectionString = Encrypt(request.ConnectionString);
        entity.Schema = request.Schema?.ToUpperInvariant();
        entity.CharSetOverride = request.CharSetOverride;
        entity.IsEnabled = request.IsEnabled;
        entity.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return ToDto(entity);
    }

    public async Task<bool> DeleteAsync(int id)
    {
        var entity = await _db.DataSources.FindAsync(id);
        if (entity == null) return false;

        // Cascade cleanup: delete dependent records that have Restrict FK
        var tableIds = await _db.MetaTables
            .Where(t => t.DataSourceId == id)
            .Select(t => t.Id)
            .ToListAsync();

        if (tableIds.Count > 0)
        {
            var columnIds = await _db.MetaColumns
                .Where(c => tableIds.Contains(c.MetaTableId))
                .Select(c => c.Id)
                .ToListAsync();

            // Remove QueryConfigs that reference any MetaTable of this DS (via MainTable)
            var configs = await _db.QueryConfigs
                .Where(q => tableIds.Contains(q.MainTableId))
                .ToListAsync();
            _db.QueryConfigs.RemoveRange(configs);

            // Remove Menus that reference those configs
            var menus = await _db.Menus
                .Where(m => m.QueryConfigId != null && configs.Select(c => c.Id).Contains(m.QueryConfigId.Value))
                .ToListAsync();
            foreach (var menu in menus) menu.QueryConfigId = null;

            await _db.SaveChangesAsync();

            // Now MetaTables can be deleted (columns cascade)
            var tables = await _db.MetaTables
                .Where(t => t.DataSourceId == id)
                .ToListAsync();
            _db.MetaTables.RemoveRange(tables);
            await _db.SaveChangesAsync();
        }

        _db.DataSources.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<TestConnectionResult> TestConnectionAsync(int id)
    {
        var entity = await _db.DataSources.FindAsync(id);
        if (entity == null)
            return new TestConnectionResult { Success = false, Message = "数据源不存在" };

        return await TestOracleConnectionAsync(Decrypt(entity.ConnectionString));
    }

    public async Task<TestConnectionResult> TestConnectionStringAsync(string connectionString)
    {
        return await TestOracleConnectionAsync(connectionString);
    }

    private async Task<TestConnectionResult> TestOracleConnectionAsync(string connectionString)
    {
        var result = new TestConnectionResult();
        try
        {
            using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync();

            // get version
            var version = await conn.ExecuteScalarAsync<string>(
                "SELECT VERSION FROM PRODUCT_COMPONENT_VERSION WHERE ROWNUM = 1");

            // get character set
            var nlsParams = await conn.QueryAsync<(string Parameter, string Value)>(
                "SELECT PARAMETER, VALUE FROM NLS_DATABASE_PARAMETERS WHERE PARAMETER IN ('NLS_CHARACTERSET', 'NLS_NCHAR_CHARACTERSET')");

            var charSetInfo = string.Join("; ", nlsParams.Select(p => $"{p.Parameter}={p.Value}"));

            // count tables
            var tableCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM ALL_TABLES WHERE OWNER = SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')");

            result.Success = true;
            result.Message = "连接成功";
            result.DbVersion = version;
            result.CharSet = charSetInfo;
            result.TableCount = tableCount;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Message = $"连接失败: {ex.Message}";
        }
        return result;
    }

    private string Encrypt(string plainText)
    {
        var key = _config["Encryption:Key"] ?? "HospitalStats@2026!SecretKey123";
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        aes.IV = new byte[16];
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        return Convert.ToBase64String(cipherBytes);
    }

    public string Decrypt(string cipherText)
    {
        var key = _config["Encryption:Key"] ?? "HospitalStats@2026!SecretKey123";
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        aes.IV = new byte[16];
        using var decryptor = aes.CreateDecryptor();
        var cipherBytes = Convert.FromBase64String(cipherText);
        var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private static DataSourceDto ToDto(DataSource entity) => new()
    {
        Id = entity.Id,
        Name = entity.Name,
        DbType = entity.DbType,
        Schema = entity.Schema,
        CharSetOverride = entity.CharSetOverride,
        CharSetInfo = entity.CharSetInfo,
        IsEnabled = entity.IsEnabled,
        CreatedAt = entity.CreatedAt,
        UpdatedAt = entity.UpdatedAt
    };
}
