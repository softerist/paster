[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$outDir = Join-Path $root 'artifacts\tests'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$sources = @(
    (Join-Path $root 'src\Paster.Core\Configuration.cs'),
    (Join-Path $root 'src\Paster.Core\Contracts.cs'),
    (Join-Path $root 'src\Paster.Core\TransferCoordinator.cs'),
    (Join-Path $root 'tests\CoreTests\Program.cs')
)
$compilerArgs = @('/nologo','/target:exe','/langversion:5','/warn:4','/reference:System.dll','/reference:System.Core.dll','/reference:System.Runtime.Serialization.dll',("/out:{0}" -f (Join-Path $outDir 'CoreTests.exe'))) + $sources
& $compiler @compilerArgs
if ($LASTEXITCODE -ne 0) { throw "Test compilation failed with exit code $LASTEXITCODE." }
& (Join-Path $outDir 'CoreTests.exe')
if ($LASTEXITCODE -ne 0) { throw "Tests failed with exit code $LASTEXITCODE." }
