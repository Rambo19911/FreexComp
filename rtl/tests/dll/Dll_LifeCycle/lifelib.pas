// covers: RTL-950, RTL-951, RTL-952, RTL-955, RTL-963
library lifelib;
// the library describes itself in a buffer of the host; its units, its block and its exit procedure leave lines in
// the order file

uses SysUtils, LifeTypes, LifeA, LifeB, LifeC;

procedure Describe(Loaded: NativeUInt; Info: PLifeInfo); stdcall;
begin
  Info^.IsLibrary := Ord(IsLibrary);
  Info^.HInstanceIsLoaded := Ord(HInstance = Loaded);
  Info^.IsConsole := Ord(IsConsole);
  Info^.MainThreadID := MainThreadID;
  Info^.ParamCount := ParamCount;
  StrPLCopy(@Info^.ParamStr0[0], ParamStr(0), High(TLifePath));
  StrPLCopy(@Info^.ParamStr2[0], ParamStr(2), High(TLifePath));
  StrPLCopy(@Info^.ModuleName[0], GetModuleName(HInstance), High(TLifePath));
end;

// Output of the library is its own text file: what is still buffered is written when the library is unloaded
procedure Say; stdcall;
begin
  Writeln('library: a line of its own Output');
end;

procedure LibraryExit;
begin
  Log('exit procedure');
end;

exports
  Describe,
  Say;

begin
  Log('library block');
  ExitProc := @LibraryExit;
end.
