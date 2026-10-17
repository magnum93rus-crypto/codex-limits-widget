$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'import-uia.ps1')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
New-Item -ItemType Directory -Force -Path (Join-Path $PSScriptRoot 'runtime') | Out-Null
$testExe = Join-Path $PSScriptRoot 'runtime\ModelTests.exe'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/link:$PSScriptRoot\runtime\Interop.UIAutomationClient.dll" "/out:$testExe" (Join-Path $PSScriptRoot 'src\LimitsModel.cs') (Join-Path $PSScriptRoot 'src\NativeAnchor.cs') (Join-Path $PSScriptRoot 'src\FooterReader.cs') (Join-Path $PSScriptRoot 'tests\ModelTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& $testExe
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
$fakeExe = Join-Path $PSScriptRoot 'runtime\FakeCodexServer.exe'
$clientExe = Join-Path $PSScriptRoot 'runtime\ClientTests.exe'
& $compiler /nologo /target:exe /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/out:$fakeExe" (Join-Path $PSScriptRoot 'src\LimitsModel.cs') (Join-Path $PSScriptRoot 'tests\FakeCodexServer.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fake server compilation failed' }
& $compiler /nologo /target:exe /codepage:65001 /reference:System.Web.Extensions.dll /reference:System.Drawing.dll "/out:$clientExe" (Join-Path $PSScriptRoot 'src\LimitsModel.cs') (Join-Path $PSScriptRoot 'src\CodexClient.cs') (Join-Path $PSScriptRoot 'tests\ClientTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Client test compilation failed' }
& $clientExe $fakeExe (Join-Path $PSScriptRoot 'runtime\client-test-data')
if ($LASTEXITCODE -ne 0) { throw 'Client tests failed' }
