// covers: DAT-160, DAT-161, DAT-162
program SqlTable;
{ TSQLiteTable: the generated statement, a table with a primary key, IndexFieldNames, changing TableName
  and IndexFieldNames while open, a table without primary key and its hidden rowid field, a user column
  named rowid, a table without row id, a view, identifiers that need quoting, and the operations a table
  refuses. }
uses
  SysUtils, Classes, DB, SQLiteData, DataTestUtils, TestDataSet, Conformance;

type
  TCalls = class
  public
    T: TSQLiteTable;
    List: TStrings;
    procedure Open;
    procedure Edit;
    procedure ExecSQL;
    procedure AssignSQL;
    procedure SetSQLText;
    procedure AddSQLLine;
    procedure WriteRowId;
    procedure SetIndexNope;
    procedure SetTableNope;
  end;

var
  Calls: TCalls;
  C: TSQLiteConnection;

procedure TCalls.Open;
begin
  T.Open;
end;

procedure TCalls.Edit;
begin
  T.Edit;
end;

procedure TCalls.ExecSQL;
begin
  T.ExecSQL;
end;

procedure TCalls.AssignSQL;
begin
  T.SQL := List;
end;

procedure TCalls.SetSQLText;
begin
  T.SQL.Text := 'SELECT 1';
end;

procedure TCalls.AddSQLLine;
begin
  T.SQL.Add('WHERE 1 = 0');
end;

procedure TCalls.WriteRowId;
begin
  T.FieldByName('rowid').AsLargeInt := 99;
end;

procedure TCalls.SetIndexNope;
begin
  T.IndexFieldNames := 'nope';
end;

procedure TCalls.SetTableNope;
begin
  T.TableName := 'nope';
end;

function NewTable(const TableName: string): TSQLiteTable;
begin
  Result := TSQLiteTable.Create(nil);
  Result.Connection := C;
  Result.TableName := TableName;
  Calls.T := Result;
end;

