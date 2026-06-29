using System.Text;
using HospitalStats.Api.Extensions;
using Serilog;

// Required for GBK/GB2312/GB18030 decoding on .NET 8+
Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "app-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

try
{
    Log.Information("Starting HospitalStats API (Community Edition)");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        // When running as Windows Service, working dir may be System32
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Host.UseSerilog();
    builder.Host.UseWindowsService();

    // ── 社区版服务注册 ──
    builder.AddHospitalStatsServices();

    var app = builder.Build();

    // 生产环境安全密钥检查
    if (string.IsNullOrEmpty(builder.Configuration["Jwt:Key"]))
        Log.Warning("!!! Jwt:Key 未设置，使用开发默认密钥。生产环境请设置环境变量 !!!");
    if (string.IsNullOrEmpty(builder.Configuration["Encryption:Key"]))
        Log.Warning("!!! Encryption:Key 未设置，使用开发默认密钥。生产环境请设置环境变量 !!!");

    // ── 数据库初始化 ──
    await app.InitializeDatabaseAsync();

    // ── 中间件管线 ──
    app.UseHospitalStatsMiddleware();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
