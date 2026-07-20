[CmdletBinding()]
param(
    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:[-+][0-9A-Za-z.-]+)?$')]
    [string]$Version = '1.0.0',
    [ValidateSet('win-x64','win-arm64')]
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Artifacts = Join-Path $Root 'artifacts'
$StagingRoot = Join-Path $Artifacts 'staging'
$ReleaseRoot = Join-Path $Artifacts 'releases'
$PackageName = "PaperNote-Desktop-$Version-$Runtime"
$PublishDir = Join-Path $StagingRoot $PackageName
$ZipPath = Join-Path $ReleaseRoot "$PackageName.zip"

function Assert-InWorkspace([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $prefix = $Root.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw "Unsafe path outside workspace: $full" }
}

Assert-InWorkspace $Artifacts
Assert-InWorkspace $PublishDir
Assert-InWorkspace $ZipPath
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'test.ps1') }
if (Test-Path -LiteralPath $PublishDir) { Remove-Item -LiteralPath $PublishDir -Recurse -Force }
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
New-Item -ItemType Directory -Path $ReleaseRoot -Force | Out-Null

$project = Join-Path $Root 'src\PaperNote.Desktop\PaperNote.Desktop.csproj'
dotnet publish $project -c Release -r $Runtime --self-contained true -o $PublishDir -p:Version=$Version -p:DebugType=None -p:DebugSymbols=false -p:PublishSingleFile=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

# Native packages may include debug symbol files; keep public releases lean and avoid shipping them.
Get-ChildItem -LiteralPath $PublishDir -Recurse -File -Filter '*.pdb' | Remove-Item -Force

$publicDocuments = @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','PRIVACY.md','SECURITY.md','CONTRIBUTING.md','CODE_OF_CONDUCT.md','BRANDING.md','SUPPORT.md')
foreach ($document in $publicDocuments) {
    Copy-Item -LiteralPath (Join-Path $Root $document) -Destination (Join-Path $PublishDir $document)
}
New-Item -ItemType Directory -Path (Join-Path $PublishDir 'legal') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $Root 'legal\third-party') -Destination (Join-Path $PublishDir 'legal\third-party') -Recurse
Copy-Item -LiteralPath (Join-Path $Root 'docs') -Destination (Join-Path $PublishDir 'docs') -Recurse

$releaseReadme = @"
PaperNote Desktop $Version ($Runtime)

1. Run PaperNote.Desktop.exe.
2. The app stores notebooks locally and does not require an account.
3. Read PRIVACY.md before using real notes.
4. Project and third-party license files are included in this package.

This is an independent open-source project and is not an official port of any commercial note product.
"@
Set-Content -LiteralPath (Join-Path $PublishDir 'README-Release.txt') -Value $releaseReadme -Encoding utf8
if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
Compress-Archive -Path (Join-Path $PublishDir '*') -DestinationPath $ZipPath -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash
Set-Content -LiteralPath "$ZipPath.sha256" -Value "$hash  $([IO.Path]::GetFileName($ZipPath))" -Encoding ascii
Write-Host "RELEASE PACKAGE READY: $ZipPath"
Write-Host "SHA256: $hash"
