# Debugging and optimised builds

FreexComp debugs the program you build: breakpoints, stepping, the call stack, local variables and watches all
read the debug information the compiler writes into the program. How reliable that information is depends on
the configuration you build with.

## Which configuration to debug
A new project has two configurations:

| Configuration | Optimisation | Debug information |
|---|---|---|
| `Debug` | none | yes |
| `Release` | full | no |

Debug with the `Debug` configuration. Choose it in the configuration box on the tool bar or in the Project menu;
the status bar shows the active configuration, for example `Debug | Win64`. In this configuration every
statement keeps its own code and every variable keeps its place in memory, so the debugger shows what the
program really does: each line can take a breakpoint, Step Over moves one line at a time, and Locals and
Watches show the current values.

## Optimised builds
An optimised build is faster and smaller because the compiler rearranges the code: it keeps variables in
registers or removes them, merges and reorders statements and copies small routines into their callers. You
can tick `Generate debug information` for such a configuration in Project › Options › Compiler, but the
debugger then sees the optimised code, not your source line by line:

- a variable may be shown as unavailable, or with a value that is out of date;
- stepping may skip lines, return to a line it has already shown, or jump between lines in an unexpected order;
- a breakpoint on a line whose code was merged into another line or removed may never be hit;
- a routine that was copied into its caller does not appear as its own entry in the call stack.

This is expected behaviour of optimised code, not a fault in your program. To find a problem, switch back to
`Debug`, rebuild and debug there. If a problem appears only in the optimised build, narrow it down with output
of your own (for example `WriteLn` or a log file) rather than with the values the debugger shows.
