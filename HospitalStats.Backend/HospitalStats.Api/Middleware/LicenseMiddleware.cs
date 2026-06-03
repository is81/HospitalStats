using System.Text.Json;
using HospitalStats.Api.Services;

namespace HospitalStats.Api.Middleware;

public class LicenseMiddleware
{
    private readonly RequestDelegate _next;

    // Paths that don't require activation
    private static readonly HashSet<string> _allowPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login",
        "/api/license/machine-code",
        "/api/license/activate",
        "/api/license/status"
    };

    public LicenseMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        if (_allowPaths.Contains(path) || !path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var license = context.RequestServices.GetRequiredService<LicenseService>();
        if (!await license.IsActivatedAsync())
        {
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                message = "系统未激活。请联系管理员获取激活码。",
                machineCode = LicenseService.GetMachineCode()
            }));
            return;
        }

        await _next(context);
    }
}
