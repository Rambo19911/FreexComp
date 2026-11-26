# Runs `dotnet <args>` with a hard wall-clock limit and leaves nothing of its own running.
# While it waits it records (every 0.5 s) every process of its tree (test hosts, programs the tests start, their children). At the
# end - normal exit or limit reached - every recorded process that is still alive is killed. Processes of other runs
# (other agents, other sessions) are never touched: a process counts as ours only when its recorded parent is alive in the
# same snapshot with the creation time recorded for it and the process was created after that parent (process ids are
# reused, also while a child outlives its parent), each recorded process is stopped on its own (no tree kill that would
# follow reused parent ids), and system processes are never stopped. MSBuild node reuse is switched off for the run, so
# `dotnet test` / `dotnet build` leave no build nodes behind.
#
#   powershell -ExecutionPolicy Bypass -File scripts/run-limited.ps1 -Seconds 300 -Log out.log test <project> [args]
#
# Output of dotnet goes to -Log (stderr to <Log>.err); what this script did goes to <Log>.runner (with the command line of
# every process it stopped).
# Exit code: dotnet's exit code, or 124 when the limit was reached.
param(
    [Parameter(Mandatory)] [int] $Seconds,
    [Parameter(Mandatory)] [string] $Log,
    [string] $WorkDir = (Get-Location).ProviderPath,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $DotnetArgs
)
$runner = "$Log.runner"
Remove-Item -LiteralPath $runner -ErrorAction SilentlyContinue
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$p = Start-Process -FilePath 'dotnet' -ArgumentList $DotnetArgs -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
    -RedirectStandardOutput $Log -RedirectStandardError "$Log.err" -ErrorAction Stop
$null = $p.Handle # keeps the handle so that ExitCode is known after the exit
$tree = @{ $p.Id = 'dotnet' }
$born = @{ $p.Id = $p.StartTime }
$lines = @{}
$slack = [TimeSpan]::FromSeconds(1) # CreationDate and StartTime differ in precision
$system = @('winlogon', 'csrss', 'dwm', 'fontdrvhost', 'explorer', 'lsass', 'services', 'smss', 'wininit', 'svchost',
    'sihost', 'ctfmon', 'LockApp', 'System', 'Idle')

function Add-Descendants {
    $all = @(Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, CreationDate, CommandLine -ErrorAction SilentlyContinue)
    $alive = @{}
    foreach ($proc in $all) { $alive[[int]$proc.ProcessId] = $proc.CreationDate }
    do {
        $added = $false
        foreach ($proc in $all) {
            $id = [int]$proc.ProcessId
            $parent = [int]$proc.ParentProcessId
            if (-not $tree.ContainsKey($parent) -or $tree.ContainsKey($id)) { continue }
            # the recorded parent must still be that process (same creation time) - an ended parent's id may be reused
            if (-not $alive.ContainsKey($parent) -or [Math]::Abs(($alive[$parent] - $born[$parent]).TotalSeconds) -gt 1) { continue }
            # a child is younger than its parent
            if ($proc.CreationDate -lt $born[$parent] - $slack) { continue }
            $tree[$id] = $proc.Name
            $born[$id] = $proc.CreationDate
            $lines[$id] = $proc.CommandLine
            $added = $true
        }
    } while ($added)
}

$deadline = (Get-Date).AddSeconds($Seconds)
$finished = $false
while (-not $finished -and (Get-Date) -lt $deadline) {
    Add-Descendants
    $finished = $p.WaitForExit(500)
}
if (-not $finished) {
    Add-Descendants
    "limit of $Seconds s reached: dotnet $($DotnetArgs -join ' ')" | Add-Content $runner
}
foreach ($id in @($tree.Keys)) {
    $live = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($live -and [Math]::Abs(($live.StartTime - $born[$id]).TotalSeconds) -gt 1) { continue } # the id was reused
    if ($live -and $system -contains $live.Name) { "left system process $($live.Name) $id" | Add-Content $runner; continue }
    if ($live -and -not ($finished -and $id -eq $p.Id)) {
        "killed $($live.Name) $id`: $($lines[$id])" | Add-Content $runner
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
}
if ($finished) {
    "exit $($p.ExitCode)" | Add-Content $runner
    exit $p.ExitCode
}
exit 124
