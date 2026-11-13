# Per-agent git worktrees: every area agent edits only its own checkout, so agents never build each other's
# half-finished changes. Only the lead runs this script.
#
#   scripts/agent-worktree.ps1 create <area>   new worktree on branch agent/<area>, starting at main
#   scripts/agent-worktree.ps1 sync   <area>   rebase agent/<area> onto main (after other areas landed)
#   scripts/agent-worktree.ps1 land   <area>   rebase onto main, then fast-forward main to agent/<area>
#   scripts/agent-worktree.ps1 remove <area>   delete the worktree and its branch (must have nothing unlanded)
#   scripts/agent-worktree.ps1 list
#
# Worktrees live next to the repository: <repo>-worktrees\<area>. Shared, untracked material is linked in:
# third_party\ and docs\ are directory junctions to the main checkout (read-only by convention), CLAUDE.md and
# STATUS.md are copied on create/sync. Exit code 0 = done, 1 = refused or failed (message printed).
param(
    [Parameter(Mandatory = $true, Position = 0)][ValidateSet('create', 'sync', 'land', 'remove', 'list')][string]$Action,
    [Parameter(Position = 1)][string]$Area
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$root = "$repo-worktrees"
$areas = 'compiler', 'rtl', 'ui-framework', 'ide', 'debugger', 'data', 'lead-scratch'

function Fail($msg) { Write-Host "[FAIL] $msg"; exit 1 }
function Invoke-Git([string[]]$a, [string]$dir = $repo) {
    # git writes progress to stderr; in Windows PowerShell 5.1 that must not count as an error. Exit code decides.
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $out = & git.exe -C $dir @a 2>&1 } finally { $ErrorActionPreference = $old }
    if ($LASTEXITCODE -ne 0) { Fail ("git " + ($a -join ' ') + "`n" + ($out | Out-String)) }
    return $out
}
function Clean($dir) { return [string]::IsNullOrWhiteSpace((& git.exe -C $dir status --porcelain | Out-String)) }

if ($Action -eq 'list') { Invoke-Git @('worktree', 'list'); exit 0 }
if (-not $Area -or $areas -notcontains $Area) { Fail "area must be one of: $($areas -join ', ')" }
$wt = Join-Path $root $Area
$branch = "agent/$Area"

switch ($Action) {
    'create' {
        if (Test-Path $wt) { Fail "$wt already exists" }
        if (-not (Clean $repo)) { Write-Host "[warn] main checkout has uncommitted changes; the worktree starts from the last commit of main" }
        New-Item -ItemType Directory -Force $root | Out-Null
        Invoke-Git @('worktree', 'add', '-b', $branch, $wt, 'main') | Out-Null
        New-Item -ItemType Directory -Force (Join-Path $wt 'third_party') | Out-Null
        foreach ($d in 'llvm', 'python-embed', 'sqlite') {
            $src = Join-Path $repo "third_party\$d"
            if (Test-Path $src) { New-Item -ItemType Junction -Path (Join-Path $wt "third_party\$d") -Target $src | Out-Null }
        }
        New-Item -ItemType Junction -Path (Join-Path $wt 'docs') -Target (Join-Path $repo 'docs') | Out-Null
        foreach ($f in 'CLAUDE.md', 'STATUS.md') { Copy-Item (Join-Path $repo $f) (Join-Path $wt $f) }
        Write-Host "[ ok ] $wt on $branch"
    }
    'sync' {
        if (-not (Test-Path $wt)) { Fail "$wt does not exist" }
        if (-not (Clean $wt)) { Fail "$wt has uncommitted changes; commit them on $branch first" }
        Invoke-Git @('rebase', 'main') $wt | Out-Null
        foreach ($f in 'CLAUDE.md', 'STATUS.md') { Copy-Item (Join-Path $repo $f) (Join-Path $wt $f) -Force }
        Write-Host "[ ok ] $branch rebased onto main"
    }
    'land' {
        if (-not (Test-Path $wt)) { Fail "$wt does not exist" }
        if (-not (Clean $wt)) { Fail "$wt has uncommitted changes; commit them on $branch first" }
        # Uncommitted files in the main checkout are allowed: git refuses the fast-forward itself if any of them
        # would be overwritten by the landed commits.
        if (-not (Clean $repo)) { Write-Host "[warn] main checkout has uncommitted changes; landing only if git can fast-forward around them" }
        $cur = (Invoke-Git @('rev-parse', '--abbrev-ref', 'HEAD')) | Out-String
        if ($cur.Trim() -ne 'main') { Fail "main checkout is on '$($cur.Trim())', not main" }
        Invoke-Git @('rebase', 'main') $wt | Out-Null
        Invoke-Git @('merge', '--ff-only', $branch) | Out-Null
        $head = (Invoke-Git @('rev-parse', '--short', 'HEAD') | Out-String).Trim()
        Write-Host "[ ok ] main fast-forwarded to $branch ($head)"
    }
    'remove' {
        if (-not (Test-Path $wt)) { Fail "$wt does not exist" }
        if (-not (Clean $wt)) { Fail "$wt has uncommitted changes" }
        $ahead = (Invoke-Git @('rev-list', '--count', "main..$branch") | Out-String).Trim()
        if ($ahead -ne '0') { Fail "$branch has $ahead commit(s) not on main; land them first" }
        # Junctions first, with rmdir (removes the link only, never the target's contents).
        foreach ($j in 'docs', 'third_party\llvm', 'third_party\python-embed', 'third_party\sqlite') {
            $p = Join-Path $wt $j
            if (Test-Path $p) { & cmd.exe /c rmdir "$p" | Out-Null }
        }
        Invoke-Git @('worktree', 'remove', '--force', $wt) | Out-Null
        Invoke-Git @('branch', '-d', $branch) | Out-Null
        Write-Host "[ ok ] removed $wt and $branch"
    }
}
exit 0
