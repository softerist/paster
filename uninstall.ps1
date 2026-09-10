[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Paster')
)
$ErrorActionPreference = 'Stop'
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Paster' -ErrorAction SilentlyContinue
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'Paster.Management', [System.IO.Pipes.PipeDirection]::Out)
try { $pipe.Connect(300); $writer = New-Object System.IO.StreamWriter($pipe); $writer.AutoFlush = $true; $writer.WriteLine('exit') } catch { Write-Verbose 'No running Paster instance was reachable.' } finally { if ($pipe) { $pipe.Dispose() } }
if (Test-Path -LiteralPath $InstallRoot) { Remove-Item -LiteralPath $InstallRoot -Recurse -Force }
Write-Output ("Removed Paster installation and per-user autostart from {0}." -f $InstallRoot)
