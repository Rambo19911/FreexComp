# Spike D6-0 — result (tasks G-001, G-002)

Run on 2026-11-23, Windows 11 x64, offline, from the debugger worktree.
Spec: `docs/spec/debugger/debug-info.md` §9; decision rule: ADR 005.

**Decision: `DebugContainer = CodeView`.** Both containers pass part A (A1–A8 plus the gate step B2);
CodeView is the preferred container and is chosen. DWARF remains a working fallback.
**Output relay: `OutputRelayMode = Events`** (B4 passed; task G-059 is not needed).

## How to run

```
powershell -ExecutionPolicy Bypass -File debugger/tests/spike/run-spike.ps1 [-Keep]
```
The script builds every program for both containers (`clang -c -O0` → `lld-link /entry:_start
/subsystem:console /nodefaultlib` + `/debug /pdb:` or `/debug:dwarf`), builds the throw-away runner
(`runner/`, not in the solution, no packages), runs all DAP sessions, prints the tables, checks that no
debuggee, debug adapter or debug server process is left running, and exits 0 iff part A passed for at least
one container. Every DAP wait is bounded (10 s requests, 15 s stops, 5 s pause, 3 s disconnect); there are no
sleeps. `-Keep` keeps the build directory with one DAP transcript per session (`*.dap.log`).

| File | Purpose |
|---|---|
| `hello.pas`, `hello.codeview.ll`, `hello.dwarf.ll` | spec program; hand-written IR following debug-info.md §1–§5 (only the container flag differs) |
| `loop.pas`, `loop.ll` | endless loop (B5, B6) |
| `osexc.pas`, `osexc.ll` | `RaiseException($E0504153, 0, 0, nil)` with no SEH handler (B9) |
| `sehcatch.ll` | informational I4: non-continuable `RaiseException` caught by an SEH filter (the RTL raise mechanism) |
| `av.ll` | informational I5: unhandled write to address 0 |
| `kernel32.def`, `ntdll.def` | import libraries (`llvm-dlltool`) |
| `runner/` | C# DAP client + session script (`DapClient.cs`, `Program.cs`) |

The `.ll` files are templates: `@SRCDIR@` (absolute directory of the `.pas`), `@MD5@` (DBG-103 checksum) and,
for the variants, `@CONTAINER_FLAG@` are substituted by the script. The DAP messages are exactly those of
dap-usage.md §2 (`initialize`) and §3.1 (`launch`, including `initCommands` and `commandEscapePrefix`).

## Tool versions

| Tool | Version |
|---|---|
| clang | 23.1.5 (llvm-project 392dfff97d29b59d77e5cac6d4b6ef2585fe7117) |
| lld-link | LLD 23.1.5 (same revision) |
| lldb | 23.1.5 (same revision) |
| lldb-dap | LLVM version 23.1.5; liblldb 23.1.5 (same revision) |
| Python runtime (bundled, `third_party/python-embed`) | 3.13.15 |
| .NET SDK (runner) | 10.0.401 |

## Part A — gate

`CodeView-no-env` = CodeView without `LLDB_USE_NATIVE_PDB_READER=1` (information only).

| Step | Criterion | CodeView | DWARF | CodeView-no-env |
|---|---|---|---|---|
| A1 | `initialize` success ≤ 10 s, `supportsConfigurationDoneRequest` | PASS | PASS | PASS |
| A2 | `setBreakpoints hello.pas [12]` verified, line 12 | PASS | PASS | PASS |
| A3 | `stopped` ≤ 15 s, reason `breakpoint`, `threadId` | PASS | PASS | PASS |
| A4 | frame 0 `source.path` ends `hello.pas`, line 12 | PASS | PASS | PASS |
| A5 | locals `A`, `B` exist, `A` = `21` | PASS | PASS | PASS |
| A6 | `next` → reason `step`, line 13 | PASS | PASS | PASS |
| A7 | `B` = `42` | PASS | PASS | PASS |
| A8 | `continue` → `exited` 0 ≤ 15 s; `disconnect` answered | PASS | PASS | PASS |
| B2 | `readMemory &A` 4 bytes = `15 00 00 00` (gate step) | PASS | PASS | PASS |

