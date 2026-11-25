# Project commands and trust

A project file can list commands that belong to its build: commands to run before the build and commands
to run after it. Such commands can do anything a program started by you can do. A project you received
from someone else may therefore contain commands you do not want to run. FreexComp asks before it accepts
them.

## The question
When you build or run a project that has such commands and you have not trusted it yet, the IDE shows the
window `Project Commands`. It lists every command, complete and unchanged, for each project that is about
to be built. Nothing has been started at that point.

You have four answers:

| Answer | What happens |
|---|---|
| `Build Without Events` | The project is built now, without its commands. Nothing is remembered; the question comes again next time. This is the answer of the Enter key. |
| `Trust This Project` | The project is remembered as trusted, with the commands it has now, and is built. |
| `Trust Folder...` | You choose a folder. Every project in that folder and below it is trusted from now on. |
| `Cancel` | Nothing is built and nothing is remembered. |

A trusted project is asked about again when its commands change. The window then says that the commands
have changed since you trusted the project.

A project that has no such commands is never asked about.

## What trust allows today
This version does not run the commands of a project yet: a trusted project is built, and the build reports
that its commands were ignored. Trust decides which projects will be allowed to run their commands once
that is available, so answer the question as if they ran.

## Trusted locations
Tools > Options > Environment > Trusted Locations shows the folders and projects you have trusted. Select
an entry and choose `Remove` to stop trusting it, or `Remove All` to clear the list. Entries are added
only by the question above.

## Building from the command line
`pbuild` does not ask. A project with commands that is not trusted fails with the error `E9021`. Two
options decide for one call:

| Option | Meaning |
|---|---|
| `--trust-events` | Accept the commands of the projects in this build, this time only. |
| `--no-events` | Build without the commands. |

`pbuild` never changes the list of trusted locations.

## Where the list is kept
The list is the file `trust.json` in your FreexComp settings folder (`%APPDATA%\FreexComp`). It holds the
trusted folders, and for each trusted project its path and a check value of its commands. The IDE and
`pbuild` read the same file. If the file cannot be read, no project is trusted and the build says so.
