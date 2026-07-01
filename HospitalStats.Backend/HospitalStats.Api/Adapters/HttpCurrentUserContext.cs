using System.Security.Claims;
using HospitalStats.Api.Abstractions;

namespace HospitalStats.Api.Adapters;

/// <summary>
/// Implements ICurrentUserContext using ASP.NET Core IHttpContextAccessor
/// to extract JWT claims (UserId, DeptName) for context-aware filter resolution.
/// </summary>
public class HttpCurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Dictionary<string, string> GetContextValues()
    {
        var values = new Dictionary<string, string>();
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null) return values;

        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!string.IsNullOrEmpty(userId))
            values["UserId"] = userId;

        var deptName = user.FindFirst("dept_name")?.Value;
        if (!string.IsNullOrEmpty(deptName))
            values["DeptName"] = deptName;

        return values;
    }
}
