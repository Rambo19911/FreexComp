# Toolchain proof: runs every spike against third_party/ and prints PASS/FAIL per check.
# Usage: powershell -ExecutionPolicy Bypass -File tests/spikes/run.ps1      Exit code 0 = all passed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$bin  = Join-Path $root 'third_party\llvm\bin'
$py   = Join-Path $root 'third_party\python-embed'
$sql  = Join-Path $root 'third_party\sqlite'
$out  = Join-Path ([IO.Path]::GetTempPath()) ("FreexComp.Spikes\" + [guid]::NewGuid())
New-Item -ItemType Directory -Force $out | Out-Null
$fail = 0
function Check($name, $ok) { if ($ok) { Write-Host "[PASS] $name" } else { Write-Host "[FAIL] $name"; $script:fail++ } }
function Tool([string]$exe, [string[]]$a) { & (Join-Path $bin $exe) $a; if ($LASTEXITCODE -ne 0) { throw "$exe failed: $a" } }
function Build($ll, $exe, $extra) {
    $obj = Join-Path $out ([IO.Path]::GetFileNameWithoutExtension($exe) + '.obj')
    $c = @('-Wno-override-module', '-c', $ll, '-o', $obj); if ($extra.clang) { $c += $extra.clang }
    Tool 'clang.exe' $c
    $l = @($obj, (Join-Path $out 'kernel32.lib')); if ($extra.link) { $l += $extra.link }
    $l += @('-entry:_start', '-subsystem:console', '-nodefaultlib', "-out:$(Join-Path $out $exe)")
    Tool 'lld-link.exe' $l
}
function Run($exe) { $p = Start-Process (Join-Path $out $exe) -Wait -PassThru -NoNewWindow -RedirectStandardOutput (Join-Path $out "$exe.txt"); return @{ Code = $p.ExitCode; Text = (Get-Content (Join-Path $out "$exe.txt") -Raw) } }

Tool 'llvm-dlltool.exe' @('-m', 'i386:x86-64', '-d', (Join-Path $PSScriptRoot 'kernel32.def'), '-l', (Join-Path $out 'kernel32.lib'))
Tool 'llvm-dlltool.exe' @('-m', 'i386:x86-64', '-d', (Join-Path $PSScriptRoot 'ntdll.def'), '-l', (Join-Path $out 'ntdll.lib'))

# 1. IR -> exe with no C runtime and no Windows SDK
Build (Join-Path $PSScriptRoot 'hello.ll') 'hello.exe' @{}
$r = Run 'hello.exe'; Check 'pipeline: .ll -> clang -> lld-link, no CRT (output + exit code 7)' ($r.Code -eq 7 -and $r.Text -match 'hello from FreexComp')

# 2. SEH exceptions: filter -> finally -> except, at -O0 and -O2
foreach ($o in '-O0', '-O2') {
    Build (Join-Path $PSScriptRoot 'seh.ll') "seh$o.exe" @{ clang = @($o); link = @((Join-Path $out 'ntdll.lib')) }
    $r = Run "seh$o.exe"; Check "SEH try/finally/except via __C_specific_handler ($o)" ($r.Code -eq 42 -and ($r.Text -replace '\s+', ' ').Trim() -eq 'filter finally caught')
}

# 3. Compiler-provided __chkstk (module asm), memset, _fltused with a 200 KB stack frame
foreach ($o in '-O0', '-O2') {
    Build (Join-Path $PSScriptRoot 'stk.ll') "stk$o.exe" @{ clang = @($o) }
    Check "__chkstk + memset + _fltused, large frame ($o)" ((Run "stk$o.exe").Code -eq 21)
}

# 4. Manifest embedding (variant 1: lld-link -manifest:embed)
$m = Join-Path $PSScriptRoot 'app.manifest'
Build (Join-Path $PSScriptRoot 'hello.ll') 'man.exe' @{ link = @('-manifest:embed', "-manifestinput:$m") }
$res = & (Join-Path $bin 'llvm-readobj.exe') --coff-resources (Join-Path $out 'man.exe') | Out-String
Check 'manifest embedded as RT_MANIFEST 1 and exe still runs' ($res -match 'MANIFEST \(ID 24\)' -and (Run 'man.exe').Code -eq 7)

# 5. Debug info: CodeView/PDB and DWARF, scripted DAP session through lldb-dap with the bundled Python only
$host_py = (Get-Command python -ErrorAction SilentlyContinue)
if ($null -eq $host_py) { Write-Host '[SKIP] debug session (needs a host python to drive the script)' } else {
    Copy-Item (Join-Path $PSScriptRoot 'dbg.pas') $out
    Push-Location $out
    try {
        foreach ($k in 'cv', 'dw') {
            & python (Join-Path $PSScriptRoot 'gen_dbg_ll.py') $k
            $link = if ($k -eq 'cv') { @('-debug', "-pdb:$(Join-Path $out 'dbg_cv.pdb')") } else { @('-debug:dwarf') }
            Build (Join-Path $out "dbg_$k.ll") "dbg_$k.exe" @{ link = $link }
            $t = & python -u (Join-Path $PSScriptRoot 'dap_session.py') (Join-Path $bin 'lldb-dap.exe') (Join-Path $out "dbg_$k.exe") (Join-Path $out 'dbg.pas') $py | Out-String
            $ok = $t -match "A2 breakpoint verified\s+\[\(True, 5\)\]" -and $t -match "\('a', '2'" -and $t -match "A7 after next\s+\('step', 'Add', 6\)" -and $t -match 'AgAAAA==' -and $t -match 'A8 exit code\s+10' -and $t -notmatch 'ERROR'
            Check "debug session: breakpoint, stack, locals, readMemory, step, exit ($k)" $ok
        }
    } finally { Pop-Location }
}

# 6. SQLite: official DLL called through an import library generated from its .def
if (Test-Path (Join-Path $sql 'sqlite3.dll')) {
    Tool 'llvm-dlltool.exe' @('-m', 'i386:x86-64', '-d', (Join-Path $sql 'sqlite3.def'), '-D', 'sqlite3.dll', '-l', (Join-Path $out 'sqlite3.lib'))
    Copy-Item (Join-Path $sql 'sqlite3.dll') $out
    Build (Join-Path $PSScriptRoot 'sql.ll') 'sql.exe' @{ link = @((Join-Path $out 'sqlite3.lib')) }
    $v = (Run 'sql.exe').Code; Check "SQLite DLL call (open :memory:, close, version $v)" ($v -ge 3040000)
    $exp = & (Join-Path $bin 'llvm-objdump.exe') -p (Join-Path $sql 'sqlite3.dll') | Out-String
    Check 'SQLite column-metadata exports present' ($exp -match 'sqlite3_column_table_name' -and $exp -match 'sqlite3_column_origin_name')
} else { Write-Host '[FAIL] third_party/sqlite/sqlite3.dll missing'; $fail++ }

if ($fail -eq 0) { Remove-Item -Recurse -Force $out; Write-Host "`nAll toolchain spikes passed." ; exit 0 }
Write-Host "`n$fail spike check(s) FAILED. Outputs kept in $out"; exit 1
