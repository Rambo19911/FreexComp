# Checks the system this script runs on against the support statement: runs the platform tests of the
# policy test project (tests/policy/FreexComp.Policy.Tests, Category=Platform) against the current tree and
# prints a three-line report:
#
#   Host: <system name> <major>.<minor>.<build> x64
#   Tests: total N, passed N, failed N, skipped N
#   Exit code: 0
#
#   -Output <file>  also write the report to this file (UTF-8, LF)
#   -Filter <expr>  test filter (default: Category=Platform)
#   -NoBuild        run the test project as it is built; do not build it first
#
# Exit code 0 only when the host line was reported and every selected test passed (none failed, none
# skipped, at least one ran); otherwise 1. Needs the .NET SDK and the toolchain that scripts/bootstrap.ps1
# fetched. Downloads nothing. Works in Windows PowerShell 5.1; keep this file ASCII.
param(
    [string]$Output,
    [string]$Filter = 'Category=Platform',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'tests\policy\FreexComp.Policy.Tests'
$hostFile = Join-Path $root 'artifacts\platform\host.txt'
$results = Join-Path ([System.IO.Path]::GetTempPath()) ('freexcomp-platform-' + [System.Guid]::NewGuid().ToString('N'))

# The host line must come from this run.
if (Test-Path -LiteralPath $hostFile) { Remove-Item -LiteralPath $hostFile -Force -Confirm:$false }

$arguments = @('test', $project, '--filter', $Filter, '--nologo', '--logger', 'trx;LogFileName=platform.trx', '--results-directory', $results)
if ($NoBuild) { $arguments += '--no-build' }

try {
    # Standard output only: its text is not part of the report, the counts come from the result file.
    & dotnet @arguments | Out-Null
    $testExit = $LASTEXITCODE

    $total = 0; $passed = 0; $failed = 0
    $trx = Join-Path $results 'platform.trx'
    if (Test-Path -LiteralPath $trx) {
        $counters = ([xml](Get-Content -LiteralPath $trx -Raw -Encoding UTF8)).TestRun.ResultSummary.Counters
        $total = [int]$counters.total
        $passed = [int]$counters.passed
        $failed = [int]$counters.failed
    }
    $skipped = $total - $passed - $failed

    $hostLine = $null
    if (Test-Path -LiteralPath $hostFile) {
        $hostLine = ([System.IO.File]::ReadAllText($hostFile)).Trim()
    }

    $ok = ($testExit -eq 0) -and ($total -gt 0) -and ($passed -eq $total) -and (-not [string]::IsNullOrEmpty($hostLine))
    if ($ok) { $exit = 0 } else { $exit = 1 }
    if ([string]::IsNullOrEmpty($hostLine)) { $hostLine = '(not reported)' }

    $report = @(
        "Host: $hostLine",
        "Tests: total $total, passed $passed, failed $failed, skipped $skipped",
        "Exit code: $exit"
    )
    $report | ForEach-Object { Write-Output $_ }
    if ($Output) {
        $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Output)
        [System.IO.File]::WriteAllText($target, ([string]::Join("`n", $report) + "`n"), (New-Object System.Text.UTF8Encoding($false)))
    }
}
finally {
    if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Recurse -Force -Confirm:$false }
}

exit $exit
