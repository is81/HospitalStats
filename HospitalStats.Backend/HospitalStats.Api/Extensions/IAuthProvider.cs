namespace HospitalStats.Api.Extensions;

/// <summary>
/// 外部认证提供者接口（企业版功能）。
/// 用于对接 SSO、LDAP、AD 域等企业级身份认证。
/// 社区版不提供实现，仅使用内置 JWT 认证。
/// </summary>
public interface IAuthProvider
{
    /// <summary>
    /// 尝试通过外部认证源验证用户身份。
    /// </summary>
    /// <param name="username">用户名</param>
    /// <param name="password">密码</param>
    /// <returns>认证成功返回用户信息，失败返回 null。</returns>
    Task<ExternalAuthResult?> AuthenticateAsync(string username, string password);

    /// <summary>
    /// 是否已启用此认证提供者。
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// 提供者名称（如 "LDAP"、"AD"、"OAuth"），用于 UI 展示。
    /// </summary>
    string ProviderName { get; }
}

/// <summary>
/// 外部认证结果。
/// </summary>
public class ExternalAuthResult
{
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string? Email { get; init; }
    public string? DeptName { get; init; }
    public string[] Groups { get; init; } = [];
}
