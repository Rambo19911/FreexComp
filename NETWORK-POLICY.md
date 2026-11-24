# Network behaviour of FreexComp

FreexComp works with the network cable unplugged. Nothing is downloaded, activated, checked or reported
when it is installed, started or used to build a program.

## What FreexComp never does
- No registration, sign-in, activation or licence check.
- No usage statistics, crash uploads or any other telemetry.
- No update check, online catalogue or online help.

## Features that can open a connection
None in this version. A later feature may connect only to an address that you entered or after a button
that you pressed; every such feature is listed here with the place where you remove the address again.

## Call sites
Maintained for the automated policy check: every place in the tools' own code that is able to open a
connection, including connections that never leave this computer.

| Feature | Card | Assembly | Type | Kind | Default | Shown and removed in |
|---|---|---|---|---|---|---|
| Console window for started programs (pipe to the helper process on this computer) | Phase 3 | FreexComp.Ide | FreexComp.Ide.Platform.Windows.ConsoleHost | pipe-local | always (local only) | — |
| Handing a file to the already running IDE (pipe between two IDE processes of the same user) | Phase 5 | FreexComp.Ide | FreexComp.Ide.Platform.Windows.InstanceHandOver | pipe-local | always (local only) | — |
