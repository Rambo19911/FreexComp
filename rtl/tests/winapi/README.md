# Win32 binding tests

`Win_Kernel32Smoke` and `Win_Kernel32Files` call every function the `Windows` unit declares for kernel32 and
ntdll (RTL-860), except:

| Function | Covered by |
|---|---|
| `ExitProcess` | `System` calls it at the end of every test program (`Sys_HaltExitCode`) |
| `RaiseException` | needs exception handling (M2.4); `Exc_RaiseCatch` (R-061) |

`DefFilesMatchDeclarations` (Harness/DefFileTests.cs) checks the `.def` files against the declarations.
