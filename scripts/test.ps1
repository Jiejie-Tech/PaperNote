[CmdletBinding()]
param([switch]$SkipAndroidRuntime)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'android-common.ps1')
$environment = Get-PaperNoteAndroidEnvironment
$root = $environment.BuildRoot

function Invoke-Dotnet([string[]]$Arguments, [string]$FailureMessage) {
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw $FailureMessage }
}

& (Join-Path $PSScriptRoot 'audit-open-source.ps1')

Invoke-Dotnet @('build', (Join-Path $root 'PaperNote.sln'), '-c', 'Release') 'Release solution build failed.'
Invoke-Dotnet @('run', '--project', (Join-Path $root 'tests\PaperNote.Core.Tests\PaperNote.Core.Tests.csproj'), '-c', 'Release', '--no-build') 'PaperNote.Core tests failed.'
Invoke-Dotnet @('run', '--project', (Join-Path $root 'tests\SmokeTest\SmokeTest.csproj'), '-c', 'Release', '--no-build') 'Core storage smoke test failed.'
Invoke-Dotnet @('run', '--project', (Join-Path $root 'tests\BackgroundUiTest\BackgroundUiTest.csproj'), '-c', 'Release', '--no-build') 'Background WPF UI test failed.'

& (Join-Path $PSScriptRoot 'build-android.ps1')
if ($SkipAndroidRuntime) {
    & (Join-Path $PSScriptRoot 'test-android.ps1') -SkipBuild -SkipUi
} else {
    & (Join-Path $PSScriptRoot 'test-android.ps1') -SkipBuild
}

Write-Host 'ALL PAPERNOTE BACKGROUND TESTS PASS'
