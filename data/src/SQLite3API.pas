// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ SQLite3API - raw declarations of the SQLite C API and the dynamic loader of the stock sqlite3.dll
  (DAT-100...DAT-104), plus the UTF-16 <-> UTF-8 conversion for text that crosses the API as UTF-8
  (DAT-110). The library is loaded on first use with LoadLibraryW + GetProcAddress, so a missing library
  is an ordinary failure the caller can report, not a loader failure at program start. }
unit SQLite3API;

interface

uses
  SysUtils;

type
  Psqlite3 = Pointer;
  Psqlite3_stmt = Pointer;
  PUtf8 = PAnsiChar;
  sqlite3_int64 = Int64;

const
  SQLITE_OK = 0;
  SQLITE_ERROR = 1;
  SQLITE_BUSY = 5;
  SQLITE_LOCKED = 6;
  SQLITE_READONLY = 8;
  SQLITE_CANTOPEN = 14;
  SQLITE_CONSTRAINT = 19;
  SQLITE_MISUSE = 21;
  SQLITE_RANGE = 25;
  SQLITE_NOTADB = 26;
  SQLITE_ROW = 100;
  SQLITE_DONE = 101;

  SQLITE_CONSTRAINT_CHECK = 275;
  SQLITE_CONSTRAINT_FOREIGNKEY = 787;
  SQLITE_CONSTRAINT_NOTNULL = 1299;
  SQLITE_CONSTRAINT_PRIMARYKEY = 1555;
  SQLITE_CONSTRAINT_TRIGGER = 1811;
  SQLITE_CONSTRAINT_UNIQUE = 2067;
  SQLITE_CONSTRAINT_ROWID = 2579;

  SQLITE_INTEGER = 1;
  SQLITE_FLOAT = 2;
  SQLITE_TEXT = 3;
  SQLITE_BLOB = 4;
  SQLITE_NULL = 5;

  SQLITE_OPEN_READONLY = $00000001;
  SQLITE_OPEN_READWRITE = $00000002;
  SQLITE_OPEN_CREATE = $00000004;
  SQLITE_OPEN_URI = $00000040;
  SQLITE_OPEN_FULLMUTEX = $00010000;

  SQLITE_STATIC = Pointer(0);
  SQLITE_TRANSIENT = Pointer(-1);

  { The oldest library version accepted (DAT-104). }
  SQLiteMinVersionNumber = 3008000;
  SQLiteDefaultLibraryName = 'sqlite3.dll';

