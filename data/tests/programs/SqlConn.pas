// covers: DAT-101, DAT-102, DAT-103, DAT-104, DAT-115, DAT-116, DAT-117, DAT-120, DAT-121, DAT-122, DAT-123, DAT-124, DAT-125, DAT-126, DAT-128, DAT-129
program SqlConn;
{ The SQLite connection. Sections: errors (ESQLiteError and the mapping of result codes), open (open and
  close, modes, path resolution, busy timeout, pragmas, events), exec (scripts and change counts),
  transaction, schema helpers, and the close cascade to registered datasets.
  The library failures run first: a successful load stays for the life of the process. }
uses
  SysUtils, Classes, DB, MemData, SQLite3API, SQLiteData, DataTestUtils;

type
  { gives the test the protected streaming switches }
  TTestConnection = class(TSQLiteConnection)
  public
    procedure BeginLoading;
    procedure EndLoading;
  end;

  TCalls = class
  public
    C: TSQLiteConnection;
    Text: string;
    Mode: TSQLiteOpenMode;
    List: TStrings;
    procedure Open;
    procedure Exec;
    procedure Version;
    procedure SetDatabase;
    procedure SetOpenMode;
    procedure SetLibraryName;
    procedure StartTransaction;
    procedure Commit;
    procedure Rollback;
    procedure LastId;
    procedure TableNames;
    procedure FieldNames;
    procedure CheckBusy;
  end;

  TRecorder = class
  public
    C: TSQLiteConnection;
    Log: string;
    procedure Add(const S: string);
    function Take: string;
    procedure BeforeConnect(Sender: TObject);
    procedure AfterConnect(Sender: TObject);
    procedure BeforeDisconnect(Sender: TObject);
    procedure AfterDisconnect(Sender: TObject);
    procedure BeforeClose(DataSet: TDataSet);
    procedure AfterClose(DataSet: TDataSet);
    procedure Detach(Destroying: Boolean);
    procedure DetachSecond(Destroying: Boolean);
  end;

var
  Calls: TCalls;
  ExeDir: string;

procedure TTestConnection.BeginLoading;
begin
  Loading;
end;

procedure TTestConnection.EndLoading;
begin
  Loaded;
end;

procedure TCalls.Open;
begin
  C.Open;
end;

procedure TCalls.Exec;
begin
  C.ExecSQL(Text);
end;

procedure TCalls.Version;
begin
  Text := C.LibraryVersion;
end;

procedure TCalls.SetDatabase;
begin
  C.Database := Text;
end;

procedure TCalls.SetOpenMode;
begin
  C.OpenMode := Mode;
end;

procedure TCalls.SetLibraryName;
begin
  C.LibraryName := Text;
end;

procedure TCalls.StartTransaction;
begin
  C.StartTransaction;
end;

procedure TCalls.Commit;
begin
  C.Commit;
end;

procedure TCalls.Rollback;
begin
  C.Rollback;
end;

procedure TCalls.LastId;
begin
  C.LastInsertRowId;
end;

procedure TCalls.TableNames;
begin
  C.GetTableNames(List);
end;

procedure TCalls.FieldNames;
begin
  C.GetFieldNames(Text, List);
end;

procedure TCalls.CheckBusy;
begin
  SQLiteCheck(nil, SQLITE_BUSY, 'the statement');
end;

function BoolText(Value: Boolean): string;
begin
  if Value then
    Result := 'True'
  else
    Result := 'False';
end;

procedure TRecorder.Add(const S: string);
begin
  if Log <> '' then
    Log := Log + ',';
  Log := Log + S;
end;

function TRecorder.Take: string;
begin
  Result := Log;
  Log := '';
end;

procedure TRecorder.BeforeConnect(Sender: TObject);
begin
  Add('BeforeConnect:' + BoolText(C.Connected));
end;

procedure TRecorder.AfterConnect(Sender: TObject);
begin
  Add('AfterConnect:' + BoolText(C.Connected));
end;

procedure TRecorder.BeforeDisconnect(Sender: TObject);
begin
  Add('BeforeDisconnect:' + BoolText(C.Connected));
end;

procedure TRecorder.AfterDisconnect(Sender: TObject);
begin
  Add('AfterDisconnect:' + BoolText(C.Connected));
end;

procedure TRecorder.BeforeClose(DataSet: TDataSet);
begin
  Add('BeforeClose ' + DataSet.Name);
end;

procedure TRecorder.AfterClose(DataSet: TDataSet);
begin
  Add('AfterClose ' + DataSet.Name);
end;

procedure TRecorder.Detach(Destroying: Boolean);
begin
  if Destroying then
    Add('Detach destroying')
  else
    Add('Detach closing tx=' + BoolText(C.InTransaction));
