# Spike D6-0 (ADR 005, docs/spec/debugger/debug-info.md §9).
# Builds the hand-written IR of hello.pas / loop.pas / osexc.pas for both debug-info containers (CodeView/PDB and
# DWARF 5), then drives the debug adapter of the pinned LLVM package through scripted DAP sessions (parts A and B)
# and prints the PASS/FAIL tables. Runs offline; every debuggee and adapter process is terminated.
# Usage:  powershell -ExecutionPolicy Bypass -File debugger/tests/spike/run-spike.ps1 [-Keep]
# Exit code 0 iff part A (A1-A8 plus B2, which the spec treats as a gate step) passed for at least one container.
param([switch]$Keep)
$ErrorActionPreference = 'Stop'

$spike = $PSScriptRoot
$root  = (Resolve-Path (Join-Path $spike '..\..\..')).Path
$bin   = Join-Path $root 'third_party\llvm\bin'
$py    = Join-Path $root 'third_party\python-embed'
$out   = Join-Path ([IO.Path]::GetTempPath()) ('FreexComp.SpikeD6-0\' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $out | Out-Null

foreach ($t in 'clang.exe', 'lld-link.exe', 'llvm-dlltool.exe', 'lldb-dap.exe', 'lldb.exe') {
    if (-not (Test-Path (Join-Path $bin $t))) { Write-Host "[FAIL] $t missing in $bin"; exit 1 }
}
if (-not (Test-Path (Join-Path $py 'python3.dll'))) { Write-Host "[FAIL] bundled Python runtime missing in $py"; exit 1 }

function Invoke-Tool([string]$exe, [string[]]$a) {
    & (Join-Path $bin $exe) $a
    if ($LASTEXITCODE -ne 0) { throw "$exe failed ($LASTEXITCODE): $a" }
}
function First-Line([string]$exe, [string[]]$a, [string]$match = '\S') {
    $o = & $exe $a 2>$null | Where-Object { $_ -match $match } | Select-Object -First 1
    if ($null -eq $o) { return '(no output)' } else { return "$o".Trim() }
}

# --- tool versions (recorded in RESULT.md) ---
$savedPath = $env:PATH
$env:PATH = "$py;$savedPath"     # process-local: the debugger library needs the bundled Python runtime (ADR 005)
$versions = [ordered]@{
    'clang'        = First-Line (Join-Path $bin 'clang.exe') @('--version')
    'lld-link'     = First-Line (Join-Path $bin 'lld-link.exe') @('--version')
    'lldb'         = First-Line (Join-Path $bin 'lldb.exe') @('--version')
    'lldb-dap'     = First-Line (Join-Path $bin 'lldb-dap.exe') @('--version') 'LLVM version'
    'python-embed' = First-Line (Join-Path $py 'python.exe') @('--version')
    'dotnet'       = First-Line 'dotnet' @('--version')
}
$env:PATH = $savedPath
$versions.GetEnumerator() | ForEach-Object { '{0,-13} {1}' -f $_.Key, $_.Value } | Tee-Object -FilePath (Join-Path $out 'versions.txt')

# --- build: IR templates -> obj -> exe for each container ---
$utf8 = New-Object System.Text.UTF8Encoding($false)
$srcDir = $spike -replace '\\', '\\'
$flags = @{ cv = '!{i32 2, !"CodeView", i32 1}'; dw = '!{i32 7, !"Dwarf Version", i32 5}' }
$k32 = Join-Path $out 'kernel32.lib'
Invoke-Tool 'llvm-dlltool.exe' @('-m', 'i386:x86-64', '-d', (Join-Path $spike 'kernel32.def'), '-l', $k32)
foreach ($k in 'cv', 'dw') {
    $programs = @(
        @{ Name = 'hello'; Template = $(if ($k -eq 'cv') { 'hello.codeview.ll' } else { 'hello.dwarf.ll' }) },
        @{ Name = 'loop';  Template = 'loop.ll' },
        @{ Name = 'osexc'; Template = 'osexc.ll' })
    foreach ($p in $programs) {
        $md5 = (Get-FileHash -Algorithm MD5 (Join-Path $spike "$($p.Name).pas")).Hash.ToLowerInvariant()
        $text = [IO.File]::ReadAllText((Join-Path $spike $p.Template))
        $text = $text.Replace('@SRCDIR@', $srcDir).Replace('@MD5@', $md5).Replace('@CONTAINER_FLAG@', $flags[$k])
        $ll  = Join-Path $out "$($p.Name)_$k.ll"
        $obj = Join-Path $out "$($p.Name)_$k.obj"
        $exe = Join-Path $out "$($p.Name)_$k.exe"
        [IO.File]::WriteAllText($ll, $text, $utf8)
        Invoke-Tool 'clang.exe' @('-Wno-override-module', '-c', '-O0', $ll, '-o', $obj)
        $link = @($obj, $k32, '/entry:_start', '/subsystem:console', '/nodefaultlib', "/out:$exe")
        if ($k -eq 'cv') { $link += @('/debug', "/pdb:$(Join-Path $out "$($p.Name)_$k.pdb")") } else { $link += @('/debug:dwarf') }
        Invoke-Tool 'lld-link.exe' $link
    }
}

# Steps I4, I5 (no debug info): SEH try/except around a non-continuable RaiseException; unhandled access violation.
$nt = Join-Path $out 'ntdll.lib'
Invoke-Tool 'llvm-dlltool.exe' @('-m', 'i386:x86-64', '-d', (Join-Path $spike 'ntdll.def'), '-l', $nt)
Invoke-Tool 'clang.exe' @('-Wno-override-module', '-c', '-O0', (Join-Path $spike 'sehcatch.ll'), '-o', (Join-Path $out 'sehcatch.obj'))
Invoke-Tool 'lld-link.exe' @((Join-Path $out 'sehcatch.obj'), $k32, $nt, '/entry:_start', '/subsystem:console', '/nodefaultlib', "/out:$(Join-Path $out 'sehcatch.exe')")
Invoke-Tool 'clang.exe' @('-Wno-override-module', '-c', '-O0', (Join-Path $spike 'av.ll'), '-o', (Join-Path $out 'av.obj'))
Invoke-Tool 'lld-link.exe' @((Join-Path $out 'av.obj'), $k32, '/entry:_start', '/subsystem:console', '/nodefaultlib', "/out:$(Join-Path $out 'av.exe')")

# --- DAP script runner (throw-away C# console project, not part of the solution) ---
$runnerOut = Join-Path $out 'runner'
& dotnet build (Join-Path $spike 'runner\SpikeRunner.csproj') -c Release -o $runnerOut --nologo -v q -clp:NoSummary
if ($LASTEXITCODE -ne 0) { Write-Host '[FAIL] runner build failed'; exit 1 }

function Get-ToolProcessIds { @(Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($bin, [StringComparison]::OrdinalIgnoreCase) } | ForEach-Object { $_.Id }) }
$toolsBefore = Get-ToolProcessIds

