# What you may do with your applications

Programs you build with FreexComp are yours. This page says what that means and what little remains to
keep in mind. It is information, not legal advice.

## Your rights
- You may sell your programs, give them away or use them inside your company.
- You may keep your source code to yourself.
- You pay nothing to anyone for building or distributing them, and there is no limit on copies or users.
- You do not have to mention FreexComp in your program, its documentation or its About box.

## Why this is so
The runtime library, the user interface library and the data-access library become part of every program
that uses them. They are published under the Apache License 2.0 with the LLVM exception. The exception says
that when parts of the libraries are compiled into your program, the notice duties of the licence do not
apply to that program.

## What becomes part of your program

| Part | Licence | How it gets there |
|---|---|---|
| `rtl/src` | Apache-2.0 WITH LLVM-exception | the runtime library; its units are compiled into every program |
| `rtl/winapi/src` | Apache-2.0 WITH LLVM-exception | the declarations of the Windows functions and types; compiled into the programs that use them |
| `rtl/winapi/def` | Apache-2.0 WITH LLVM-exception | import libraries are generated from these lists of system function names |
| `ui-framework/src` | Apache-2.0 WITH LLVM-exception | the user interface library; compiled into programs that show windows |
| `data/src` | Apache-2.0 WITH LLVM-exception | the data-access library; compiled into programs that use databases |
| compiler output | Apache-2.0 WITH LLVM-exception | start-up code, unit initialisation tables, type information and the application manifest that the compiler generates for the program |
| LLVM | Apache-2.0 WITH LLVM-exception | machine code is produced by the code generator; no library of it is linked |
| SQLite | Public domain | `sqlite3.dll` is deployed beside programs that use the data-access library |
| Unicode Character Database | Unicode-3.0 | the character tables of the runtime library are generated from its data and compiled into programs that use them |

The directories are those of the library source below the FreexComp directory; see *Library source*.

## What you still have to do
- If you pass on the library source code itself, changed or unchanged, keep its licence file with it.
- A program that uses the data-access library is deployed together with the SQLite library file
  (`sqlite3.dll`). SQLite is in the public domain; you may ship it freely.
- The character tables inside your program are made from the Unicode Character Database. Its licence
  (Unicode License V3) lets you use and sell them freely; put its copyright and permission notice into your
  program's documentation. The notice is in the `licenses` folder, in the file `THIRD-PARTY-NOTICES.txt`.

## Windows libraries
Your program calls libraries that are part of Windows. They are already on every computer the program
runs on and are not distributed with it.

## Where the licence texts are
The `licenses` folder of the installation holds all licence texts. In the IDE, Help › Third-Party Licenses
shows the components the product itself is built from.
