# Library source

FreexComp comes with the complete source of its libraries. There is no separate, pre-compiled copy: the
files you can read are the files the compiler compiles into every program you build.

## Where the source is
Below the FreexComp directory:

| Directory | Contents |
|---|---|
| `rtl/src` | the runtime library (`System`, `SysUtils`, `Classes` and the other general units) and the list of scoped unit names, `unit-aliases.json` |
| `rtl/winapi/src` | the units that declare the Windows functions and types (`Windows`, `Messages` and others) |
| `ui-framework/src` | the UI framework (`Forms`, `Controls`, `StdCtrls` and the other visual units) |
| `data/src` | the data-access library and the data-aware controls |

Each unit is one file with the name of the unit: `SysUtils` is `rtl/src/SysUtils.pas`.

## Reading it
Place the cursor on a library name in the editor and choose Go to Declaration: the library file opens at
the declaration. With Run › Step Into Library Source switched on, Step Into also enters library routines
while you debug.

A library file opens protected, so that you do not change it by accident. Choose `Edit Anyway` on the bar
above the text when you do want to change it.

## Changing it
To change a library unit for one project only, open the unit and choose `Copy to Project` on the bar above
the text. FreexComp copies the file into the project's folder and adds it to the project. From then on
this project is built from the copy; every other project still uses the library unit. To go back, remove
the file from the project and delete it. Units that include other files (`System`, `Math`, `Character`)
cannot be copied this way.

You may also change the library source itself. Keep in mind:

- A changed library unit affects **every** project compiled afterwards, not only the one you are working on.
  Nothing has to be installed or registered: the next build of any project compiles the changed file.
- Keep a copy of the original file, or keep the directories under version control, so that you can go back.
- Do not rename or remove units: other library units and your projects refer to them by name.

## Checking your changes
After a change, rebuild all library units to see that they still compile together. In a PowerShell window in
the FreexComp directory:

    powershell -ExecutionPolicy Bypass -File scripts/build-runtime.ps1

The script compiles every unit of the four directories and shows the compiler's messages, each with file,
line and column. It ends with a summary such as

    build-runtime: 40 units, 0 errors, 0 warnings

and with exit code 0 when everything compiled, 1 when there were errors. The object files go to a temporary
directory that is removed afterwards; nothing is written into the source directories.

| Option | Meaning |
|---|---|
| `-Configuration Release` | compile optimised; the default, `Debug`, compiles without optimisation and with debug information |
| `-ObjDir <directory>` | keep the object files in this directory |
| `-Keep` | do not remove the temporary object directory; its path is printed |
| `-Home <directory>` | check another FreexComp directory |
| `-Opc <file>` | use this compiler (`opc.exe`) |

A unit that cannot be compiled on its own can be left out by adding its name and the reason to
`scripts/build-runtime.exclude.txt`. The list is empty as delivered.

The script needs no network connection.

What you may do with programs you build: see *What you may do with your applications*.
