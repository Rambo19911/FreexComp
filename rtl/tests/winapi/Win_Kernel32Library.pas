// phase: 8
// covers: RTL-954, RTL-969
program Win_Kernel32Library;
{ Windows unit, library loading (dll-runtime.md RTL-954): SetErrorMode, LoadLibraryExW / LoadLibraryEx with their
  flags, GetProcAddress by name and by ordinal, FreeLibrary, DisableThreadLibraryCalls and the notification codes of
  a library entry routine. The ordinal of an export differs between Windows versions, so it is read from the export
  directory of the loaded module (PE format, learn.microsoft.com "PE Format", section "The .edata Section"). }
uses Windows, TestKit;

{$POINTERMATH ON}

function SameName(A, B: PAnsiChar): Boolean;
begin
  while (A^ <> #0) and (A^ = B^) do
  begin
    Inc(A);
    Inc(B);
  end;
  Result := A^ = B^;
end;

{ the ordinal under which Module exports Name; 0 if it has no such name }
function OrdinalOf(Module: HMODULE; Name: PAnsiChar): Cardinal;
var
  Base, Nt, Dir: PByte;
  Names: PCardinal;
  NameOrdinals: PWord;
  I, Count: Cardinal;
begin
  Result := 0;
  Base := PByte(Module);
  Nt := Base + PInteger(Base + $3C)^;
  { optional header at +24; export table = data directory 0, at +112 in the PE32+ header and +96 in the PE32 one }
  if PWord(Nt + 24)^ = $20B then
    Dir := Base + PCardinal(Nt + 24 + 112)^
  else
    Dir := Base + PCardinal(Nt + 24 + 96)^;
  Count := PCardinal(Dir + 24)^;
  Names := PCardinal(Base + PCardinal(Dir + 32)^);
  NameOrdinals := PWord(Base + PCardinal(Dir + 36)^);
  for I := 0 to Count - 1 do
    if SameName(PAnsiChar(Base + Names[I]), Name) then
    begin
      Result := PCardinal(Dir + 16)^ + NameOrdinals[I];
      Exit;
    end;
end;

procedure Constants;
begin
  Section('constants');
  CheckEqI(0, DLL_PROCESS_DETACH, 'DLL_PROCESS_DETACH');
  CheckEqI(1, DLL_PROCESS_ATTACH, 'DLL_PROCESS_ATTACH');
  CheckEqI(2, DLL_THREAD_ATTACH, 'DLL_THREAD_ATTACH');
  CheckEqI(3, DLL_THREAD_DETACH, 'DLL_THREAD_DETACH');
  CheckEqI($0001, SEM_FAILCRITICALERRORS, 'SEM_FAILCRITICALERRORS');
  CheckEqI($0002, SEM_NOGPFAULTERRORBOX, 'SEM_NOGPFAULTERRORBOX');
  CheckEqI($8000, SEM_NOOPENFILEERRORBOX, 'SEM_NOOPENFILEERRORBOX');
  CheckEqI($00000001, DONT_RESOLVE_DLL_REFERENCES, 'DONT_RESOLVE_DLL_REFERENCES');
  CheckEqI($00000002, LOAD_LIBRARY_AS_DATAFILE, 'LOAD_LIBRARY_AS_DATAFILE');
  CheckEqI($00000008, LOAD_WITH_ALTERED_SEARCH_PATH, 'LOAD_WITH_ALTERED_SEARCH_PATH');
  CheckEqI($00000010, LOAD_IGNORE_CODE_AUTHZ_LEVEL, 'LOAD_IGNORE_CODE_AUTHZ_LEVEL');
  CheckEqI($00000020, LOAD_LIBRARY_AS_IMAGE_RESOURCE, 'LOAD_LIBRARY_AS_IMAGE_RESOURCE');
  CheckEqI($00000040, LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE, 'LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE');
  CheckEqI($00000080, LOAD_LIBRARY_REQUIRE_SIGNED_TARGET, 'LOAD_LIBRARY_REQUIRE_SIGNED_TARGET');
  CheckEqI($00000100, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR, 'LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR');
  CheckEqI($00000200, LOAD_LIBRARY_SEARCH_APPLICATION_DIR, 'LOAD_LIBRARY_SEARCH_APPLICATION_DIR');
  CheckEqI($00000400, LOAD_LIBRARY_SEARCH_USER_DIRS, 'LOAD_LIBRARY_SEARCH_USER_DIRS');
  CheckEqI($00000800, LOAD_LIBRARY_SEARCH_SYSTEM32, 'LOAD_LIBRARY_SEARCH_SYSTEM32');
  CheckEqI($00001000, LOAD_LIBRARY_SEARCH_DEFAULT_DIRS, 'LOAD_LIBRARY_SEARCH_DEFAULT_DIRS');
  CheckEqI($00002000, LOAD_LIBRARY_SAFE_CURRENT_DIRS, 'LOAD_LIBRARY_SAFE_CURRENT_DIRS');
end;

procedure ErrorMode;
var
  Start, Mine: UINT;
begin
  Section('SetErrorMode');
  Mine := SEM_FAILCRITICALERRORS or SEM_NOOPENFILEERRORBOX;
  Start := SetErrorMode(Mine);
  CheckEqI(Mine, SetErrorMode(Start) and Mine, 'SetErrorMode returns the previous mode');
  CheckEqI(Start, SetErrorMode(Start), 'SetErrorMode restored the first mode');
end;

procedure ByNameAndOrdinal(Lib: HMODULE; Name: PAnsiChar; const Title: string);
var
  Ordinal: Cardinal;
  ByName: Pointer;
begin
  ByName := GetProcAddress(Lib, Name);
  Check(ByName <> nil, Title + ': GetProcAddress by name');
  Ordinal := OrdinalOf(Lib, Name);
  Check((Ordinal > 0) and (Ordinal <= $FFFF), Title + ': ordinal found in the export directory');
  Check(GetProcAddress(Lib, PAnsiChar(NativeUInt(Ordinal))) = ByName, Title + ': GetProcAddress by ordinal');
end;

procedure Loading;
var
  Kernel, User, Data, Missing: HMODULE;
  Start: UINT;
  Error: DWORD;
begin
  Section('LoadLibraryExW / GetProcAddress / FreeLibrary');
  Kernel := LoadLibraryExW('kernel32.dll', 0, 0);
  CheckEqI(Int64(GetModuleHandleW('kernel32.dll')), Int64(Kernel), 'LoadLibraryExW of a loaded module');
  ByNameAndOrdinal(Kernel, 'GetTickCount', 'kernel32');
  Check(GetProcAddress(Kernel, 'FreexCompNoSuchExport') = nil, 'GetProcAddress unknown name');
  Check(GetProcAddress(Kernel, PAnsiChar(NativeUInt($FFFF))) = nil, 'GetProcAddress unknown ordinal');
  Check(FreeLibrary(Kernel), 'FreeLibrary kernel32');

  User := LoadLibraryEx('user32.dll', 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
  Check(User <> 0, 'LoadLibraryEx alias with LOAD_LIBRARY_SEARCH_SYSTEM32');
  ByNameAndOrdinal(User, 'GetDesktopWindow', 'user32');
  Check(FreeLibrary(User), 'FreeLibrary user32');

  Data := LoadLibraryExW('user32.dll', 0, LOAD_LIBRARY_AS_DATAFILE);
  Check(Data <> 0, 'LoadLibraryExW as a data file');
  Check(FreeLibrary(Data), 'FreeLibrary of a data file mapping');

  Start := SetErrorMode(SEM_FAILCRITICALERRORS or SEM_NOOPENFILEERRORBOX);
  Missing := LoadLibraryExW('rtl-no-such-library.dll', 0, 0);
  Error := GetLastError;
  SetErrorMode(Start);
  CheckEqI(0, Int64(Missing), 'LoadLibraryExW of a missing file');
  CheckEqI(126, Error, 'last error of a missing file');
  Check(not FreeLibrary(0), 'FreeLibrary(0) fails');
end;

procedure ThreadCalls;
var
  Lib: HMODULE;
begin
  Section('DisableThreadLibraryCalls');
  { a library nothing else in this program uses, so the switch has no effect on the rest of the run }
  Lib := LoadLibraryExW('version.dll', 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
  Check(Lib <> 0, 'version.dll loaded');
  Check(DisableThreadLibraryCalls(Lib), 'DisableThreadLibraryCalls');
  Check(FreeLibrary(Lib), 'FreeLibrary version.dll');
  Check(not DisableThreadLibraryCalls(0), 'DisableThreadLibraryCalls(0) fails');
end;

begin
  Constants;
  ErrorMode;
  Loading;
  ThreadCalls;
  Done;
end.
