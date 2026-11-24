# FreexComp

A free, fully local RAD IDE for Windows: visual form designer, Object Pascal compiler, debugger and data access.

**Status:** early development – nothing usable yet.

## Requirements (development)
- Windows 10/11 x64
- .NET SDK 10.0
- git

No Visual Studio, MSVC or Windows SDK is needed.

## Setup
```
powershell -ExecutionPolicy Bypass -File scripts/bootstrap.ps1
```
Downloads the pinned LLVM toolchain, an embeddable Python runtime (needed by the debugger back end) and the
SQLite DLL into `third_party/`. Nothing is installed system-wide.

## Build and test
```
dotnet build FreexComp.sln
dotnet test FreexComp.sln
```

## License
FreexComp is licensed under the Apache License 2.0 (`LICENSE`).
The runtime libraries are licensed under Apache-2.0 WITH LLVM-exception (`LICENSE-RUNTIME`), so programs built with
FreexComp can be distributed without attribution duties from these libraries.
Third-party components are listed in `THIRD-PARTY-NOTICES.txt`.
