// covers: DAT-100, DAT-101, DAT-102, DAT-103, DAT-104
program SqlApi;
{ The raw binding: the loader (search order, failures, load once), the version check, and the constants
  cross-checked against what the library itself reports (Api_Constants_MatchLibrary). The failing loads
  run first: a successful load stays for the life of the process. }
uses
  SysUtils, SQLite3API, DataTestUtils;

const
  OpenCreate = SQLITE_OPEN_READWRITE or SQLITE_OPEN_CREATE or SQLITE_OPEN_FULLMUTEX;

var
  { the buffer of the last conversion; every caller uses the pointer at once }
  Utf8Buffer: TBytes;

function Utf8(const S: string): PUtf8;
begin
  Utf8Buffer := Utf16ToUtf8(S);
  Result := PUtf8(@Utf8Buffer[0]);
end;

{ Runs every statement of a script on a raw handle; the result code of the first failing call, else SQLITE_OK. }
function Run(Db: Psqlite3; const SQL: string): Integer;
var
  Buf: TBytes;
  P, Tail: PUtf8;
  Stmt: Psqlite3_stmt;
begin
  Buf := Utf16ToUtf8(SQL);
  P := PUtf8(@Buf[0]);
  Result := SQLITE_OK;
  while P^ <> #0 do
  begin
    Result := sqlite3_prepare_v2(Db, P, -1, Stmt, Tail);
    if Result <> SQLITE_OK then
      Exit;
    if Stmt = nil then
      Break;
    repeat
      Result := sqlite3_step(Stmt);
    until Result <> SQLITE_ROW;
    sqlite3_finalize(Stmt);
    if Result <> SQLITE_DONE then
      Exit;
    Result := SQLITE_OK;
    P := Tail;
  end;
end;

function ErrStr(Code: Integer): string;
begin
  Result := Utf8ToUtf16(sqlite3_errstr(Code));
end;

procedure TestFailingLoads;
var
  Err: string;
begin
  Section('failing loads');
  Check(not SQLiteLoaded, 'nothing is loaded at program start');
  Check(not Assigned(sqlite3_libversion), 'the function variables are nil');

  Check(not SQLiteLoad('nope.dll', Err), 'a missing library does not load');
  CheckEq(Err, 'Cannot load SQLite library "nope.dll"', 'the reason names the library');
  Check(not SQLiteLoad('.\sqlite3.dll', Err), 'a name with a path separator is that exact file only');
  CheckEq(Err, 'Cannot load SQLite library ".\sqlite3.dll"', 'the reason names the path');
  Check(not SQLiteLoaded, 'still nothing loaded');

  Check(not SQLiteLoad('kernel32.dll', Err), 'a library without the functions does not load');
  CheckEq(Err, 'SQLite library is missing function "sqlite3_libversion"', 'the reason names the first missing function');
  Check(not SQLiteLoaded, 'a rejected library is not kept');
  Check(not Assigned(sqlite3_libversion), 'the function variables are nil again');

  CheckEq(SQLiteVersionError(3007017, '3.7.17'), 'SQLite library version 3.7.17 is too old (3.8.0 or later required)',
    'version 3.7.17 is too old');
  CheckEq(SQLiteVersionError(3007999, '3.7.999'), 'SQLite library version 3.7.999 is too old (3.8.0 or later required)',
    'the last version below the limit');
  CheckEq(SQLiteVersionError(3008000, '3.8.0'), '', 'version 3.8.0 is accepted');
  CheckEq(SQLiteVersionError(3054001, '3.54.1'), '', 'a current version is accepted');
end;

procedure TestLoad;
var
  Err: string;
  N: Integer;