{ The first column of the first row of a query, as text ('NULL' for null, '<none>' without a row). }
function One(const SQL: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C;
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

{ 'name:type name:type ...' with (hidden) and (read-only) marks }
function FieldList(DS: TDataSet): string;
var
  I: Integer;
  F: TField;
begin
  Result := '';
  for I := 0 to DS.FieldCount - 1 do
  begin
    F := DS.Fields[I];
    if I > 0 then
      Result := Result + ' ';
    Result := Result + F.FieldName + ':' + FieldTypeName(F.DataType);
    if not F.Visible then
      Result := Result + '(hidden)';
    if F.ReadOnly then
      Result := Result + '(read-only)';
  end;
end;

{ the values of one field over all rows, as 'a,b,c' (NULL for null) }
function Column(DS: TDataSet; const FieldName: string): string;
var
  F: TField;
begin
  Result := '';
  F := DS.FieldByName(FieldName);
  DS.First;
  while not DS.Eof do
  begin
    if Result <> '' then
      Result := Result + ',';
    if F.IsNull then
      Result := Result + 'NULL'
    else
      Result := Result + F.AsString;
    DS.Next;
  end;
end;

function Verdict(T: TSQLiteTable): string;
begin
  if T.CanModify then
    Result := 'updatable'
  else
    Result := 'read-only: ' + T.ReadOnlyReason;
end;

procedure CreateSchema;
begin
  C.ExecSQL(
    'CREATE TABLE people (id INTEGER PRIMARY KEY, name VARCHAR(20) NOT NULL, age INTEGER);' +
    'INSERT INTO people (name, age) VALUES (''Cy'', 30), (''Ada'', 36), (''Bob'', NULL);' +
    'CREATE TABLE log (msg TEXT, n INTEGER);' +
    'CREATE TABLE shadow (rowid TEXT, v INTEGER);' +
    'INSERT INTO shadow VALUES (''x'', 1);' +
    'CREATE TABLE pair (a INTEGER, b TEXT, v TEXT, PRIMARY KEY (a, b)) WITHOUT ROWID;' +
    'INSERT INTO pair VALUES (1, ''x'', ''one-x''), (1, ''y'', ''one-y''), (2, ''x'', ''two-x'');' +
    'CREATE VIEW adults AS SELECT id, name FROM people WHERE age >= 18;' +
    'CREATE TABLE "my ""t""" ("a b" INTEGER PRIMARY KEY, "order" TEXT);' +
    'INSERT INTO "my ""t""" VALUES (1, ''z''), (2, ''a'');');
end;

procedure TestRefused;
var
  T: TSQLiteTable;
  L: TStringList;
begin
  Section('refused operations');
  T := TSQLiteTable.Create(nil);
  Calls.T := T;
  CheckRaises(Calls.Open, 'ESQLiteError', 'Table name is empty', 'Open without a table name');
  T.TableName := 'people';
  CheckRaises(Calls.Open, 'ESQLiteError', 'Connection is not assigned', 'Open without a connection');
  T.Connection := C;
  T.TableName := '';
  CheckRaises(Calls.Open, 'ESQLiteError', 'Table name is empty', 'Open with a connection and no table name');
  Check(not T.Active, 'the table stays closed');
  T.TableName := 'nope';
  CheckRaises(Calls.Open, 'ESQLiteError', 'SQLite error 1: no such table: nope', 'Open of a missing table');
  T.TableName := 'people';
  L := TStringList.Create;
  L.Add('SELECT 1');
  Calls.List := L;
  CheckRaises(Calls.AssignSQL, 'EDatabaseError', 'Not supported', 'SQL := on a closed table');
  CheckRaises(Calls.SetSQLText, 'EDatabaseError', 'Not supported', 'SQL.Text := on a closed table');
  CheckRaises(Calls.ExecSQL, 'EDatabaseError', 'Not supported', 'ExecSQL on a closed table');
  T.Open;
  CheckRaises(Calls.AssignSQL, 'EDatabaseError', 'Not supported', 'SQL := on an open table');
  CheckRaises(Calls.SetSQLText, 'EDatabaseError', 'Not supported', 'SQL.Text := on an open table');
  CheckRaises(Calls.AddSQLLine, 'EDatabaseError', 'Not supported', 'SQL.Add on an open table');
  CheckRaises(Calls.ExecSQL, 'EDatabaseError', 'Not supported', 'ExecSQL on an open table');
  Check(T.Active, 'the table stays open');
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people"', 'the generated statement is kept');
  T.Refresh;
  CheckEqInt(T.RecordCount, 3, 'and Refresh still runs it');
  L.Free;
  T.Free;
end;

procedure TestPrimaryKey;
var
  T: TSQLiteTable;
begin
  Section('table with a primary key');
  T := NewTable('people');
  Check(not T.Active, 'assigning TableName does not open');
  CheckEqInt(T.Params.Count, 0, 'no parameters');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people"', 'the statement');
  CheckEq(FieldList(T), 'id:ftAutoInc(read-only) name:ftString age:ftInteger', 'the fields');
  Check(T.FindField('rowid') = nil, 'no rowid field');
  CheckEq(Verdict(T), 'updatable', 'the table is updatable');
  CheckEq(T.UpdateTable, 'people', 'UpdateTable is the table name');
  CheckEq(T.KeyFields, '', 'KeyFields is empty');
  Check(T.FieldByName('name').Required and not T.FieldByName('age').Required, 'Required follows NOT NULL');
  CheckEq(Column(T, 'name'), 'Cy,Ada,Bob', 'without IndexFieldNames the rows come in row id order');
  T.Append;
  T.FieldByName('name').AsString := 'Dee';
  T.FieldByName('age').AsInteger := 50;
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 4, 'the key of the inserted row');
  CheckEq(One('SELECT name || ''/'' || age FROM people WHERE id = 4'), 'Dee/50', 'the row is in the table');
  T.First;
  T.Edit;
  T.FieldByName('age').AsInteger := 31;
  T.Post;
  CheckEq(One('SELECT age FROM people WHERE id = 1'), '31', 'an edit is written');
  T.Last;
  T.Delete;
  CheckEq(One('SELECT count(*) FROM people'), '3', 'a delete is written');
  C.ExecSQL('INSERT INTO people (name, age) VALUES (''Eve'', 22)');
  CheckEqInt(T.RecordCount, 3, 'a row added behind the back of the table is not seen');
  T.Refresh;
  CheckEq(Column(T, 'name'), 'Cy,Ada,Bob,Eve', 'until Refresh');
  T.Close;
  T.ReadOnly := True;
  T.Open;
  CheckEq(Verdict(T), 'read-only: Dataset is read-only', 'ReadOnly');
  CheckRaises(Calls.Edit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit on a read-only table');
  T.Free;
end;

procedure TestIndexFieldNames;
var
  T: TSQLiteTable;
  Rec: TEventRecorder;
begin
  Section('IndexFieldNames');
  T := NewTable('people');
  T.IndexFieldNames := 'name';
  Check(not T.Active, 'assigning IndexFieldNames does not open');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people" ORDER BY "people"."name"', 'the statement');
  CheckEq(Column(T, 'name'), 'Ada,Bob,Cy,Eve', 'ordered by name');
  Rec := TEventRecorder.Create(T);
  T.IndexFieldNames := 'name';
  CheckEq(Rec.Take, '', 'assigning the same value does nothing');
  T.IndexFieldNames := 'age;name';
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose,BeforeOpen,[deUpdateState],AfterOpen,AfterScroll',
    'a change on an open table closes and reopens it');
  Rec.Free;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people" ORDER BY "people"."age", "people"."name"', 'two fields');
  CheckEq(Column(T, 'name'), 'Bob,Eve,Cy,Ada', 'ordered by age (null first), then name');
  T.IndexFieldNames := ' name ; ;id ';
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people" ORDER BY "people"."name", "people"."id"',
    'names are trimmed, empty parts skipped');
  T.IndexFieldNames := ';';
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "people"', 'only separators: no order');
  T.IndexFieldNames := 'name';
  T.Append;
  T.FieldByName('name').AsString := 'Abe';
  T.Post;
  CheckEq(Column(T, 'name'), 'Ada,Bob,Cy,Eve,Abe', 'a new row stays where it was inserted');
  T.Refresh;
  CheckEq(Column(T, 'name'), 'Abe,Ada,Bob,Cy,Eve', 'until Refresh sorts it in');
  CheckEq(Verdict(T), 'updatable', 'an ordered table is updatable');
  Check(T.Locate('name', ['Abe'], []), 'the new row is found');
  T.Delete;
  CheckEq(One('SELECT count(*) FROM people WHERE name = ''Abe'''), '0', 'and deleted');
  CheckRaises(Calls.SetIndexNope, 'ESQLiteError', 'SQLite error 1: no such column: people.nope', 'an unknown field');
  Check(not T.Active, 'the table is closed after the failed reopen');
  CheckEq(T.IndexFieldNames, 'nope', 'and has the new value');
  CheckRaises(Calls.Open, 'ESQLiteError', 'no such column', 'Open fails as well');
  T.IndexFieldNames := '';
  Check(not T.Active, 'a change on a closed table does not open it');
  T.Open;
  CheckEq(Column(T, 'name'), 'Cy,Ada,Bob,Eve', 'row id order again');
  T.Free;
