[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = Split-Path -Parent $PSScriptRoot
$targets = @('src', 'tests', 'scripts')
$extensions = @('.cs', '.xaml', '.ps1')
$failures = [Collections.Generic.List[string]]::new()

function Test-SuspiciousQuestionLiteral([string]$Line) {
    $quote = [char]0
    $escaped = $false
    $questionRun = 0
    foreach ($character in $Line.ToCharArray()) {
        if ($quote -eq [char]0) {
            if ($character -eq [char]34 -or $character -eq [char]39) {
                $quote = $character
                $questionRun = 0
            }
            continue
        }

        if ($escaped) {
            $escaped = $false
            $questionRun = 0
            continue
        }
        if ($character -eq [char]92) {
            $escaped = $true
            $questionRun = 0
            continue
        }
        if ($character -eq $quote) {
            $quote = [char]0
            $questionRun = 0
            continue
        }
        if ($character -eq [char]63) {
            $questionRun++
            if ($questionRun -ge 3) { return $true }
        } else {
            $questionRun = 0
        }
    }
    return $false
}

foreach ($target in $targets) {
    $directory = Join-Path $root $target
    foreach ($file in Get-ChildItem -LiteralPath $directory -Recurse -File | Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() }) {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text.Contains([char]0xFFFD)) {
            $failures.Add("$($file.FullName): contains Unicode replacement character U+FFFD")
        }
        $corruptionMarkers = @(
            (-join @([char]0x951F, [char]0x65A4, [char]0x62F7)),
            (-join @([char]0x70EB, [char]0x70EB, [char]0x70EB)),
            (-join @([char]0x5C6F, [char]0x5C6F, [char]0x5C6F))
        )
        if ($corruptionMarkers | Where-Object { $text.Contains($_) }) {
            $failures.Add("$($file.FullName): contains a common encoding-corruption marker")
        }

        $lineNumber = 0
        foreach ($line in [regex]::Split($text, '\r?\n')) {
            $lineNumber++
            if (Test-SuspiciousQuestionLiteral $line) {
                $failures.Add("$($file.FullName):${lineNumber}: suspicious question-mark text literal")
            }
        }
    }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    throw "Text integrity check failed with $($failures.Count) issue(s)."
}

Write-Host 'TEXT INTEGRITY CHECK PASS'
