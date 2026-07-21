[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$required = @(
    'LICENSE',
    'README.md',
    'THIRD-PARTY-NOTICES.md',
    'PRIVACY.md',
    'SECURITY.md',
    'CONTRIBUTING.md',
    'CODE_OF_CONDUCT.md',
    'BRANDING.md',
    'SUPPORT.md',
    '.gitignore',
    '.gitattributes',
    'docs/ANDROID.md',
    'docs/BUILD-ANDROID.md',
    'docs/RELEASE.md',
    'legal/third-party/PDFtoImage-LICENSE.txt',
    'legal/third-party/SkiaSharp-LICENSE.txt',
    'legal/third-party/SkiaSharp-THIRD-PARTY-NOTICES.txt',
    'legal/third-party/Apache-2.0.txt',
    'legal/third-party/Microsoft-MAUI-LICENSE.txt',
    'legal/third-party/Microsoft-MAUI-THIRD-PARTY-NOTICES.txt',
    'legal/third-party/Microsoft-Extensions-THIRD-PARTY-NOTICES.txt',
    'legal/third-party/Microsoft-Android-Bindings-LICENSE.md',
    'legal/third-party/Microsoft-Android-Bindings-THIRD-PARTY-NOTICES.txt',
    'legal/third-party/OpenSans-OFL.txt'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Root $_)) })
if ($missing.Count -gt 0) { throw "Missing required public files: $($missing -join ', ')" }

$publicRoots = @('src','tests','docs','scripts','videos','.github','legal') |
    ForEach-Object { Join-Path $Root $_ } |
    Where-Object { Test-Path -LiteralPath $_ }
$rootFiles = Get-ChildItem -LiteralPath $Root -File
$allowedExtensions = @('.cs','.xaml','.csproj','.md','.json','.ps1','.yml','.yaml','.txt','.html','.css','.js','.sln','.props')
$excludedPathPattern = [Regex]::Escape([string][IO.Path]::DirectorySeparatorChar) + '(bin|obj|artifacts|dist|node_modules|work)' + [Regex]::Escape([string][IO.Path]::DirectorySeparatorChar)
$publicFiles = @($rootFiles) + @(
    Get-ChildItem -LiteralPath $publicRoots -Recurse -File |
        Where-Object {
            $_.FullName -notmatch $excludedPathPattern -and
            $_.Extension -in $allowedExtensions
        }
)
$publicFiles = @($publicFiles | Sort-Object FullName -Unique)

# Exclude this verifier so its pattern declarations are not treated as product copy.
$contentFiles = @($publicFiles | Where-Object { $_.FullName -ne $PSCommandPath })
$copyPatterns = @(
    ([string][char]0x91CD + [char]0x70B9 + [char]0x590D + [char]0x523B),
    ([string][char]0x6A21 + [char]0x4EFF + [char]0x4F18 + [char]0x5148 + [char]0x7EA7),
    ([string][char]0x7167 + [char]0x642C + '(?:' + [char]0x4EA7 + [char]0x54C1 + '|' + [char]0x754C + [char]0x9762 + ')'),
    ([string][char]0x4EFF + [char]0x5236 + '(?:' + [char]0x4EA7 + [char]0x54C1 + '|' + [char]0x754C + [char]0x9762 + ')'),
    ([string][char]0x7ADE + [char]0x54C1 + '.*(?:' + [char]0x4E00 + [char]0x6BD4 + [char]0x4E00 + '|' + [char]0x590D + [char]0x523B + ')')
)
$copyHits = @($contentFiles | Select-String -Pattern ($copyPatterns -join '|'))
if ($copyHits.Count -gt 0) {
    $details = $copyHits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Third-party clone-oriented wording remains:`n$($details -join [Environment]::NewLine)"
}

$thirdPartyRoot = (Join-Path $Root 'legal\third-party')
$documentationFiles = @($contentFiles | Where-Object { $_.Extension -in '.md','.html','.yml','.yaml','.txt' -and -not $_.FullName.StartsWith($thirdPartyRoot,[StringComparison]::OrdinalIgnoreCase) })
$stalePatterns = @('\.NET 9','net9\.0','FormatVersion 13','PaperNote Desktop v[0-9]')
$staleHits = @($documentationFiles | Select-String -Pattern ($stalePatterns -join '|'))
if ($staleHits.Count -gt 0) {
    $details = $staleHits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Stale single-platform or format wording remains:`n$($details -join [Environment]::NewLine)"
}

$secretPattern = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}'
$secretHits = @($publicFiles | Select-String -Pattern $secretPattern)
if ($secretHits.Count -gt 0) {
    $details = $secretHits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Possible secret material found:`n$($details -join [Environment]::NewLine)"
}

$corruptionHits = @($publicFiles | Select-String -Pattern '\?{4,}')
if ($corruptionHits.Count -gt 0) {
    $details = $corruptionHits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Possible text encoding corruption found:`n$($details -join [Environment]::NewLine)"
}

$userPathPrefix = 'C:' + [IO.Path]::DirectorySeparatorChar + 'Users' + [IO.Path]::DirectorySeparatorChar
$userPathHits = @($publicFiles | Select-String -SimpleMatch $userPathPrefix)
if ($userPathHits.Count -gt 0) {
    $details = $userPathHits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Personal absolute path found:`n$($details -join [Environment]::NewLine)"
}

Write-Host 'OPEN SOURCE AUDIT PASS'
Write-Host 'Required documents and licenses, cross-platform wording, text encoding, secrets, and personal paths passed.'
