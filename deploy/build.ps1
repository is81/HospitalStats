# ============================================================
# HospitalStats 部署准备脚本（开发机上执行）
# 用法: .\build.ps1
# 输出: .\publish\  文件夹，复制到服务器即可
# ============================================================

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

Write-Host "=== 1/4 生成生产密钥 ===" -ForegroundColor Cyan

function New-RandomKey($length) {
    $chars = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789!@#$%^&*()-_=+'
    -join (1..$length | ForEach-Object { $chars[(Get-Random -Max $chars.Length)] })
}

$jwtKey = New-RandomKey 48
$encKey = New-RandomKey 32

Write-Host "  JWT 密钥: $jwtKey"
Write-Host "  加密密钥: $encKey"

# 生成 appsettings.Production.json
$prodSettings = @{
    ConnectionStrings = @{
        ConfigDb = "Data Source=$AppDir\config.db"
    }
    Urls = "http://0.0.0.0:5000"
    Jwt = @{
        Key = $jwtKey
        Issuer = "HospitalStats"
        Audience = "HospitalStats"
    }
    Encryption = @{
        Key = $encKey
    }
    Backup = @{
        IntervalMinutes = 60
        MaxCount = 168  # 7 days
    }
    Logging = @{
        LogLevel = @{
            Default = "Warning"
            "Microsoft.AspNetCore" = "Warning"
        }
    }
}

$prodSettings | ConvertTo-Json -Depth 4 | Out-File -FilePath "publish\appsettings.Production.json" -Encoding utf8
Write-Host "  已生成 appsettings.Production.json"

Write-Host "`n=== 2/4 构建前端 ===" -ForegroundColor Cyan
Set-Location ..\hospital-stats-frontend
npm run build
Set-Location ..\deploy

Write-Host "`n=== 3/4 发布后端 ===" -ForegroundColor Cyan
Set-Location ..\HospitalStats.Backend\HospitalStats.Api
dotnet publish -c Release -o ..\..\deploy\publish --self-contained false
Set-Location ..\..\deploy

Write-Host "`n=== 4/4 复制安装脚本 ===" -ForegroundColor Cyan
Copy-Item -Force install.ps1 publish\

Write-Host "`n=== 完成 ===" -ForegroundColor Green
Write-Host "发布包位于: $PSScriptRoot\publish\" -ForegroundColor Green
Write-Host ""
Write-Host "发布包大小:" (Get-ChildItem -Recurse publish | Measure-Object -Property Length -Sum).Sum / 1MB | ForEach-Object { "{0:N1} MB" -f $_ }
Write-Host ""
Write-Host "下一步:" -ForegroundColor Yellow
Write-Host "  1. 将 publish\ 文件夹整个复制到服务器 C:\HospitalStats\"
Write-Host "  2. 在服务器上以管理员身份运行 C:\HospitalStats\install.ps1"
