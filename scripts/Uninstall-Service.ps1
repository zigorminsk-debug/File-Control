#Requires -RunAsAdministrator
Stop-Service FileControl -Force -ErrorAction SilentlyContinue
sc.exe delete FileControl
