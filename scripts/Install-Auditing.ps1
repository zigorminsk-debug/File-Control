#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][string[]]$Path)
$ErrorActionPreference = 'Stop'
auditpol /set /subcategory:"File System" /success:enable /failure:enable | Out-Host
foreach ($p in $Path) {
  if (!(Test-Path -LiteralPath $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
  $acl = Get-Acl -LiteralPath $p -Audit
  $rule = New-Object System.Security.AccessControl.FileSystemAuditRule(
    'Everyone',
    [System.Security.AccessControl.FileSystemRights]'CreateFiles,WriteData,AppendData,Delete,DeleteSubdirectoriesAndFiles',
    [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
    [System.Security.AccessControl.PropagationFlags]::None,
    [System.Security.AccessControl.AuditFlags]::Success)
  $acl.SetAuditRule($rule); Set-Acl -LiteralPath $p -AclObject $acl
  Write-Host "Audit enabled: $p"
}
