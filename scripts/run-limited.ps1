# Runs `dotnet <args>` with a hard wall-clock limit and leaves nothing of its own running.
# While it waits it records (every 0.5 s) every process of its tree (test hosts, programs the tests start, their children). At the
# end - normal exit or limit reached - every recorded process that is still alive is killed. Processes of other runs
# (other agents) are never touched: a process counts as ours only when it was created after its recorded parent (process
# ids are reused), each recorded process is stopped on its own (no tree kill that would follow reused parent ids), and
# system processes are never stopped.
#
#   powershell -ExecutionPolicy Bypass -File scripts/run-limited.ps1 -Seconds 300 -Log out.log test <project> [args]
#
# Output of dotnet goes to -Log (stderr to <Log>.err); what this script did goes to <Log>.runner.
# Exit code: dotnet's exit code, or 124 when the limit was reached.
param(
    [Parameter(Mandatory)] [int] $Seconds,
    [Parameter(Mandatory)] [string] $Log,
    [string] $WorkDir = (Get-Location).ProviderPath,
    [Parameter(Mandatory, ValueFromRemainingArguments)] [string[]] $DotnetArgs
)
$runner = "$Log.runner"
Remove-Item -LiteralPath $runner -ErrorAction SilentlyContinue
$p = Start-Process -FilePath 'dotnet' -ArgumentList $DotnetArgs -WorkingDirectory $WorkDir -NoNewWindow -PassThru `
    -RedirectStandardOutput $Log -RedirectStandardError "$Log.err" -ErrorAction Stop
$null = $p.Handle # keeps the handle so that ExitCode is known after the exit
$tree = @{ $p.Id = 'dotnet' }
$born = @{ $p.Id = $p.StartTime }
$slack = [TimeSpan]::FromSeconds(1) # CreationDate and StartTime differ in precision
$system = @('winlogon', 'csrss', 'dwm', 'fontdrvhost', 'explorer', 'lsass', 'services', 'smss', 'wininit', 'svchost',
    'sihost', 'ctfmon', 'LockApp', 'System', 'Idle')

function Add-Descendants {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name, CreationDate -ErrorAction SilentlyContinue
    do {
        $added = $false
        foreach ($proc in $all) {
            # a child is younger than its parent: an older process whose parent id was reused is not ours
            if ($tree.ContainsKey([int]$proc.ParentProcessId) -and -not $tree.ContainsKey([int]$proc.ProcessId) -and
                $proc.CreationDate -ge $born[[int]$proc.ParentProcessId] - $slack) {
                $tree[[int]$proc.ProcessId] = $proc.Name
                $born[[int]$proc.ProcessId] = $proc.CreationDate
                $added = $true
            }
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
        "killed $($live.Name) $id" | Add-Content $runner
        Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
    }
}
if ($finished) {
    "exit $($p.ExitCode)" | Add-Content $runner
    exit $p.ExitCode
}
exit 124
