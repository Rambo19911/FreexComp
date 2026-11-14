# Win32 binding tests

`Win_Kernel32Smoke` and `Win_Kernel32Files` call every function the `Windows` unit declares for kernel32 and
ntdll (RTL-860). `Win_User32Smoke` calls every function it declares for user32, `Win_Gdi32Smoke` every function it
declares for gdi32. Exceptions:

| Function | Covered by |
|---|---|
| `ExitProcess` | `System` calls it at the end of every test program (`Sys_HaltExitCode`) |
| `RaiseException` | needs exception handling (M2.4); `Exc_RaiseCatch` (R-061) |
| `MessageBoxW` | shows a modal box that waits for a user; called by the UI framework's message dialogs, not by an unattended test |
| `MessageBeep` | plays a sound; called by the UI framework, not by an unattended test |

`DefFilesMatchDeclarations` (Harness/DefFileTests.cs) checks the `.def` files against the declarations.
