// covers: DAT-140, DAT-141, DAT-142, DAT-143, DAT-144, DAT-145, DAT-146, DAT-147, DAT-148
program SqlQuery;
{ TSQLiteQuery as a statement. Sections: params (SQL and Params wiring), prepare (Prepare, Unprepare and
  their errors), binding (one value of every parameter type, checked with typeof() and quote()), exec
  (ExecSQL, RowsAffected, errors), result sets (statements without columns, column names, fetch-all,
  errors while loading, the connection cascade). }
uses
  SysUtils, Classes, DB, SQLite3API, SQLiteData, DataTestUtils;

type
  TCalls = class
  public
    C: TSQLiteConnection;
    Q: TSQLiteQuery;
    Name: string;
    procedure Prepare;
    procedure Open;
    procedure Exec;
    procedure ParamByName;
    procedure SetConnection;
    procedure UpdateDefs;
    procedure DropTable;
  end;

var
  Calls: TCalls;

procedure TCalls.Prepare;
begin
  Q.Prepare;
end;

procedure TCalls.Open;
begin
  Q.Open;
end;

procedure TCalls.Exec;
begin
  Q.ExecSQL;
end;

procedure TCalls.ParamByName;
begin
  Q.ParamByName(Name);
end;

procedure TCalls.SetConnection;
begin
  Q.Connection := C;
end;

procedure TCalls.UpdateDefs;
begin
  Q.FieldDefs.Update;
end;

procedure TCalls.DropTable;
begin
  C.ExecSQL('DROP TABLE ' + Name);
end;

function NewConnection: TSQLiteConnection;
begin
  Result := TSQLiteConnection.Create(nil);
  Result.Database := ':memory:';
end;

function NewQuery(C: TSQLiteConnection): TSQLiteQuery;
begin
  Result := TSQLiteQuery.Create(nil);
  Result.Connection := C;
end;

{ The first column of the first row of a query, as text ('NULL' for null, '<none>' without a row). }
function One(C: TSQLiteConnection; const SQL: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := NewQuery(C);
  Q.SQL.Text := SQL;
  Q.Open;
  if Q.IsEmpty then
    Result := '<none>'
  else if Q.Fields[0].IsNull then
    Result := 'NULL'
  else
    Result := Q.Fields[0].AsString;
  Q.Free;
end;

function ParamNames(Q: TSQLiteQuery): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Q.Params.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Q.Params[I].Name;
  end;
end;

function FieldNames(Q: TSQLiteQuery): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Q.FieldCount - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Q.Fields[I].FieldName;
  end;
end;

{ The rows of an open query as "v|v;v|v". }
function Dump(Q: TSQLiteQuery): string;
var
  I: Integer;
begin
  Result := '';
  Q.First;
  while not Q.Eof do
  begin
    if Result <> '' then
      Result := Result + ';';
    for I := 0 to Q.FieldCount - 1 do
    begin
      if I > 0 then
        Result := Result + '|';
      if Q.Fields[I].IsNull then
        Result := Result + 'NULL'
      else
        Result := Result + Q.Fields[I].AsString;
    end;
    Q.Next;
  end;
end;

procedure TestParams;
var
  C: TSQLiteConnection;
  Q: TSQLiteQuery;
begin
  Section('params: SQL and Params wiring');
  C := NewConnection;
  Q := NewQuery(C);
  Calls.C := C;
  Calls.Q := Q;
  CheckEqBool(Q.ParamCheck, True, 'ParamCheck is True by default');
  CheckEqBool(Q.ReadOnly, False, 'ReadOnly is False by default');
  CheckEqInt(Q.Params.Count, 0, 'no parameters without SQL');
  Q.SQL.Text := 'SELECT :a AS x, :b AS y, :a AS z';
  CheckEq(ParamNames(Q), 'a,b', 'assigning SQL builds the parameters, one per name');
  Q.ParamByName('a').AsInteger := 7;
  Q.SQL.Text := 'SELECT :c, :a';
  CheckEq(ParamNames(Q), 'c,a', 'changing SQL rebuilds them in the order of first occurrence');
  CheckEqInt(Q.ParamByName('a').AsInteger, 7, 'a parameter that occurs again keeps its value');
  Q.SQL.Clear;
  Q.SQL.Add('SELECT :one,');
  Q.SQL.Add('  '':not'' , :two -- :neither');
  CheckEq(ParamNames(Q), 'one,two', 'every change of the lines is parsed; literals and comments are skipped');
  Check(Q.ParamByName('ONE') = Q.Params[0], 'ParamByName is case-insensitive');
  Calls.Name := 'nope';
  CheckRaises(Calls.ParamByName, 'EDatabaseError', 'Parameter ''nope'' not found', 'ParamByName of a missing name');
  Q.ParamCheck := False;
  Q.SQL.Text := 'SELECT :other';
  CheckEq(ParamNames(Q), 'one,two', 'with ParamCheck False the parameters are left alone');
  Q.ParamCheck := True;
  Q.SQL.Text := 'SELECT 1 AS n';
  CheckEqInt(Q.Params.Count, 0, 'SQL without parameters clears them');

  Q.Open;
  CheckEqBool(Q.Active, True, 'the query opens');
  CheckEqBool(Q.Prepared, True, 'and is prepared');
  Q.SQL.Text := 'SELECT 2 AS n';
  CheckEqBool(Q.Active, False, 'changing SQL closes an active dataset');
  CheckEqBool(Q.Prepared, False, 'and unprepares');
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 2, 'the new statement runs at the next Open');
  Q.Free;
  C.Free;
