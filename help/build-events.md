# Build events

A project can run commands of your own as part of its build: before the build starts and after it has
finished. Typical uses are creating a source file with a tool, or copying the built program and its data
files into a test folder.

## Where to enter them
Project > Options > Build Events has two lists, `Before build` and `After build`. Each line is one command,
written as you would type it in a command prompt window.

By default the lists apply to every configuration. To give one configuration its own commands, choose it in
the `Configuration` box and tick `Use separate commands for this configuration`. That configuration then
uses only its own lists; the others are not affected.

## How the commands run
- The commands of `Before build` run first, in the order of the list. Then the project is compiled and
  linked. Then the commands of `After build` run.
- Every command runs through the Windows command interpreter, in the folder of the project file. Programs,
  batch files, redirection (`>`), and joining commands with `&&` work as in a command prompt window.
- What a command prints appears in the Output window. Nothing can be typed into a running command: a
  command that waits for input is given none.
- A command that ends with an exit code other than 0 stops the build. The build then reports the error
  `E9020` with the command and its exit code.
- The commands run every time the project is built, also when no source file has changed.
- `Run after-build commands` decides whether the `After build` list also runs when compiling failed.
- Cancel Build stops a running command.

## Macros
A command may contain these names; they are replaced before the command runs. Use `Insert Macro` to pick one.

| Macro | Replaced by |
|---|---|
| `$(ProjectDir)` | the folder of the project file |
| `$(ProjectName)` | the name of the project |
| `$(Config)` | the configuration being built, for example `Debug` |
| `$(Platform)` | the target platform, for example `win64` |
| `$(OutputDir)` | the folder the program is built into |
| `$(OutputPath)` | the full path of the built program |

Folders are given without a backslash at the end. Put quotes around a macro when the path may contain
spaces:

    copy /y "$(OutputPath)" "$(ProjectDir)\deploy"

A name that is not in the table stops the build with the error `E9022`. To write the characters `$(`
themselves, type `$$(`.

## Trust
Commands can do anything a program started by you can do. FreexComp therefore runs them only for a project
you have trusted, and asks again when the commands have changed — also after you changed them yourself.
See *Project commands and trust*.

## From the command line
`pbuild` runs the commands of a trusted project in the same way. `--trust-events` accepts them for one
call, `--no-events` builds without them, and `--verbose` shows each command with its macros replaced.
