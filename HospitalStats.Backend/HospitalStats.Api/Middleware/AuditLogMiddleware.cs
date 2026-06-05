using System.Diagnostics;
using System.Security.Claims;
using System.Text;

namespace HospitalStats.Api.Middleware;

public class AuditLogMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string _logDir = Path.Combine(AppContext.BaseDirectory, "logs");

    private static readonly HashSet<string> _skipBodyPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/api/auth/login", "/api/auth/change-password"
    };

    private static readonly HashSet<string> _bodyMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "POST", "PUT", "PATCH", "DELETE"
    };

    public AuditLogMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        string? requestBody = null;

        // Capture request body for write operations (skip auth endpoints)
        if (_bodyMethods.Contains(context.Request.Method)
            && !_skipBodyPaths.Contains(context.Request.Path.Value ?? ""))
        {
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8,
                leaveOpen: true);
            requestBody = await reader.ReadToEndAsync();
            if (requestBody.Length > 2000)
                requestBody = requestBody[..2000] + "...(truncated)";
            context.Request.Body.Position = 0;
        }

        await _next(context);
        sw.Stop();

        var username = context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "(anonymous)";
        var method = context.Request.Method;
        var path = context.Request.Path + context.Request.QueryString;
        var statusCode = context.Response.StatusCode;
        var elapsed = sw.ElapsedMilliseconds;

        var entry = new StringBuilder();
        entry.Append(DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss.fff"));
        entry.Append(" | ");
        entry.Append(username);
        entry.Append(" | ");
        entry.Append(method);
        entry.Append(' ');
        entry.Append(path);
        entry.Append(" | ");
        entry.Append(statusCode);
        entry.Append(" | ");
        entry.Append(elapsed);
        entry.Append("ms");

        if (requestBody != null)
        {
            entry.Append(" | ");
            var compact = requestBody.Replace('\n', ' ').Replace('\r', ' ');
            if (compact.Length > 300) compact = compact[..300] + "...";
            entry.Append(compact);
        }

        entry.AppendLine();

        try
        {
            Directory.CreateDirectory(_logDir);
            var fileName = $"audit-{DateTime.UtcNow:yyyy-MM-dd}.log";
            await File.AppendAllTextAsync(Path.Combine(_logDir, fileName), entry.ToString(),
                Encoding.UTF8);
        }
        catch
        {
            // Don't let audit failure break the app
        }
    }
}