Evidence (CodeView, abridged from the run output):
- A1 capabilities: configurationDone, functionBreakpoints, readMemory, writeMemory, setVariable,
  supportTerminateDebuggee, modules all `true`; `exceptionBreakpointFilters` only C++/ObjC filters.
- A2 `{"id":1,"instructionReference":"0x7FF6220A102C","line":12,"source":{"name":"hello.pas","path":"…\\spike\\hello.pas"},"verified":true}`
  (DWARF additionally returns `"column":3`). Verified immediately, no `breakpoint` event needed.
- A3 `{"allThreadsStopped":true,"description":"breakpoint 1.1","hitBreakpointIds":[1],"reason":"breakpoint","text":"breakpoint 1.1","threadId":13220}`
- A4 frame 0 name `_start` (CodeView: linkage name) / `hello` (DWARF: `DISubprogram.name`), path = absolute `.pas` path, line 12.
- A5 scopes `Locals(locals) | Globals() | Registers(registers)`; locals `A=21 (int, memoryReference=0x7F3A6FFB84)`, `B=0 (int, …)`.
- A8 `exited {"exitCode":0}` then `terminated`; `disconnect` → success.

## Part B — design assumptions (fallbacks)

| Step | Criterion | CodeView | DWARF | Fallback taken (chosen container) |
|---|---|---|---|---|
| B1 | `evaluate &A` (watch) → hex address | PASS | PASS | none. Note: `memoryReference` is **absent** in the `evaluate` response; the address comes from `result` (`'0x0000007f3a6ffb84'`, type `int *`), i.e. the second branch of DBG-082. `variables` entries do carry `memoryReference` (DBG-085 source also available). |
| B2 | `readMemory` 4 bytes = `15 00 00 00` | PASS | PASS | none (`data":"FQAAAA=="`, `address` echoed in upper-case hex) |
| B3 | `stepIn` at 12 → `Twice`, line 5 | PASS | **FAIL** (line 4) | none on CodeView. On DWARF the step-in stops on the prologue line 4 (parameter spill at `scopeLine`, DBG-117); one `next` then reaches line 5 (I3), so the UserCodeFilter fallback would work if DWARF were ever selected. |
| B4 | `output` category `stdout` containing `42` | PASS | PASS | none: `OutputRelayMode = Events`; stdout text arrives unchanged (`"42\r\n"`). DWARF sessions also emit category `important`. |
| B5 | loop: `pause` → `stopped` ≤ 5 s | PASS | PASS | none, **but** the stop is `{"reason":"exception","description":"interrupt","threadId":<new thread>}`, not `reason:"pause"`, and the thread is a newly created break-in thread (not the one `threads` listed). See spec gap S1. |
| B6 | `setBreakpoints` while running → `breakpoint` stop | PASS | PASS | none: DBG-066 holds (verified immediately, stop on line 8 on the original thread). |
| B7 | `setFunctionBreakpoints Twice` → top frame `Twice` | PASS | PASS | none. CodeView binds after the prologue (line 5, frame name `hello.Twice`); DWARF binds at the first line-table row (line 4, frame name `Twice`). |
| B8 | `writeMemory 63 00 00 00` at `&A`, continue → prints `198` | PASS | PASS | none: response `{"bytesWritten":4}`; re-read gives `YwAAAA==`; stdout `"198\r\n"`. |
| B9 | `RaiseException($E0504153)` → `exception` stop; continue → OS delivers it | **FAIL** | **FAIL** | first half passes: `{"reason":"exception","description":"Exception 0xe0504153 encountered at address 0x7ffbe9fd569a"}`, frame 0 `RaiseException` in KernelBase, frame 1 `Boom` line 5. Second half fails: after `continue` the exception is **swallowed** (the adapter resumes as if handled): `RaiseException` returns, the program runs on and exits with code 0. Fallback: DBG-247 cannot be applied as written; see spec gap S2 and G-Q13. |

