# Supported systems

FreexComp and the programs it builds run on these systems:

| System | Editions | Status |
|---|---|---|
| Windows 11 | x64 | Supported |
| Windows 10, version 1809 or later | x64 | Supported |
| Windows Server | — | Not supported yet |
| 32-bit Windows programs | — | Not supported yet |

## What a built program declares

Every program gets an application manifest, generated at build time and embedded in the executable file.
It states five things. The program declares Windows 10 and Windows 11 as the systems it supports, so the
system reports its true version to the program. The program is long-path aware: file names longer than
260 characters work, but only where the long-path policy of the system is switched on. A window program
is DPI aware per monitor, so it stays sharp when it moves between screens with different scaling. A window
program uses version 6 of the Common Controls, which gives its controls the current look. The program runs
with the rights of the user who starts it (execution level `asInvoker`) and never asks for elevation by
itself.

Three keys under `manifest` in the project file change this:

- `executionLevel`: `asInvoker` (default), `highestAvailable` or `requireAdministrator`.
- `dpiAwareness`: `perMonitorV2` (default for window programs), `system` or `unaware`. Console programs
  declare no DPI awareness unless the key is set.
- `customFile`: the path of your own manifest file, relative to the project or absolute. It replaces the
  generated manifest and cannot be combined with the other two keys.

## Checking the system at run time

A built program does not refuse to start on an older system. A program that depends on a newer release
asks for it with `TOSVersion` from the unit `SysUtils`:

    if TOSVersion.CheckBuild(22000) then
      Writeln('Windows 11 or later');

`TOSVersion.CheckBuild(22000)` is true on every release of Windows 11.

## Requirements of the development tools

The IDE, `opc` and `pbuild` run on the same systems and need no other installed software.
