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
    private readonly ILogger<DataSourceService> _logger;

    public DataSourceService(AppDbContext db, IConfiguration config, ILogger<DataSourceService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
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

        using var tx = await _db.Database.BeginTransactionAsync();
        try
        {
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
            await tx.CommitAsync();
            return true;
        }
        catch
        {
            await tx.RollbackAsync();
            throw;
        }
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
        var key = ResolveEncryptionKey();
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        aes.GenerateIV(); // Random IV per encryption
        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        // Prepend IV to ciphertext: IV (16) + ciphertext
        var result = new byte[aes.IV.Length + cipherBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherBytes, 0, result, aes.IV.Length, cipherBytes.Length);
        return Convert.ToBase64String(result);
    }

    public string Decrypt(string cipherText)
    {
        var key = ResolveEncryptionKey();
        var raw = Convert.FromBase64String(cipherText);

        // Try new format first (IV prepended to ciphertext).
        if (raw.Length >= 32)
        {
            try
            {
                var result = DecryptWithIV(raw, key, raw[..16]);
                if (IsValidConnString(result)) return result;
            }
            catch { /* Fall through to old format */ }
        }

        // Old format (zero IV) for backward compatibility
        return DecryptWithIV(raw, key, new byte[16]);
    }

    private static bool IsValidConnString(string? s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        // Oracle connection strings contain key-value pairs like "Data Source=..."
        // or start with "User Id=" etc. Garbage output from wrong decryption
        // won't match this pattern.
        return s.Contains("Data Source=", StringComparison.OrdinalIgnoreCase) ||
               s.Contains("User Id=", StringComparison.OrdinalIgnoreCase);
    }

    private static string DecryptWithIV(byte[] raw, string key, byte[] iv)
    {
        // When iv is new-style (non-zero), raw = IV prefix + ciphertext
        var actualCipher = iv.SequenceEqual(new byte[16]) ? raw : raw[16..];
        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(actualCipher, 0, actualCipher.Length);
        return Encoding.UTF8.GetString(plainBytes);
    }

    private string ResolveEncryptionKey()
    {
        var key = _config["Encryption:Key"];
        if (string.IsNullOrEmpty(key))
        {
            _logger.LogCritical("Encryption:Key is not configured! Using hardcoded fallback — " +
                "set Encryption:Key environment variable for production.");
            key = "HospitalStats@2026!SecretKey123";
        }
        return key;
    }

    public async Task<List<string>> GetDeptOptionsAsync()
    {
        var metaTable = await _db.MetaTables.FirstOrDefaultAsync(t => t.TableName == "DEPT_DICT");
        if (metaTable == null) return new List<string>();

        var ds = await _db.DataSources.FindAsync(metaTable.DataSourceId);
        if (ds == null) return new List<string>();

        var connStr = Decrypt(ds.ConnectionString);
        var charSetOverride = ds.CharSetOverride;
        var useHexEncoding = !string.IsNullOrEmpty(charSetOverride);
        using var conn = new OracleConnection(connStr);
        await conn.OpenAsync();

        var schema = metaTable.SchemaName ?? "HOSPITAL";
        var colExpr = useHexEncoding
            ? $"RAWTOHEX(UTL_RAW.CAST_TO_RAW(\"DEPT_NAME\")) as \"DEPT_NAME\""
            : "\"DEPT_NAME\"";
        var sql = $"SELECT {colExpr} FROM (SELECT DISTINCT \"DEPT_NAME\", \"SERIAL_NO\" FROM \"{schema}\".\"DEPT_DICT\") ORDER BY \"SERIAL_NO\"";
        var values = await conn.QueryAsync<string>(sql);
        return values
            .Where(v => v != null)
            .Select(v => useHexEncoding
                ? QueryExecutionService.DecodeHexString(v!, charSetOverride)
                : QueryExecutionService.ConvertEncoding(v!, charSetOverride))
            .ToList();
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