### Informational steps

| Step | Question | CodeView | DWARF | Finding |
|---|---|---|---|---|
| I1 | does `initCommands` `settings set target.process.thread.step-avoid-regexp ""` work? | PASS | PASS | echoed on the console output, no error: keep it (dap-usage.md §3.1 **[spike]** confirmed). |
| I2 | function breakpoint by **linkage** name `hello.Twice` (raise-hook style, like `System._Raise`) | PASS | PASS | resolves and stops exactly like the short name. |
| I3 | DWARF: `next` after a step-in that landed on line 4 | — | PASS | reaches line 5 (fallback of B3 verified). |
| I4 | non-continuable `RaiseException` caught by an SEH filter (`__C_specific_handler`), run under the adapter | PASS | — | **no stop at all**, handler runs, exit code 42 (same as without the debugger). The adapter does not stop on exceptions that the program handles. |
| I5 | unhandled access violation, `continue` three times | FAIL (expected) | — | every `continue` re-executes the faulting instruction and stops again (`Exception 0xc0000005 … Access violation writing location 0x00000000`); the process never ends by itself and is terminated by `disconnect`. |

Conclusion from B9 + I4 + I5: on Windows the adapter reports an OS exception only when **no handler takes
it** (last-chance behaviour), and `continue` from such a stop resumes the faulting context with the exception
masked. Pascal exceptions handled by `try … except` are invisible to the adapter (notification must come
from the raise-hook function breakpoint, which works: B7, I2).

### Other observations for later tasks
- `lldb-dap` 23 on Windows starts `lldb-server` (console: `lldb-server-local_build`, `Launched '…' as process N`,
  `lldb-server exiting...`); the debuggee is a descendant of the adapter, so killing the adapter's process tree
  (DBG-020) covers it. The run checks that no adapter / server / debuggee process survives.
- `launch` response arrives **after** `configurationDone` (DBG-053 order confirmed); a `capabilities` event
  follows `launch` (`supportsModuleSymbolsRequest`, `supportsRestartRequest`, `supportsStepInTargetsRequest`).