var
  { Required functions: all assigned after a successful SQLiteLoad, nil before. Named like the C functions. }
  sqlite3_libversion: function: PUtf8; cdecl;
  sqlite3_libversion_number: function: Integer; cdecl;
  sqlite3_open_v2: function(filename: PUtf8; out ppDb: Psqlite3; flags: Integer; zVfs: PUtf8): Integer; cdecl;
  sqlite3_close_v2: function(db: Psqlite3): Integer; cdecl;
  sqlite3_extended_result_codes: function(db: Psqlite3; onoff: Integer): Integer; cdecl;
  sqlite3_busy_timeout: function(db: Psqlite3; ms: Integer): Integer; cdecl;
  sqlite3_errcode: function(db: Psqlite3): Integer; cdecl;
  sqlite3_extended_errcode: function(db: Psqlite3): Integer; cdecl;
  sqlite3_errmsg: function(db: Psqlite3): PUtf8; cdecl;
  sqlite3_errstr: function(rc: Integer): PUtf8; cdecl;
  sqlite3_prepare_v2: function(db: Psqlite3; zSql: PUtf8; nByte: Integer; out ppStmt: Psqlite3_stmt;
    out pzTail: PUtf8): Integer; cdecl;
  sqlite3_finalize: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_reset: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_clear_bindings: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_step: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_stmt_readonly: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_bind_parameter_count: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_bind_parameter_name: function(stmt: Psqlite3_stmt; idx: Integer): PUtf8; cdecl;
  sqlite3_bind_parameter_index: function(stmt: Psqlite3_stmt; zName: PUtf8): Integer; cdecl;
  sqlite3_bind_null: function(stmt: Psqlite3_stmt; idx: Integer): Integer; cdecl;
  sqlite3_bind_int64: function(stmt: Psqlite3_stmt; idx: Integer; value: sqlite3_int64): Integer; cdecl;
  sqlite3_bind_double: function(stmt: Psqlite3_stmt; idx: Integer; value: Double): Integer; cdecl;
  sqlite3_bind_text16: function(stmt: Psqlite3_stmt; idx: Integer; text: Pointer; nBytes: Integer;
    destructor_: Pointer): Integer; cdecl;
  sqlite3_bind_text: function(stmt: Psqlite3_stmt; idx: Integer; text: PUtf8; nBytes: Integer;
    destructor_: Pointer): Integer; cdecl;
  sqlite3_bind_blob: function(stmt: Psqlite3_stmt; idx: Integer; data: Pointer; n: Integer;
    destructor_: Pointer): Integer; cdecl;
  sqlite3_bind_zeroblob: function(stmt: Psqlite3_stmt; idx: Integer; n: Integer): Integer; cdecl;
  sqlite3_column_count: function(stmt: Psqlite3_stmt): Integer; cdecl;
  sqlite3_column_name: function(stmt: Psqlite3_stmt; iCol: Integer): PUtf8; cdecl;
  sqlite3_column_decltype: function(stmt: Psqlite3_stmt; iCol: Integer): PUtf8; cdecl;
  sqlite3_column_type: function(stmt: Psqlite3_stmt; iCol: Integer): Integer; cdecl;
  sqlite3_column_int64: function(stmt: Psqlite3_stmt; iCol: Integer): sqlite3_int64; cdecl;
  sqlite3_column_double: function(stmt: Psqlite3_stmt; iCol: Integer): Double; cdecl;
  sqlite3_column_text16: function(stmt: Psqlite3_stmt; iCol: Integer): Pointer; cdecl;
  sqlite3_column_bytes16: function(stmt: Psqlite3_stmt; iCol: Integer): Integer; cdecl;
  sqlite3_column_blob: function(stmt: Psqlite3_stmt; iCol: Integer): Pointer; cdecl;
  sqlite3_column_bytes: function(stmt: Psqlite3_stmt; iCol: Integer): Integer; cdecl;
  sqlite3_last_insert_rowid: function(db: Psqlite3): sqlite3_int64; cdecl;
  sqlite3_changes: function(db: Psqlite3): Integer; cdecl;
  sqlite3_total_changes: function(db: Psqlite3): Integer; cdecl;
  sqlite3_get_autocommit: function(db: Psqlite3): Integer; cdecl;
  sqlite3_table_column_metadata: function(db: Psqlite3; zDbName, zTableName, zColumnName: PUtf8;
    out pzDataType, pzCollSeq: PUtf8; out pNotNull, pPrimaryKey, pAutoinc: Integer): Integer; cdecl;

  { Optional functions: nil when the library was built without them. }
  sqlite3_column_table_name: function(stmt: Psqlite3_stmt; iCol: Integer): PUtf8; cdecl;
  sqlite3_column_origin_name: function(stmt: Psqlite3_stmt; iCol: Integer): PUtf8; cdecl;
  sqlite3_column_database_name: function(stmt: Psqlite3_stmt; iCol: Integer): PUtf8; cdecl;
  sqlite3_interrupt: procedure(db: Psqlite3); cdecl;

{ Loads the library on first use (DAT-102: a name with a path separator is that exact file; else the
  directory of the executable, then the system search) and resolves the functions (DAT-104). True when the
  library is loaded, now or by an earlier call - the first successful load serves the whole process and
  LibraryName is not looked at again (DAT-103). False with the reason in ErrorText; nothing stays loaded. }
function SQLiteLoad(const LibraryName: string; out ErrorText: string): Boolean;
function SQLiteLoaded: Boolean;
{ '' when a library with this version number is accepted, else the reason (DAT-104). }
function SQLiteVersionError(VersionNumber: Integer; const Version: string): string;

{ UTF-8 bytes of S followed by one zero byte; an unpaired surrogate becomes U+FFFD. }
function Utf16ToUtf8(const S: string): TBytes;
{ The zero-terminated UTF-8 text at P; nil gives ''. Every maximal subpart of an ill-formed sequence
  becomes one U+FFFD. }
