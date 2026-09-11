[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Paster')
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$binary = Join-Path $root 'dist\paster.exe'
& (Join-Path $root 'build.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$installedBinary = Join-Path $InstallRoot 'paster.exe'
$running = @(Get-Process -Name 'Paster' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    & $binary --exit
    foreach ($process in $running) {
        if (-not $process.WaitForExit(5000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(2000) | Out-Null
        }
    }
}
New-Item -ItemType Directory -Force -Path $InstallRoot | Out-Null
Copy-Item -LiteralPath $binary -Destination $installedBinary -Force
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
New-Item -Path $runKey -Force | Out-Null
New-ItemProperty -Path $runKey -Name 'Paster' -Value ('"{0}" --background' -f $installedBinary) -PropertyType String -Force | Out-Null
$started = Start-Process -FilePath $installedBinary -ArgumentList '--background' -WindowStyle Hidden -PassThru
if ($started.WaitForExit(750)) { throw "Paster exited during startup with code $($started.ExitCode)." }
Write-Output ("Installed and started Paster from {0}. Autostart is enabled for this user." -f $InstallRoot)
