﻿# ============================================================
# HospitalStats 服务器安装脚本（在 Windows Server 上以管理员运行）
# 用法: 右键 → 以管理员身份运行 PowerShell → .\install.ps1
# ============================================================
#Requires -RunAsAdministrator

$ErrorActionPreference = "Stop"
$ServiceName = "HospitalStats"
$AppDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $AppDir

Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  HospitalStats 服务器安装" -ForegroundColor Cyan
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""

# ===== 1. 检查 .NET 8 Runtime =====
Write-Host "=== 1/5 检查 .NET 8 Runtime ===" -ForegroundColor Cyan
$dotnetVersion = dotnet --list-runtimes 2>$null | Select-String "Microsoft.AspNetCore.App 8."
if (-not $dotnetVersion) {
    Write-Host ""
    Write-Host "  未找到 .NET 8 ASP.NET Core Runtime！" -ForegroundColor Red
    Write-Host ""
    Write-Host "  请安装 .NET 8.0 Hosting Bundle（Windows x64）：" -ForegroundColor Yellow
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "  下载 ASP.NET Core Runtime 8.0.x → Windows → Hosting Bundle" -ForegroundColor Yellow
    Write-Host "  安装完成后重新运行此脚本。" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}
Write-Host "  已安装: $dotnetVersion" -ForegroundColor Green

# ===== 2. 防火墙 =====
Write-Host "`n=== 2/5 配置防火墙 ===" -ForegroundColor Cyan
$fw = Get-NetFirewallRule -DisplayName "$ServiceName-5000" -ErrorAction SilentlyContinue
if (-not $fw) {
    New-NetFirewallRule -DisplayName "$ServiceName-5000" -Direction Inbound -LocalPort 5000 -Protocol TCP -Action Allow | Out-Null
    Write-Host "  已添加防火墙规则: 端口 5000 入站允许" -ForegroundColor Green
} else {
    Write-Host "  防火墙规则已存在" -ForegroundColor Green
}

# ===== 3. 停止旧服务 =====
Write-Host "`n=== 3/5 处理现有服务 ===" -ForegroundColor Cyan
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "  已有服务，正在停止并删除..." -ForegroundColor Yellow
    Stop-Service $ServiceName -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 3
    sc.exe delete $ServiceName 2>&1 | Out-Null
    Start-Sleep -Seconds 2
    Write-Host "  旧服务已删除" -ForegroundColor Green
} else {
    Write-Host "  无现有服务" -ForegroundColor Green
}

# ===== 4. 创建 Windows 服务 =====
Write-Host "`n=== 4/5 注册 Windows 服务 ===" -ForegroundColor Cyan

$exePath = Join-Path $AppDir "HospitalStats.Api.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "  错误: 找不到 $exePath" -ForegroundColor Red
    Write-Host "  请将 publish\ 文件夹所有内容复制到 $AppDir 后再运行此脚本" -ForegroundColor Red
    exit 1
}

# 使用 sc.exe 创建服务
# binPath 必须包含 --contentRoot 指定工作目录
$binPath = "`"$exePath`" --contentRoot `"$AppDir`" --urls http://0.0.0.0:5000"
sc.exe create $ServiceName binPath= $binPath start= auto 2>&1 | Out-Null

# 设置失败恢复: 失败后重启 3 次，间隔 60 秒
sc.exe failure $ServiceName reset= 30 actions= restart/60000/restart/60000/restart/60000 2>&1 | Out-Null

# 通过注册表设置环境变量（ASPNETCORE_ENVIRONMENT=Production）
$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $regPath -Name "Environment" `
    -Value "ASPNETCORE_ENVIRONMENT=Production`0DOTNET_CONTENTROOT=$AppDir" `
    -PropertyType MultiString -Force | Out-Null

Write-Host "  服务 $ServiceName 已注册" -ForegroundColor Green
Write-Host "  启动类型: 自动" -ForegroundColor Green
Write-Host "  失败恢复: 3 次重启，间隔 60 秒" -ForegroundColor Green

# ===== 5. 启动服务 =====
Write-Host "`n=== 5/5 启动服务 ===" -ForegroundColor Cyan
Start-Service $ServiceName -ErrorAction Stop
Start-Sleep -Seconds 6

$svc = Get-Service $ServiceName
if ($svc.Status -eq "Running") {
    Write-Host "  服务运行中！" -ForegroundColor Green

    # 快速验证
    Write-Host "`n  验证连接..." -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/" -UseBasicParsing -TimeoutSec 10
        Write-Host "  前端首页: HTTP $($response.StatusCode) OK" -ForegroundColor Green
    } catch {
        Write-Host "  警告: 无法访问 http://localhost:5000/，请检查日志" -ForegroundColor Yellow
    }
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5000/api/auth/login" -Method POST `
            -Body '{"username":"admin","password":"<随机>"}' -ContentType "application/json" `
            -UseBasicParsing -TimeoutSec 10
        Write-Host "  登录接口: HTTP $($response.StatusCode) OK" -ForegroundColor Green
    } catch {
        Write-Host "  警告: 登录接口异常，请检查日志" -ForegroundColor Yellow
    }
} else {
    Write-Host "  服务未能启动，状态: $($svc.Status)" -ForegroundColor Red
    Write-Host "  请运行以下命令查看错误日志:" -ForegroundColor Yellow
    Write-Host "    Get-Content $AppDir\logs\app-*.log -Tail 50" -ForegroundColor White
    Write-Host ""
    Write-Host "  或查看 Windows 事件日志:" -ForegroundColor Yellow
    Write-Host "    Get-EventLog -LogName Application -Source '$ServiceName' -Newest 10" -ForegroundColor White
    exit 1
}

Write-Host ""
Write-Host "======================================" -ForegroundColor Cyan
Write-Host "  安装完成！" -ForegroundColor Green
Write-Host "======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  访问地址: http://$(Get-NetIPAddress -AddressFamily IPv4 | Where-Object InterfaceAlias -notmatch 'Loopback|Virtual' | Select -First 1 -ExpandProperty IPAddress):5000" -ForegroundColor Green
Write-Host ""
# 首次启动密码随机生成，查看 stdout 日志获取
Write-Host "  管理员: admin / <随机密码，查看 stdout 日志>  (首次登录务必修改！)" -ForegroundColor Yellow
Write-Host ""
Write-Host "--- 管理命令 ---" -ForegroundColor Cyan
Write-Host "  查看状态:  Get-Service $ServiceName"
Write-Host "  停止服务:  Stop-Service $ServiceName"
Write-Host "  启动服务:  Start-Service $ServiceName"
Write-Host "  重启服务:  Restart-Service $ServiceName"
Write-Host "  查看日志:  Get-Content $AppDir\logs\app-*.log -Tail 50"
Write-Host "  卸载:      Stop-Service $ServiceName; sc.exe delete $ServiceName"
