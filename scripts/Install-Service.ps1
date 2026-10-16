#Requires -RunAsAdministrator
param([string]$InstallDir="$env:ProgramFiles\FileControl",[Parameter(Mandatory=$true)][string[]]$MonitoredPath)
$ErrorActionPreference='Stop'; $source=Split-Path $PSScriptRoot -Parent
if(Get-Service FileControl -ErrorAction SilentlyContinue){Stop-Service FileControl -Force; sc.exe delete FileControl | Out-Null; Start-Sleep 2}
New-Item -ItemType Directory -Force $InstallDir | Out-Null; Copy-Item "$source\*" $InstallDir -Recurse -Force
$configPath = Join-Path $InstallDir 'appsettings.json'
$config = Get-Content $configPath -Raw | ConvertFrom-Json
$config.FileControl.MonitoredPaths = @($MonitoredPath)
$config | ConvertTo-Json -Depth 10 | Set-Content $configPath -Encoding UTF8
& "$InstallDir\scripts\Install-Auditing.ps1" -Path $MonitoredPath
sc.exe create FileControl binPath= "`"$InstallDir\FileControl.exe`"" start= auto obj= LocalSystem | Out-Host
sc.exe description FileControl "Аудит операций с файлами и документами Office" | Out-Host
Start-Service FileControl; Write-Host "File Control: http://localhost:8787"
