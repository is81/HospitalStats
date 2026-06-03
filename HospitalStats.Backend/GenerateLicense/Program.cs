using System.Security.Cryptography;
using System.Text;

// CLI mode: GenerateLicense.exe <机器码> [--days N]
string? machineCode = null;
int days = 0;
bool daysFromCli = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--days" && i + 1 < args.Length) { days = int.Parse(args[++i]); daysFromCli = true; }
    else if (args[i] == "--forever") { days = 0; daysFromCli = true; }
    else if (!args[i].StartsWith("--")) { machineCode = args[i]; }
}

// Interactive mode
if (string.IsNullOrEmpty(machineCode))
{
    Console.Write("请输入机器码: ");
    machineCode = Console.ReadLine()?.Trim();
}
if (string.IsNullOrEmpty(machineCode))
{
    Console.WriteLine("机器码不能为空。\n按任意键退出...");
    try { Console.ReadKey(); } catch { }
    return 1;
}

if (!daysFromCli && string.IsNullOrEmpty(args.FirstOrDefault(a => a == "--forever")))
{
    Console.Write("请输入授权天数 (0=永久): ");
    var input = Console.ReadLine()?.Trim();
    int.TryParse(input, out days);
}

var expiryDate = days > 0
    ? DateTime.Now.AddDays(days).ToString("yyyyMMdd")
    : "20991231";

const string secretSalt = "HospitalStats@2026!LicenseKey#Secured";
var payload = machineCode + "|" + expiryDate;
var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretSalt));
var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
var activationCode = expiryDate + "-" + Convert.ToHexString(hash)[..16];

Console.WriteLine();
Console.WriteLine($"机器码:    {machineCode}");
Console.WriteLine($"到期日:    {expiryDate[..4]}-{expiryDate[4..6]}-{expiryDate[6..]}");
Console.WriteLine($"激活码:    {activationCode}");
Console.WriteLine();
try { Console.WriteLine("按任意键退出..."); Console.ReadKey(); } catch { }
return 0;
