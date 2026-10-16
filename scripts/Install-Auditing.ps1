#Requires -RunAsAdministrator
param([Parameter(Mandatory=$true)][string[]]$Path)
$ErrorActionPreference = 'Stop'
# Language-neutral GUID of the "File System" audit subcategory. Using its English
# name fails silently on localized (for example Russian) Windows installations.
auditpol /set /subcategory:"{0cce921d-69ae-11d9-bed3-505054503030}" /success:enable /failure:enable | Out-Host
if ($LASTEXITCODE -ne 0) { throw "auditpol failed with exit code $LASTEXITCODE" }
foreach ($p in $Path) {
  if (!(Test-Path -LiteralPath $p)) { New-Item -ItemType Directory -Path $p -Force | Out-Null }
  $acl = Get-Acl -LiteralPath $p -Audit
  # Use the well-known SID instead of the localized account name ("Everyone" is
  # named differently on Russian and other localized Windows installations).
  $everyone = New-Object System.Security.Principal.SecurityIdentifier('S-1-1-0')
  $rule = New-Object System.Security.AccessControl.FileSystemAuditRule(
    $everyone,
    [System.Security.AccessControl.FileSystemRights]'CreateFiles,WriteData,AppendData,Delete,DeleteSubdirectoriesAndFiles',
    [System.Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit',
    [System.Security.AccessControl.PropagationFlags]::None,
    [System.Security.AccessControl.AuditFlags]::Success)
  $acl.SetAuditRule($rule); Set-Acl -LiteralPath $p -AclObject $acl
  Write-Host "Audit enabled: $p"
}