end;

procedure TRecorder.DetachSecond(Destroying: Boolean);
begin
  if Destroying then
    Add('Detach2 destroying')
  else
    Add('Detach2 closing');
end;

procedure Watch(Rec: TRecorder; C: TSQLiteConnection);
begin
  Rec.C := C;
  C.BeforeConnect := Rec.BeforeConnect;
  C.AfterConnect := Rec.AfterConnect;
  C.BeforeDisconnect := Rec.BeforeDisconnect;
  C.AfterDisconnect := Rec.AfterDisconnect;
end;

function NewRecorder(C: TSQLiteConnection): TRecorder;
begin
  Result := TRecorder.Create;
  Watch(Result, C);
end;

{ Message, codes and statement text of the ESQLiteError that Proc raises. }
procedure CheckSqlError(Proc: TTestMethod; const Msg: string; Code, Ext: Integer; const SQL, Name: string);
var
  Got: string;
begin
  Got := 'no exception';
  try
    Proc;
  except
    on E: ESQLiteError do
      Got := E.Message + ' | ' + IntToStr(E.ErrorCode) + ' | ' + IntToStr(E.ExtendedCode) + ' | ' + E.SQL;
    on E: Exception do
      Got := E.ClassName + ': ' + E.Message;
  end;
  CheckEq(Got, Msg + ' | ' + IntToStr(Code) + ' | ' + IntToStr(Ext) + ' | ' + SQL, Name);
end;

procedure CheckExecError(C: TSQLiteConnection; const Script, Msg: string; Code, Ext: Integer; const SQL, Name: string);
begin
  Calls.C := C;
  Calls.Text := Script;
  CheckSqlError(Calls.Exec, Msg, Code, Ext, SQL, Name);
end;

