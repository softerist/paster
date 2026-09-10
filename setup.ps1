[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Paster')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$binary = Join-Path $root 'dist\Paster.exe'
if (-not (Test-Path -LiteralPath $binary)) { & (Join-Path $root 'build.ps1'); if ($LASTEXITCODE -ne 0) { throw 'Build failed.' } }
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -LiteralPath $binary -Destination (Join-Path $InstallRoot 'Paster.exe') -Force
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name 'Paster' -Value ('"{0}" --background' -f (Join-Path $InstallRoot 'Paster.exe')) -PropertyType String -Force | Out-Null
Write-Output ("Installed Paster to {0}. Autostart is enabled for this user." -f $InstallRoot)
