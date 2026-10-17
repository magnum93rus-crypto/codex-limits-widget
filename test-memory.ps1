param([ValidateRange(1000,1000000)][int]$Scans = 3000)
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'import-uia.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'runtime') | Out-Null
$exe = Join-Path $PSScriptRoot 'runtime\MemorySoak.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/link:$PSScriptRoot\runtime\Interop.UIAutomationClient.dll" "/out:$exe" (Join-Path $PSScriptRoot 'src\LimitsModel.cs') (Join-Path $PSScriptRoot 'src\NativeAnchor.cs') (Join-Path $PSScriptRoot 'src\FooterReader.cs') (Join-Path $PSScriptRoot 'tests\MemorySoak.cs')
if ($LASTEXITCODE -ne 0) { throw 'Memory test compilation failed' }
& $exe $Scans
if ($LASTEXITCODE -ne 0) { throw 'Memory test failed or no visible Codex composer' }