{ The first column of every row of a query, joined with commas; read through the raw API on the
  connection's handle. }
function Query(C: TSQLiteConnection; const SQL: string): string;
var
  Buf: TBytes;
  Stmt: Psqlite3_stmt;
  Tail: PUtf8;
  P: Pointer;
  S: string;
  First: Boolean;
begin
  Buf := Utf16ToUtf8(SQL);
  if sqlite3_prepare_v2(C.Handle, PUtf8(@Buf[0]), -1, Stmt, Tail) <> SQLITE_OK then
  begin
    Result := 'prepare failed: ' + Utf8ToUtf16(sqlite3_errmsg(C.Handle));
    Exit;
  end;
  Result := '';
  First := True;
  while sqlite3_step(Stmt) = SQLITE_ROW do
  begin
    if not First then
      Result := Result + ',';
    First := False;
    P := sqlite3_column_text16(Stmt, 0);
    SetString(S, PChar(P), sqlite3_column_bytes16(Stmt, 0) div 2);
    Result := Result + S;
  end;
  sqlite3_finalize(Stmt);
end;

function Join(L: TStrings): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
  begin
    if I > 0 then
      Result := Result + '|';
    Result := Result + L[I];
  end;
end;

function NewMemory: TSQLiteConnection;
begin
  Result := TSQLiteConnection.Create(nil);
  Result.Database := ':memory:';
  Result.Open;
end;

procedure TestErrors;
var
  C: TSQLiteConnection;
  Rec: TRecorder;
  E: ESQLiteError;
begin
  Section('errors: library');
  C := TSQLiteConnection.Create(nil);
  Rec := NewRecorder(C);
  Calls.C := C;
  C.Database := ':memory:';
  C.LibraryName := 'nope.dll';
  CheckSqlError(Calls.Open, 'Cannot load SQLite library "nope.dll"', -1, -1, '', 'Open with a missing library');
  CheckEq(Rec.Take, 'BeforeConnect:False', 'BeforeConnect ran before the library was loaded');
  Check(not C.Connected and (C.Handle = nil), 'the connection stays closed');
  CheckSqlError(Calls.Version, 'Cannot load SQLite library "nope.dll"', -1, -1, '', 'LibraryVersion with a missing library');
  C.LibraryName := 'kernel32.dll';
  CheckSqlError(Calls.Open, 'SQLite library is missing function "sqlite3_libversion"', -1, -1, '',
    'Open with a library that is not SQLite');
  Check(not SQLiteLoaded, 'nothing is loaded yet');
  Rec.Take;
  C.LibraryName := 'sqlite3.dll';
  C.Open;
  CheckEq(Rec.Take, 'BeforeConnect:False,AfterConnect:True', 'Open with the library beside the executable');
  Check(SQLiteLoaded, 'the library is loaded on first use');
  CheckEq(C.LibraryVersion, Utf8ToUtf16(sqlite3_libversion()), 'LibraryVersion');
  C.Free;
  Rec.Free;
  C := TSQLiteConnection.Create(nil);
  Calls.C := C;
  C.LibraryName := 'nope.dll';
  CheckEq(C.LibraryVersion, Utf8ToUtf16(sqlite3_libversion()), 'a later connection uses the loaded library whatever its LibraryName');
  C.Free;

  Section('errors: result codes');
  SQLiteCheck(nil, SQLITE_OK, '');
  SQLiteCheck(nil, SQLITE_ROW, '');
  SQLiteCheck(nil, SQLITE_DONE, '');
  Check(True, 'SQLITE_OK, SQLITE_ROW and SQLITE_DONE raise nothing');
  CheckSqlError(Calls.CheckBusy, 'SQLite error 5: database is locked', 5, 5, 'the statement',
    'without a handle the text of the result code is used');
  E := ESQLiteError.CreateCode('m', 19, 1299, 'q');
  Check(E is EDatabaseError, 'ESQLiteError is an EDatabaseError');
  CheckEq(E.Message + ' ' + IntToStr(E.ErrorCode) + ' ' + IntToStr(E.ExtendedCode) + ' ' + E.SQL, 'm 19 1299 q',
    'message, codes and statement text');
  E.Free;

  C := NewMemory;
  C.ExecSQL('CREATE TABLE contacts(id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT UNIQUE, ' +
    'age INTEGER CONSTRAINT age_range CHECK (age BETWEEN 0 AND 150))');
  CheckExecError(C, 'SELECT * FROM nope', 'SQLite error 1: no such table: nope', 1, 1, 'SELECT * FROM nope',
    'an unknown table');
  CheckExecError(C, 'INSERT INTO contacts(name) VALUES (NULL)',
    'SQLite error 1299: NOT NULL constraint failed: contacts.name', 19, 1299,
    'INSERT INTO contacts(name) VALUES (NULL)', 'a NOT NULL violation has the extended code');
  CheckExecError(C, 'INSERT INTO contacts(name, age) VALUES (''a'', 200)',
    'SQLite error 275: CHECK constraint failed: age_range', 19, 275,
    'INSERT INTO contacts(name, age) VALUES (''a'', 200)', 'a CHECK violation');
  CheckEqInt(C.ExecSQL('INSERT INTO contacts(name, email) VALUES (''a'', ''x'')'), 1, 'the connection stays usable');
  CheckExecError(C, 'INSERT INTO contacts(name, email) VALUES (''b'', ''x'')',
    'SQLite error 2067: UNIQUE constraint failed: contacts.email', 19, 2067,
    'INSERT INTO contacts(name, email) VALUES (''b'', ''x'')', 'a UNIQUE violation');
  CheckExecError(C, 'INSERT INTO contacts(id, name) VALUES (1, ''c'')',
    'SQLite error 1555: UNIQUE constraint failed: contacts.id', 19, 1555,
    'INSERT INTO contacts(id, name) VALUES (1, ''c'')', 'a primary key violation');
  CheckExecError(C, 'SELEC 1', 'SQLite error 1: near "SELEC": syntax error', 1, 1, 'SELEC 1', 'a syntax error');
  CheckEq(Query(C, 'SELECT count(*) FROM contacts'), '1', 'one row was stored');
  C.Free;
end;

procedure TestOpen;
var
  C, B: TSQLiteConnection;
  T: TTestConnection;
  Rec: TRecorder;
  FileName: string;
begin
  Section('open: defaults');
  C := TSQLiteConnection.Create(nil);
  Calls.C := C;
  Rec := NewRecorder(C);
  CheckEq(C.Database, '', 'Database');
  Check(C.OpenMode = omCreateReadWrite, 'OpenMode');
  CheckEqInt(C.BusyTimeout, 5000, 'BusyTimeout');
  Check(C.ForeignKeys, 'ForeignKeys');
  CheckEq(C.LibraryName, 'sqlite3.dll', 'LibraryName');
  CheckEqInt(C.Params.Count, 0, 'Params');
  Check(not C.Connected and (C.Handle = nil), 'closed, no handle');

  Section('open: open and close');
  CheckSqlError(Calls.Open, 'Database name is empty', 0, 0, '', 'Open without a database name');
  CheckEq(Rec.Take, '', 'no event for it');
  C.Close;
  CheckEq(Rec.Take, '', 'Close on a closed connection does nothing');
  C.Database := ':memory:';
  C.Open;
  CheckEq(Rec.Take, 'BeforeConnect:False,AfterConnect:True', 'the events of Open');
  Check(C.Connected and (C.Handle <> nil), 'connected, with a handle');
  C.Open;
  CheckEq(Rec.Take, '', 'Open on an open connection does nothing');
  C.Close;
  CheckEq(Rec.Take, 'BeforeDisconnect:True,AfterDisconnect:False', 'the events of Close');
  Check(not C.Connected and (C.Handle = nil), 'closed again');
  C.Connected := True;
  CheckEq(Rec.Take, 'BeforeConnect:False,AfterConnect:True', 'Connected := True opens');
  C.Connected := True;
  CheckEq(Rec.Take, '', 'once');
  C.Connected := False;
  CheckEq(Rec.Take, 'BeforeDisconnect:True,AfterDisconnect:False', 'Connected := False closes');

  Section('open: properties while connected');
  C.Open;
  Calls.Text := 'other.db';
  CheckSqlError(Calls.SetDatabase, 'Cannot change this property while connected', 0, 0, '', 'Database');
  Calls.Mode := omReadOnly;
  CheckSqlError(Calls.SetOpenMode, 'Cannot change this property while connected', 0, 0, '', 'OpenMode');
  Calls.Text := 'other.dll';
  CheckSqlError(Calls.SetLibraryName, 'Cannot change this property while connected', 0, 0, '', 'LibraryName');
  C.Database := ':memory:';
  C.OpenMode := omCreateReadWrite;
  C.LibraryName := 'sqlite3.dll';
  Check(C.Connected, 'assigning the current values is allowed');
  CheckEq(Query(C, 'PRAGMA busy_timeout'), '5000', 'the busy timeout was applied at open');
  C.BusyTimeout := 250;
  CheckEq(Query(C, 'PRAGMA busy_timeout'), '250', 'BusyTimeout is applied at once');
  CheckEq(Query(C, 'PRAGMA foreign_keys'), '1', 'foreign keys are on');
  C.ForeignKeys := False;
  CheckEq(Query(C, 'PRAGMA foreign_keys'), '0', 'ForeignKeys is applied at once');
  C.ForeignKeys := True;
  C.ExecSQL('CREATE TABLE p(id INTEGER PRIMARY KEY); CREATE TABLE ch(pid INTEGER REFERENCES p(id))');
  CheckExecError(C, 'INSERT INTO ch VALUES (5)', 'SQLite error 787: FOREIGN KEY constraint failed', 19, 787,
    'INSERT INTO ch VALUES (5)', 'foreign keys are enforced');
  C.Close;
  C.ForeignKeys := False;
  C.BusyTimeout := 0;
  C.Open;
  CheckEq(Query(C, 'PRAGMA foreign_keys') + ' ' + Query(C, 'PRAGMA busy_timeout'), '0 0', 'values set while closed are applied at open');
  C.Close;
  C.ForeignKeys := True;
  C.BusyTimeout := 5000;
  Rec.Take;

  Section('open: pragmas');
  C.Params.Add('user_version=7');
  C.Params.Add('');
  C.Params.Add('  cache_size =  -123  ');
  C.Open;
  CheckEq(Query(C, 'PRAGMA user_version') + ' ' + Query(C, 'PRAGMA cache_size'), '7 -123', 'each line is one pragma');
  C.Close;
  Rec.Take;
  C.Params.Text := 'user_version=1' + sLineBreak + 'bad name=2';
  CheckSqlError(Calls.Open, 'Invalid pragma name "bad name"', 0, 0, '', 'a pragma name with a space');
  CheckEq(Rec.Take, 'BeforeConnect:False', 'no AfterConnect');
  Check(not C.Connected and (C.Handle = nil), 'a failure after the handle exists leaves the connection closed');
  C.Params.Text := '1abc=2';
  CheckSqlError(Calls.Open, 'Invalid pragma name "1abc"', 0, 0, '', 'a pragma name starting with a digit');
  C.Params.Text := '=2';
  CheckSqlError(Calls.Open, 'Invalid pragma name ""', 0, 0, '', 'an empty pragma name');
  C.Params.Text := 'user_version';
  CheckSqlError(Calls.Open, 'Invalid pragma name "user_version"', 0, 0, '', 'a line without a value');
  C.Params.Text := 'user_version=)';
  CheckSqlError(Calls.Open, 'SQLite error 1: near ")": syntax error', 1, 1, 'PRAGMA user_version = )',
    'the value is inserted verbatim');
  Check(not C.Connected and (C.Handle = nil), 'closed after the failing pragma');
  C.Params.Text := '_x9=1';
  C.Open;
  Check(C.Connected, 'an unknown pragma with a valid name is ignored by SQLite');
  C.Close;
  C.Params.Clear;
  Rec.Take;

  Section('open: deferred while loading');
  T := TTestConnection.Create(nil);
  T.Database := ':memory:';
  T.BeginLoading;
  T.Connected := True;
  Check(not T.Connected, 'Connected := True while loading does not open');
  T.EndLoading;
  Check(T.Connected, 'Loaded opens the connection');
  T.Free;
  T := TTestConnection.Create(nil);
  T.BeginLoading;
  T.EndLoading;
  Check(not T.Connected, 'Loaded without a stored Connected opens nothing');
  T.Free;

  Section('open: modes');
  FileName := GetCurrentDir + '\conn.db';
  C.Database := FileName;
  C.OpenMode := omReadWrite;
  CheckSqlError(Calls.Open, 'SQLite error 14: unable to open database file', 14, 14, '', 'omReadWrite needs an existing file');
  Check(not C.Connected and (C.Handle = nil) and not FileExists(FileName), 'closed, no file');
  CheckEq(Rec.Take, 'BeforeConnect:False', 'only BeforeConnect ran');
  C.OpenMode := omReadOnly;
  CheckSqlError(Calls.Open, 'SQLite error 14: unable to open database file', 14, 14, '', 'omReadOnly needs an existing file');
  C.OpenMode := omCreateReadWrite;
  C.Open;
  CheckEqInt(C.ExecSQL('CREATE TABLE t(v TEXT); INSERT INTO t VALUES (''a'')'), 1, 'omCreateReadWrite creates and writes');
  Check(FileExists(FileName), 'the file exists');
  C.Close;
  C.OpenMode := omReadWrite;
  C.Open;
  CheckEqInt(C.ExecSQL('INSERT INTO t VALUES (''b'')'), 1, 'omReadWrite writes an existing file');
  C.Close;
  C.OpenMode := omReadOnly;
  C.Open;
  CheckEq(Query(C, 'SELECT v FROM t ORDER BY v'), 'a,b', 'omReadOnly reads');
  CheckExecError(C, 'INSERT INTO t VALUES (''c'')', 'SQLite error 8: attempt to write a readonly database', 8, 8,
    'INSERT INTO t VALUES (''c'')', 'omReadOnly does not write');
  C.Close;
  C.OpenMode := omCreateReadWrite;

  Section('open: busy timeout');
  C.Open;
  B := TSQLiteConnection.Create(nil);
  B.Database := FileName;
  B.BusyTimeout := 0;
  B.Open;
  C.ExecSQL('BEGIN EXCLUSIVE');
  CheckExecError(B, 'INSERT INTO t VALUES (''d'')', 'SQLite error 5: database is locked', 5, 5,
    'INSERT INTO t VALUES (''d'')', 'a locked database fails at once with BusyTimeout = 0');
  C.ExecSQL('COMMIT');
  CheckEqInt(B.ExecSQL('INSERT INTO t VALUES (''d'')'), 1, 'and works after the lock is gone');
  B.Free;
  C.Close;

  Section('open: a failed open releases the file');
  Calls.C := C;
  C.Params.Text := 'bad name=1';
  CheckSqlError(Calls.Open, 'Invalid pragma name "bad name"', 0, 0, '', 'a failing open of a file database');
  C.Params.Clear;
  Check(DeleteFile(FileName), 'the file can be deleted');
  Rec.Take;

  Section('open: path resolution');
  C.Database := 'rel.db';
  C.Open;
  C.ExecSQL('CREATE TABLE t(v)');
  Check(FileExists(ExeDir + 'rel.db'), 'a relative name is resolved against the directory of the executable');
  Check(not FileExists(GetCurrentDir + '\rel.db'), 'not against the current directory');
  C.Close;
  Check(DeleteFile(ExeDir + 'rel.db'), 'the file is deleted');
  C.Database := 'sub\..\rel2.db';
  C.Open;
  C.ExecSQL('CREATE TABLE t(v)');
  Check(FileExists(ExeDir + 'rel2.db'), 'the path is normalised');
  C.Close;
  Check(DeleteFile(ExeDir + 'rel2.db'), 'that file is deleted');
  C.Database := 'file:uri.db?mode=memory';
  C.Open;
  CheckEqInt(C.ExecSQL('CREATE TABLE t(v); INSERT INTO t VALUES (1)'), 1, 'a file: name is passed as a URI');
  Check(not FileExists(ExeDir + 'uri.db') and not FileExists(GetCurrentDir + '\uri.db'), 'mode=memory creates no file');
  C.Close;
  C.Free;
  Rec.Free;
end;

procedure TestExec;
var
  C: TSQLiteConnection;
begin
  Section('exec');
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  CheckExecError(C, 'SELECT 1', 'Connection is closed', 0, 0, '', 'ExecSQL on a closed connection');
  C.Open;
  CheckEqInt(C.ExecSQL('CREATE TABLE t(id INTEGER PRIMARY KEY, v TEXT);' + sLineBreak +
    'INSERT INTO t(v) VALUES (''a'');' + sLineBreak +
    'INSERT INTO t(v) VALUES (''b''), (''c'');'), 3, 'a script returns the total of rows changed');
  CheckEq(Query(C, 'SELECT v FROM t ORDER BY id'), 'a,b,c', 'every statement ran');
  CheckEqInt(C.ExecSQL('UPDATE t SET v = v || ''!'''), 3, 'an update');
  CheckEqInt(C.ExecSQL('DELETE FROM t WHERE id = 1'), 1, 'a delete');
  CheckEqInt(C.ExecSQL('SELECT * FROM t'), 0, 'a query changes nothing; its rows are discarded');
  CheckEqInt(C.ExecSQL('UPDATE t SET v = ''q'' WHERE id = 999'), 0, 'an update that matches no row');
  CheckEqInt(C.ExecSQL('INSERT INTO t(v) VALUES (''z''); SELECT * FROM t; CREATE TABLE u(x); PRAGMA user_version = 3; BEGIN IMMEDIATE; COMMIT'),
    1, 'statements that change no rows add nothing after one that did');
  CheckEq(Query(C, 'PRAGMA user_version'), '3', 'they ran');
  CheckEqInt(C.ExecSQL(''), 0, 'an empty script');
  CheckEqInt(C.ExecSQL('  ' + sLineBreak + '  '), 0, 'white space only');
  CheckEqInt(C.ExecSQL('-- nothing' + sLineBreak + '/* at all */'), 0, 'comments only');
  CheckEqInt(C.ExecSQL(';;INSERT INTO t(v) VALUES (''y'');; -- done' + sLineBreak + ';'), 1, 'empty statements between semicolons');
  CheckEqInt(C.ExecSQL('/* first */ INSERT INTO t(v) VALUES (''x;'') /* ; */; -- last' + sLineBreak +
    'INSERT INTO t(v) VALUES (''w'')'), 2, 'semicolons in literals and comments do not split');
  CheckEq(Query(C, 'SELECT count(*) FROM t'), '6', 'six rows now');
  C.ExecSQL('CREATE TABLE log(n); CREATE TRIGGER t_ins AFTER INSERT ON t BEGIN INSERT INTO log VALUES (1); INSERT INTO log VALUES (2); END');
  CheckEqInt(C.ExecSQL('INSERT INTO t(v) VALUES (''v'')'), 1, 'rows changed by triggers are not counted');
  CheckEq(Query(C, 'SELECT count(*) FROM log'), '2', 'the trigger ran');
  CheckEqInt(C.ExecSQL('DELETE FROM log'), 2, 'a delete of all rows');
  C.ExecSQL('DROP TRIGGER t_ins');

  Section('exec: errors');
  CheckExecError(C, 'INSERT INTO t(v) VALUES (''d'');' + sLineBreak + '  INSERT INTO nope VALUES (''a;b'') ; INSERT INTO t(v) VALUES (''e'')',
    'SQLite error 1: no such table: nope', 1, 1, 'INSERT INTO nope VALUES (''a;b'')',
    'a statement that does not compile is named by the error');
  CheckEq(Query(C, 'SELECT count(*) FROM t WHERE v IN (''d'', ''e'')'), '1', 'the statements before it stay, those after it did not run');
  C.ExecSQL('CREATE TABLE n(name TEXT NOT NULL)');
  CheckExecError(C, 'INSERT INTO n VALUES (''f'');' + sLineBreak + ' INSERT INTO n VALUES (NULL) ;' + sLineBreak + 'INSERT INTO n VALUES (''g'')',
    'SQLite error 1299: NOT NULL constraint failed: n.name', 19, 1299, 'INSERT INTO n VALUES (NULL)',
    'a statement that fails while running is named by the error');
  CheckEq(Query(C, 'SELECT name FROM n'), 'f', 'the same rule for the other statements');
  CheckEqInt(C.ExecSQL('INSERT INTO n VALUES (''h'')'), 1, 'the connection stays usable');
  C.Free;
end;

procedure TestTransactions;
var
  C: TSQLiteConnection;
  FileName: string;
begin
  Section('transaction');
  C := TSQLiteConnection.Create(nil);
  Calls.C := C;
  C.Database := ':memory:';
  Check(not C.InTransaction, 'InTransaction is False on a closed connection');
  CheckSqlError(Calls.StartTransaction, 'Connection is closed', 0, 0, '', 'StartTransaction on a closed connection');
  CheckSqlError(Calls.Commit, 'No transaction is active', 0, 0, '', 'Commit on a closed connection');
  CheckSqlError(Calls.Rollback, 'No transaction is active', 0, 0, '', 'Rollback on a closed connection');
  C.Open;
  C.ExecSQL('CREATE TABLE t(v)');
  Check(not C.InTransaction, 'no transaction after Open');
  CheckSqlError(Calls.Commit, 'No transaction is active', 0, 0, '', 'Commit without a transaction');
  CheckSqlError(Calls.Rollback, 'No transaction is active', 0, 0, '', 'Rollback without a transaction');
  C.StartTransaction;
  Check(C.InTransaction, 'StartTransaction begins one');
  CheckSqlError(Calls.StartTransaction, 'A transaction is already active', 0, 0, '', 'no nesting');
  Check(C.InTransaction, 'the first transaction is still active');
  C.ExecSQL('INSERT INTO t VALUES (1)');
  C.Commit;
  Check(not C.InTransaction, 'Commit ends it');
  CheckEq(Query(C, 'SELECT count(*) FROM t'), '1', 'the row is kept');
  C.StartTransaction;
  C.ExecSQL('INSERT INTO t VALUES (2); INSERT INTO t VALUES (3)');
  CheckEq(Query(C, 'SELECT count(*) FROM t'), '3', 'rows are visible inside the transaction');
  C.Rollback;
  Check(not C.InTransaction, 'Rollback ends it');
  CheckEq(Query(C, 'SELECT count(*) FROM t'), '1', 'the rows are gone');
  C.ExecSQL('BEGIN');
  Check(C.InTransaction, 'InTransaction follows a BEGIN executed as SQL');
  CheckSqlError(Calls.StartTransaction, 'A transaction is already active', 0, 0, '', 'StartTransaction inside it');
  C.Commit;
  Check(not C.InTransaction, 'Commit ends that one too');
  C.Free;

  FileName := GetCurrentDir + '\tx.db';
  C := TSQLiteConnection.Create(nil);
  C.Database := FileName;
  C.Open;
  C.ExecSQL('CREATE TABLE t(v); INSERT INTO t VALUES (1)');
  C.StartTransaction;
  C.ExecSQL('INSERT INTO t VALUES (2)');
  C.Close;
  Check(not C.InTransaction, 'Close ends an active transaction');
  C.Open;
  CheckEq(Query(C, 'SELECT count(*) FROM t'), '1', 'by rolling it back');
  C.Free;
  Check(DeleteFile(FileName), 'the file is deleted');
end;

procedure TestSchema;
var
  C: TSQLiteConnection;
  L: TStringList;
begin
  Section('schema helpers');
  C := TSQLiteConnection.Create(nil);
  Calls.C := C;
  C.Database := ':memory:';
  L := TStringList.Create;
  Calls.List := L;
  CheckSqlError(Calls.TableNames, 'Connection is closed', 0, 0, '', 'GetTableNames on a closed connection');
  Calls.Text := 't';
  CheckSqlError(Calls.FieldNames, 'Connection is closed', 0, 0, '', 'GetFieldNames on a closed connection');
  CheckSqlError(Calls.LastId, 'Connection is closed', 0, 0, '', 'LastInsertRowId on a closed connection');
  C.Open;
  L.Add('junk');
  C.GetTableNames(L);
  CheckEqInt(L.Count, 0, 'an empty database has no tables; the list is cleared first');
  C.ExecSQL('CREATE TABLE zeta(a);' +
    'CREATE TABLE "Alpha"(b);' +
    'CREATE TABLE "we ""ird"(c, "d e", "f""g");' +
    'CREATE TABLE seq(id INTEGER PRIMARY KEY AUTOINCREMENT, n TEXT NOT NULL, born DATE);' +
    'CREATE VIEW vw AS SELECT * FROM zeta;' +
    'CREATE INDEX ix ON zeta(a);' +
    'INSERT INTO seq(n) VALUES (''x'')');
  CheckEq(Query(C, 'SELECT count(*) FROM sqlite_master WHERE name = ''sqlite_sequence'''), '1', 'an internal table exists');
  L.Add('junk');
  C.GetTableNames(L);
  CheckEq(Join(L), 'Alpha|seq|we "ird|zeta', 'GetTableNames: user tables by name, no views, indexes or internal tables');
  L.Add('junk');
  C.GetFieldNames('seq', L);
  CheckEq(Join(L), 'id|n|born', 'GetFieldNames in column order');
  C.GetFieldNames('SEQ', L);
  CheckEq(Join(L), 'id|n|born', 'the table name is not case-sensitive');
  C.GetFieldNames('we "ird', L);
  CheckEq(Join(L), 'c|d e|f"g', 'a table name with a quote and a space');
  L.Add('junk');
  C.GetFieldNames('nope', L);
  CheckEqInt(L.Count, 0, 'an unknown table gives an empty list');
  CheckEq(SQLiteQuoteIdent('abc'), '"abc"', 'an identifier is quoted');
  CheckEq(SQLiteQuoteIdent('a"b""c'), '"a""b""""c"', 'with every quote doubled');
  CheckEq(SQLiteQuoteIdent(''), '""', 'also when empty');

  CheckEqInt(C.LastInsertRowId, 1, 'LastInsertRowId after the first insert');
  C.ExecSQL('INSERT INTO seq(id, n) VALUES (40, ''y'')');
  CheckEqInt(C.LastInsertRowId, 40, 'after an insert with an explicit key');
  C.ExecSQL('INSERT INTO seq(n) VALUES (''z'')');
  CheckEqInt(C.LastInsertRowId, 41, 'after the next automatic key');
  C.ExecSQL('INSERT INTO seq(id, n) VALUES (5000000000, ''big'')');
  Check(C.LastInsertRowId = 5000000000, 'a 64-bit row id');
  L.Free;
  C.Free;
end;

function NewTable(const Name: string; Rec: TRecorder): TMemTable;
begin
  Result := TMemTable.Create(nil);
  Result.Name := Name;
  Result.FieldDefs.Add('v', ftInteger);
  Result.CreateDataSet;
  Result.BeforeClose := Rec.BeforeClose;
  Result.AfterClose := Rec.AfterClose;
end;

procedure TestCascade;
var
  C: TSQLiteConnection;
  Rec: TRecorder;
  A, B, U: TMemTable;
begin
  Section('close cascade');
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  Rec := NewRecorder(C);
  A := NewTable('A', Rec);
  B := NewTable('B', Rec);
  U := NewTable('U', Rec);
  CheckEqInt(C.DataSetCount, 0, 'no dataset is registered at first');
  C.RegisterDataSet(A, Rec.Detach);
  C.RegisterDataSet(B, Rec.DetachSecond);
  C.RegisterDataSet(A, Rec.Detach);
  C.RegisterDataSet(U, Rec.DetachSecond);
  C.UnregisterDataSet(U);
  C.UnregisterDataSet(U);
  CheckEqInt(C.DataSetCount, 2, 'a dataset is registered once; an unregistered one is forgotten');
  Check((C.DataSets[0] = A) and (C.DataSets[1] = B), 'in registration order');
  C.Open;
  C.StartTransaction;
  Rec.Take;
  C.Close;
  CheckEq(Rec.Take, 'BeforeDisconnect:True,BeforeClose A,AfterClose A,BeforeClose B,AfterClose B,' +
    'Detach closing tx=True,Detach2 closing,AfterDisconnect:False',
    'Close: datasets close, statements are released, then rollback and disconnect');
  Check(not A.Active and not B.Active, 'the registered datasets are closed');
  Check(U.Active, 'the unregistered one is not');
  CheckEqInt(C.DataSetCount, 2, 'the datasets stay registered');
  A.Open;
  C.Close;
  CheckEq(Rec.Take, '', 'Close on a closed connection leaves the datasets alone');
  C.Open;
  Rec.Take;
  C.Close;
  CheckEq(Rec.Take, 'BeforeDisconnect:True,BeforeClose A,AfterClose A,Detach closing tx=False,Detach2 closing,AfterDisconnect:False',
    'a closed dataset is not closed again');

  Section('destroying the connection');
  A.Open;
  C.Open;
  Rec.Take;
  C.Free;
  CheckEq(Rec.Take, 'BeforeDisconnect:True,BeforeClose A,AfterClose A,Detach closing tx=False,Detach2 closing,' +
    'AfterDisconnect:False,Detach destroying,Detach2 destroying', 'an open connection closes, then detaches its datasets');
  Check(not A.Active, 'the dataset is closed');

  C := TSQLiteConnection.Create(nil);
  Watch(Rec, C);
  C.RegisterDataSet(A, Rec.Detach);
  A.Open;
  C.Free;
  CheckEq(Rec.Take, 'Detach destroying', 'a closed connection only detaches');
  Check(A.Active, 'and leaves the dataset open');
  A.Free;
  B.Free;
  U.Free;
  Rec.Free;
end;

begin
  ExeDir := ExtractFilePath(ParamStr(0));
  Calls := TCalls.Create;
  TestErrors;
  TestOpen;
  TestExec;
  TestTransactions;
  TestSchema;
  TestCascade;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
