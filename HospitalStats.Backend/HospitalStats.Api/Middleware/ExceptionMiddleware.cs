using System.Net;
using System.Text.Json;

namespace HospitalStats.Api.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ArgumentException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            context.Response.ContentType = "application/json";
            var logger = context.RequestServices.GetRequiredService<ILogger<ExceptionMiddleware>>();
            logger.LogWarning(ex, "Bad request: {Path}", context.Request.Path);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = ex.Message }));
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            var logger = context.RequestServices.GetRequiredService<ILogger<ExceptionMiddleware>>();
            logger.LogError(ex, "Unhandled exception at {Method} {Path}", context.Request.Method, context.Request.Path);
            await context.Response.WriteAsync(JsonSerializer.Serialize(new { message = "服务器内部错误，请查看日志" }));
        }
    }
}