end;

procedure TestTableName;
var
  T: TSQLiteTable;
  Rec: TEventRecorder;
begin
  Section('TableName');
  T := NewTable('people');
  T.IndexFieldNames := 'v';
  T.TableName := 'pair';
  T.Open;
  CheckEq(Column(T, 'v'), 'one-x,one-y,two-x', 'the first table');
  Rec := TEventRecorder.Create(T);
  T.TableName := 'pair';
  CheckEq(Rec.Take, '', 'assigning the same value does nothing');
  T.IndexFieldNames := '';
  Rec.Clear;
  T.TableName := 'people';
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose,BeforeOpen,[deUpdateState],AfterOpen,AfterScroll',
    'a change on an open table closes and reopens it');
  Rec.Free;
  CheckEq(FieldList(T), 'id:ftAutoInc(read-only) name:ftString age:ftInteger', 'the fields of the other table');
  CheckEq(T.UpdateTable, 'people', 'UpdateTable follows');
  CheckRaises(Calls.SetTableNope, 'ESQLiteError', 'no such table: nope', 'a missing table');
  Check(not T.Active, 'the table is closed after the failed reopen');
  T.TableName := 'people';
  Check(not T.Active, 'a change on a closed table does not open it');
  T.Prepare;
  Check(T.Prepared, 'Prepare prepares the generated statement');
  T.TableName := 'pair';
  Check(not T.Prepared, 'a new table name unprepares');
  T.Open;
  CheckEqInt(T.FieldCount, 3, 'and the next Open reads the new table');
  C.Close;
  Check(not T.Active, 'closing the connection closes the table');
  CheckRaises(Calls.Open, 'ESQLiteError', 'no such table: pair',
    'Open opens the connection: a new, empty memory database');
  Check(C.Connected, 'the connection is open');
  T.Free;
