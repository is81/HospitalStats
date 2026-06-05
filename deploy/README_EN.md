# HospitalStats Production Deployment Guide

## Prerequisites

| Environment | Requirements |
|-------------|--------------|
| Dev Machine | Node.js ≥ 18, .NET 8 SDK, Git Bash |
| Server | Windows Server 2019/2022 x64 |
| Network | Server can reach Oracle data sources |

## Step 1: Build on Dev Machine

```powershell
# Run on dev machine
cd <your-project-path>/deploy
.\build.ps1
```

The script automatically:
- Generates random JWT and encryption keys
- Builds the frontend (Vue → static files)
- Publishes the backend (.NET → publish folder)
- Copies the install script into the output
- Output: `.\publish\` folder

## Step 2: Copy to Server

Copy the entire `deploy\publish\` folder to the server:

```
C:\HospitalStats\
├── HospitalStats.Api.exe       ← App entry point
├── HospitalStats.Api.dll
├── *.dll                       ← .NET dependencies
├── appsettings.Production.json ← Production keys (auto-generated)
├── wwwroot\                    ← Frontend static files
├── config.db                   ← Created automatically on first run
├── logs\                       ← Logs (auto-created)
├── backups\                    ← Database backups (auto-created)
└── install.ps1                 ← Install script
```

## Step 3: Install on Server

1. **Install .NET 8 Runtime Hosting Bundle** (if not already installed)
   - Download: https://dotnet.microsoft.com/download/dotnet/8.0
   - Select ASP.NET Core Runtime 8.0.x → Windows x64 → Hosting Bundle

2. **Run PowerShell as Administrator** and navigate to the folder:
   ```powershell
   cd C:\HospitalStats
   .\install.ps1
   ```

3. The script automatically:
   - Checks .NET Runtime
   - Configures firewall (opens port 5000)
   - Registers Windows Service (auto-start)
   - Starts the service and verifies

## Step 4: Verify

Open `http://<server-ip>:5000` in a browser.

- Admin login: admin / random password (printed to console or stdout log on first startup)
- **Change the admin password immediately after first login!**

## Operations

```powershell
# Check service status
Get-Service HospitalStats

# Restart service
Restart-Service HospitalStats

# Watch live logs
Get-Content C:\HospitalStats\logs\app-*.log -Tail 50 -Wait

# Manual database backup
Copy-Item C:\HospitalStats\config.db "C:\HospitalStats\backups\config_manual_$(Get-Date -Format 'yyyyMMdd_HHmmss').db"

# Uninstall
Stop-Service HospitalStats
sc.exe delete HospitalStats
```

## Upgrading

```powershell
# Stop service
Stop-Service HospitalStats

# Backup database
Copy-Item C:\HospitalStats\config.db C:\HospitalStats\backups\

# Replace files (keep config.db and appsettings.Production.json)
# Copy new publish contents to C:\HospitalStats\

# Start service
Start-Service HospitalStats
```

## Troubleshooting

### Service fails to start

```powershell
# Check Windows Event Log
Get-EventLog -LogName Application -Source "HospitalStats" -Newest 10

# Check application logs
Get-Content C:\HospitalStats\logs\app-*.log -Tail 100

# Common causes
# - .NET 8 Runtime not installed → install Hosting Bundle
# - Port 5000 already in use → netstat -ano | findstr 5000
# - Malformed appsettings.Production.json → check JSON syntax
# - config.db corrupted or no permissions → check file access, restore backup
```

### Query unresponsive or timeout

```powershell
# Test Oracle connectivity from admin page: Data Sources → Test Connection
# Check logs for error details
Get-Content C:\HospitalStats\logs\app-*.log -Tail 50 | Select-String "error|fail|timeout"
```

### config.db accidentally replaced

```powershell
Stop-Service HospitalStats
Copy-Item C:\HospitalStats\backups\config_latest.db C:\HospitalStats\config.db
Start-Service HospitalStats
```

### Settings changes not taking effect

- System Settings page changes apply **immediately**, no restart needed
- appsettings.Production.json changes require a restart: `Restart-Service HospitalStats`

## Rollback

```powershell
Stop-Service HospitalStats
Move-Item C:\HospitalStats C:\HospitalStats.bak
Copy-Item C:\backup\HospitalStats-publish-old C:\HospitalStats -Recurse
Copy-Item C:\HospitalStats.bak\config.db C:\HospitalStats\config.db
Start-Service HospitalStats
# Verify, then remove backup: Remove-Item C:\HospitalStats.bak -Recurse
```

## Security Notes

- `appsettings.Production.json` contains production secrets — do not commit to Git
- Default port is 5000; for production, consider placing IIS in front for HTTPS
- Regularly check `C:\HospitalStats\backups\` to confirm automatic backups are working
- Oracle connection strings are AES-encrypted and stored in config.db; the key is read from `Encryption:Key`
