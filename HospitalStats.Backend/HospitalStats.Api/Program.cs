using System.Text;
using HospitalStats.Api.Data;
using HospitalStats.Api.Middleware;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "app-.log");
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

Log.Information("NLS_LANG process={Proc}, machine={Mach}, user={User}",
    Environment.GetEnvironmentVariable("NLS_LANG", EnvironmentVariableTarget.Process) ?? "(null)",
    Environment.GetEnvironmentVariable("NLS_LANG", EnvironmentVariableTarget.Machine) ?? "(null)",
    Environment.GetEnvironmentVariable("NLS_LANG", EnvironmentVariableTarget.User) ?? "(null)");

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
    var jwtKey = builder.Configuration["Jwt:Key"] ?? "HospitalStats@JwtSecretKey2026!";
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

    // Warn if using default keys
    if (builder.Configuration["Jwt:Key"] == "HospitalStats@JwtSecretKey2026!ThisShouldBeChangedInProduction"
        || string.IsNullOrEmpty(builder.Configuration["Jwt:Key"]))
        Log.Warning("!!! 使用默认 JWT 密钥，生产环境请设置 Jwt:Key 环境变量 !!!");
    if (builder.Configuration["Encryption:Key"] == "HospitalStats@2026!SecretKey123"
        || string.IsNullOrEmpty(builder.Configuration["Encryption:Key"]))
        Log.Warning("!!! 使用默认加密密钥，生产环境请设置 Encryption:Key 环境变量 !!!");

    // Auto-migrate & seed
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        if (!db.Users.Any())
        {
            var adminUser = new HospitalStats.Api.Models.User
            {
                Username = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                DisplayName = "系统管理员"
            };
            db.Users.Add(adminUser);
            db.SaveChanges();

            var adminRole = new HospitalStats.Api.Models.Role
            {
                Name = "admin",
                Description = "系统管理员（内置）"
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