end;

procedure TestPrepare;
var
  C: TSQLiteConnection;
  Q: TSQLiteQuery;
  E: ESQLiteError;
begin
  Section('prepare');
  C := NewConnection;
  Q := TSQLiteQuery.Create(nil);
  Calls.C := C;
  Calls.Q := Q;
  Q.SQL.Text := 'SELECT 1';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'Connection is not assigned', 'Prepare without a connection');
  CheckRaises(Calls.Open, 'ESQLiteError', 'Connection is not assigned', 'Open without a connection');
  CheckRaises(Calls.Exec, 'ESQLiteError', 'Connection is not assigned', 'ExecSQL without a connection');
  Q.Connection := C;
  CheckEqBool(Q.Prepared, False, 'not prepared at first');
  CheckEqBool(C.Connected, False, 'the connection is closed');
  Q.Prepare;
  CheckEqBool(C.Connected, True, 'Prepare opens a closed connection');
  CheckEqBool(Q.Prepared, True, 'Prepared after Prepare');
  CheckEqBool(Q.Active, False, 'Prepare does not open the dataset');
  Q.Prepare;
  CheckEqBool(Q.Prepared, True, 'Prepare again is a no-op');
  Q.Unprepare;
  CheckEqBool(Q.Prepared, False, 'Unprepare finalizes');
  Q.Unprepare;
  CheckEqBool(Q.Prepared, False, 'Unprepare again is a no-op');
  Q.Prepared := True;
  CheckEqBool(Q.Prepared, True, 'Prepared := True prepares');
  Q.Prepared := False;
  CheckEqBool(Q.Prepared, False, 'Prepared := False unprepares');

  Q.SQL.Text := '';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'SQL text is empty', 'empty SQL');
  Q.SQL.Text := '  -- only a comment';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'SQL text is empty', 'comment-only SQL');
  CheckRaises(Calls.Open, 'ESQLiteError', 'SQL text is empty', 'Open with empty SQL');
  CheckEqBool(Q.Active, False, 'the dataset stays closed');
  Q.SQL.Text := 'SELECT 1; SELECT 2';
  CheckRaises(Calls.Prepare, 'ESQLiteError',
    'Only one statement is allowed in a query; use TSQLiteConnection.ExecSQL for scripts', 'two statements');
  CheckEqBool(Q.Prepared, False, 'nothing stays prepared');
  Q.SQL.Text := 'SELECT 1;  ' + #13#10 + '  ';
  Q.Prepare;
  CheckEqBool(Q.Prepared, True, 'a terminating semicolon and white space are allowed');
  Q.SQL.Text := 'SELECT ?';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'Positional parameters are not supported; use :name',
    'a positional parameter');
  Q.SQL.Text := 'SELECT :a, ?2';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'Positional parameters are not supported; use :name',
    'a numbered parameter');
  CheckEqBool(Q.Prepared, False, 'nothing stays prepared after it');

  Q.SQL.Text := 'SELEC 1';
  CheckRaises(Calls.Prepare, 'ESQLiteError', 'SQLite error 1: near "SELEC": syntax error', 'a syntax error');
  E := nil;
  try
    Q.Prepare;
  except
    on X: ESQLiteError do
    begin
      CheckEqInt(X.ErrorCode, SQLITE_ERROR, 'its primary code');
      CheckEq(X.SQL, 'SELEC 1', 'its SQL is the statement text');
      E := X;
    end;
  end;
  Check(E <> nil, 'the error is an ESQLiteError');
  Q.SQL.Text := 'SELECT * FROM nope';
  CheckRaises(Calls.Open, 'ESQLiteError', 'SQLite error 1: no such table: nope', 'Open of an unknown table');

  Q.SQL.Text := 'SELECT 5 AS n';
  Q.Open;
  CheckEqBool(Q.Prepared, True, 'Open prepares implicitly');
  Q.Close;
  CheckEqBool(Q.Prepared, True, 'closing the dataset keeps the statement prepared');
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 5, 'the prepared statement is used again');
  C.Close;
  CheckEqBool(Q.Active, False, 'closing the connection closes the dataset');
  CheckEqBool(Q.Prepared, False, 'and unprepares');
  Q.Open;
  CheckEqBool(C.Connected, True, 'Open opens the connection again');
  CheckEqInt(Q.Fields[0].AsInteger, 5, 'and runs the statement');
  Q.Free;
  C.Free;
