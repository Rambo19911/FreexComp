# Project groups

A project group holds several projects that you work on together, for example a program and the libraries
it uses. The group is a small file of its own (`.pgroup`); the projects stay where they are.

## The group file
A group file is a JSON file. Example `Tools.pgroup`:

    { "formatVersion": 1, "projects": ["App/App.pproj", "Lib/Lib.pproj"], "activeProject": "App/App.pproj" }

| Key | Meaning |
|---|---|
| `formatVersion` | always `1` |
| `projects` | the project files, in the order of the group, relative to the folder of the group file and written with `/`; a project on another drive is written with its full path |
| `activeProject` | the project that is loaded when the group is opened |

Keys of your own are kept when FreexComp writes the file. You never save a group by hand: the IDE writes
the group file at once after every change of its projects, their order or the active project.

## Opening a group
File > Open Project… opens a project file or a group file. A group also opens from the recent list, from
the command line of the IDE and by dropping the file on the main window (a file dropped on the Project
Manager while a project is open is added to that project as a file instead). When you open a single project,
it is a group of one; nothing is written, and everything works as before.

The Project Manager shows one root row per project, in the order of the group. A project whose file is
missing or cannot be read stays in the list, greyed, with a warning sign; its tooltip gives the path and
the reason. Such a project can be removed from the group, but not made active or built. A group file that
cannot be read is not opened, and what was open stays open.

File > Close Project closes the whole group.

## Adding and removing projects

| Command | What it does |
|---|---|
| Project > Add Existing Project… | adds one or more project files at the end of the group; a project that is already in the group is skipped |
| Project > Add New Project… | creates a new project, adds it and makes it the active project |
| `Remove from Group` | removes the project from the group; no file is deleted and nothing is asked. Removing the active project closes it first, and the next project becomes active |
| `Move Project Up`, `Move Project Down` | change the order of the group; dragging a project row between two others does the same |

The last three are in the context menu of a project row in the Project Manager.

As soon as a single project gets a second one, the projects form an unsaved group. Choose File > Save
Project Group As… to give it a file; the default name is the name of the first project followed by
`Group.pgroup`, in that project's folder. Closing an unsaved group asks whether to save it, with the answers
`Save`, `Don't Save` and `Cancel`.

## The active project
One project of the group is the active project. Its name is shown in bold. The editor, code completion,
the form designer, the debugger, Run and every project command work on it.

The other projects are listed. You can expand them and open their files, but such a file opens as a plain
document: it gets syntax checking only, and a unit with a form opens as code. Files of a listed project
cannot be added, removed, renamed or moved; make the project active first.

To switch, double-click the row of another project, or choose `Set as Active Project` from its context menu.
Switching closes one project and opens the other:

- Unsaved changes of the outgoing project are asked about once. `Cancel` leaves everything as it was.
- Its open documents are closed and its session is saved. The incoming project opens with its own session.
- The tool bar, the status bar, the window title and the Structure window follow; the Messages window is
  cleared. Documents that belong to neither project stay open.
- The group file records the new active project.

## Building
Project > Build, Rebuild and Clean, their tool-bar buttons and their shortcuts work on the active project.
The projects it depends on are built first. `Build`, `Rebuild` and `Clean` in the context menu of another
project's row work on that project and its dependencies.

Project > Build All Projects, Rebuild All Projects and Clean All Projects work on every project of the
group. They are available when the group has two or more projects. Every project uses its own active
configuration and platform. An unsaved group must be saved first; if you cancel the save, nothing is built.
The active project does not change, and nothing is run.

The build order follows the dependencies (see below). Projects that do not depend on each other are built
in the order of the group, the active project last.

## When a project fails
Tools > Options > Environment > General has the check box `Continue with the other projects when a project
fails`. It is off by default.

| Setting | What happens after a project fails |
|---|---|
| off | no further project is started. Every project that was still to come is reported as skipped: `App: skipped because the build stopped after Lib failed` |
| on | the projects that depend on the failed one are skipped: `App: skipped because Lib failed`. All other projects are still built |

The skipped lines appear in the Output window. When a build of several projects had a failure, its last line
and the status bar show a summary such as `2 succeeded, 1 failed, 1 skipped: App`. The errors of every
project are listed in the Messages window.

The setting is stored in your settings file as `build.continueAfterFailure` (`true` or `false`).

## Dependencies
A project builds after the projects it depends on. The list is the key `dependencies` of the project file,
with paths relative to the project's folder:

    "dependencies": ["../Lib/Lib.pproj"]

To edit it, choose Project > Dependencies…, or `Dependencies…` in the context menu of a project row. The
window `Dependencies of App` lists every other project of the group with a check box. A dependency that is
not in the group is shown greyed, with `(not in the group)` after it; untick it to remove it. To add a
project that is not in the group, add it to the group first. `OK` writes the list into the project file.

- A dependency that is not in the group is still built before the project that needs it.
- When a dependency fails, the projects that depend on it are not built.
- Dependencies decide the build order only. They do not add the other project's folders to the search paths.

## No circular dependencies
A project must not depend on itself, neither directly nor through other projects. When a tick in the
dependencies window would create such a circle, the window shows `This selection creates a circular
dependency:` with the projects in order, for example `App → Lib → App`, and `OK` stays disabled until you
remove a tick.

A circle that comes from a project file edited by hand stops the build with the error `E9006`, which names
the full path of each project in the circle. Nothing is built.

## From the command line
`pbuild` builds a group file like a project file:

    pbuild Tools.pgroup
    pbuild Tools.pgroup --target rebuild --stop-on-failure

| Option | Meaning for a group |
|---|---|
| `--target build`, `rebuild`, `clean` | builds, rebuilds or cleans every project of the group |
| `--target run` | builds the active project and the projects it depends on, then runs the active project |
| `--config <name>` | every project builds this configuration; a project that does not have it fails with `E9005` |
| `--platform <id>` | every project builds this platform; a project that does not list it fails with `E9012` |
| `--stop-on-failure` | starts no further project after the first one that fails |

Without `--stop-on-failure`, `pbuild` goes on with the projects that do not depend on the failed one: the
opposite of the IDE's default. `pbuild App.pproj` also builds the dependencies of `App` first, without a
group file.
