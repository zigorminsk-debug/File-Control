#Requires -RunAsAdministrator
Stop-Service FileControl -Force -ErrorAction SilentlyContinue
sc.exe delete FileControl
Remove-Item (Join-Path $env:PUBLIC 'Desktop\File Control.lnk') -Force -ErrorAction SilentlyContinue
Remove-Item (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs\File Control.lnk') -Force -ErrorAction SilentlyContinue