end;

{ typeof() and quote() of a bound parameter. }
function Bound(Q: TSQLiteQuery): string;
begin
  Q.Close;
  Q.Open;
  Result := Q.Fields[0].AsString + ' ' + Q.Fields[1].AsString;
end;

procedure TestBinding;
var
  C: TSQLiteConnection;
  Q: TSQLiteQuery;
  P: TParam;
  B: TBytes;
  S: string;
begin
  Section('binding');
  C := NewConnection;
  Q := NewQuery(C);
  Calls.C := C;
  Calls.Q := Q;
  Q.SQL.Text := 'SELECT typeof(:p) AS t, quote(:p) AS q';
  P := Q.ParamByName('p');
  CheckEq(Bound(Q), 'null NULL', 'an unbound parameter is NULL');
  P.AsInteger := 42;
  CheckEq(Bound(Q), 'integer 42', 'AsInteger');
  P.AsInteger := -1;
  CheckEq(Bound(Q), 'integer -1', 'a negative integer');
  P.AsLargeInt := 1099511627776;
  CheckEq(Bound(Q), 'integer 1099511627776', 'AsLargeInt');
  P.AsFloat := 1.5;
  CheckEq(Bound(Q), 'real 1.5', 'AsFloat');
  P.AsFloat := 2;
  CheckEq(Bound(Q), 'real 2.0', 'an integral float stays REAL');
  P.AsBoolean := True;
  CheckEq(Bound(Q), 'integer 1', 'AsBoolean True');
  P.AsBoolean := False;
  CheckEq(Bound(Q), 'integer 0', 'AsBoolean False');
  P.AsString := 'it''s';
  CheckEq(Bound(Q), 'text ''it''''s''', 'AsString');
  P.AsString := '';
  CheckEq(Bound(Q), 'text ''''', 'an empty string is empty TEXT, not NULL');
  P.AsString := 'gr' + #$00FC + #$00DF + ' ' + #$D83D#$DE00;
  Q.Close;
  Q.Open;
  CheckEq(Q.Fields[0].AsString, 'text', 'non-ASCII text');
  CheckEq(Q.Fields[1].AsString, '''gr' + #$00FC + #$00DF + ' ' + #$D83D#$DE00 + '''', 'comes back unchanged');
  SetLength(B, 3);
  B[0] := 1;
  B[1] := 2;
  B[2] := 255;
  P.AsBytes := B;
  CheckEq(Bound(Q), 'blob X''0102FF''', 'AsBytes');
  SetLength(B, 0);
  P.AsBytes := B;
  CheckEq(Bound(Q), 'blob X''''', 'empty bytes are an empty BLOB, not NULL');
  P.AsDateTime := EncodeDate(2024, 3, 5) + EncodeTime(10, 20, 30, 0);
  CheckEq(Bound(Q), 'text ''2024-03-05 10:20:30''', 'AsDateTime');
  P.AsDateTime := EncodeDate(2024, 3, 5) + EncodeTime(10, 20, 30, 250);
  CheckEq(Bound(Q), 'text ''2024-03-05 10:20:30.250''', 'AsDateTime with milliseconds');
  P.AsDateTime := EncodeDate(987, 12, 31);
  CheckEq(Bound(Q), 'text ''0987-12-31 00:00:00''', 'a year below 1000 has four digits');
  P.Clear;
  CheckEq(Bound(Q), 'null NULL', 'a cleared parameter is NULL');

  Q.SQL.Text := 'SELECT :v AS v';
  S := 'a' + #0 + 'b';
  Q.ParamByName('v').AsString := S;
  Q.Open;
  CheckEqInt(Length(Q.Fields[0].AsString), 3, 'an embedded #0 does not cut a text value');
  Check(Q.Fields[0].AsString = S, 'and the text comes back unchanged');

  Q.SQL.Text := 'SELECT :a * 100 + @a * 10 + $a AS n';
  CheckEq(ParamNames(Q), 'a', 'only the :name form is parsed');
  Q.ParamByName('a').AsInteger := 3;
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 333, 'the @name and $name forms are bound by name too');
  Q.SQL.Text := 'SELECT :Num + :NUM AS n';
  CheckEq(ParamNames(Q), 'Num', 'names differing in case are one parameter');
  Q.ParamByName('num').AsInteger := 4;
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 8, 'and every spelling is bound');

  Q.ParamCheck := False;
  Q.SQL.Text := 'SELECT :zz AS n';
  CheckRaises(Calls.Open, 'ESQLiteError', 'No value for parameter ''zz''', 'a statement parameter without a TParam');
  CheckEqBool(Q.Active, False, 'the dataset stays closed');
  CheckRaises(Calls.Exec, 'ESQLiteError', 'No value for parameter ''zz''', 'the same in ExecSQL');
  Q.ParamCheck := True;

  Q.SQL.Text := 'SELECT :n + 1 AS n';
  Q.ParamByName('n').AsInteger := 1;
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 2, 'first execution');
  Q.ParamByName('n').AsInteger := 10;
  CheckEqInt(Q.Fields[0].AsInteger, 2, 'changing a parameter does not touch the open dataset');
  Q.Refresh;
  CheckEqInt(Q.Fields[0].AsInteger, 11, 'Refresh binds the current values');
  Q.ParamByName('n').AsInteger := 20;
  Q.Close;
  Q.Open;
  CheckEqInt(Q.Fields[0].AsInteger, 21, 'and so does the next Open');
  Q.ParamByName('n').Bound := False;
  Q.Close;
  Q.Open;
  Check(Q.Fields[0].IsNull, 'an unbound parameter replaces the earlier binding by NULL');
  Q.Free;
  C.Free;
