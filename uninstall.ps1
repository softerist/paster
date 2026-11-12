[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'Paster')
)
$ErrorActionPreference = 'Stop'
$installedBinary = Join-Path $InstallRoot 'paster.exe'
$session = (Get-Process -Id $PID).SessionId
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $runKey -Name 'Paster' -ErrorAction SilentlyContinue
# The installed copy keeps paster.exe locked until it exits, so removal waits for it (and stops it if it does not exit).
$running = @(Get-Process -Name 'Paster' -ErrorAction SilentlyContinue | Where-Object {
    if ($_.SessionId -ne $session) { return $false }
    try { [String]::Equals([IO.Path]::GetFullPath($_.Path), [IO.Path]::GetFullPath($installedBinary), [StringComparison]::OrdinalIgnoreCase) } catch { $false }
})
$pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', "Paster.Management.$session", [System.IO.Pipes.PipeDirection]::Out)
try { $pipe.Connect(300); $writer = New-Object System.IO.StreamWriter($pipe); $writer.AutoFlush = $true; $writer.WriteLine('exit') } catch { Write-Verbose 'No running Paster instance was reachable.' } finally { if ($pipe) { $pipe.Dispose() } }
foreach ($process in $running) {
    if (-not $process.WaitForExit(5000)) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit(2000) | Out-Null
    }
}
if (Test-Path -LiteralPath $InstallRoot) { Remove-Item -LiteralPath $InstallRoot -Recurse -Force }
Write-Output ("Removed Paster installation and per-user autostart from {0}." -f $InstallRoot)
