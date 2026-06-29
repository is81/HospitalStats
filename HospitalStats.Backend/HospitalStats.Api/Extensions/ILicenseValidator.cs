namespace HospitalStats.Api.Extensions;

/// <summary>
/// 授权验证器接口（企业版功能）。
/// 企业版启动时验证 License Key 的合法性。
/// 社区版不提供实现，始终处于"社区版模式"。
/// </summary>
public interface ILicenseValidator
{
    /// <summary>
    /// 验证 License Key 是否有效。
    /// </summary>
    /// <param name="licenseKey">用户输入的 License Key</param>
    /// <returns>验证结果。</returns>
    Task<LicenseValidationResult> ValidateAsync(string licenseKey);

    /// <summary>
    /// 获取当前已激活的 License 信息。
    /// 如果未激活，返回 null。
    /// </summary>
    Task<LicenseInfo?> GetCurrentLicenseAsync();

    /// <summary>
    /// 当前是否为企业版模式。
    /// false 表示运行在社区版模式或 License 已过期。
    /// </summary>
    bool IsEnterpriseMode { get; }
}

/// <summary>
/// License 验证结果。
/// </summary>
public class LicenseValidationResult
{
    public bool IsValid { get; init; }
    public string? ErrorMessage { get; init; }
    public LicenseInfo? License { get; init; }
}

/// <summary>
/// License 信息。
/// </summary>
public class LicenseInfo
{
    public string LicenseKey { get; init; } = string.Empty;
    public string LicensedTo { get; init; } = string.Empty;
    public DateTime IssuedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public string Tier { get; init; } = "basic"; // basic / full
    public string[] EnabledModules { get; init; } = [];
}
