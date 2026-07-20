[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$Root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

$required = @('LICENSE','README.md','THIRD-PARTY-NOTICES.md','PRIVACY.md','SECURITY.md','CONTRIBUTING.md','CODE_OF_CONDUCT.md','BRANDING.md','.gitignore','.gitattributes')
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Root $_)) })
if ($missing.Count -gt 0) { throw "Missing required public files: $($missing -join ', ')" }

$scanRoots = @('src','tests','docs') | ForEach-Object { Join-Path $Root $_ }
$textFiles = Get-ChildItem -LiteralPath $scanRoots -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.cs','.xaml','.csproj','.md','.json','.ps1','.yml','.yaml' }
$copyPatterns = @(
    ('重点' + [char]0x590D + [char]0x523B),
    ('模' + '仿优先级'),
    '照搬(?:产品|界面)',
    ('仿' + '制(?:产品|界面)'),
    ('竞品.*(?:一比一|' + [char]0x590D + [char]0x523B + ')')
)
$copyHits = @($textFiles | Select-String -Pattern ($copyPatterns -join '|'))
if ($copyHits.Count -gt 0) {
    $details = $copyHits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Third-party clone-oriented wording remains:
$($details -join [Environment]::NewLine)"
}

$publicRoots = @((Join-Path $Root 'src'),(Join-Path $Root 'tests'),(Join-Path $Root 'docs'),(Join-Path $Root 'scripts'))
$rootFiles = Get-ChildItem -LiteralPath $Root -File | Where-Object { $_.Extension -in '.md','.sln','.props','.json','.yml','.yaml' }
$publicFiles = @($rootFiles) + @(Get-ChildItem -LiteralPath $publicRoots -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Extension -in '.cs','.xaml','.csproj','.md','.json','.ps1','.yml','.yaml','.txt' })

$secretPattern = '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----|(?<![A-Za-z0-9])sk-[A-Za-z0-9_-]{20,}|ghp_[A-Za-z0-9]{20,}|xox[baprs]-[A-Za-z0-9-]{10,}'
$secretHits = @($publicFiles | Select-String -Pattern $secretPattern)
if ($secretHits.Count -gt 0) {
    $details = $secretHits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Possible secret material found:
$($details -join [Environment]::NewLine)"
}

$corruptionHits = @($publicFiles | Select-String -Pattern '\?{4,}')
if ($corruptionHits.Count -gt 0) {
    $details = $corruptionHits | ForEach-Object { "$($_.Path):$($_.LineNumber): $($_.Line.Trim())" }
    throw "Possible text encoding corruption found:
$($details -join [Environment]::NewLine)"
}

$userPathPrefix = 'C:' + [IO.Path]::DirectorySeparatorChar + 'Users' + [IO.Path]::DirectorySeparatorChar
$userPathHits = @($publicFiles | Select-String -SimpleMatch $userPathPrefix)
if ($userPathHits.Count -gt 0) {
    $details = $userPathHits | ForEach-Object { "$($_.Path):$($_.LineNumber)" }
    throw "Personal absolute path found:
$($details -join [Environment]::NewLine)"
}

Write-Host 'OPEN SOURCE AUDIT PASS'
Write-Host 'Required documents, clone wording, text encoding, secrets, and personal paths passed.'
