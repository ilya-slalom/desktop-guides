param(
    [Parameter(Mandatory = $true)]
    [string] $Commit,

    [string] $MainRef = 'origin/main',

    [string] $Repository = $env:GITHUB_REPOSITORY,

    # Maps a commit sha to 'success', 'failure' or $null (no completed push run).
    [scriptblock] $RunConclusion
)
# T17.1: a release is built only from main, on code that windows-ci passed.
# windows-ci skips docs-only pushes, so the nearest ancestor with a completed
# push run counts when everything after it is docs.
$ErrorActionPreference = 'Stop'
# Expected non-zero git exits (not an ancestor, unknown commit) are checked via
# $LASTEXITCODE; PowerShell 7 must not turn them into errors first.
$PSNativeCommandUseErrorActionPreference = $false

# Runs git with stderr discarded and without Stop semantics (Windows PowerShell
# 5.1 raises on native stderr under Stop), leaving the exit code in $LASTEXITCODE.
function Invoke-Git {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & git @args 2>$null } finally { $ErrorActionPreference = $previous }
}

if (-not $RunConclusion) {
    if (-not $Repository) { throw 'Pass -Repository or set GITHUB_REPOSITORY.' }
    $RunConclusion = {
        param($sha)
        $json = gh api "repos/$Repository/actions/workflows/windows-ci.yml/runs?head_sha=$sha&event=push&status=completed" `
            --jq '[.workflow_runs[] | {conclusion, created_at}] | sort_by(.created_at) | last | .conclusion'
        if ($LASTEXITCODE -ne 0) { throw "Couldn't read windows-ci runs for $sha." }
        $value = "$json".Trim()
        if ($value -eq '' -or $value -eq 'null') { $null } else { $value }
    }
}

$resolved = Invoke-Git rev-parse --verify "$Commit^{commit}"
if ($LASTEXITCODE -ne 0 -or -not $resolved) { throw "Unknown commit '$Commit'." }
$sha = "$resolved".Trim()
Invoke-Git merge-base --is-ancestor $sha $MainRef | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Commit $sha is not on $MainRef." }

$candidates = @(Invoke-Git rev-list --first-parent --max-count=50 $sha)
foreach ($candidate in $candidates) {
    $conclusion = & $RunConclusion $candidate
    if ($null -eq $conclusion) { continue }
    if ($conclusion -ne 'success') {
        throw "windows-ci's push run for $candidate concluded '$conclusion'."
    }
    $changed = @(Invoke-Git diff --name-only $candidate $sha | Where-Object { $_ })
    $code = @($changed | Where-Object { $_ -notlike 'docs/*' -and $_ -notlike '*.md' })
    if ($code.Count -gt 0) {
        throw "Files changed since the last green run ($candidate) need their own run: $($code -join ', ')"
    }
    return $candidate
}
throw "No completed windows-ci push run among the last $($candidates.Count) commits before $sha."
