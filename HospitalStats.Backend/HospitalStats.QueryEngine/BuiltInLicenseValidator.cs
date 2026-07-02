using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HospitalStats.QueryEngine;

/// <summary>
/// License information extracted from a valid license key.
public record LicenseInfo(string LicensedTo, DateTime ExpiresAt, string Tier, string[] Modules, string[] Instances);

/// <summary>
/// Built-in offline license validator using HMAC-SHA256 signed keys.
/// No server / network required — the license key itself carries all
/// validation data (licensee, expiry, tier) and is cryptographically signed.
///
/// Key format: `Base64(JSON_payload).Base64(HMAC-SHA256_signature)`
///
/// To generate license keys, use the GenerateLicense tool:
///   dotnet run -- --licensed-to "XX医院" --expires 2027-06-01 --tier full
/// </summary>
public static class BuiltInLicenseValidator
{
    private static byte[]? _hmacKey;
    internal static LicenseInfo? CurrentLicense { get; private set; }

    /// <summary>
    /// Set the HMAC signing key. Call once before any validation.
    /// The key must be kept secret — anyone with the key can generate valid licenses.
    /// This library ships with NO default key. You must provide your own.
    /// </summary>
    public static void SetSigningKey(string hmacKey)
    {
        if (string.IsNullOrWhiteSpace(hmacKey))
            throw new ArgumentException("HMAC key must not be empty");
        _hmacKey = SHA256.HashData(Encoding.UTF8.GetBytes(hmacKey));
    }

    /// <summary>
    /// Validates a license key offline. Returns (isValid, licensee, expiry, tier).
    /// </summary>
    public static (bool IsValid, string? LicensedTo, DateTime? ExpiresAt, string? Tier, string[]? Modules, string[]? Instances, string? Error)
        Validate(string licenseKey)
    {
        if (_hmacKey == null)
            return (false, null, null, null, null, null, "HMAC 签名密钥未设置。请先调用 BuiltInLicenseValidator.SetSigningKey()");

        if (string.IsNullOrWhiteSpace(licenseKey))
            return (false, null, null, null, null, null, "License Key 不能为空");

        // 1. Parse format: <payload>.<signature>
        var parts = licenseKey.Trim().Split('.');
        if (parts.Length != 2)
            return (false, null, null, null, null,
                "License Key 格式无效，应为 <payload>.<signature>");

        // 2. Decode Base64
        byte[] payloadBytes, expectedSignature;
        try
        {
            payloadBytes = Convert.FromBase64String(parts[0]);
            expectedSignature = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return (false, null, null, null, null, null, "License Key Base64 解码失败");
        }

        // 3. Verify HMAC-SHA256 signature
        using var hmac = new HMACSHA256(_hmacKey);
        var computedSignature = hmac.ComputeHash(payloadBytes);

        if (!CryptographicOperations.FixedTimeEquals(computedSignature, expectedSignature))
            return (false, null, null, null, null, null, "签名验证失败——License Key 可能被篡改");

        // 4. Parse JSON payload
        LicensePayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<LicensePayload>(
                Encoding.UTF8.GetString(payloadBytes));
        }
        catch (JsonException)
        {
            return (false, null, null, null, null, null, "License 数据解析失败");
        }

        if (payload == null)
            return (false, null, null, null, null, null, "License 数据为空");

        // 5. Check expiry
        if (payload.ExpiresAt <= DateTime.UtcNow)
            return (false, payload.LicensedTo, payload.ExpiresAt, payload.Tier, payload.Modules, payload.Instances,
                $"License 已过期（{payload.ExpiresAt:yyyy-MM-dd}）");

        // 6. Valid
        return (true, payload.LicensedTo, payload.ExpiresAt, payload.Tier, payload.Modules, payload.Instances, null);
    }

    /// <summary>
    /// Convenience: validate and return bool only (for use as EngineLicense callback).
    /// </summary>
    public static Task<bool> ValidateAsync(string licenseKey)
    {
        var (isValid, licensedTo, expiresAt, tier, modules, instances, _) = Validate(licenseKey);
        if (isValid)
            CurrentLicense = new LicenseInfo(licensedTo!, expiresAt!.Value, tier!, modules!, instances!);
        return Task.FromResult(isValid);
    }

    /// <summary>
    /// Initialize the engine with the built-in offline validator.
    /// Call this once at startup instead of providing a custom validator.
    /// </summary>
    /// <param name="licenseKey">The signed license key.</param>
    /// <param name="hmacSigningKey">The HMAC signing key. Must match the key used to generate licenses. Keep secret.</param>
    public static void InitializeOffline(string licenseKey, string hmacSigningKey)
    {
        SetSigningKey(hmacSigningKey);
        EngineLicense.Initialize(ValidateAsync, licenseKey);
    }

    /// <summary>
    /// Check if the current license includes a specific feature module.
    /// Always returns true when no validator is configured (AGPL mode).
    /// </summary>
    public static bool HasModule(string moduleName)
    {
        if (CurrentLicense == null) return true; // AGPL mode — all features
        return CurrentLicense.Modules.Contains(moduleName, StringComparer.OrdinalIgnoreCase);
    }

    private class LicensePayload
    {
        [JsonPropertyName("licensedTo")]
        public string LicensedTo { get; set; } = string.Empty;

        [JsonPropertyName("issuedAt")]
        public DateTime IssuedAt { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }

        [JsonPropertyName("tier")]
        public string Tier { get; set; } = "basic";

        [JsonPropertyName("modules")]
        public string[] Modules { get; set; } = [];

        [JsonPropertyName("instances")]
        public string[] Instances { get; set; } = [];
    }
}
