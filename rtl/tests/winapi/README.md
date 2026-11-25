# Win32 binding tests

`Win_Kernel32Smoke` and `Win_Kernel32Files` call every function the `Windows` unit declares for kernel32 and
ntdll (RTL-860), except the activation-context functions GetSystemDirectoryW, CreateActCtxW and ActivateActCtx,
which `Win_ActCtx` calls, and the library-loading functions LoadLibraryExW, DisableThreadLibraryCalls and SetErrorMode,
which `Win_Kernel32Library` calls, and GetThreadTimes, which `Win_GetThreadTimes` calls. `Win_User32Smoke`, `Win_User32Input`, `Win_User32Helpers` and `Win_GetGuiResources` call every function it
declares for user32 (one program grew too long and was split; the helpers and GetGuiResources were added later), `Win_Gdi32Smoke` every
function it declares for gdi32. `Win_CommCtrlSmoke` and `Win_CommCtrlViews` call every function the `CommCtrl` unit
declares for comctl32;
`Win_CommDlgDecl` calls the functions of `CommDlg` (comdlg32) and `ShellAPI` (shell32) that run unattended.
`LongPaths` and `LongPaths_Unaware` call the registry functions it declares for advapi32 (through the shared unit
`rtl/tests/LongPathKit.pas`, which reads the system long-path policy with them).
`AX_Layouts` calls every function the `ActiveX` unit declares for oleaut32, ole32 and shlwapi and checks its record
layouts (com-support.md section 2).
`Win_RichEditLayouts` checks the record layouts of the `RichEdit` unit (4-byte packing); `Win_RichEditSmoke` and
`Win_RichEditFormat` (the second half: one program grew too long) use its messages, flags and records against a
control of the engine created under a hidden window, through the shared unit `rtl/tests/RichEditKit.pas`. The unit declares
no function. Not exercised against the system: the `EN_LINK` notification (the engine sends it for mouse input over
a link only), so `EN_LINK` and the use of `TENLink` rest on the reference pages and the layout test.
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
