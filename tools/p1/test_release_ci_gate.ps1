$ErrorActionPreference = 'Stop'
$gate = Join-Path $PSScriptRoot 'release_ci_gate.ps1'

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Rejected([scriptblock] $Action, [string] $Message, [string] $Pattern) {
    $failure = $null
    try { & $Action } catch { $failure = $_.Exception.Message }
    Assert-True ($null -ne $failure) $Message
    Assert-True ($failure -match $Pattern) "$Message (got: $failure)"
}

$repo = Join-Path ([IO.Path]::GetTempPath()) "desktop-guides-gate-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $repo | Out-Null
function RepoGit([string[]] $Arguments) {
    $output = & git -C $repo @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed." }
    $output
}
function Commit([string] $Path, [string] $Text) {
    $file = Join-Path $repo $Path
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
    Set-Content -LiteralPath $file -Value $Text
    RepoGit @('add', '-A') | Out-Null
    RepoGit @('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', $Path) | Out-Null
    (RepoGit @('rev-parse', 'HEAD')).Trim()
}
try {
    RepoGit @('init', '-q', '-b', 'main') | Out-Null
    $code = Commit 'src/a.cs' 'one'
    $docs = Commit 'docs/notes.md' 'two'
    $readme = Commit 'README.md' 'three'
    $later = Commit 'src/a.cs' 'four'
    RepoGit @('checkout', '-q', '-b', 'side') | Out-Null
    $side = Commit 'src/b.cs' 'side'
    RepoGit @('checkout', '-q', 'main') | Out-Null
    RepoGit @('mv', 'src/a.cs', 'docs/a.cs') | Out-Null
    RepoGit @('-c', 'user.name=t', '-c', 'user.email=t@t', 'commit', '-q', '-m', 'move code into docs') | Out-Null
    $moved = (RepoGit @('rev-parse', 'HEAD')).Trim()

    $green = { param($sha) if ($sha -eq $code) { 'success' } else { $null } }
    $red = { param($sha) if ($sha -eq $code) { 'failure' } else { $null } }

    Push-Location $repo
    try {
        $relied = & $gate -Commit $code -MainRef main -RunConclusion $green
        Assert-True ($relied -eq $code) 'A commit with its own green run should pass.'
        $relied = & $gate -Commit $readme -MainRef main -RunConclusion $green
        Assert-True ($relied -eq $code) 'Docs-only commits after a green run should pass.'

        Assert-Rejected { & $gate -Commit $later -MainRef main -RunConclusion $green } `
            'A code change without its own green run must be rejected.' 'src/a.cs'
        $greenLater = { param($sha) if ($sha -eq $later) { 'success' } else { $null } }
        Assert-Rejected { & $gate -Commit $moved -MainRef main -RunConclusion $greenLater } `
            'Moving code into docs after a green run must be rejected.' 'need their own run'
        Assert-Rejected { & $gate -Commit $docs -MainRef main -RunConclusion $red } `
            'A failed run must be rejected even for a docs-only tag.' "concluded 'failure'"
        Assert-Rejected { & $gate -Commit $side -MainRef main -RunConclusion { param($sha) 'success' } } `
            'A commit that is not on main must be rejected.' 'is not on main'
        Assert-Rejected { & $gate -Commit $docs -MainRef main -RunConclusion { param($sha) $null } } `
            'No completed run at all must be rejected.' 'No completed windows-ci'
        Assert-Rejected { & $gate -Commit ('0' * 40) -MainRef main -RunConclusion $green } `
            'An unknown commit must be rejected.' 'Unknown commit'
        # T17.2: a run whose only failure is the workflow shard concludes
        # 'failure', and that blocks the commit it ran on.
        Assert-Rejected { & $gate -Commit $code -MainRef main -RunConclusion $red } `
            'A run that failed only its workflow shard must be rejected.' "concluded 'failure'"
    }
    finally {
        Pop-Location
    }
    # The gate reads the run's conclusion, so the workflow shard counts only
    # if push runs include it and no job may fail without failing the run.
    $ci = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '..\..\.github\workflows\windows-ci.yml')
    Assert-True ($ci.Contains('{"shard":"workflow","groups":"workflow"}')) `
        'The workflow shard is missing from the push and pull_request matrix.'
    # T17.2 C: push and PR runs test the portable exe too.
    Assert-True ($ci.Contains("|| '[""msix"",""portable""]'")) `
        'The portable lane is missing from the push and pull_request matrix.'
    Assert-True (-not $ci.Contains('continue-on-error')) 'A windows-ci job may fail without failing the run.'
    # The rejected cases leave git's expected failure in $LASTEXITCODE, which
    # the CI pwsh wrapper would report as the step's exit code.
    $global:LASTEXITCODE = 0
    Write-Output 'release_ci_gate: all cases passed.'
}
finally {
    Remove-Item -LiteralPath $repo -Recurse -Force
}
