# HospitalStats 生产部署指南

## 前提条件

| 环境 | 要求 |
|------|------|
| 开发机 | Node.js ≥ 18, .NET 8 SDK, Git Bash |
| 服务器 | Windows Server 2019/2022 x64 |
| 网络 | 服务器能访问 Oracle 数据源 |

## 第一步：开发机上打包

```powershell
# 在开发机上执行
cd F:\HospitalStats\deploy
.\build.ps1
```

脚本会自动：
- 生成随机 JWT 和加密密钥
- 构建前端（Vue → static files）
- 发布后端（.NET → publish 文件夹）
- 复制安装脚本到发布包
- 输出: `.\publish\` 文件夹

## 第二步：复制到服务器

将 `F:\HospitalStats\deploy\publish\` 整个文件夹复制到服务器：

```
C:\HospitalStats\
├── HospitalStats.Api.exe       ← 应用入口
├── HospitalStats.Api.dll
├── *.dll                       ← .NET 依赖
├── appsettings.Production.json ← 生产密钥（自动生成）
├── wwwroot\                    ← 前端静态文件
├── config.db                   ← 首次运行自动创建
├── logs\                       ← 日志（自动创建）
├── backups\                    ← 数据库备份（自动创建）
└── install.ps1                 ← 安装脚本
```

## 第三步：服务器上安装

1. **安装 .NET 8 Runtime Hosting Bundle**（如未安装）
   - 下载: https://dotnet.microsoft.com/download/dotnet/8.0
   - 选 ASP.NET Core Runtime 8.0.x → Windows x64 → Hosting Bundle

2. **以管理员身份运行 PowerShell**，进入目录：
   ```powershell
   cd C:\HospitalStats
   .\install.ps1
   ```

3. 脚本会自动：
   - 检查 .NET Runtime
   - 配置防火墙（开放 5000 端口）
   - 注册 Windows 服务（自动启动）
   - 启动服务并验证

## 第四步：验证

浏览器打开 `http://服务器IP:5000`

- 管理员登录: admin / 随机密码（首次启动打印到控制台或 stdout 日志）
- **首次登录后务必修改管理员密码！**

## 管理运维

```powershell
# 查看服务状态
Get-Service HospitalStats

# 重启服务
Restart-Service HospitalStats

# 查看实时日志
Get-Content C:\HospitalStats\logs\app-*.log -Tail 50 -Wait

# 手动备份数据库
Copy-Item C:\HospitalStats\config.db "C:\HospitalStats\backups\config_manual_$(Get-Date -Format 'yyyyMMdd_HHmmss').db"

# 卸载
Stop-Service HospitalStats
sc.exe delete HospitalStats
```

## 升级部署

```powershell
# 停止服务
Stop-Service HospitalStats

# 备份数据库
Copy-Item C:\HospitalStats\config.db C:\HospitalStats\backups\

# 替换文件（保留 config.db 和 appsettings.Production.json）
# 复制新的 publish 内容到 C:\HospitalStats\

# 启动服务
Start-Service HospitalStats
```

## 安全提醒

- `appsettings.Production.json` 包含生产密钥，勿提交到 Git
- 默认端口 5000，生产环境建议前挂 IIS 处理 HTTPS
- 定期检查 `C:\HospitalStats\backups\` 确认自动备份正常
- Oracle 连接串 AES 加密存储在 config.db，密钥从 Encryption:Key 读取
