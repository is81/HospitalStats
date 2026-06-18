using System.Text;
using HospitalStats.Api.Data;
using HospitalStats.Api.Middleware;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
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
    Log.Information("Starting HospitalStats API");

    var builder = WebApplication.CreateBuilder(new WebApplicationOptions
    {
        Args = args,
        // When running as Windows Service, working dir may be System32
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Host.UseSerilog();
    builder.Host.UseWindowsService();

    // EF Core + SQLite
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseSqlite(builder.Configuration.GetConnectionString("ConfigDb")));

    // JWT
    var jwtKey = builder.Configuration["Jwt:Key"];
    if (string.IsNullOrEmpty(jwtKey) && builder.Environment.IsProduction())
        throw new InvalidOperationException("Jwt:Key is required in production. Set it via environment variable or appsettings.json.");
    jwtKey ??= "HospitalStats@DevOnly_ChangeInProduction";

    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "HospitalStats",
                ValidAudience = builder.Configuration["Jwt:Audience"] ?? "HospitalStats",
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
            };
        });
    builder.Services.AddAuthorization();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddDefaultPolicy(policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            }
            else
            {
                policy.WithOrigins(builder.Configuration["Cors:Origins"] ?? "http://localhost")
                      .AllowAnyMethod().AllowAnyHeader();
            }
        });
    });

    // Services
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<DataSourceService>();
    builder.Services.AddScoped<MetaScannerService>();
    builder.Services.AddScoped<QueryExecutionService>();
    builder.Services.AddScoped<SqlParsingService>();
    builder.Services.AddSingleton<SystemSettingsService>();
    builder.Services.AddMemoryCache();
    builder.Services.AddHostedService<ConfigDbBackupService>();

    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo { Title = "HospitalStats API", Version = "v1" });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Description = "JWT Authorization header. Example: \"Bearer {token}\"",
            Name = "Authorization",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
    });

    var app = builder.Build();

    // Warn / block on insecure keys
    if (string.IsNullOrEmpty(builder.Configuration["Jwt:Key"]))
    {
        if (app.Environment.IsProduction())
            throw new InvalidOperationException("Jwt:Key is required in production.");
        Log.Warning("!!! Jwt:Key 未设置，使用开发默认密钥。生产环境请设置环境变量 !!!");
    }
    if (string.IsNullOrEmpty(builder.Configuration["Encryption:Key"]))
    {
        if (app.Environment.IsProduction())
            throw new InvalidOperationException("Encryption:Key is required in production.");
        Log.Warning("!!! Encryption:Key 未设置，使用开发默认密钥。生产环境请设置环境变量 !!!");
    }
    // Auto-migrate & seed
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        // Schema migrations (no EF Migrations — add columns manually)
        MigrateSchema(db);

        if (!db.Users.Any())
        {
            var adminPassword = Guid.NewGuid().ToString("N")[..10];
            var adminUser = new HospitalStats.Api.Models.User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(adminPassword),
                DisplayName = "系统管理员"
            };
            db.Users.Add(adminUser);
            db.SaveChanges();
            Log.Information("管理员账号: admin / {Password}（首次登录后请修改）", adminPassword);

            var adminRole = new HospitalStats.Api.Models.Role
            {
                Name = "admin",
                Description = "系统管理员（内置）",
                DashboardAccess = true
            };
            db.Roles.Add(adminRole);
            db.SaveChanges();

            db.UserRoles.Add(new HospitalStats.Api.Models.UserRole
            {
                UserId = adminUser.Id,
                RoleId = adminRole.Id
            });
            db.SaveChanges();
        }

        // Seed default system settings
        SeedIfMissing(db, "QueryTimeoutSeconds", "120");
        SeedIfMissing(db, "MaxRowCount", "50000");
        SeedIfMissing(db, "HistoryLimit", "50000");
        SeedIfMissing(db, "TrendDefaultDays", "30");
    }

    // Global exception handling
    app.UseMiddleware<ExceptionMiddleware>();

    // Request logging
    app.UseSerilogRequestLogging(options =>
    {
        options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000} ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseCors();
    app.UseAuthentication();
    app.UseMiddleware<AuditLogMiddleware>();
    app.UseAuthorization();

    // Serve frontend static files in production (single deployable)
    if (!app.Environment.IsDevelopment())
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapFallbackToFile("index.html");
    }

    app.MapControllers();

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

// Apply schema changes that would normally be EF migrations.
// Called inside the service scope so DbContext is available.
static void MigrateSchema(HospitalStats.Api.Data.AppDbContext db)
{
    // QueryJoins.LeftDateTrunc — added 2026-05-27
    TryAddColumn(db, "QueryJoins", "LeftDateTrunc", "INTEGER NOT NULL DEFAULT 0");
    // QueryConfigs.OriginalSql — added 2026-05-29
    TryAddColumn(db, "QueryConfigs", "OriginalSql", "TEXT");
    // SystemSettings — added 2026-06-03
    TryAddTable(db, "SystemSettings", "CREATE TABLE IF NOT EXISTS SystemSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL DEFAULT '')");
    // QueryHistories — added 2026-06-08
    TryAddTable(db, "QueryHistories", "CREATE TABLE IF NOT EXISTS QueryHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER, QueryConfigId INTEGER, QueryConfigName TEXT NOT NULL DEFAULT '', FiltersJson TEXT, ExecutedAt TEXT NOT NULL, RowCount INTEGER NOT NULL DEFAULT 0, ElapsedMs INTEGER NOT NULL DEFAULT 0)");
    // DashboardCards.CompareMode — added 2026-06-08
    TryAddColumn(db, "DashboardCards", "CompareMode", "TEXT");
    // Roles.DashboardAccess — added 2026-06-12
    TryAddColumn(db, "Roles", "DashboardAccess", "INTEGER NOT NULL DEFAULT 0");
}

static void TryAddColumn(HospitalStats.Api.Data.AppDbContext db, string table, string column, string type)
{
    try
    {
        db.Database.ExecuteSqlRaw(
            $"ALTER TABLE {table} ADD COLUMN {column} {type}");
        Log.Information("Schema migration: {Table}.{Column} column added", table, column);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Schema migration: {Table}.{Column} skipped", table, column);
    }
}

static void TryAddTable(HospitalStats.Api.Data.AppDbContext db, string name, string sql)
{
    try
    {
        db.Database.ExecuteSqlRaw(sql);
        Log.Information("Schema migration: table {Table} created", name);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Schema migration: table {Table} skipped", name);
    }
}

static void SeedIfMissing(AppDbContext db, string key, string value)
{
    try
    {
        if (!db.SystemSettings.Any(s => s.Key == key))
            db.SystemSettings.Add(new HospitalStats.Api.Models.SystemSetting { Key = key, Value = value });
        db.SaveChanges();
    }
    catch
    {
        // Table may not exist yet on very first run before migration
    }
}