end;

procedure TestNoPrimaryKey;
var
  T: TSQLiteTable;
  RowId, Msg, N: TField;
begin
  Section('table without a primary key');
  T := NewTable('log');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT rowid AS "rowid", * FROM "log"', 'the statement selects the row id');
  CheckEq(FieldList(T), 'rowid:ftLargeint(hidden)(read-only) msg:ftMemo n:ftInteger', 'the fields');
  CheckEq(Verdict(T), 'updatable', 'the table is updatable');
  RowId := T.FieldByName('rowid');
  Msg := T.FieldByName('msg');
  N := T.FieldByName('n');
  Check(not RowId.CanModify and Msg.CanModify, 'the rowid field cannot be modified');
  T.Append;
  CheckRaises(Calls.WriteRowId, 'EDatabaseError', 'Field ''rowid'' cannot be modified', 'a write to the rowid field');
  Msg.AsString := 'same';
  N.AsInteger := 1;
  T.Post;
  Check(RowId.AsLargeInt = 1, 'the row id of the inserted row is read back');
  T.Append;
  Msg.AsString := 'same';
  N.AsInteger := 1;
  T.Post;
  Check(RowId.AsLargeInt = 2, 'the second row id');
  T.Append;
  Msg.AsString := 'third';
  T.Post;
  T.RecNo := 2;
  T.Edit;
  N.AsInteger := 20;
  T.Post;
  CheckEq(One('SELECT group_concat(n) FROM (SELECT ifnull(n, ''NULL'') AS n FROM log ORDER BY rowid)'), '1,20,NULL',
    'of two equal rows only the edited one changes');
  T.First;
  T.Delete;
  CheckEq(One('SELECT group_concat(rowid) FROM log'), '2,3', 'of two equal rows only the deleted one goes');
  T.Refresh;
  CheckEq(Column(T, 'rowid'), '2,3', 'the row ids after Refresh');
  CheckEq(FieldList(T), 'rowid:ftLargeint(hidden)(read-only) msg:ftMemo n:ftInteger', 'the fields after Refresh');
  T.IndexFieldNames := 'n';
  CheckEq(Trim(T.SQL.Text), 'SELECT rowid AS "rowid", * FROM "log" ORDER BY "log"."n"', 'with IndexFieldNames');
  CheckEq(Column(T, 'rowid'), '3,2', 'ordered by n');
  CheckEq(FieldList(T), 'rowid:ftLargeint(hidden)(read-only) msg:ftMemo n:ftInteger', 'the fields after the reopen');
  T.Last;
  T.Edit;
  T.FieldByName('msg').AsString := 'edited';
  T.Post;
  CheckEq(One('SELECT msg FROM log WHERE rowid = 2'), 'edited', 'an edit in an ordered table');

  Section('the table changes between two opens');
  T.Close;
  C.ExecSQL('DROP TABLE log; CREATE TABLE log (id INTEGER PRIMARY KEY, msg TEXT); INSERT INTO log (msg) VALUES (''new'')');
  T.IndexFieldNames := '';
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "log"', 'the statement follows the new definition');
  CheckEq(FieldList(T), 'id:ftAutoInc(read-only) msg:ftMemo', 'the fields');
  T.Close;
  C.ExecSQL('DROP TABLE log; CREATE TABLE log (msg TEXT, n INTEGER)');
  T.Open;
  CheckEq(FieldList(T), 'rowid:ftLargeint(hidden)(read-only) msg:ftMemo n:ftInteger', 'and back');
  T.Free;
