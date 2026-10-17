$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runtime = Join-Path $PSScriptRoot 'runtime'
New-Item -ItemType Directory -Force -Path $runtime | Out-Null
$importer = Join-Path $runtime 'ImportUia.exe'
$interop = Join-Path $runtime 'Interop.UIAutomationClient.dll'
& $compiler /nologo /target:exe /platform:x64 "/out:$importer" (Join-Path $PSScriptRoot 'tools\ImportUia.cs')
if ($LASTEXITCODE -ne 0) { throw 'UI Automation importer compilation failed' }
& $importer $interop
if ($LASTEXITCODE -ne 0) { throw 'UI Automation import failed' }
