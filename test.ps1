$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'runtime') | Out-Null
$testExe = Join-Path $PSScriptRoot 'runtime\ModelTests.exe'
$wpf = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/reference:$wpf\UIAutomationClient.dll" "/reference:$wpf\UIAutomationTypes.dll" "/reference:$wpf\WindowsBase.dll" "/out:$testExe" (Join-Path $PSScriptRoot 'src\LimitsModel.cs') (Join-Path $PSScriptRoot 'src\NativeAnchor.cs') (Join-Path $PSScriptRoot 'tests\ModelTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