end;

procedure TestOtherTables;
var
  T: TSQLiteTable;
begin
  Section('a user column named rowid');
  T := NewTable('shadow');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "shadow"', 'the statement');
  CheckEq(FieldList(T), 'rowid:ftMemo v:ftInteger', 'the fields are the columns of the table');
  CheckEq(Verdict(T), 'read-only: No usable key', 'the table is read-only');
  CheckEq(T.FieldByName('rowid').AsString, 'x', 'the column is read');
  CheckRaises(Calls.Edit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit');
  T.Free;

  Section('table without row id');
  T := NewTable('pair');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "pair"', 'the statement');
  CheckEq(FieldList(T), 'a:ftInteger b:ftMemo v:ftMemo', 'the fields');
  CheckEq(Verdict(T), 'updatable', 'the table is updatable by its primary key');
  T.RecNo := 2;
  T.Edit;
  T.FieldByName('v').AsString := 'changed';
  T.Post;
  CheckEq(One('SELECT group_concat(v) FROM (SELECT v FROM pair ORDER BY a, b)'), 'one-x,changed,two-x', 'an edit');
  T.Append;
  T.FieldByName('a').AsInteger := 3;
  T.FieldByName('b').AsString := 'z';
  T.Post;
  CheckEq(One('SELECT count(*) FROM pair WHERE a = 3 AND b = ''z'' AND v IS NULL'), '1', 'an insert');
  T.Delete;
  CheckEq(One('SELECT count(*) FROM pair'), '3', 'a delete');
  T.Free;

  Section('view');
  T := NewTable('adults');
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "adults"', 'the statement');
  CheckEq(FieldList(T), 'id:ftInteger name:ftString', 'the fields');
  CheckEq(Verdict(T), 'read-only: Update table is a view', 'a view is read-only');
  CheckEq(Column(T, 'name'), 'Cy,Ada', 'the rows');
  T.Free;

  Section('identifiers that need quoting');
  T := NewTable('my "t"');
  T.IndexFieldNames := 'order';
  T.Open;
  CheckEq(Trim(T.SQL.Text), 'SELECT * FROM "my ""t""" ORDER BY "my ""t"""."order"', 'the statement');
  CheckEq(FieldList(T), 'a b:ftAutoInc(read-only) order:ftMemo', 'the fields');
  CheckEq(Column(T, 'order'), 'a,z', 'the rows');
  T.Append;
  T.FieldByName('order').AsString := 'm';
  T.Post;
  CheckEqInt(T.FieldByName('a b').AsInteger, 3, 'an insert');
  T.Edit;
  T.FieldByName('order').AsString := 'n';
  T.Post;
  CheckEq(One('SELECT "order" FROM "my ""t""" WHERE "a b" = 3'), 'n', 'an edit');
  T.Delete;
  CheckEq(One('SELECT count(*) FROM "my ""t"""'), '2', 'a delete');
  T.Free;
end;

begin
  Calls := TCalls.Create;
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  C.Open;
  CreateSchema;
  TestRefused;
  TestPrimaryKey;
  TestIndexFieldNames;
  TestTableName;
  C.Close;
  C.Open;
  CreateSchema;
  TestNoPrimaryKey;
  TestOtherTables;
  C.Free;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
