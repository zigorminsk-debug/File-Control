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
Start-Service FileControl

# Create shortcuts for every server user. The shortcut opens the background
# service UI and never starts a second copy of the monitoring process.
$shell = New-Object -ComObject WScript.Shell
$shortcutPaths = @(
  (Join-Path $env:PUBLIC 'Desktop\File Control.lnk'),
  (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\File Control.lnk')
)
foreach ($shortcutPath in $shortcutPaths) {
  $shortcut = $shell.CreateShortcut($shortcutPath)
  $shortcut.TargetPath = "$env:SystemRoot\explorer.exe"
  $shortcut.Arguments = 'http://127.0.0.1:8787'
  $shortcut.WorkingDirectory = $InstallDir
  $shortcut.IconLocation = "$InstallDir\FileControl.exe,0"
  $shortcut.Description = 'Открыть панель мониторинга File Control'
  $shortcut.Save()
}
Write-Host "File Control installed. Desktop shortcut created. URL: http://localhost:8787"