begin
  Section('load');
  Err := 'x';
  Check(SQLiteLoad('sqlite3.dll', Err), 'the library beside the executable loads');
  CheckEq(Err, '', 'no reason on success');
  Check(SQLiteLoaded, 'loaded');
  Check(SQLiteLoad('nope.dll', Err), 'a later load with another name uses the loaded library');
  Check(SQLiteLoad('kernel32.dll', Err), 'whatever the name');

  Check(Assigned(sqlite3_libversion) and Assigned(sqlite3_libversion_number) and Assigned(sqlite3_open_v2) and
    Assigned(sqlite3_close_v2) and Assigned(sqlite3_extended_result_codes) and Assigned(sqlite3_busy_timeout) and
    Assigned(sqlite3_errcode) and Assigned(sqlite3_extended_errcode) and Assigned(sqlite3_errmsg) and
    Assigned(sqlite3_errstr), 'library and connection functions');
  Check(Assigned(sqlite3_prepare_v2) and Assigned(sqlite3_finalize) and Assigned(sqlite3_reset) and
    Assigned(sqlite3_clear_bindings) and Assigned(sqlite3_step) and Assigned(sqlite3_stmt_readonly),
    'statement functions');
  Check(Assigned(sqlite3_bind_parameter_count) and Assigned(sqlite3_bind_parameter_name) and
    Assigned(sqlite3_bind_parameter_index) and Assigned(sqlite3_bind_null) and Assigned(sqlite3_bind_int64) and
    Assigned(sqlite3_bind_double) and Assigned(sqlite3_bind_text16) and Assigned(sqlite3_bind_blob) and
    Assigned(sqlite3_bind_zeroblob), 'bind functions');
  Check(Assigned(sqlite3_column_count) and Assigned(sqlite3_column_name) and Assigned(sqlite3_column_decltype) and
    Assigned(sqlite3_column_type) and Assigned(sqlite3_column_int64) and Assigned(sqlite3_column_double) and
    Assigned(sqlite3_column_text16) and Assigned(sqlite3_column_bytes16) and Assigned(sqlite3_column_blob) and
    Assigned(sqlite3_column_bytes), 'column functions');
  Check(Assigned(sqlite3_last_insert_rowid) and Assigned(sqlite3_changes) and Assigned(sqlite3_total_changes) and
    Assigned(sqlite3_get_autocommit) and Assigned(sqlite3_table_column_metadata), 'connection state functions');
  Check(Assigned(sqlite3_column_table_name), 'optional sqlite3_column_table_name is present');
  Check(Assigned(sqlite3_column_origin_name), 'optional sqlite3_column_origin_name is present');
  Check(Assigned(sqlite3_column_database_name), 'optional sqlite3_column_database_name is present');
  Check(Assigned(sqlite3_interrupt), 'optional sqlite3_interrupt is present');

  N := sqlite3_libversion_number();
  Check(N >= 3008000, 'the version number passes the check');
  CheckEq(Utf8ToUtf16(sqlite3_libversion()),
    IntToStr(N div 1000000) + '.' + IntToStr(N div 1000 mod 1000) + '.' + IntToStr(N mod 1000),
    'the version text matches the version number');
end;

procedure TestResultCodes;
begin
  Section('result code texts');
  CheckEq(ErrStr(SQLITE_OK), 'not an error', 'SQLITE_OK');
  CheckEq(ErrStr(SQLITE_ERROR), 'SQL logic error', 'SQLITE_ERROR');
  CheckEq(ErrStr(SQLITE_BUSY), 'database is locked', 'SQLITE_BUSY');
  CheckEq(ErrStr(SQLITE_LOCKED), 'database table is locked', 'SQLITE_LOCKED');
  CheckEq(ErrStr(SQLITE_READONLY), 'attempt to write a readonly database', 'SQLITE_READONLY');
  CheckEq(ErrStr(SQLITE_CANTOPEN), 'unable to open database file', 'SQLITE_CANTOPEN');
  CheckEq(ErrStr(SQLITE_CONSTRAINT), 'constraint failed', 'SQLITE_CONSTRAINT');
  CheckEq(ErrStr(SQLITE_MISUSE), 'bad parameter or other API misuse', 'SQLITE_MISUSE');
  CheckEq(ErrStr(SQLITE_RANGE), 'column index out of range', 'SQLITE_RANGE');
  CheckEq(ErrStr(SQLITE_NOTADB), 'file is not a database', 'SQLITE_NOTADB');
  CheckEq(ErrStr(SQLITE_ROW), 'another row available', 'SQLITE_ROW');
  CheckEq(ErrStr(SQLITE_DONE), 'no more rows available', 'SQLITE_DONE');
end;

procedure TestObservedCodes;
var
  Db, Db2: Psqlite3;
  Stmt: Psqlite3_stmt;
  Tail: PUtf8;
  FileName, S: string;
  Zero: Word;
