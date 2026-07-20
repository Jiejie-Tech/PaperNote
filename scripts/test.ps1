[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

& (Join-Path $PSScriptRoot 'audit-open-source.ps1')
dotnet build (Join-Path $Root 'PaperNote.sln') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Release build failed.' }
dotnet run --project (Join-Path $Root 'tests\SmokeTest\SmokeTest.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Core smoke test failed.' }
dotnet run --project (Join-Path $Root 'tests\BackgroundUiTest\BackgroundUiTest.csproj') -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw 'Background WPF UI test failed.' }
Write-Host 'ALL PAPERNOTE BACKGROUND TESTS PASS'