function Utf8ToUtf16(P: PUtf8): string;
{ The same for Len bytes at P (zero bytes included). }
function Utf8ToUtf16Len(P: PUtf8; Len: NativeInt): string;

implementation

{$POINTERMATH ON}

uses
  Windows;

var
  LoadLock: TRTLCriticalSection;
  { the loaded library; 0 until a load succeeded }
  Module: HMODULE;

{ -------------------------------------------------------------------------------------------------- loader }

function SQLiteVersionError(VersionNumber: Integer; const Version: string): string;
begin
  if VersionNumber < SQLiteMinVersionNumber then
    Result := 'SQLite library version ' + Version + ' is too old (3.8.0 or later required)'
  else
    Result := '';
end;

function HasPathSeparator(const Name: string): Boolean;
begin
  Result := (Pos('\', Name) > 0) or (Pos('/', Name) > 0);
end;

function OpenLibrary(const LibraryName: string): HMODULE;
begin
  if HasPathSeparator(LibraryName) then
    { as a full path: the system would search for a relative one in its own directories }
    Result := LoadLibraryW(PChar(ExpandFileName(LibraryName)))
  else
  begin
    Result := LoadLibraryW(PChar(ExtractFilePath(ParamStr(0)) + LibraryName));
    if Result = 0 then
      Result := LoadLibraryW(PChar(LibraryName));
  end;
end;

{ Stores the address of an exported function in a function variable (nil when Lib is 0); for a required
  function that is absent, Missing receives its name (the first one is kept). }
procedure Resolve(Lib: HMODULE; var Target; const Name: AnsiString; Required: Boolean; var Missing: string);
begin
  if Lib = 0 then
    Pointer(Target) := nil
  else
    Pointer(Target) := GetProcAddress(Lib, PAnsiChar(Name));
  if Required and (Pointer(Target) = nil) and (Missing = '') then
    Missing := string(Name);
end;

{ '' or the name of the first required function the library does not export. Lib = 0 clears every
  function variable. }
function ResolveAll(Lib: HMODULE): string;
begin
  Result := '';
  Resolve(Lib, sqlite3_libversion, 'sqlite3_libversion', True, Result);
  Resolve(Lib, sqlite3_libversion_number, 'sqlite3_libversion_number', True, Result);
  Resolve(Lib, sqlite3_open_v2, 'sqlite3_open_v2', True, Result);
  Resolve(Lib, sqlite3_close_v2, 'sqlite3_close_v2', True, Result);
  Resolve(Lib, sqlite3_extended_result_codes, 'sqlite3_extended_result_codes', True, Result);
  Resolve(Lib, sqlite3_busy_timeout, 'sqlite3_busy_timeout', True, Result);
  Resolve(Lib, sqlite3_errcode, 'sqlite3_errcode', True, Result);
  Resolve(Lib, sqlite3_extended_errcode, 'sqlite3_extended_errcode', True, Result);
  Resolve(Lib, sqlite3_errmsg, 'sqlite3_errmsg', True, Result);
  Resolve(Lib, sqlite3_errstr, 'sqlite3_errstr', True, Result);
  Resolve(Lib, sqlite3_prepare_v2, 'sqlite3_prepare_v2', True, Result);
  Resolve(Lib, sqlite3_finalize, 'sqlite3_finalize', True, Result);
  Resolve(Lib, sqlite3_reset, 'sqlite3_reset', True, Result);
  Resolve(Lib, sqlite3_clear_bindings, 'sqlite3_clear_bindings', True, Result);
  Resolve(Lib, sqlite3_step, 'sqlite3_step', True, Result);
  Resolve(Lib, sqlite3_stmt_readonly, 'sqlite3_stmt_readonly', True, Result);
  Resolve(Lib, sqlite3_bind_parameter_count, 'sqlite3_bind_parameter_count', True, Result);
  Resolve(Lib, sqlite3_bind_parameter_name, 'sqlite3_bind_parameter_name', True, Result);
  Resolve(Lib, sqlite3_bind_parameter_index, 'sqlite3_bind_parameter_index', True, Result);
  Resolve(Lib, sqlite3_bind_null, 'sqlite3_bind_null', True, Result);
  Resolve(Lib, sqlite3_bind_int64, 'sqlite3_bind_int64', True, Result);
  Resolve(Lib, sqlite3_bind_double, 'sqlite3_bind_double', True, Result);
  Resolve(Lib, sqlite3_bind_text16, 'sqlite3_bind_text16', True, Result);
  Resolve(Lib, sqlite3_bind_text, 'sqlite3_bind_text', True, Result);
  Resolve(Lib, sqlite3_bind_blob, 'sqlite3_bind_blob', True, Result);
  Resolve(Lib, sqlite3_bind_zeroblob, 'sqlite3_bind_zeroblob', True, Result);
  Resolve(Lib, sqlite3_column_count, 'sqlite3_column_count', True, Result);
  Resolve(Lib, sqlite3_column_name, 'sqlite3_column_name', True, Result);
  Resolve(Lib, sqlite3_column_decltype, 'sqlite3_column_decltype', True, Result);
  Resolve(Lib, sqlite3_column_type, 'sqlite3_column_type', True, Result);
  Resolve(Lib, sqlite3_column_int64, 'sqlite3_column_int64', True, Result);
  Resolve(Lib, sqlite3_column_double, 'sqlite3_column_double', True, Result);
  Resolve(Lib, sqlite3_column_text16, 'sqlite3_column_text16', True, Result);
  Resolve(Lib, sqlite3_column_bytes16, 'sqlite3_column_bytes16', True, Result);
  Resolve(Lib, sqlite3_column_blob, 'sqlite3_column_blob', True, Result);
  Resolve(Lib, sqlite3_column_bytes, 'sqlite3_column_bytes', True, Result);
  Resolve(Lib, sqlite3_last_insert_rowid, 'sqlite3_last_insert_rowid', True, Result);
  Resolve(Lib, sqlite3_changes, 'sqlite3_changes', True, Result);
  Resolve(Lib, sqlite3_total_changes, 'sqlite3_total_changes', True, Result);
  Resolve(Lib, sqlite3_get_autocommit, 'sqlite3_get_autocommit', True, Result);
  Resolve(Lib, sqlite3_table_column_metadata, 'sqlite3_table_column_metadata', True, Result);
  Resolve(Lib, sqlite3_column_table_name, 'sqlite3_column_table_name', False, Result);
  Resolve(Lib, sqlite3_column_origin_name, 'sqlite3_column_origin_name', False, Result);
  Resolve(Lib, sqlite3_column_database_name, 'sqlite3_column_database_name', False, Result);
  Resolve(Lib, sqlite3_interrupt, 'sqlite3_interrupt', False, Result);
end;

{ One load attempt; the caller holds the lock. '' or the reason of the failure. }
function TryLoad(const LibraryName: string): string;
var
  Lib: HMODULE;
  Missing: string;
begin
  Lib := OpenLibrary(LibraryName);
  if Lib = 0 then
  begin
    Result := 'Cannot load SQLite library "' + LibraryName + '"';
    Exit;
  end;
  Missing := ResolveAll(Lib);
  if Missing <> '' then
    Result := 'SQLite library is missing function "' + Missing + '"'
  else
    Result := SQLiteVersionError(sqlite3_libversion_number(), Utf8ToUtf16(sqlite3_libversion()));
  if Result = '' then
    Module := Lib
  else
  begin
    { a rejected library leaves no function variable behind }
    ResolveAll(0);
    FreeLibrary(Lib);
  end;
end;

function SQLiteLoad(const LibraryName: string; out ErrorText: string): Boolean;
begin
  ErrorText := '';
  EnterCriticalSection(LoadLock);
  try
    if Module = 0 then
      ErrorText := TryLoad(LibraryName);
    Result := Module <> 0;
  finally
    LeaveCriticalSection(LoadLock);
  end;
end;

function SQLiteLoaded: Boolean;
begin
  Result := Module <> 0;
end;

{ ---------------------------------------------------------------------------------------------- conversion }

function Utf16ToUtf8(const S: string): TBytes;
var
  I, N, Len: Integer;
  C: Cardinal;
begin
  Len := Length(S);
  { three bytes per code unit is the worst case (a surrogate pair is four bytes for two units) }
  SetLength(Result, Len * 3 + 1);
  N := 0;
  I := 1;
  while I <= Len do
  begin
    C := Ord(S[I]);
    Inc(I);
    if (C >= $D800) and (C <= $DBFF) and (I <= Len) and (Ord(S[I]) >= $DC00) and (Ord(S[I]) <= $DFFF) then
    begin
      C := $10000 + ((C - $D800) shl 10) + (Cardinal(Ord(S[I])) - $DC00);
      Inc(I);
    end
    else if (C >= $D800) and (C <= $DFFF) then
      C := $FFFD;
    if C < $80 then
    begin
      Result[N] := C;
      Inc(N);
    end
    else if C < $800 then
    begin
      Result[N] := $C0 or (C shr 6);
      Result[N + 1] := $80 or (C and $3F);
      Inc(N, 2);
    end
    else if C < $10000 then
    begin
      Result[N] := $E0 or (C shr 12);
      Result[N + 1] := $80 or ((C shr 6) and $3F);
      Result[N + 2] := $80 or (C and $3F);
      Inc(N, 3);
    end
    else
    begin
      Result[N] := $F0 or (C shr 18);
      Result[N + 1] := $80 or ((C shr 12) and $3F);
      Result[N + 2] := $80 or ((C shr 6) and $3F);
      Result[N + 3] := $80 or (C and $3F);
      Inc(N, 4);
    end;
  end;
  Result[N] := 0;
  SetLength(Result, N + 1);
end;

function Utf8ToUtf16Len(P: PUtf8; Len: NativeInt): string;
var
  Src: PByte;
  I: NativeInt;
  N, Need, K: Integer;
  B, C, Lo, Hi: Cardinal;
  Good: Boolean;
begin
  Result := '';
  if (P = nil) or (Len <= 0) then
    Exit;
  Src := PByte(P);
  { never more code units than bytes }
  SetLength(Result, Len);
  N := 0;
  I := 0;
  while I < Len do
  begin
    B := Src[I];
    Inc(I);
    { the well-formed sequences: lead byte, number of continuation bytes, range of the first of them }
    Need := 0;
    Lo := $80;
    Hi := $BF;
    Good := True;
    C := B;
    if B < $80 then
      Need := 0
    else if (B >= $C2) and (B <= $DF) then
    begin
      Need := 1;
      C := B and $1F;
    end
    else if (B >= $E0) and (B <= $EF) then
    begin
      Need := 2;
      C := B and $0F;
      if B = $E0 then
        Lo := $A0
      else if B = $ED then
        Hi := $9F;
    end
    else if (B >= $F0) and (B <= $F4) then
    begin
      Need := 3;
      C := B and $07;
      if B = $F0 then
        Lo := $90
      else if B = $F4 then
        Hi := $8F;
    end
    else
      Good := False;
    K := 0;
    while Good and (K < Need) do
    begin
      if (I < Len) and (Src[I] >= Lo) and (Src[I] <= Hi) then
      begin
        C := (C shl 6) or (Src[I] and $3F);
        Inc(I);
        Inc(K);
        Lo := $80;
        Hi := $BF;
      end
      else
        { the bytes read so far are one maximal subpart; decoding goes on at the offending byte }
        Good := False;
    end;
    if not Good then
      C := $FFFD;
    if C < $10000 then
    begin
      Inc(N);
      Result[N] := Char(C);
    end
    else
    begin
      C := C - $10000;
      Result[N + 1] := Char($D800 + (C shr 10));
      Result[N + 2] := Char($DC00 + (C and $3FF));
      Inc(N, 2);
    end;
  end;
  SetLength(Result, N);
end;

function Utf8ToUtf16(P: PUtf8): string;
var
  Len: NativeInt;
begin
  Len := 0;
  if P <> nil then
    while P[Len] <> #0 do
      Inc(Len);
  Result := Utf8ToUtf16Len(P, Len);
end;

initialization
  InitializeCriticalSection(LoadLock);

end.
