# Writing and calling a library

A library project builds a dynamic-link library (`<name>.dll`) and an import library (`<name>.lib`).
Create one with File › New › Project › Dynamic-link library.

## Exporting routines
Only routines listed under `exports` in the library's main file can be called from outside:

    function Add(A, B: Integer): Integer; stdcall;
    ...
    exports
      Add,
      Sub index 3 name 'Subtract';

`name` sets the exported name, `index` the ordinal. Declare the same calling convention (for example
`stdcall`) in the library and in every program that calls it.

## Calling a library
    function Add(A, B: Integer): Integer; stdcall; external 'Mathlib.dll';

When a project lists a library project under its dependencies, the build finds the import library and
copies the DLL next to the program, so Run works without copying anything by hand.

## Types that are safe across the boundary
Pass only integer, floating-point, Boolean, character and enumeration types, pointers, records made of
these, and null-terminated character pointers (`PChar`). Do not pass strings, dynamic arrays, objects,
interfaces or variants: they are managed by the memory manager of the side that created them.

## The memory rule
The program and the library do not share a memory manager. Memory is freed by the side that allocated
it. To return text, let the caller supply a buffer and its length, or export a routine that frees what
the library allocated.

## Initialization sections
The initialization and finalization sections of a library run while the operating system is loading or
unloading it. Keep them short: do not load other libraries, start or wait for threads, show windows or
wait on synchronisation objects there. Export an explicit start-up routine for such work.

## Running and debugging
A library cannot run by itself. Set Project › Options › Run › Host application (or Run › Run
Parameters) to a program that loads the library; Run Without Debugging builds the library and starts
that program. Starting a library under the debugger is not available yet; breakpoints are kept.

What you may do with programs you build: see *What you may do with your applications*.