begin
  Section('codes observed');
  CheckEqInt(sqlite3_open_v2(Utf8(':memory:'), Db, OpenCreate, nil), SQLITE_OK, 'open in memory');
  Check(Db <> nil, 'a handle');
  CheckEqInt(sqlite3_extended_result_codes(Db, 1), SQLITE_OK, 'extended result codes on');

  CheckEqInt(sqlite3_prepare_v2(Db, Utf8('SELECT 7, 2.5, ''x'', x''00'', NULL'), -1, Stmt, Tail), SQLITE_OK, 'prepare');
  CheckEqInt(sqlite3_step(Stmt), SQLITE_ROW, 'step returns SQLITE_ROW');
  CheckEqInt(sqlite3_column_type(Stmt, 0), SQLITE_INTEGER, 'SQLITE_INTEGER');
  CheckEqInt(sqlite3_column_type(Stmt, 1), SQLITE_FLOAT, 'SQLITE_FLOAT');
  CheckEqInt(sqlite3_column_type(Stmt, 2), SQLITE_TEXT, 'SQLITE_TEXT');
  CheckEqInt(sqlite3_column_type(Stmt, 3), SQLITE_BLOB, 'SQLITE_BLOB');
  CheckEqInt(sqlite3_column_type(Stmt, 4), SQLITE_NULL, 'SQLITE_NULL');
  CheckEqInt(sqlite3_column_int64(Stmt, 0), 7, 'the integer value');
  CheckEqInt(sqlite3_step(Stmt), SQLITE_DONE, 'step returns SQLITE_DONE');
  CheckEqInt(sqlite3_finalize(Stmt), SQLITE_OK, 'finalize');

  CheckEqInt(sqlite3_prepare_v2(Db, Utf8('SELECT ?1, ?2'), -1, Stmt, Tail), SQLITE_OK, 'prepare with parameters');
  CheckEqInt(sqlite3_bind_null(Stmt, 3), SQLITE_RANGE, 'SQLITE_RANGE for a bind index out of range');
  S := 'transient';
  CheckEqInt(sqlite3_bind_text16(Stmt, 1, PChar(S), Length(S) * 2, SQLITE_TRANSIENT), SQLITE_OK, 'bind with SQLITE_TRANSIENT');
  S := 'overwritten';
  Zero := 0;
  CheckEqInt(sqlite3_bind_text16(Stmt, 2, @Zero, 0, SQLITE_STATIC), SQLITE_OK, 'bind with SQLITE_STATIC');
  CheckEqInt(sqlite3_step(Stmt), SQLITE_ROW, 'a row');
  CheckEqInt(sqlite3_column_bytes16(Stmt, 0), 18, 'SQLite kept its own copy of the transient text');
  CheckEqInt(sqlite3_column_type(Stmt, 1), SQLITE_TEXT, 'the empty static text is text, not null');
  sqlite3_finalize(Stmt);

  CheckEqInt(Run(Db, 'SELECT * FROM nope'), SQLITE_ERROR, 'SQLITE_ERROR');
  CheckEqInt(Run(Db, 'PRAGMA foreign_keys = ON;' +
    'CREATE TABLE p(id INTEGER PRIMARY KEY, name TEXT NOT NULL, code TEXT UNIQUE, age INTEGER CHECK (age < 150));' +
    'CREATE TABLE c(pid INTEGER REFERENCES p(id));' +
    'CREATE TABLE k(a TEXT, b TEXT, PRIMARY KEY (a, b)) WITHOUT ROWID;' +
    'CREATE TABLE r(v TEXT);' +
    'CREATE TABLE g(v TEXT);' +
    'CREATE TRIGGER g_no BEFORE INSERT ON g BEGIN SELECT RAISE(ABORT, ''no''); END;' +
    'INSERT INTO p(id, name, code, age) VALUES (1, ''a'', ''x'', 1);' +
    'INSERT INTO k VALUES (''a'', ''b'');' +
    'INSERT INTO r(rowid, v) VALUES (1, ''a'');'), SQLITE_OK, 'the schema');
  CheckEqInt(Run(Db, 'INSERT INTO p(name, age) VALUES (''b'', 200)'), SQLITE_CONSTRAINT_CHECK, 'SQLITE_CONSTRAINT_CHECK');
  CheckEqInt(sqlite3_extended_errcode(Db), SQLITE_CONSTRAINT_CHECK, 'the extended code of the handle');
  CheckEqInt(sqlite3_errcode(Db) and $FF, SQLITE_CONSTRAINT, 'its primary code is SQLITE_CONSTRAINT');
  CheckEqInt(Run(Db, 'INSERT INTO c VALUES (99)'), SQLITE_CONSTRAINT_FOREIGNKEY, 'SQLITE_CONSTRAINT_FOREIGNKEY');
  CheckEqInt(Run(Db, 'INSERT INTO p(name) VALUES (NULL)'), SQLITE_CONSTRAINT_NOTNULL, 'SQLITE_CONSTRAINT_NOTNULL');
  CheckEqInt(Run(Db, 'INSERT INTO k VALUES (''a'', ''b'')'), SQLITE_CONSTRAINT_PRIMARYKEY, 'SQLITE_CONSTRAINT_PRIMARYKEY');
  CheckEqInt(Run(Db, 'INSERT INTO p(name, code) VALUES (''c'', ''x'')'), SQLITE_CONSTRAINT_UNIQUE, 'SQLITE_CONSTRAINT_UNIQUE');
  CheckEqInt(Run(Db, 'INSERT INTO r(rowid, v) VALUES (1, ''b'')'), SQLITE_CONSTRAINT_ROWID, 'SQLITE_CONSTRAINT_ROWID');
  CheckEqInt(Run(Db, 'INSERT INTO g VALUES (''a'')'), SQLITE_CONSTRAINT_TRIGGER, 'SQLITE_CONSTRAINT_TRIGGER');
  CheckEqInt(sqlite3_close_v2(Db), SQLITE_OK, 'close');

  Section('open flags');
  FileName := GetCurrentDir + '\api.db';
  CheckEqInt(sqlite3_open_v2(Utf8(FileName), Db, SQLITE_OPEN_READWRITE or SQLITE_OPEN_FULLMUTEX, nil) and $FF,
    SQLITE_CANTOPEN, 'SQLITE_OPEN_READWRITE does not create a file');
  CheckEqInt(sqlite3_close_v2(Db), SQLITE_OK, 'the handle of a failed open is closed');
  Check(not FileExists(FileName), 'no file');
  CheckEqInt(sqlite3_open_v2(Utf8(FileName), Db, OpenCreate, nil), SQLITE_OK, 'SQLITE_OPEN_CREATE creates it');
  CheckEqInt(Run(Db, 'CREATE TABLE t(v)'), SQLITE_OK, 'and it can be written');
  Check(FileExists(FileName), 'the file exists');

  CheckEqInt(sqlite3_open_v2(Utf8(FileName), Db2, SQLITE_OPEN_READONLY or SQLITE_OPEN_FULLMUTEX, nil), SQLITE_OK,
    'SQLITE_OPEN_READONLY opens it');
  CheckEqInt(Run(Db2, 'SELECT * FROM t'), SQLITE_OK, 'for reading');
  CheckEqInt(Run(Db2, 'INSERT INTO t VALUES (1)'), SQLITE_READONLY, 'SQLITE_READONLY for a write');

  CheckEqInt(sqlite3_busy_timeout(Db2, 0), SQLITE_OK, 'no busy timeout');
  CheckEqInt(Run(Db, 'BEGIN EXCLUSIVE'), SQLITE_OK, 'the first connection locks the file');
  CheckEqInt(Run(Db2, 'SELECT * FROM t'), SQLITE_BUSY, 'SQLITE_BUSY for the second');
  CheckEqInt(sqlite3_get_autocommit(Db), 0, 'not in autocommit mode inside a transaction');
  CheckEqInt(Run(Db, 'COMMIT'), SQLITE_OK, 'commit');
  CheckEqInt(sqlite3_get_autocommit(Db), 1, 'autocommit again');
  sqlite3_close_v2(Db2);
  sqlite3_close_v2(Db);
  Check(DeleteFile(FileName), 'the file is deleted');

  CheckEqInt(sqlite3_open_v2(Utf8('file:uri.db?mode=memory'), Db, OpenCreate or SQLITE_OPEN_URI, nil), SQLITE_OK,
    'SQLITE_OPEN_URI accepts a file: name');
  CheckEqInt(Run(Db, 'CREATE TABLE t(v)'), SQLITE_OK, 'an in-memory database');
  sqlite3_close_v2(Db);
  Check(not FileExists(GetCurrentDir + '\uri.db'), 'that creates no file');
end;

begin
  TestFailingLoads;
  TestLoad;
  TestResultCodes;
  TestObservedCodes;
  WriteLn('failed checks: ', FailedChecks);
end.
