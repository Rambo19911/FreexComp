# Runs `dotnet <args>` with a hard wall-clock limit and leaves nothing of its own running.
# While it waits it records every process of its tree (test hosts, programs the tests start, their children). At the
# end - normal exit or limit reached - every recorded process that is still alive is killed. Processes of other runs
# (other agents) are never touched.
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
$tree = @{ $p.Id = 'dotnet' }

function Add-Descendants {
    $all = Get-CimInstance Win32_Process -Property ProcessId, ParentProcessId, Name -ErrorAction SilentlyContinue
    do {
        $added = $false
        foreach ($proc in $all) {
            if ($tree.ContainsKey([int]$proc.ParentProcessId) -and -not $tree.ContainsKey([int]$proc.ProcessId)) {
                $tree[[int]$proc.ProcessId] = $proc.Name
                $added = $true
            }
        }
    } while ($added)
}

$deadline = (Get-Date).AddSeconds($Seconds)
$finished = $false
while (-not $finished -and (Get-Date) -lt $deadline) {
    Add-Descendants
    $finished = $p.WaitForExit(2000)
}
if (-not $finished) {
    Add-Descendants
    "limit of $Seconds s reached: dotnet $($DotnetArgs -join ' ')" | Add-Content $runner
}
foreach ($id in @($tree.Keys)) {
    $live = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($live -and -not ($finished -and $id -eq $p.Id)) {
        "killed $($live.Name) $id" | Add-Content $runner
        & taskkill.exe /PID $id /T /F 2>&1 | Out-Null
    }
}
if ($finished) {
    "exit $($p.ExitCode)" | Add-Content $runner
    exit $p.ExitCode
}
exit 124
