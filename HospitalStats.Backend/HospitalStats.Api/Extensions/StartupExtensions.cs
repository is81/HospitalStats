using System.Text;
using HospitalStats.Api.Data;
using HospitalStats.Api.Middleware;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

namespace HospitalStats.Api.Extensions;

/// <summary>
/// 社区版启动配置扩展方法。
/// 企业版 Program.cs 调用同样方法 + 叠加 AddEnterpriseServices()。
/// </summary>
public static class StartupExtensions
{
    // ============================================================
    // Service 注册
    // ============================================================

    /// <summary>
    /// 注册社区版全部服务：EF Core、JWT、CORS、业务服务、Swagger。
    /// 企业版在调用此方法后追加 <c>builder.Services.AddEnterpriseServices(...)</c>。
    /// </summary>
    public static WebApplicationBuilder AddHospitalStatsServices(this WebApplicationBuilder builder)
    {
        // EF Core + SQLite
        builder.Services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("ConfigDb")));

        // JWT
        var jwtKey = builder.Configuration["Jwt:Key"];
        if (string.IsNullOrWhiteSpace(jwtKey))
        {
            if (builder.Environment.IsProduction())
                throw new InvalidOperationException("Jwt:Key is required in production. Set it via environment variable or appsettings.json.");
            jwtKey = "HospitalStats@DevOnly_ChangeInProduction";
        }

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
                    policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
                else
                    policy.WithOrigins(builder.Configuration["Cors:Origins"] ?? "http://localhost")
                          .AllowAnyMethod().AllowAnyHeader();
            });
        });

        // 业务服务
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<DataSourceService>();
        builder.Services.AddScoped<MetaScannerService>();
        builder.Services.AddScoped<QueryExecutionService>();
        builder.Services.AddScoped<SqlParsingService>();
        builder.Services.AddSingleton<SystemSettingsService>();
        builder.Services.AddMemoryCache();
        builder.Services.AddHostedService<ConfigDbBackupService>();

        // API
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

        return builder;
    }

    // ============================================================
    // 中间件管线
    // ============================================================

    /// <summary>
    /// 配置社区版中间件管线：异常处理、日志、Swagger、CORS、认证、审计、静态文件。
    /// 企业版在调用此方法后追加 <c>app.UseEnterpriseMiddleware()</c>。
    /// </summary>
    public static WebApplication UseHospitalStatsMiddleware(this WebApplication app)
    {
        // 全局异常处理
        app.UseMiddleware<ExceptionMiddleware>();

        // HTTP 请求日志
        app.UseSerilogRequestLogging(options =>
        {
            options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000} ms";
        });

        // Swagger（仅开发环境）
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.UseCors();
        app.UseAuthentication();
        app.UseMiddleware<AuditLogMiddleware>();
        app.UseAuthorization();

        // 生产环境：静态文件 + SPA fallback
        if (!app.Environment.IsDevelopment())
        {
            app.UseDefaultFiles();
            app.UseStaticFiles();
            app.MapFallbackToFile("index.html");
        }

        app.MapControllers();

        return app;
    }

    // ============================================================
    // 数据库初始化（种子数据 + 结构迁移）
    // ============================================================

    /// <summary>
    /// 执行数据库初始化：EnsureCreated、结构迁移、种子数据。
    /// 社区版和企业版启动时各调用一次。幂等，重复执行无副作用。
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.EnsureCreatedAsync();

        MigrateSchema(db);

        // 创建默认管理员
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
            await db.SaveChangesAsync();
            Log.Information("管理员账号: admin / {Password}（首次登录后请修改）", adminPassword);

            var adminRole = new HospitalStats.Api.Models.Role
            {
                Name = "admin",
                Description = "系统管理员（内置）",
                DashboardAccess = true
            };
            db.Roles.Add(adminRole);
            await db.SaveChangesAsync();

            db.UserRoles.Add(new HospitalStats.Api.Models.UserRole
            {
                UserId = adminUser.Id,
                RoleId = adminRole.Id
            });
            await db.SaveChangesAsync();
        }

        // 种子系统设置
        SeedIfMissing(db, "QueryTimeoutSeconds", "120");
        SeedIfMissing(db, "MaxRowCount", "50000");
        SeedIfMissing(db, "HistoryLimit", "50000");
        SeedIfMissing(db, "TrendDefaultDays", "30");
        await db.SaveChangesAsync();
    }

    // ============================================================
    // 内部实现
    // ============================================================

    private static void MigrateSchema(AppDbContext db)
    {
        TryAddColumn(db, "QueryJoins", "LeftDateTrunc", "INTEGER NOT NULL DEFAULT 0");
        TryAddColumn(db, "QueryConfigs", "OriginalSql", "TEXT");
        TryAddTable(db, "SystemSettings", "CREATE TABLE IF NOT EXISTS SystemSettings (Key TEXT PRIMARY KEY, Value TEXT NOT NULL DEFAULT '')");
        TryAddTable(db, "QueryHistories", "CREATE TABLE IF NOT EXISTS QueryHistories (Id INTEGER PRIMARY KEY AUTOINCREMENT, UserId INTEGER, QueryConfigId INTEGER, QueryConfigName TEXT NOT NULL DEFAULT '', FiltersJson TEXT, ExecutedAt TEXT NOT NULL, RowCount INTEGER NOT NULL DEFAULT 0, ElapsedMs INTEGER NOT NULL DEFAULT 0)");
        TryAddColumn(db, "DashboardCards", "CompareMode", "TEXT");
        TryAddColumn(db, "Roles", "DashboardAccess", "INTEGER NOT NULL DEFAULT 0");
    }

    private static void TryAddColumn(AppDbContext db, string table, string column, string type)
    {
        try
        {
            db.Database.ExecuteSqlRaw($"ALTER TABLE {table} ADD COLUMN {column} {type}");
            Log.Information("Schema migration: {Table}.{Column} column added", table, column);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Schema migration: {Table}.{Column} skipped", table, column);
        }
    }

    private static void TryAddTable(AppDbContext db, string name, string sql)
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

    private static void SeedIfMissing(AppDbContext db, string key, string value)
    {
        try
        {
            if (!db.SystemSettings.Any(s => s.Key == key))
                db.SystemSettings.Add(new HospitalStats.Api.Models.SystemSetting { Key = key, Value = value });
        }
        catch
        {
            // 表可能尚未创建
        }
    }
}
