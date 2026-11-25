# Resource files

A resource is data stored inside the built program: its icon, its version information, strings, or any
file of your own. Resources are described in a resource script (`.rc`), a text file. The script is compiled
to a compiled resource file (`.res`), which is linked into the program.

## A resource script
Each line names a resource, its type and its content. Example `app.rc`:

    1 ICON "app.ico"
    LOGO RCDATA "logo.png"

    STRINGTABLE
    BEGIN
      100, "Hello"
    END

Supported are `ICON`, `BITMAP`, `CURSOR`, `RCDATA` (a file, or data between `BEGIN` and `END`),
`STRINGTABLE`, `VERSIONINFO`, a manifest, types with a name or number of your own, the `LANGUAGE`
statement, `#include`, `#define`, `#if`, `#ifdef`, `#endif` and comments.

- Save the script as UTF-8. A script saved as UTF-16 is rejected with the error `E3113`.
- A file named in the script is searched in the folder of the script first, then in the search paths of
  the project.
- Languages are written as numbers, for example `LANGUAGE 9, 1`. Without a `LANGUAGE` statement a resource
  gets the language 1033 (English, United States).
- Windows shows the first icon for the program file: icons with a name come first, in alphabetical
  order, then icons with a number, the lowest first.

## In a project
Add the `.rc` or `.res` file to the project. A script is compiled on every build in which it or a file it
uses has changed; a `.res` file is linked as it is. Nothing else has to be set.

Two scripts of one project must not have the same file name: both would be compiled to the same file
(error `E3114`).

## In source code
A unit or program can name a resource file itself:

    {$R app.res}
    {$R icons.rc}
    {$R *.res}

`*` stands for the name of the source file, so `{$R *.res}` in `Main.pas` means `Main.res`. A name without
an extension gets `.res`. The file is searched relative to the source file that contains the line. The
resources become part of every program that uses the unit.

## The manifest
FreexComp puts an application manifest into every program by itself. A resource file that also contains a
manifest stops the build with the error `E3115`. Either remove the manifest from the resource file, or set
the manifest option of the project to none; then the manifest of your resource file is used.

## Reading a resource in the program
`TResourceStream` in the unit `Classes` reads a resource as a stream. The type constants such as
`RT_RCDATA` are in the unit `Windows`:

    var
      S: TResourceStream;
    begin
      S := TResourceStream.Create(HInstance, 'LOGO', RT_RCDATA);
      try
        // S.Size, S.Read, S.SaveToFile ...
      finally
        S.Free;
      end;
    end;

`CreateFromID` does the same for a resource with a number instead of a name. A resource that does not
exist raises `EResNotFound`.

## From the command line
`prc` compiles one script without a project:

    prc app.rc -o app.res

`-I <folder>` adds a folder to search, `-D NAME` or `-D NAME=value` defines a symbol for `#if` and
`#ifdef`, and `--verbose` shows the commands that are run.