& dotnet (Join-Path $runnerOut 'SpikeRunner.dll') --dap (Join-Path $bin 'lldb-dap.exe') --python $py --src $spike --out $out
$code = $LASTEXITCODE

# --- no stray processes: a debuggee still running from the output directory, or a debug adapter / debug server
#     started during this run and still alive, is a failure (and is killed). The runner kills every adapter
#     process tree it started (the debug server and the debuggee are descendants of the adapter). ---
$stray = @(Get-Process -ErrorAction SilentlyContinue | Where-Object {
    $_.Path -and ($_.Path.StartsWith($out, [StringComparison]::OrdinalIgnoreCase) -or
                  ($_.Path.StartsWith($bin, [StringComparison]::OrdinalIgnoreCase) -and $toolsBefore -notcontains $_.Id)) })
foreach ($s in $stray) { Write-Host "[FAIL] stray process $($s.Id) $($s.Path) (killed)"; Stop-Process -Id $s.Id -Force -ErrorAction SilentlyContinue }
if ($stray.Count -gt 0) { $code = 1 } else { Write-Host '[PASS] no debuggee, debug adapter or debug server process left running' }

if ($code -eq 0 -and -not $Keep) { Remove-Item -Recurse -Force $out }
else { Write-Host "Outputs and DAP transcripts kept in $out" }
exit $code