end;

procedure TestExec;
var
  C: TSQLiteConnection;
  Q: TSQLiteQuery;
  I: Integer;
begin
  Section('exec');
  C := NewConnection;
  Q := NewQuery(C);
  Calls.C := C;
  Calls.Q := Q;
  CheckEqInt(Q.RowsAffected, -1, 'RowsAffected is -1 before any ExecSQL');
  Q.SQL.Text := 'CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, n INTEGER)';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 0, 'a CREATE TABLE changes no rows');
  CheckEqBool(Q.Prepared, True, 'ExecSQL prepares implicitly');
  CheckEqBool(Q.Active, False, 'and does not open the dataset');
  Q.SQL.Text := 'INSERT INTO t (name, n) VALUES (:name, :n)';
  for I := 1 to 4 do
  begin
    Q.ParamByName('name').AsString := 'row' + IntToStr(I);
    Q.ParamByName('n').AsInteger := I * 10;
    Q.ExecSQL;
  end;
  CheckEqInt(Q.RowsAffected, 1, 'an INSERT of one row');
  CheckEq(One(C, 'SELECT group_concat(name || ''='' || n, '','') FROM t'), 'row1=10,row2=20,row3=30,row4=40',
    'the prepared statement ran once per ExecSQL with the values of that call');
  Q.SQL.Text := 'UPDATE t SET n = n + 1 WHERE n >= :min';
  Q.ParamByName('min').AsInteger := 20;
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 3, 'an UPDATE of three rows');
  Q.SQL.Text := 'CREATE TABLE u (x)';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 0, 'a CREATE TABLE after it does not repeat the count');
  Q.SQL.Text := 'DELETE FROM t WHERE n > 1000';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 0, 'a DELETE that matches nothing');
  Q.SQL.Text := 'SELECT * FROM t';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 0, 'ExecSQL of a SELECT ignores the rows');
  CheckEqBool(Q.Active, False, 'and the dataset stays closed');
  Q.SQL.Text := 'DELETE FROM t WHERE id = 4';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 1, 'a DELETE of one row');
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 0, 'the same DELETE again finds nothing');

  Q.SQL.Text := 'INSERT INTO t (name) VALUES (:name)';
  CheckRaises(Calls.Exec, 'ESQLiteError', 'SQLite error 1299: NOT NULL constraint failed: t.name',
    'a constraint error in ExecSQL');
  Q.ParamByName('name').AsString := 'late';
  Q.ExecSQL;
  CheckEqInt(Q.RowsAffected, 1, 'the statement is usable after the error');
  CheckEq(One(C, 'SELECT count(*) FROM t'), '4', 'and the connection too');

  Q.SQL.Text := 'SELECT id FROM t ORDER BY id';
  Q.Open;
  CheckRaises(Calls.Exec, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'ExecSQL on an active dataset');
  CheckEqBool(Q.Active, True, 'the dataset stays open');
  Q.Free;
  C.Free;
