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
$artifactApk = Join-Path $artifactDirectory 'PaperNote-Android-1.0.0.apk'
Copy-Item -LiteralPath $sourceApk.FullName -Destination $artifactApk -Force

$badging = (& $environment.Aapt2 dump badging $artifactApk 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) { throw 'Unable to read APK metadata.' }
$requiredMetadata = @(
    "name='com.jiejietech.papernote'",
    "versionCode='1'",
    "versionName='1.0.0'",
    "minSdkVersion:'23'",
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
$metadataPath = Join-Path $artifactDirectory 'PaperNote-Android-1.0.0.metadata.txt'
@(
    'PaperNote Android package metadata',
    'package=com.jiejietech.papernote',
    'versionName=1.0.0',
    'versionCode=1',
    'minSdk=23',
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

Write-Host "ANDROID APK READY: $artifactApk"
Write-Host "SHA256: $hash"
Write-Host "SIGNING KEY (private, not in repository): $keyStore"
