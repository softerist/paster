[CmdletBinding()]
param(
    [switch]$RunTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The Windows C# compiler (csc.exe) was not found.' }

$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$output = Join-Path $dist 'Paster.exe'
$runningDistInstances = @(Get-Process -Name 'Paster' -ErrorAction SilentlyContinue | Where-Object {
    try { [String]::Equals([IO.Path]::GetFullPath($_.Path), [IO.Path]::GetFullPath($output), [StringComparison]::OrdinalIgnoreCase) }
    catch { $false }
})
if ($runningDistInstances.Count -gt 0) {
    & $output --exit
    foreach ($process in $runningDistInstances) {
        if (-not $process.WaitForExit(2000)) {
            Stop-Process -Id $process.Id -Force
            $process.WaitForExit(2000) | Out-Null
        }
    }
}
$sources = @(
    (Join-Path $root 'src\Paster.Core\Configuration.cs'),
    (Join-Path $root 'src\Paster.Core\Contracts.cs'),
    (Join-Path $root 'src\Paster.Core\TransferCoordinator.cs'),
    (Join-Path $root 'src\Paster.Windows\Program.cs'),
    (Join-Path $root 'src\Paster.Windows\WindowsPlatform.cs'),
    (Join-Path $root 'src\Paster.Windows\Management.cs')
)
foreach ($source in $sources) { if (-not (Test-Path -LiteralPath $source)) { throw "Missing source: $source" } }

$compilerArgs = @('/nologo','/target:winexe','/langversion:5','/optimize+','/warn:4',
    '/reference:System.dll','/reference:System.Core.dll','/reference:System.Drawing.dll',
    '/reference:System.Windows.Forms.dll','/reference:System.Runtime.Serialization.dll',
    ("/out:{0}" -f $output)) + $sources
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Compilation failed with exit code $LASTEXITCODE." }
Write-Output ("Built {0}" -f $output)

if ($RunTests) { & (Join-Path $root 'tests\run-tests.ps1') }