end;

procedure TestResultSets;
var
  C, C2: TSQLiteConnection;
  Q: TSQLiteQuery;
  S: string;
  I: Integer;
begin
  Section('result sets');
  C := NewConnection;
  Q := NewQuery(C);
  Calls.C := C;
  Calls.Q := Q;
  C.Open;
  C.ExecSQL('CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT, n INTEGER);' +
    'INSERT INTO t (name, n) VALUES (''a'', 1), (''b'', 2), (''c'', 3)');
  Q.SQL.Text := 'INSERT INTO t (name) VALUES (''x'')';
  CheckRaises(Calls.Open, 'ESQLiteError', 'Statement does not return a result set; use ExecSQL',
    'Open of a statement without columns');
  CheckEqBool(Q.Active, False, 'the dataset stays closed');
  CheckEq(One(C, 'SELECT count(*) FROM t'), '3', 'and the statement did not run');

  Q.SQL.Text := 'SELECT 1 AS a, 2 AS a, 3 AS A, 4 AS b, 5 AS b, 6 AS c';
  Q.Open;
  CheckEq(FieldNames(Q), 'a,a_1,A_2,b,b_1,c', 'duplicate column names get suffixes in column order');
  CheckEq(Dump(Q), '1|2|3|4|5|6', 'and keep their values');
  Q.SQL.Text := 'SELECT 1 AS a, 2 AS a, 3 AS a_1';
  Q.Open;
  CheckEq(FieldNames(Q), 'a,a_1,a_1_1', 'a suffixed name that exists is a duplicate itself');
  Q.SQL.Text := 'SELECT 1 AS "", 2 AS x, 3 AS ""';
  Q.Open;
  CheckEq(FieldNames(Q), 'Column1,x,Column3', 'empty column names become Column<N>');
  Q.SQL.Text := 'SELECT t.id, u.id, t.name FROM t JOIN t AS u ON u.id = t.id WHERE t.id = 2';
  Q.Open;
  CheckEq(FieldNames(Q), 'id,id_1,name', 'the columns of a join');
  CheckEq(Dump(Q), '2|2|b', 'with their values');

  Q.SQL.Text := 'SELECT id, name, n FROM t ORDER BY id';
  Q.Open;
  CheckEqInt(Q.RecordCount, 3, 'all rows are fetched at Open');
  CheckEq(Dump(Q), '1|a|1;2|b|2;3|c|3', 'in statement order');
  C.ExecSQL('INSERT INTO t (name, n) VALUES (''d'', 4)');
  CheckEqInt(Q.RecordCount, 3, 'a later change of the table does not reach the open dataset');
  Q.Refresh;
  CheckEq(Dump(Q), '1|a|1;2|b|2;3|c|3;4|d|4', 'Refresh runs the statement again');
  C.ExecSQL('DELETE FROM t WHERE id = 4');
  Q.Close;
  Q.Open;
  CheckEqInt(Q.RecordCount, 3, 'and so does closing and opening');
  Q.Unprepare;
  CheckEqBool(Q.Active, True, 'Unprepare leaves an open dataset open');
  C.ExecSQL('INSERT INTO t (name, n) VALUES (''e'', 5)');
  Q.Refresh;
  CheckEqInt(Q.RecordCount, 4, 'Refresh prepares the statement again');
  C.ExecSQL('DELETE FROM t WHERE id = 4');
  C.ExecSQL('CREATE TABLE gone (x); INSERT INTO gone VALUES (1), (2)');
  Q.SQL.Text := 'SELECT x FROM gone';
  Q.Open;
  C.ExecSQL('DROP TABLE gone');
  CheckEqInt(Q.RecordCount, 2, 'no statement stays active after Open: the table can be dropped, the rows stay');
  Q.Close;

  { many rows }
  C.ExecSQL('CREATE TABLE big (i INTEGER)');
  C.StartTransaction;
  Q.SQL.Text := 'INSERT INTO big VALUES (:i)';
  for I := 1 to 2000 do
  begin
    Q.ParamByName('i').AsInteger := I;
    Q.ExecSQL;
  end;
  C.Commit;
  Q.SQL.Text := 'SELECT i FROM big ORDER BY i';
  Q.Open;
  CheckEqInt(Q.RecordCount, 2000, 'two thousand rows are all in memory');
  Q.Last;
  CheckEqInt(Q.Fields[0].AsInteger, 2000, 'up to the last one');

  Section('errors while loading');
  C.ExecSQL('CREATE TABLE ov (id INTEGER PRIMARY KEY, x INTEGER);' +
    'INSERT INTO ov (x) VALUES (1), (-9223372036854775807 - 1), (3)');
  Q.SQL.Text := 'SELECT abs(x) AS a FROM ov ORDER BY id';
  CheckRaises(Calls.Open, 'ESQLiteError', 'SQLite error 1: integer overflow', 'an SQLite error at the second row');
  CheckEqBool(Q.Active, False, 'the dataset does not become active');
  CheckEqInt(Q.FieldCount, 0, 'and has no fields');
  C.ExecSQL('UPDATE ov SET x = -2 WHERE id = 2');
  Q.Open;
  CheckEq(Dump(Q), '1;2;3', 'the statement was reset: it runs after the data is repaired');
  Q.Close;
  C.ExecSQL('CREATE TABLE bad (id INTEGER PRIMARY KEY, n INTEGER); INSERT INTO bad (n) VALUES (1), (''abc'')');
  Q.SQL.Text := 'SELECT n FROM bad ORDER BY id';
  CheckRaises(Calls.Open, 'EDatabaseError', '''abc'' is not a valid integer value for field ''n''',
    'a convert error at the second row');
  CheckEqBool(Q.Active, False, 'the dataset does not become active');
  Calls.Name := 'bad';
  Calls.DropTable;
  CheckEq(One(C, 'SELECT count(*) FROM sqlite_master WHERE name = ''bad'''), '0',
    'the statement was reset: the table can be dropped');
  C.ExecSQL('CREATE TABLE ov2 (id INTEGER PRIMARY KEY, x INTEGER); INSERT INTO ov2 (x) VALUES (1), (2)');
  Q.SQL.Text := 'SELECT abs(x) AS a FROM ov2 ORDER BY id';
  Q.Open;
  C.ExecSQL('UPDATE ov2 SET x = -9223372036854775807 - 1 WHERE id = 2');
  S := '';
  try
    Q.Refresh;
  except
    on E: ESQLiteError do
      S := E.Message;
  end;
  CheckEq(S, 'SQLite error 1: integer overflow', 'an SQLite error in Refresh propagates');
  CheckEqBool(Q.Active, True, 'the dataset stays open');
  Calls.Name := 'ov2';
  Calls.DropTable;
  Check(True, 'and the statement was reset');
  Q.Close;

  Section('field definitions of a closed query');
  C.ExecSQL('CREATE TABLE d (id INTEGER PRIMARY KEY, name VARCHAR(20))');
  Q.SQL.Text := 'SELECT id, name, 1.5 AS f FROM d';
  Q.FieldDefs.Update;
  CheckEqBool(Q.Active, False, 'FieldDefs.Update leaves the dataset closed');
  CheckEqInt(Q.FieldDefs.Count, 3, 'and builds the definitions');
  CheckEq(Q.FieldDefs[1].Name, 'name', 'with their names');
  CheckEqInt(Q.FieldDefs[1].Size, 20, 'and sizes');
  Calls.Name := 'd';
  Calls.DropTable;
  Check(True, 'no statement stays active after it');
  CheckRaises(Calls.UpdateDefs, 'ESQLiteError', 'no such table: d', 'FieldDefs.Update reports the errors of the statement');

  Section('connection');
  Q.SQL.Text := 'SELECT 1 AS n';
  Q.Open;
  C2 := NewConnection;
  Calls.C := C2;
  CheckRaises(Calls.SetConnection, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'changing Connection on an active dataset');
  Check(Q.Connection = C, 'the connection is unchanged');
  CheckEqInt(C.DataSetCount, 1, 'the query is registered with its connection');
  Q.Close;
  Q.Connection := C2;
  CheckEqBool(Q.Prepared, False, 'changing Connection unprepares');
  CheckEqInt(C.DataSetCount, 0, 'the query left the old connection');
  CheckEqInt(C2.DataSetCount, 1, 'and is registered with the new one');
  Q.Open;
  CheckEqBool(C2.Connected, True, 'the new connection is opened by Open');
  C2.Free;
  CheckEqBool(Q.Active, False, 'destroying the connection closes the dataset');
  Check(Q.Connection = nil, 'and clears Connection');
  Q.Connection := C;
  Q.Open;
  Q.Free;
  CheckEqInt(C.DataSetCount, 0, 'a destroyed query leaves its connection');
  C.Free;
end;

begin
  Calls := TCalls.Create;
  TestParams;
  TestPrepare;
  TestBinding;
  TestExec;
  TestResultSets;
  WriteLn('failed checks: ', FailedChecks);
end.