- Frames without source come back with `source.path` like ``C:\Windows\System32\KernelBase.dll`RaiseException``,
  `sourceReference: 1`, `presentationHint: "deemphasize"` and a positive `line` (36). See spec gap S3.
- Basic types are reported as `int` on both containers; names and Pascal types must come from the sidecar (as designed, DBG-100).
- `LLDB_USE_NATIVE_PDB_READER=1` made no observable difference with this toolchain (column CodeView-no-env).
  Keeping it per DBG-005 is harmless.
- CodeView stack frame names are linkage names (`_start`, `hello.Twice`); DWARF uses `DISubprogram.name`
  (`hello`, `Twice`). DBG-075 (sidecar display names) covers both.

## Proposed ADR 005 update (text for the lead)

> **2026-11-23 – spike D6-0 re-run by the debugger agent (`debugger/tests/spike/run-spike.ps1`, hand-written IR
> of `hello.pas` per debug-info.md §1–§5, LLVM 23.1.5, bundled Python 3.13.15):** part A (A1–A8 and B2) passes
> on CodeView/PDB and on DWARF 5. **`DebugContainer = CodeView`** (confirmed). Part B: B1, B2, B4, B5, B6,
> B7, B8 pass on both; B3 passes on CodeView (on DWARF step-in stops on the prologue line, one `next` reaches
> the first statement); **B9 fails on both**: the adapter stops only on exceptions no handler takes
> (last chance; handled exceptions cause no stop), and `continue` from that stop resumes with the exception
> masked (a raised exception returns from `RaiseException`, a fault re-executes and stops again). Consequences:
> `OutputRelayMode = Events` (G-059 not needed); `pause` produces `reason:"exception"`,
> `description:"interrupt"` on a new break-in thread; OS-exception stops are treated as fatal: resuming after
> one terminates the debuggee (features.md DBG-247 updated); first-chance OS-exception notification needs card
> DBG-828. Function breakpoints resolve both short and linkage names (`System._Raise` style).

## Spec gaps and proposed corrections (for the lead; `docs/` is read-only for this agent)

| # | Where | Gap | Proposed text / simplest consistent behaviour |
|---|---|---|---|
| S1 | dap-usage.md DBG-091, architecture.md §7 | `pause` yields `reason:"exception"` + `description:"interrupt"`, and the stop thread is an injected break-in thread. DBG-091 excludes exception stops from the pause window, so the pause would be misclassified as an OS exception. | "A stop with `reason` `pause`, or `reason` `exception` and `description` `interrupt` (case-insensitive), is the pause stop. Its public `ThreadId` is the first thread of the `threads` response that is not the stopping thread (fallback: the stopping thread)." |
| S2 | features.md DBG-247/248/249, scripted-test.md (if it expects first-chance OS exceptions) | The adapter never reports first-chance exceptions and masks the exception on `continue`. | DBG-247: "A DAP stop with `reason: "exception"` (other than the pause stop) is an **unhandled** OS exception. It is published (`BreakOnOsExceptions` ignored, the program is about to die). `ContinueAsync`/step from this stop does not resume the debuggee: the session sends `disconnect` with `terminateDebuggee:true` and publishes `Terminated` with `ExitCode` = the exception code (cause `Exited`)." DBG-248: a `pascalExceptionCode` stop now means an unhandled Pascal exception (the hook stop, if enabled, came before it). First-chance OS notification only through card DBG-828. |
| S3 | dap-usage.md DBG-074 | Frames without source have a non-file `source.path` (``…dll`Func``), `sourceReference > 0` and `line > 0`; DBG-074 would turn them into source locations. | "`Location` is non-null iff `source.path` is present, `source.sourceReference` is absent or 0, the path is rooted and contains no `` ` `` and `line > 0`." |
| S4 | dap-usage.md DBG-082 example | `evaluate` returns no `memoryReference` for `&A`; the result text is lower-case hex with leading zeros (`0x0000007f3a6ffb84`). | Example only; the rule (fallback to the `0x` token of `result`) is right and is what passed. |
| S5 | features.md DBG-242 (2) | "without debug info the breakpoint is at the routine's first instruction": with debug info on CodeView the breakpoint is placed after the prologue (B7 line 5), on DWARF at the first row. At `-O0` the prologue only spills `RCX`, so the register is still valid; the wording should say "at or just after the prologue (RCX unchanged at `-O0`)". | wording only |

## QUESTIONS-draft (new items for `docs/spec/debugger/QUESTIONS-draft.md`)

| # | Question | Option taken (safest) | Decide |
|---|---|---|---|
| G-Q13 | Spike B9 failed on both containers: the adapter stops only on unhandled OS exceptions, and `continue` swallows them (program continues after `RaiseException`; a fault loops). B9 fails equally on DWARF, so the container choice is unaffected. | OS-exception stops are published as fatal; resuming terminates the debuggee with the exception code as exit code (S2). First-chance notification of OS exceptions waits for card DBG-828 (RTL vectored handler calling the raise hook). | lead / owner |
| G-Q14 | Pascal exceptions caught by `try … except` never stop in the adapter (I4); notification depends entirely on the raise-hook function breakpoint (works, B7/I2). If function breakpoints are unavailable, DBG-246's OS-exception path sees only unhandled Pascal exceptions. | Keep DBG-246 as the degraded path, documented as "unhandled only". | lead |
| G-Q15 | B3 passes on CodeView but fails on DWARF (prologue line stop). Recorded as required by debug-info.md §9.2. | No action while `DebugContainer = CodeView`; the UserCodeFilter auto-`next` fallback is verified (I3) should DWARF ever be selected. | — (note) |
