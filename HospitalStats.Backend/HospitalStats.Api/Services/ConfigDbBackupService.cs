using Microsoft.Data.Sqlite;

namespace HospitalStats.Api.Services;

public class ConfigDbBackupService : BackgroundService
{
    private readonly string _dbPath;
    private readonly string _backupDir;
    private readonly int _intervalMinutes;
    private readonly int _maxBackups;
    private readonly ILogger<ConfigDbBackupService> _logger;

    public ConfigDbBackupService(IConfiguration config, ILogger<ConfigDbBackupService> logger)
    {
        _logger = logger;

        var connStr = config.GetConnectionString("ConfigDb") ?? "Data Source=config.db";
        var builder = new SqliteConnectionStringBuilder(connStr);
        var dbFile = builder.DataSource;
        if (!Path.IsPathRooted(dbFile))
            dbFile = Path.Combine(AppContext.BaseDirectory, dbFile);
        _dbPath = Path.GetFullPath(dbFile);

        _backupDir = Path.Combine(Path.GetDirectoryName(_dbPath) ?? ".", "backups");
        _intervalMinutes = config.GetValue("Backup:IntervalMinutes", 60);
        _maxBackups = config.GetValue("Backup:MaxCount", 24);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // initial backup on startup
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await BackupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ConfigDb 备份失败");
            }

            await Task.Delay(TimeSpan.FromMinutes(_intervalMinutes), stoppingToken);
        }
    }

    private async Task BackupAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_dbPath))
        {
            _logger.LogWarning("ConfigDb 文件不存在，跳过备份: {Path}", _dbPath);
            return;
        }

        ct.ThrowIfCancellationRequested();

        Directory.CreateDirectory(_backupDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
        var backupFile = Path.Combine(_backupDir, $"config_{timestamp}.db");

        // WAL checkpoint then file copy (safe for SQLite WAL mode)
        using (var source = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await source.OpenAsync(ct);
            using var cmd = source.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await cmd.ExecuteNonQueryAsync(ct);
        }

        File.Copy(_dbPath, backupFile, overwrite: true);

        _logger.LogInformation("ConfigDb 备份完成: {File} ({Size} bytes)",
            backupFile, new FileInfo(backupFile).Length);

        // rotate old backups
        var files = Directory.GetFiles(_backupDir, "config_*.db")
            .OrderByDescending(f => f)
            .ToList();

        foreach (var old in files.Skip(_maxBackups))
        {
            File.Delete(old);
            _logger.LogInformation("清理旧备份: {File}", old);
        }
    }
}
