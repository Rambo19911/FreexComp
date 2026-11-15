# Win32 binding tests

`Win_Kernel32Smoke` and `Win_Kernel32Files` call every function the `Windows` unit declares for kernel32 and
ntdll (RTL-860), except the activation-context functions GetSystemDirectoryW, CreateActCtxW and ActivateActCtx,
which `Win_ActCtx` calls. `Win_User32Smoke` calls every function it declares for user32, `Win_Gdi32Smoke` every function it
declares for gdi32. `Win_CommCtrlSmoke` calls every function the `CommCtrl` unit declares for comctl32;
`Win_CommDlgDecl` calls the functions of `CommDlg` (comdlg32) and `ShellAPI` (shell32) that run unattended.
Exceptions:

| Function | Covered by |
|---|---|
| `ExitProcess` | `System` calls it at the end of every test program (`Sys_HaltExitCode`) |
| `RaiseException` | needs exception handling (M2.4); `Exc_RaiseCatch` (R-061) |
| `MessageBoxW` | shows a modal box that waits for a user; called by the UI framework's message dialogs, not by an unattended test |
| `MessageBeep` | plays a sound; called by the UI framework, not by an unattended test |
| `GetOpenFileNameW`, `GetSaveFileNameW`, `ChooseColorW`, `ChooseFontW` (and their unsuffixed aliases) | show a modal common dialog that waits for a user; `Win_CommDlgDecl` checks each signature by assigning it to a procedural variable of the documented type, `StructSizes` checks the records; called by the UI framework's dialog components |
| `ShellExecuteW` (and `ShellExecute`) | starts another program; signature checked by `Win_CommDlgDecl` as above |

`DefFilesMatchDeclarations` (Harness/DefFileTests.cs) checks the `.def` files against the declarations.
