using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace HospitalStats.QueryEngine;

/// <summary>
/// License validation hook for HospitalStats.QueryEngine.
/// The engine itself does NOT ship with a license validator implementation.
/// Callers inject a validation callback via <see cref="Initialize"/>.
///
/// Dual-licensing model:
/// - AGPL v3: Free for open-source use (your entire app must be AGPL-compatible)
/// - Commercial: Paid license removes copyleft obligations
///
/// Contact: is81@qq.com
/// </summary>
public static class EngineLicense
{
    private static Func<string, Task<bool>>? _validator;
    private static string? _currentLicenseKey;
    private static DateTime _lastValidationTime = DateTime.MinValue;
    private static bool _lastValidationResult;
    private static readonly TimeSpan ValidationInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    /// <summary>
    /// Initialize the license system with a validation callback.
    /// The callback receives a license key and returns true if valid.
    /// Call this once at application startup before any engine calls.
    ///
    /// For offline validation (no server needed), use <see cref="BuiltInLicenseValidator.InitializeOffline"/> instead.
    /// </summary>
    /// <param name="validator">Async function that validates a license key. Return true if valid.</param>
    /// <param name="licenseKey">The license key to validate.</param>
    public static void Initialize(Func<string, Task<bool>> validator, string licenseKey)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _currentLicenseKey = licenseKey ?? throw new ArgumentNullException(nameof(licenseKey));
        _lastValidationTime = DateTime.MinValue;
        _lastValidationResult = false;
    }

    /// <summary>
    /// Initialize with the built-in offline HMAC-SHA256 validator.
    /// No server required — the license key itself carries signed validation data.
    ///
    /// Generate license keys with the enterprise tools/GenerateLicense console app.
    /// </summary>
    /// <param name="licenseKey">The signed license key.</param>
    public static void InitializeOffline(string licenseKey)
    {
        BuiltInLicenseValidator.InitializeOffline(licenseKey);
    }

    /// <summary>
    /// Validate the current license. When no validator has been configured
    /// (AGPL / source-code usage), this is a silent no-op. When a validator
    /// IS configured (commercial NuGet usage), throws if unlicensed.
    /// Called internally by QueryEngine before each operation.
    /// </summary>
    internal static async Task ValidateAsync()
    {
        // No validator configured → AGPL source usage, allow freely
        if (_validator == null)
            return;

        if (_currentLicenseKey == null)
            throw new InvalidOperationException("No license key provided.");

        // Return cached result if within validation interval
        if (_lastValidationResult && DateTime.UtcNow - _lastValidationTime < ValidationInterval)
            return;

        // Allow grace period if validator is unreachable
        try
        {
            _lastValidationResult = await _validator(_currentLicenseKey);
            _lastValidationTime = DateTime.UtcNow;
        }
        catch
        {
            // If we've never successfully validated, check grace period
            if (_lastValidationTime == DateTime.MinValue)
                throw;

            // Grace period: allow 7 days of operation if validator is unreachable
            if (DateTime.UtcNow - _lastValidationTime > GracePeriod)
                throw new InvalidOperationException(
                    $"License validation failed and grace period ({GracePeriod.TotalDays} days) has expired. " +
                    "Please ensure the license server is reachable or contact support.");

            // Within grace period — allow operation with cached result
        }

        if (!_lastValidationResult)
            throw new UnauthorizedAccessException(
                "Invalid or expired license. Please contact license@hospitalstats.com to renew.");
    }

    /// <summary>
    /// Returns true if the engine is currently in licensed mode.
    /// Does not throw — safe for UI checks.
    /// </summary>
    public static bool IsLicensed
    {
        get
        {
            try
            {
                if (_validator == null || _currentLicenseKey == null)
                    return false;
                if (_lastValidationResult && DateTime.UtcNow - _lastValidationTime < ValidationInterval)
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Compute a SHA256 hash of the engine assembly for tamper detection.
    /// Callers can include this in license validation requests to detect modified DLLs.
    /// </summary>
    public static string GetAssemblyHash()
    {
        var assembly = typeof(EngineLicense).Assembly;
        var location = assembly.Location;
        if (string.IsNullOrEmpty(location) || !System.IO.File.Exists(location))
        {
            // In some deployment scenarios the assembly bytes are not accessible via file path.
            // Return the assembly name hash as a fallback identifier.
            var nameBytes = Encoding.UTF8.GetBytes(assembly.FullName ?? "HospitalStats.QueryEngine");
            return Convert.ToHexString(SHA256.HashData(nameBytes)).ToLowerInvariant();
        }
        var fileBytes = System.IO.File.ReadAllBytes(location);
        return Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
    }
}
