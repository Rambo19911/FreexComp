# Platforms

A platform is the kind of system a program is built for. Each platform has a short id, such as `win64`.
A project lists the platforms it can be built for, and one of them is its active platform.

Today 64-bit Windows (`win64`) is the only installed platform: it is the only one FreexComp can build.
A project may list other platforms, for example `win32` for 32-bit Windows, but building them stops with
the error `E9016` until they are installed. `opc --version` shows the platforms that are installed.

## The platforms of a project
The key `platforms` of the project file lists them:

    "platforms": ["win64"]

- An id starts with a lower-case letter, followed by 1 to 15 lower-case letters or digits. Each id may be
  listed once. Anything else is the error `E9002` when the project is loaded.
- A project without the key has `["win64"]`.
- A listed platform does not need to be installed. The project still opens, and its other platforms build.

In the Project Manager the project has the node `Target Platforms` as its last entry. It shows one row per
listed platform, such as `Windows 64-bit (Win64)`. The active platform is shown in bold. A platform that is
not installed is greyed, with a warning sign.

| Context menu item | What it does |
|---|---|
| `Set as Active Platform` | makes this platform the active one (double-click and `Enter` do the same) |
| `Add Platform…` | adds an installed platform that the project does not list yet. With only `win64` installed there is nothing to add, and the item is disabled |
| `Remove Platform` | removes the platform from the project. The only platform cannot be removed. When the project has options for the platform, you are asked first, and the options are removed with it. Files already built stay on disk |

A platform that is not installed can only be added in the project file itself: open it with Project > View
Project Source.

Project > Options shows the listed platforms in the row `Target platform:`, the active one followed by
`(active)`.

## The active platform
The active platform is the platform that Build, Run, the debugger and Open Output Folder use. Choose it in
any of these places:

- the platform box on the tool bar; it can be opened when the project lists two or more platforms
- Project > Active Platform
- the `Target Platforms` node of the Project Manager

The status bar and the badge of the project row show the configuration and the active platform, for example
`Debug | Win64` and `Debug · Win64`. The editor follows the active platform too: code completion, error
marks and the greyed-out parts of `{$IFDEF}` blocks use the symbols and defines of that platform.

The choice is kept in the project file under the key `activePlatform`. It changes the project, so it is
saved with the project. Without the key, the active platform is the first listed one.

In a project group every project has its own active platform. Project > Active Platform sets the one of the
active project.

## Options for one platform
The key `platformOptions` holds settings that apply to one platform only. Example:

    "platforms": ["win64", "win32"],
    "activePlatform": "win64",
    "platformOptions": {
      "win32": { "defines": ["LEGACY"], "searchPaths": ["lib32"], "outputDir": "out/$(Platform)/$(Config)" }
    },

| Key | Meaning |
|---|---|
| `defines` | symbols defined for this platform |
| `searchPaths` | further folders searched for units |
| `libraryPaths` | further folders searched for import libraries |
| `outputDir` | the folder the program of this platform is built into |

- The lists are added after those of the project and of the configuration. `"!"` as the first entry of a
  list discards everything that came before it.
- `outputDir` may use the macros `$(Config)` (the configuration) and `$(Platform)` (the platform id), and no
  others. To write the characters `$(` themselves, type `$$(`.
- Every key of `platformOptions` must be a listed platform; otherwise the project does not load (`E9002`).
  Keys inside an entry that FreexComp does not know are kept.
- Project Options has no fields for these settings yet. Edit them in the project file.

## Where the outputs go

| Platform | Program | Intermediate files |
|---|---|---|
| `win64` | the configuration's `outputDir`, for example `bin/Debug` | `obj/Debug` |
| any other, with its own `outputDir` | that folder | `obj/<id>/Debug` |
| any other, without one | the configuration's `outputDir` with the id before the configuration name: `bin/Debug` becomes `bin/win32/Debug`; a folder that does not end with the configuration name gets the id appended: `build` becomes `build/win32` | `obj/<id>/Debug` |

The folders of `win64` stay the same when you add a platform, so nothing moves. Clean removes the files of
the active platform only. In build events, `$(Platform)` is the id of the platform being built, and
`$(OutputDir)` and `$(OutputPath)` point into its folder (see *Build events*).

## From the command line
`pbuild` builds the active platform of each project. `--platform` chooses another one for this call:

    pbuild App.pproj --platform win64

- A platform that the project does not list is the error `E9012`, which names the listed platforms.
- In a project group, every project builds the platform given; a project that does not list it fails with
  `E9012`.
- There is no value that builds all platforms at once: `all` is taken as a platform id.

## A platform that is not installed
A listed platform that is not installed is shown everywhere with `(not installed)` after its name: on the
tool bar, in the Project menu, in the status bar and on the project row. Its tooltip in the Project Manager
says that the project cannot be built for it here. You can still make it the active platform; the editor
then uses the symbols of 64-bit Windows together with that platform's own defines.

Building it does nothing for the project and reports

    Platform 'win32' is not installed (installed: win64)

as the error `E9016` in the Messages window. In a group build the project counts as failed. Choose `Win64`
again to build.
