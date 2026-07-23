[CmdletBinding()]
param(
    [switch]$SkipRestore
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot 'android-common.ps1')
$environment = Get-PaperNoteAndroidEnvironment

$signingRoot = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PaperNote\Signing'
$keyStore = Join-Path $signingRoot 'papernote-release.keystore'
$passwordFile = Join-Path $signingRoot 'papernote-release.password.dpapi'
$keyAlias = 'papernote'
New-Item -ItemType Directory -Path $signingRoot -Force | Out-Null

function ConvertTo-PlainText([Security.SecureString]$SecureValue) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecureValue)
    try { [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

if (Test-Path -LiteralPath $passwordFile) {
    $securePassword = Get-Content -LiteralPath $passwordFile -Raw -Encoding ASCII | ConvertTo-SecureString
    $password = ConvertTo-PlainText $securePassword
} else {
    $password = ([guid]::NewGuid().ToString('N') + [guid]::NewGuid().ToString('N'))
    ConvertTo-SecureString $password -AsPlainText -Force | ConvertFrom-SecureString |
        Set-Content -LiteralPath $passwordFile -Encoding ASCII -NoNewline
}

if (-not (Test-Path -LiteralPath $keyStore)) {
    $keytool = (Get-Command keytool -ErrorAction Stop).Source
    & $keytool -genkeypair -v -keystore $keyStore -storetype PKCS12 -storepass $password -keypass $password `
        -alias $keyAlias -keyalg RSA -keysize 3072 -validity 10000 `
        -dname 'CN=PaperNote Release, OU=PaperNote, O=Jiejie Tech, C=CN'
    if ($LASTEXITCODE -ne 0) { throw 'Failed to create the local PaperNote Android signing key.' }
}

$project = Join-Path $environment.BuildRoot 'src\PaperNote.Mobile\PaperNote.Mobile.csproj'
[xml]$projectDefinition = Get-Content -LiteralPath $project -Raw -Encoding UTF8
$version = [string]$projectDefinition.Project.PropertyGroup.ApplicationDisplayVersion
$versionCode = [string]$projectDefinition.Project.PropertyGroup.ApplicationVersion
if ([string]::IsNullOrWhiteSpace($version) -or [string]::IsNullOrWhiteSpace($versionCode)) {
    throw 'Android version metadata is missing from PaperNote.Mobile.csproj.'
}
$packageName = "PaperNote-Android-$version"

$arguments = @(
    'build', $project,
    '-c', 'Release',
    '-f', 'net10.0-android',
    '-p:AndroidKeyStore=true',
    "-p:AndroidSigningKeyStore=$keyStore",
    "-p:AndroidSigningKeyAlias=$keyAlias",
    "-p:AndroidSigningKeyPass=$password",
    "-p:AndroidSigningStorePass=$password",
    '-p:AndroidUseApkSigner=true'
)
if ($SkipRestore) { $arguments += '--no-restore' }
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { throw 'Android Release build failed.' }

$outputDirectory = Join-Path $environment.BuildRoot 'src\PaperNote.Mobile\bin\Release\net10.0-android'
$sourceApk = Get-ChildItem -LiteralPath $outputDirectory -File -Filter '*-Signed.apk' |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $sourceApk) { throw "Signed APK was not found in $outputDirectory" }

$artifactDirectory = Join-Path $environment.RepoRoot 'artifacts\android'
New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
$artifactApk = Join-Path $artifactDirectory "$packageName.apk"
Copy-Item -LiteralPath $sourceApk.FullName -Destination $artifactApk -Force

$badging = (& $environment.Aapt2 dump badging $artifactApk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'Unable to read APK metadata.' }
$requiredMetadata = @(
    "name='com.jiejietech.papernote'",
    "versionCode='$versionCode'",
    "versionName='$version'",
    "minSdkVersion:'24'",
    "targetSdkVersion:'36'",
    "application: label='PaperNote' icon='res/mipmap"
)
foreach ($value in $requiredMetadata) {
    if ($badging -notlike "*$value*") { throw "APK metadata check failed: $value" }
}

$signature = (& $environment.ApkSigner verify --verbose --print-certs $artifactApk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $signature -notmatch 'Verified using v[12] scheme') {
    throw 'APK signature verification failed.'
}

$hash = (Get-FileHash -LiteralPath $artifactApk -Algorithm SHA256).Hash.ToLowerInvariant()
$hashPath = "$artifactApk.sha256"
Set-Content -LiteralPath $hashPath -Value "$hash  $([IO.Path]::GetFileName($artifactApk))" -Encoding ASCII
$metadataPath = Join-Path $artifactDirectory "$packageName.metadata.txt"
@(
    'PaperNote Android package metadata',
    'package=com.jiejietech.papernote',
    "versionName=$version",
    "versionCode=$versionCode",
    'minSdk=24',
    'targetSdk=36',
    'abis=armeabi-v7a,arm64-v8a,x86,x86_64',
    "sha256=$hash",
    '',
    'APK badging:',
    $badging.Trim(),
    '',
    'Signature verification:',
    $signature.Trim()
) | Set-Content -LiteralPath $metadataPath -Encoding UTF8

$releaseDirectory = Join-Path $environment.RepoRoot 'artifacts\releases'
$stagingDirectory = Join-Path $environment.RepoRoot "artifacts\staging\$packageName"
$zipPath = Join-Path $releaseDirectory "$packageName.zip"

function Assert-PathInsideRepository([string]$Path) {
    $repositoryPath = [IO.Path]::GetFullPath($environment.RepoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $resolvedPath = [IO.Path]::GetFullPath($Path)
    $requiredPrefix = $repositoryPath + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedPath.StartsWith($requiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe package path outside repository: $resolvedPath"
    }
}

Assert-PathInsideRepository $stagingDirectory
Assert-PathInsideRepository $zipPath
if (Test-Path -LiteralPath $stagingDirectory) {
    Remove-Item -LiteralPath $stagingDirectory -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null

Copy-Item -LiteralPath $artifactApk -Destination (Join-Path $stagingDirectory ([IO.Path]::GetFileName($artifactApk)))
Copy-Item -LiteralPath $hashPath -Destination (Join-Path $stagingDirectory ([IO.Path]::GetFileName($hashPath)))
Copy-Item -LiteralPath $metadataPath -Destination (Join-Path $stagingDirectory ([IO.Path]::GetFileName($metadataPath)))
Copy-Item -LiteralPath (Join-Path $environment.RepoRoot 'docs\ANDROID.md') -Destination (Join-Path $stagingDirectory 'ANDROID-GUIDE.md')
foreach ($document in @('LICENSE', 'PRIVACY.md', 'SECURITY.md', 'THIRD-PARTY-NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $environment.RepoRoot $document) -Destination (Join-Path $stagingDirectory $document)
}

$packageReadme = @(
    "PaperNote Android $version",
    '',
    '安装：',
    '1. 解压本压缩包。',
    "2. 在 Android 手机或平板上打开 $packageName.apk。",
    '3. 若系统阻止安装，只为当前文件管理器或浏览器临时开启“安装未知应用”。',
    '4. 安装完成后关闭该来源的安装权限。',
    '',
    '升级与数据安全：',
    '- 升级时直接覆盖安装，不要先卸载旧版本。',
    '- 卸载或清除应用数据会删除应用私有目录中的本地笔记。',
    '- 升级或卸载前，建议在应用内创建整库备份并复制到安全位置。',
    '',
    '文件校验：',
    "SHA-256: $hash",
    '',
    '详细说明请阅读 ANDROID-GUIDE.md。'
) -join [Environment]::NewLine
Set-Content -LiteralPath (Join-Path $stagingDirectory 'README-Android.txt') -Value $packageReadme -Encoding UTF8

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $stagingDirectory '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$zipHashPath = "$zipPath.sha256"
Set-Content -LiteralPath $zipHashPath -Value "$zipHash  $([IO.Path]::GetFileName($zipPath))" -Encoding ASCII

Write-Host "ANDROID APK READY: $artifactApk"
Write-Host "ANDROID PACKAGE READY: $zipPath"
Write-Host "APK SHA256: $hash"
Write-Host "ZIP SHA256: $zipHash"
Write-Host "SIGNING KEY (private, not in repository): $keyStore"
