param(
    [Parameter(Mandatory)] [string]$BaseSha,
    [Parameter(Mandatory)] [string]$HeadSha
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
foreach ($sha in @($BaseSha, $HeadSha)) {
    if ($sha -notmatch '^[0-9a-fA-F]{40}$') {
        throw "DCO verification requires full 40-character commit SHAs."
    }
    & git -C $repoRoot cat-file -e "$sha^{commit}"
    if ($LASTEXITCODE -ne 0) {
        throw "DCO verification commit is unavailable: $sha"
    }
}

$commits = @(& git -C $repoRoot rev-list --reverse "$BaseSha..$HeadSha")
if ($LASTEXITCODE -ne 0) {
    throw "Unable to enumerate pull-request commits for DCO verification."
}
if ($commits.Count -eq 0) {
    Write-Host "No commits require DCO verification."
    exit 0
}

$missing = New-Object System.Collections.Generic.List[string]
foreach ($commit in $commits) {
    $parents = ((& git -C $repoRoot show -s --format=%P $commit).Trim() -split '\s+')
    if ($parents.Count -gt 1) {
        continue
    }

    $message = (& git -C $repoRoot show -s --format=%B $commit) -join "`n"
    if ($message -notmatch '(?im)^Signed-off-by:\s+.+\s+<[^<>\s@]+@[^<>\s]+>$') {
        $missing.Add($commit)
    }
}

if ($missing.Count -gt 0) {
    throw "DCO sign-off is missing from commit(s): $($missing -join ', ')"
}

Write-Host "DCO verification passed for $($commits.Count) pull-request commit(s)."
