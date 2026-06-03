using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;

namespace HospitalStats.Api.Services;

public class LicenseService
{
    private const string _secretSalt = "HospitalStats@2026!LicenseKey#Secured";

    private readonly SystemSettingsService _settings;

    public LicenseService(SystemSettingsService settings)
    {
        _settings = settings;
    }

    public static string GetMachineCode()
    {
        var sb = new StringBuilder();
        sb.Append(Environment.MachineName);
        var nic = NetworkInterface.GetAllNetworkInterfaces()
            .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
        if (nic != null) sb.Append('|').Append(nic.GetPhysicalAddress());
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(hash)[..16];
    }

    /// <summary>Generate activation code: EXPIRY-SIGNATURE</summary>
    public static string GenerateActivationCode(string machineCode, string expiryDate)
    {
        var payload = machineCode + "|" + expiryDate;
        var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_secretSalt));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return expiryDate + "-" + Convert.ToHexString(hash)[..16];
    }

    /// <summary>Validate and parse activation code.</summary>
    public bool ValidateActivation(string fullCode, out string expiryDate)
    {
        expiryDate = "";
        var parts = fullCode.Split('-');
        if (parts.Length != 2) return false;
        expiryDate = parts[0];
        if (expiryDate.Length != 8 || !long.TryParse(expiryDate, out _)) return false;

        var expected = GenerateActivationCode(GetMachineCode(), expiryDate);
        return string.Equals(fullCode, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Activate with expiry.</summary>
    public async Task<bool> ActivateAsync(string fullCode)
    {
        if (!ValidateActivation(fullCode, out var expiryDate))
            return false;

        var machineCode = GetMachineCode();
        await _settings.SetAsync("LicenseActivated", "true");
        await _settings.SetAsync("LicenseMachineCode", machineCode);
        await _settings.SetAsync("LicenseExpiry", expiryDate);
        await _settings.SetAsync("LicenseActivatedAt", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        return true;
    }

    /// <summary>Check if activated and not expired on this machine.</summary>
    public async Task<bool> IsActivatedAsync()
    {
        var activated = await _settings.GetAsync("LicenseActivated");
        if (activated != "true") return false;

        var storedMachine = await _settings.GetAsync("LicenseMachineCode");
        if (storedMachine != GetMachineCode()) return false;

        var expiry = await _settings.GetAsync("LicenseExpiry");
        if (!string.IsNullOrEmpty(expiry) && long.TryParse(expiry, out var exp))
        {
            var today = long.Parse(DateTime.Now.ToString("yyyyMMdd"));
            if (today > exp) return false;
        }
        return true;
    }

    /// <summary>Clear activation for re-activation.</summary>
    public async Task ResetAsync()
    {
        await _settings.SetAsync("LicenseActivated", "false");
        await _settings.SetAsync("LicenseMachineCode", "");
        await _settings.SetAsync("LicenseExpiry", "");
    }

    /// <summary>Get activation status with expiry info.</summary>
    public async Task<string> GetStatusAsync()
    {
        var activated = await IsActivatedAsync();
        if (activated)
        {
            var expiry = await _settings.GetAsync("LicenseExpiry");
            if (!string.IsNullOrEmpty(expiry) && expiry.Length == 8)
            {
                var date = expiry[..4] + "-" + expiry[4..6] + "-" + expiry[6..];
                var days = (DateTime.Parse(date) - DateTime.Now.Date).Days;
                return days <= 30 ? $"已激活（{date} 到期，剩 {days} 天）" : $"已激活（{date} 到期）";
            }
            return "已激活（永久）";
        }

        // Check if expired
        var wasActivated = await _settings.GetAsync("LicenseActivated");
        if (wasActivated == "true")
            return "已过期，请续期 — 机器码: " + GetMachineCode();

        return "未激活 — 机器码: " + GetMachineCode();
    }
}
