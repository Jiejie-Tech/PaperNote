Set-StrictMode -Version Latest

function Get-PaperNoteAndroidEnvironment {
    [CmdletBinding()]
    param()

    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    $globalJson = Get-Content -LiteralPath (Join-Path $repoRoot 'global.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $requiredSdk = [string]$globalJson.sdk.version
    $actualSdk = (& dotnet --version).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualSdk -ne $requiredSdk) {
        throw "PaperNote requires .NET SDK $requiredSdk, but '$actualSdk' is active."
    }

    $workloads = (& dotnet workload list 2>&1 | Out-String)
    if ($LASTEXITCODE -ne 0 -or $workloads -notmatch '(?im)^\s*(maui-android|android)\s') {
        throw 'The .NET Android workload is not installed. Run: dotnet workload install maui-android'
    }

    $sdkCandidates = @(
        $env:ANDROID_SDK_ROOT,
        $env:ANDROID_HOME,
        (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Android\Sdk')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $androidSdk = $sdkCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (-not $androidSdk) { throw 'Android SDK was not found. Set ANDROID_SDK_ROOT or install the Android SDK.' }
    $androidSdk = (Resolve-Path -LiteralPath $androidSdk).Path

    $adb = Join-Path $androidSdk 'platform-tools\adb.exe'
    if (-not (Test-Path -LiteralPath $adb)) { throw "ADB was not found at $adb" }

    $buildToolsRoot = Join-Path $androidSdk 'build-tools'
    $buildTools = Get-ChildItem -LiteralPath $buildToolsRoot -Directory |
        Sort-Object { try { [version]$_.Name } catch { [version]'0.0' } } -Descending |
        Select-Object -First 1
    if (-not $buildTools) { throw "Android build-tools were not found under $buildToolsRoot" }
    $aapt2 = Join-Path $buildTools.FullName 'aapt2.exe'
    $apksigner = Join-Path $buildTools.FullName 'apksigner.bat'
    if (-not (Test-Path -LiteralPath $aapt2)) { throw "aapt2 was not found at $aapt2" }
    if (-not (Test-Path -LiteralPath $apksigner)) { throw "apksigner was not found at $apksigner" }

    $buildRoot = $repoRoot
    if ($repoRoot.ToCharArray() | Where-Object { [int]$_ -gt 127 }) {
        $asciiRoot = 'C:\PaperNoteWorkspace'
        if (Test-Path -LiteralPath $asciiRoot) {
            $item = Get-Item -LiteralPath $asciiRoot -Force
            $targets = @($item.Target) | ForEach-Object { if ($_ -is [array]) { $_ } else { [string]$_ } }
            $matches = $false
            foreach ($target in $targets) {
                if ([string]::IsNullOrWhiteSpace($target)) { continue }
                try {
                    if ((Resolve-Path -LiteralPath $target).Path -eq $repoRoot) { $matches = $true; break }
                } catch {}
            }
            if (-not $matches) { throw "$asciiRoot exists but does not point to the PaperNote workspace." }
        } else {
            New-Item -ItemType Junction -Path $asciiRoot -Target $repoRoot | Out-Null
        }
        $buildRoot = $asciiRoot
    }

    [pscustomobject]@{
        RepoRoot = $repoRoot
        BuildRoot = $buildRoot
        AndroidSdk = $androidSdk
        Adb = $adb
        Aapt2 = $aapt2
        ApkSigner = $apksigner
        DotnetSdk = $actualSdk
    }
}
