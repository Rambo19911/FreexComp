// covers: DAT-127, DAT-150, DAT-151, DAT-152, DAT-153, DAT-154, DAT-155, DAT-156, DAT-157
program SqlEdit;
{ Editable result sets of TSQLiteQuery. Sections: detect (update table, key, the reasons of a read-only
  query, field updatability), insert (generated INSERT, re-read of key, defaults and trigger values),
  update (generated UPDATE, changed-row detection, keys with NULL), delete, errors (constraint errors
  through OnPostError and OnDeleteError, transactions), and last the detection without the column
  origin functions of the library (they are switched off for the rest of the program). }
uses
  SysUtils, Classes, DB, SQLite3API, SQLiteData, DataTestUtils;

type
  TCalls = class
  public
    Q: TSQLiteQuery;
    FieldName: string;
    procedure Edit;
    procedure Insert;
    procedure Delete;
    procedure Post;
    procedure WriteField;
    procedure SetUpdateTable;
    procedure SetKeyFields;
  end;

  { the error handlers: record the error, optionally repair a field, answer with Action }
  TErrors = class
  public
    Count: Integer;
    Info: string;
    Action: TDataAction;
    FixField: string;
    FixValue: string;
    StateSeen: TDataSetState;
    procedure Reset(AAction: TDataAction);
    procedure Handle(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
  end;

var
  Calls: TCalls;
  Errors: TErrors;
  C: TSQLiteConnection;

procedure TCalls.Edit;
begin
  Q.Edit;
end;

procedure TCalls.Insert;
begin
  Q.Insert;
end;

procedure TCalls.Delete;
begin
  Q.Delete;
end;

procedure TCalls.Post;
begin
  Q.Post;
end;

procedure TCalls.WriteField;
begin
  Q.FieldByName(FieldName).AsString := '1';
end;

procedure TCalls.SetUpdateTable;
begin
  Q.UpdateTable := 'other';
end;

procedure TCalls.SetKeyFields;
begin
  Q.KeyFields := 'other';
end;

procedure TErrors.Reset(AAction: TDataAction);
begin
  Count := 0;
  Info := '';
  Action := AAction;
  FixField := '';
  FixValue := '';
end;

procedure TErrors.Handle(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  Count := Count + 1;
  Info := E.ClassName;
  if E is ESQLiteError then
    Info := Info + ' ' + IntToStr(ESQLiteError(E).ExtendedCode) + '/' + IntToStr(ESQLiteError(E).ErrorCode);
  StateSeen := DataSet.State;
  if FixField <> '' then
    DataSet.FieldByName(FixField).AsString := FixValue;
  Action := Self.Action;
end;

function NewQuery(const SQL: string): TSQLiteQuery;
begin
  Result := TSQLiteQuery.Create(nil);
  Result.Connection := C;
  Result.SQL.Text := SQL;
end;

{ The first column of the first row of a query, as text ('NULL' for null, '<none>' without a row). }
function One(const SQL: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := NewQuery(SQL);
  Q.Open;
  if Q.IsEmpty then
    Result := '<none>'
  else if Q.Fields[0].IsNull then
    Result := 'NULL'
  else
    Result := Q.Fields[0].AsString;
  Q.Free;
end;

function StateName(S: TDataSetState): string;
begin
  case S of
    dsInactive: Result := 'dsInactive';
    dsBrowse: Result := 'dsBrowse';
    dsEdit: Result := 'dsEdit';
  else
    Result := 'dsInsert';
  end;
end;

{ 'updatable' or 'read-only: <reason>' of an open query. }
function Verdict(Q: TSQLiteQuery): string;
begin
  if Q.CanModify then
  begin
    Result := 'updatable';
    if Q.ReadOnlyReason <> '' then
      Result := Result + ' but: ' + Q.ReadOnlyReason;
  end
  else
    Result := 'read-only: ' + Q.ReadOnlyReason;
end;

function Why(const SQL, UpdateTable, KeyFields: string): string;
var
  Q: TSQLiteQuery;
begin
  Q := NewQuery(SQL);
  Q.UpdateTable := UpdateTable;
  Q.KeyFields := KeyFields;
  Q.Open;
  Result := Verdict(Q);
  Q.Free;
end;

{ Whether a field of the query in dsEdit takes a write: 'yes' or the error text. }
function Writable(Q: TSQLiteQuery; const FieldName: string): string;
begin
  Result := 'yes';
  try
    Q.FieldByName(FieldName).AsString := '1';
  except
    on E: EDatabaseError do
      Result := E.Message;
  end;
end;

procedure CreateSchema;
begin
  C.ExecSQL(
    'CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT NOT NULL, status TEXT NOT NULL DEFAULT ''new'',' +
    '  age INTEGER CONSTRAINT age_range CHECK (age IS NULL OR age < 150), email TEXT UNIQUE, stamp TEXT);' +
    'CREATE TRIGGER t_ai AFTER INSERT ON t BEGIN' +
    '  UPDATE t SET stamp = ''ins-'' || NEW.id WHERE id = NEW.id; END;' +
    'CREATE TABLE nopk (code TEXT, v INTEGER);' +
    'CREATE TABLE comp (a INTEGER, b INTEGER, v TEXT, PRIMARY KEY (a, b));' +
    'CREATE TABLE codes (code VARCHAR(5) PRIMARY KEY, label TEXT);' +
    'CREATE TABLE wr (code VARCHAR(5) PRIMARY KEY, label TEXT DEFAULT ''dflt'') WITHOUT ROWID;' +
    'CREATE TABLE gen (id INTEGER PRIMARY KEY, a INTEGER, g INTEGER GENERATED ALWAYS AS (a * 2) VIRTUAL);' +
    'CREATE TABLE parent (id INTEGER PRIMARY KEY, name TEXT);' +
    'CREATE TABLE child (id INTEGER PRIMARY KEY, parent_id INTEGER REFERENCES parent (id), v TEXT);' +
    'CREATE TABLE alld (id INTEGER PRIMARY KEY, a TEXT DEFAULT ''x'', b INTEGER DEFAULT 5);' +
    'CREATE VIEW tv AS SELECT id, name FROM t');
end;

procedure TestDetect;
var
  Q: TSQLiteQuery;
  C2: TSQLiteConnection;
  FileName: string;
begin
  Section('detect: update table and key');
  CheckEq(Why('SELECT * FROM t', '', ''), 'updatable', 'all columns of a table with a primary key');
  CheckEq(Why('SELECT name, id FROM t WHERE id > 0 ORDER BY name', '', ''), 'updatable', 'some columns, with the key');
  CheckEq(Why('SELECT id AS k, name AS n FROM t', '', ''), 'updatable', 'columns under other names');
  CheckEq(Why('SELECT id, name, upper(name) AS u, 1 AS one FROM t', '', ''), 'updatable', 'with expression columns');
  CheckEq(Why('SELECT name, age FROM t', '', ''), 'read-only: No key columns in the result set', 'without the key column');
  CheckEq(Why('SELECT 1 AS x', '', ''), 'read-only: Query has no table columns', 'only expressions');
  CheckEq(Why('SELECT count(*) AS n FROM t', '', ''), 'read-only: Query has no table columns', 'an aggregate');
  CheckEq(Why('SELECT t.id, t.name, c.label FROM t, codes c', '', ''),
    'read-only: Query selects from more than one table', 'a join');
  CheckEq(Why('SELECT a, v FROM comp', '', ''), 'read-only: No key columns in the result set',
    'one column of a key of two');
  CheckEq(Why('SELECT v, b, a FROM comp', '', ''), 'updatable', 'both columns of the key');
  CheckEq(Why('SELECT code, v FROM nopk', '', ''), 'read-only: No key columns in the result set',
    'a table without a primary key');
  CheckEq(Why('SELECT rowid, code, v FROM nopk', '', ''), 'updatable', 'with an explicit rowid column');
  CheckEq(Why('SELECT oid AS k, v FROM nopk', '', ''), 'updatable', 'oid under another name');
  CheckEq(Why('SELECT v, _rowid_ FROM nopk', '', ''), 'updatable', '_rowid_');
  CheckEq(Why('SELECT label FROM codes', '', ''), 'read-only: No key columns in the result set',
    'a text key that is not selected');
  CheckEq(Why('SELECT rowid, label FROM codes', '', ''), 'updatable', 'the rowid instead of the missing key');
  CheckEq(Why('SELECT * FROM wr', '', ''), 'updatable', 'a WITHOUT ROWID table');
  CheckEq(Why('SELECT * FROM tv', '', ''), 'updatable', 'a view over one table resolves to that table');

  Section('detect: UpdateTable and KeyFields');
  CheckEq(Why('SELECT t.id, t.name, c.label FROM t, codes c', 't', ''), 'updatable', 'a join with UpdateTable');
  CheckEq(Why('SELECT t.id, t.name, c.label FROM t, codes c', 'T', ''), 'updatable', 'in any case');
  CheckEq(Why('SELECT t.id, t.name, c.label FROM t, codes c', 'codes', ''),
    'read-only: No key columns in the result set', 'the other table lacks its key');
  CheckEq(Why('SELECT * FROM tv', 'tv', ''), 'read-only: Update table is a view', 'a view as UpdateTable');
  CheckEq(Why('SELECT * FROM t', 'nope', ''), 'read-only: Update table does not exist', 'an unknown UpdateTable');
  CheckEq(Why('SELECT code, v FROM nopk', '', 'code'), 'updatable', 'KeyFields names the key');
  CheckEq(Why('SELECT code, v FROM nopk', '', 'Code;V'), 'updatable', 'two key fields');
  CheckEq(Why('SELECT code, v FROM nopk', '', 'zz'), 'read-only: Key field ''zz'' not found', 'a key field that does not exist');
  CheckEq(Why('SELECT code, v + 1 AS w FROM nopk', '', 'w'),
    'read-only: Key field ''w'' is not a column of the update table', 'a key field that is an expression');
  CheckEq(Why('SELECT name, age FROM t', '', 'name'), 'updatable', 'KeyFields instead of the primary key');

  Section('detect: read-only switches');
  Q := NewQuery('SELECT * FROM t');
  Calls.Q := Q;
  CheckEqBool(Q.CanModify, False, 'a closed query cannot be modified');
  Q.ReadOnly := True;
  Q.Open;
  CheckEq(Verdict(Q), 'read-only: Dataset is read-only', 'ReadOnly');
  CheckRaises(Calls.Edit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit on a read-only query');
  CheckRaises(Calls.Insert, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Insert on a read-only query');
  Q.ReadOnly := False;
  CheckEq(Verdict(Q), 'updatable', 'ReadOnly may change while open');
  CheckRaises(Calls.SetUpdateTable, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'UpdateTable cannot change while open');
  CheckRaises(Calls.SetKeyFields, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'KeyFields cannot change while open');
  Q.Free;
  Q := NewQuery('SELECT name FROM t');
  Calls.Q := Q;
  Q.Open;
  C.ExecSQL('INSERT INTO t (name) VALUES (''probe'')');
  Q.Refresh;
  CheckRaises(Calls.Edit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit without a key');
  CheckRaises(Calls.Delete, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Delete without a key');
  Q.Free;
  C.ExecSQL('DELETE FROM t');

  FileName := GetCurrentDir + '\readonly.db';
  C2 := TSQLiteConnection.Create(nil);
  C2.Database := FileName;
  C2.Open;
  C2.ExecSQL('CREATE TABLE t (id INTEGER PRIMARY KEY, name TEXT)');
  C2.Close;
  C2.OpenMode := omReadOnly;
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C2;
  Q.SQL.Text := 'SELECT * FROM t';
  Q.Open;
  CheckEq(Verdict(Q), 'read-only: Connection is read-only', 'a read-only connection');
  Q.Free;
  C2.Free;
  Check(DeleteFile(FileName), 'the database file is deleted');

  Section('detect: field updatability');
  C.ExecSQL('INSERT INTO t (name) VALUES (''one''); INSERT INTO gen (a) VALUES (1); INSERT INTO nopk VALUES (''c'', 1);' +
    'INSERT INTO codes VALUES (''K'', ''k'')');
  Q := NewQuery('SELECT id, name, upper(name) AS u FROM t');
  Q.Open;
  CheckEqBool(Q.FieldByName('id').DataType = ftAutoInc, True, 'the key is an auto-increment field');
  CheckEqBool(Q.FieldByName('id').ReadOnly, True, 'read-only for the user');
  Q.Edit;
  CheckEq(Writable(Q, 'name'), 'yes', 'a table column takes a write');
  CheckEq(Writable(Q, 'u'), 'Field ''u'' cannot be modified', 'an expression column does not');
  CheckEq(Writable(Q, 'id'), 'Field ''id'' cannot be modified', 'nor the auto-increment key');
  Q.Cancel;
  Q.Free;
  Q := NewQuery('SELECT * FROM gen');
  Q.Open;
  Q.Edit;
  CheckEq(Writable(Q, 'a'), 'yes', 'a stored column next to a generated one');
  CheckEq(Writable(Q, 'g'), 'Field ''g'' cannot be modified', 'a generated column is not updatable');
  Q.Cancel;
  Q.Free;
  Q := NewQuery('SELECT t.id, t.name, c.label FROM t, codes c');
  Q.UpdateTable := 't';
  Q.Open;
  Q.Edit;
  CheckEq(Writable(Q, 'name'), 'yes', 'a column of the update table of a join');
  CheckEq(Writable(Q, 'label'), 'Field ''label'' cannot be modified', 'a column of the other table');
  Q.Cancel;
  Q.Free;
  Q := NewQuery('SELECT rowid, code FROM nopk');
  Q.Open;
  CheckEqBool(Q.FieldByName('rowid').DataType = ftLargeint, True, 'an explicit rowid is a largeint field');
  Q.Edit;
  CheckEq(Writable(Q, 'rowid'), 'Field ''rowid'' cannot be modified', 'and not updatable');
  CheckEq(Writable(Q, 'code'), 'yes', 'next to an updatable column');
  Q.Cancel;
  Q.Free;
  C.ExecSQL('DELETE FROM t; DELETE FROM gen; DELETE FROM nopk; DELETE FROM codes');
end;

procedure TestInsert;
var
  Q: TSQLiteQuery;
begin
  Section('insert');
  Q := NewQuery('SELECT * FROM t ORDER BY id');
  Q.Open;
  Q.Append;
  Q.FieldByName('name').AsString := 'Ada';
  Q.Post;
  CheckEq(StateName(Q.State), 'dsBrowse', 'Post of an insert succeeds');
  CheckEqInt(Q.RecordCount, 1, 'one row in the dataset');
  CheckEqInt(Q.FieldByName('id').AsInteger, 1, 'the auto-increment key is read back');
  CheckEq(Q.FieldByName('status').AsString, 'new', 'a column default is read back');
  CheckEq(Q.FieldByName('stamp').AsString, 'ins-1', 'a value set by a trigger is read back');
  Check(Q.FieldByName('age').IsNull, 'an unassigned column without default is null');
  CheckEq(One('SELECT id || ''|'' || name || ''|'' || status || ''|'' || stamp FROM t'), '1|Ada|new|ins-1',
    'the row is in the table');
  CheckEqBool(C.InTransaction, False, 'and committed: datasets do not start transactions');
  Q.Append;
  Q.FieldByName('name').AsString := 'Bob';
  Q.FieldByName('status').AsString := 'old';
  Q.FieldByName('age').AsInteger := 41;
  Q.Post;
  CheckEqInt(Q.FieldByName('id').AsInteger, 2, 'the next key');
  CheckEq(Q.FieldByName('status').AsString, 'old', 'an assigned value wins over the default');
  CheckEq(One('SELECT status || ''|'' || age FROM t WHERE id = 2'), 'old|41', 'and is stored');
  Q.Insert;
  Q.FieldByName('name').AsString := 'Cy';
  Q.Post;
  CheckEqInt(Q.FieldByName('id').AsInteger, 3, 'an Insert before the current row gets its key too');
  CheckEqInt(Q.RecordCount, 3, 'three rows');
  Q.Free;

  Q := NewQuery('SELECT * FROM alld ORDER BY id');
  Q.Open;
  Q.Append;
  Q.Post;
  CheckEqInt(Q.RecordCount, 1, 'a Post without any assigned field inserts the default values');
  CheckEq(Q.FieldByName('id').AsString + '|' + Q.FieldByName('a').AsString + '|' + Q.FieldByName('b').AsString,
    '1|x|5', 'and they are read back');
  Q.Append;
  Q.FieldByName('a').Clear;
  Q.Post;
  Check(Q.FieldByName('a').IsNull, 'a field assigned null stays null');
  CheckEq(One('SELECT quote(a) || ''|'' || b FROM alld WHERE id = 2'), 'NULL|5',
    'the assigned null is stored instead of the default, the unassigned column takes its default');
  Q.Free;

  Q := NewQuery('SELECT id, name, upper(name) AS u FROM t ORDER BY id');
  Q.Open;
  Q.Append;
  Q.FieldByName('name').AsString := 'Dee';
  Q.Post;
  CheckEqInt(Q.FieldByName('id').AsInteger, 4, 'an insert through a query with an expression column');
  Check(Q.FieldByName('u').IsNull, 'the expression field of the new row is null');
  Q.Refresh;
  Q.Last;
  CheckEq(Q.FieldByName('u').AsString, 'DEE', 'until Refresh');
  Q.Free;

  Q := NewQuery('SELECT * FROM wr');
  Q.Open;
  Q.Append;
  Q.FieldByName('code').AsString := 'A1';
  Q.Post;
  CheckEq(Q.FieldByName('label').AsString, 'dflt', 'WITHOUT ROWID: the row is read back by its key');
  CheckEq(One('SELECT code || ''|'' || label FROM wr'), 'A1|dflt', 'and stored');
  Q.Free;

  Q := NewQuery('SELECT rowid, code, v FROM nopk');
  Q.Open;
  Q.Append;
  Q.FieldByName('code').AsString := 'x';
  Q.FieldByName('v').AsInteger := 1;
  Q.Post;
  CheckEqInt(Q.FieldByName('rowid').AsLargeInt, 1, 'the rowid of the new row is read back');
  Q.Append;
  Q.FieldByName('code').AsString := 'y';
  Q.Post;
  CheckEqInt(Q.FieldByName('rowid').AsLargeInt, 2, 'for every row');
  Q.Edit;
  Q.FieldByName('v').AsInteger := 22;
  Q.Post;
  CheckEq(One('SELECT group_concat(code || ''='' || coalesce(v, ''-''), '','') FROM nopk'), 'x=1,y=22',
    'so the new row can be edited at once');
  Q.Free;

  Q := NewQuery('SELECT * FROM gen');
  Q.Open;
  Q.Append;
  Q.FieldByName('a').AsInteger := 21;
  Q.Post;
  CheckEqInt(Q.FieldByName('g').AsInteger, 42, 'a generated column is read back');
  Q.Free;

  Q := NewQuery('SELECT v, b, a FROM comp');
  Q.Open;
  Q.Append;
  Q.FieldByName('a').AsInteger := 1;
  Q.FieldByName('b').AsInteger := 2;
  Q.FieldByName('v').AsString := 'one-two';
  Q.Post;
  CheckEq(One('SELECT a || ''|'' || b || ''|'' || v FROM comp'), '1|2|one-two', 'an insert with a key of two columns');
  Q.Free;
end;

procedure TestUpdate;
var
  Q: TSQLiteQuery;
  Before: Integer;
begin
  Section('update');
  Q := NewQuery('SELECT * FROM t ORDER BY id');
  Calls.Q := Q;
  Q.Open;
  Q.First;
  Before := sqlite3_total_changes(C.Handle);
  Q.Edit;
  Q.Post;
  CheckEqInt(sqlite3_total_changes(C.Handle) - Before, 0, 'a Post without an assigned field runs no statement');
  Q.Edit;
  Q.FieldByName('age').AsInteger := 36;
  Q.Post;
  CheckEqInt(sqlite3_total_changes(C.Handle) - Before, 1, 'a Post with an assigned field updates one row');
  CheckEq(One('SELECT name || ''|'' || age FROM t WHERE id = 1'), 'Ada|36', 'the value is stored');
  C.ExecSQL('UPDATE t SET email = ''ada@example.org'' WHERE id = 1');
  Q.Edit;
  Q.FieldByName('age').AsInteger := 37;
  Q.Post;
  CheckEq(One('SELECT age || ''|'' || email FROM t WHERE id = 1'), '37|ada@example.org',
    'only the assigned columns are written: a change made elsewhere to another column stays');
  Check(Q.FieldByName('email').IsNull, 'the dataset is not re-read after an update');
  Q.Edit;
  Q.FieldByName('age').Clear;
  Q.Post;
  CheckEq(One('SELECT quote(age) FROM t WHERE id = 1'), 'NULL', 'a cleared field is written as NULL');

  Q.Next;
  CheckEqInt(Q.FieldByName('id').AsInteger, 2, 'on the second row');
  C.ExecSQL('DELETE FROM t WHERE id = 2');
  Q.Edit;
  Q.FieldByName('name').AsString := 'Bobby';
  CheckRaises(Calls.Post, 'EDatabaseError', 'Record was changed or deleted by another user',
    'Post of a row that was deleted elsewhere');
  CheckEq(StateName(Q.State), 'dsEdit', 'the dataset stays in dsEdit');
  Q.Cancel;
  CheckEq(Q.FieldByName('name').AsString, 'Bob', 'Cancel restores the row');
  Q.Free;

  C.ExecSQL('DELETE FROM nopk; INSERT INTO nopk VALUES (NULL, 1), (''b'', 2)');
  Q := NewQuery('SELECT code, v FROM nopk ORDER BY v');
  Calls.Q := Q;
  Q.KeyFields := 'code';
  Q.Open;
  Check(Q.FieldByName('code').IsNull, 'on the row whose key is NULL');
  Q.Edit;
  Q.FieldByName('v').AsInteger := 10;
  Q.Post;
  CheckEq(One('SELECT group_concat(coalesce(code, ''null'') || ''='' || v, '','') FROM nopk'), 'null=10,b=2',
    'a NULL key matches its row');
  C.ExecSQL('INSERT INTO nopk VALUES (''b'', 3)');
  Q.Last;
  Q.Edit;
  Q.FieldByName('v').AsInteger := 20;
  CheckRaises(Calls.Post, 'EDatabaseError', 'Update affected 2 records; key is not unique', 'a key that is not unique');
  CheckEq(StateName(Q.State), 'dsEdit', 'the dataset stays in dsEdit');
  CheckEq(One('SELECT group_concat(coalesce(code, ''null'') || ''='' || v, '','') FROM nopk'), 'null=10,b=20,b=20',
    'the statement has run already: both rows are changed (use a transaction)');
  Q.Cancel;
  Q.Free;

  C.ExecSQL('INSERT INTO codes VALUES (''A'', ''a'')');
  Q := NewQuery('SELECT * FROM codes');
  Q.Open;
  Q.Edit;
  Q.FieldByName('code').AsString := 'B';
  Q.Post;
  CheckEq(One('SELECT group_concat(code || ''='' || label, '','') FROM codes'), 'B=a',
    'a key column is updated through its old value');
  Q.Edit;
  Q.FieldByName('label').AsString := 'b';
  Q.Post;
  CheckEq(One('SELECT group_concat(code || ''='' || label, '','') FROM codes'), 'B=b', 'the next update uses the new key');
  Q.Free;

  Q := NewQuery('SELECT v, b, a FROM comp');
  Q.Open;
  Q.Edit;
  Q.FieldByName('v').AsString := 'changed';
  Q.Post;
  CheckEq(One('SELECT v FROM comp WHERE a = 1 AND b = 2'), 'changed', 'an update with a key of two columns');
  Q.Free;

  Q := NewQuery('SELECT * FROM wr');
  Q.Open;
  Q.Edit;
  Q.FieldByName('label').AsString := 'set';
  Q.Post;
  CheckEq(One('SELECT label FROM wr WHERE code = ''A1'''), 'set', 'an update of a WITHOUT ROWID table');
  Q.Free;
end;

procedure TestDelete;
var
  Q: TSQLiteQuery;
begin
  Section('delete');
  { t has the rows 1 (Ada), 3 (Cy), 4 (Dee); 2 was deleted behind the back of the earlier query }
  Q := NewQuery('SELECT * FROM t ORDER BY id');
  Calls.Q := Q;
  Q.Open;
  CheckEqInt(Q.RecordCount, 3, 'three rows');
  Q.Last;
  Q.Delete;
  CheckEqInt(Q.RecordCount, 2, 'Delete removes the row from the dataset');
  CheckEq(One('SELECT group_concat(id, '','') FROM (SELECT id FROM t ORDER BY id)'), '1,3', 'and from the table');
  C.ExecSQL('DELETE FROM t WHERE id = 3');
  Q.Last;
  CheckRaises(Calls.Delete, 'EDatabaseError', 'Record was changed or deleted by another user',
    'Delete of a row that was deleted elsewhere');
  CheckEqInt(Q.RecordCount, 2, 'the row stays in the dataset');
  Q.Refresh;
  CheckEqInt(Q.RecordCount, 1, 'until Refresh');
  Q.Free;

  Q := NewQuery('SELECT code, v FROM nopk ORDER BY v DESC');
  Q.KeyFields := 'code';
  Q.Open;
  Q.Last;
  Check(Q.FieldByName('code').IsNull, 'on the row whose key is NULL');
  Q.Delete;
  CheckEq(One('SELECT group_concat(code || ''='' || v, '','') FROM nopk'), 'b=20,b=20', 'a NULL key matches its row');
  Q.Free;

  Q := NewQuery('SELECT v, b, a FROM comp');
  Q.Open;
  Q.Delete;
  CheckEq(One('SELECT count(*) FROM comp'), '0', 'a delete with a key of two columns');
  Q.Free;

  Q := NewQuery('SELECT rowid, code FROM nopk');
  Q.Open;
  Q.Delete;
  Q.Delete;
  CheckEq(One('SELECT count(*) FROM nopk'), '0', 'deletes through the rowid');
  CheckEqBool(Q.IsEmpty, True, 'the dataset is empty');
  Q.Free;
end;

procedure TestErrors;
var
  Q, P: TSQLiteQuery;
begin
  Section('errors: without a handler');
  C.ExecSQL('DELETE FROM t; INSERT INTO t (name, email) VALUES (''Ada'', ''ada@example.org'')');
  Q := NewQuery('SELECT * FROM t ORDER BY id');
  Calls.Q := Q;
  Q.Open;
  Q.Append;
  CheckRaises(Calls.Post, 'EDatabaseError', 'Field ''name'' must have a value',
    'a required field is checked before any statement');
  Q.FieldByName('name').AsString := 'Zed';
  Q.FieldByName('age').AsInteger := 200;
  CheckRaises(Calls.Post, 'ESQLiteError', 'SQLite error 275: CHECK constraint failed: age_range', 'a CHECK violation');
  CheckEq(StateName(Q.State), 'dsInsert', 'the dataset stays in dsInsert');
  CheckEq(One('SELECT count(*) FROM t'), '1', 'nothing was inserted');
  Q.FieldByName('age').AsInteger := 20;
  Q.Post;
  CheckEq(StateName(Q.State), 'dsBrowse', 'after the repair the Post succeeds');
  CheckEq(One('SELECT count(*) FROM t'), '2', 'and the row is stored');
  Q.Append;
  Q.FieldByName('name').AsString := 'Nul';
  Q.FieldByName('status').Clear;
  CheckRaises(Calls.Post, 'ESQLiteError', 'SQLite error 1299: NOT NULL constraint failed: t.status',
    'a NOT NULL violation');
  Q.Cancel;
  Q.Edit;
  Q.FieldByName('email').AsString := 'ada@example.org';
  CheckRaises(Calls.Post, 'ESQLiteError', 'SQLite error 2067: UNIQUE constraint failed: t.email', 'a UNIQUE violation');
  CheckEq(StateName(Q.State), 'dsEdit', 'the dataset stays in dsEdit');
  Q.Cancel;

  Section('errors: OnPostError');
  Q.OnPostError := Errors.Handle;
  Errors.Reset(daFail);
  Q.Edit;
  Q.FieldByName('email').AsString := 'ada@example.org';
  CheckRaises(Calls.Post, 'ESQLiteError', 'UNIQUE constraint failed: t.email', 'daFail: the error is raised');
  CheckEqInt(Errors.Count, 1, 'after one call of the handler');
  CheckEq(Errors.Info, 'ESQLiteError 2067/19', 'which sees the ESQLiteError with its codes');
  CheckEq(StateName(Errors.StateSeen), 'dsEdit', 'in dsEdit');
  CheckEq(StateName(Q.State), 'dsEdit', 'and the dataset stays there');
  Errors.Reset(daAbort);
  CheckRaises(Calls.Post, 'EAbort', '', 'daAbort: a silent exception');
  CheckEqInt(Errors.Count, 1, 'after one call of the handler');
  CheckEq(StateName(Q.State), 'dsEdit', 'the dataset stays in dsEdit');
  Errors.Reset(daRetry);
  Errors.FixField := 'email';
  Errors.FixValue := 'zed@example.org';
  Q.Post;
  CheckEqInt(Errors.Count, 1, 'daRetry: the handler repairs the field, the step runs again');
  CheckEq(StateName(Q.State), 'dsBrowse', 'and the Post succeeds');
  CheckEq(One('SELECT email FROM t WHERE name = ''Zed'''), 'zed@example.org', 'with the repaired value');
  Errors.Reset(daFail);
  Q.Append;
  Q.FieldByName('name').AsString := 'Old';
  Q.FieldByName('age').AsInteger := 150;
  CheckRaises(Calls.Post, 'ESQLiteError', 'CHECK constraint failed: age_range', 'a CHECK violation in an insert');
  CheckEq(Errors.Info, 'ESQLiteError 275/19', 'reaches the handler');
  CheckEq(StateName(Errors.StateSeen), 'dsInsert', 'in dsInsert');
  Q.Cancel;
  Q.OnPostError := nil;
  Q.Free;

  P := NewQuery('SELECT * FROM codes');
  Calls.Q := P;
  P.OnPostError := Errors.Handle;
  P.Open;
  Errors.Reset(daFail);
  P.Append;
  P.FieldByName('code').AsString := 'B';
  CheckRaises(Calls.Post, 'ESQLiteError', 'SQLite error 1555: UNIQUE constraint failed: codes.code',
    'a primary key violation');
  CheckEq(Errors.Info, 'ESQLiteError 1555/19', 'with its extended code');
  P.Cancel;
  P.Free;

  C.ExecSQL('INSERT INTO parent VALUES (1, ''p''); INSERT INTO child VALUES (1, 1, ''c'')');
  P := NewQuery('SELECT * FROM child');
  Calls.Q := P;
  P.OnPostError := Errors.Handle;
  P.Open;
  Errors.Reset(daFail);
  P.Append;
  P.FieldByName('parent_id').AsInteger := 999;
  CheckRaises(Calls.Post, 'ESQLiteError', 'SQLite error 787: FOREIGN KEY constraint failed', 'a foreign key violation');
  CheckEq(Errors.Info, 'ESQLiteError 787/19', 'with its extended code');
  P.Cancel;
  P.Free;

  Section('errors: OnDeleteError');
  P := NewQuery('SELECT * FROM parent');
  Calls.Q := P;
  P.OnDeleteError := Errors.Handle;
  P.Open;
  Errors.Reset(daFail);
  CheckRaises(Calls.Delete, 'ESQLiteError', 'SQLite error 787: FOREIGN KEY constraint failed',
    'Delete of a referenced row');
  CheckEqInt(Errors.Count, 1, 'after one call of the handler');
  CheckEq(Errors.Info, 'ESQLiteError 787/19', 'which sees the error');
  CheckEqInt(P.RecordCount, 1, 'the row stays in the dataset');
  CheckEq(One('SELECT count(*) FROM parent'), '1', 'and in the table');
  Errors.Reset(daAbort);
  CheckRaises(Calls.Delete, 'EAbort', '', 'daAbort: a silent exception');
  C.ExecSQL('DELETE FROM child');
  Errors.Reset(daFail);
  P.Delete;
  CheckEqInt(Errors.Count, 0, 'without the reference the Delete succeeds');
  CheckEq(One('SELECT count(*) FROM parent'), '0', 'and the row is gone');
  P.Free;

  Section('errors: transactions');
  Q := NewQuery('SELECT * FROM t ORDER BY id');
  Q.Open;
  CheckEqInt(Q.RecordCount, 2, 'two rows');
  C.StartTransaction;
  Q.Append;
  Q.FieldByName('name').AsString := 'Tmp';
  Q.Post;
  Q.First;
  Q.Edit;
  Q.FieldByName('name').AsString := 'Changed';
  Q.Post;
  CheckEqBool(C.InTransaction, True, 'a Post inside a transaction does not commit');
  C.Rollback;
  CheckEqInt(Q.RecordCount, 3, 'Rollback does not refresh the open dataset');
  CheckEq(Q.FieldByName('name').AsString, 'Changed', 'which keeps its values');
  Q.Refresh;
  CheckEqInt(Q.RecordCount, 2, 'Refresh shows the rolled-back state');
  Q.First;
  CheckEq(Q.FieldByName('name').AsString, 'Ada', 'with the old values');
  Q.Free;
end;

procedure TestWithoutOrigins;
var
  Q: TSQLiteQuery;
begin
  Section('detect without the column origin functions');
  { the optional functions of the library are absent from here on }
  sqlite3_column_table_name := nil;
  sqlite3_column_origin_name := nil;
  C.ExecSQL('DELETE FROM t; DELETE FROM nopk; INSERT INTO nopk VALUES (''n'', 1)');
  CheckEq(Why('SELECT * FROM t', '', ''), 'read-only: Update table is not known; set UpdateTable',
    'without UpdateTable every query is read-only');
  CheckEq(Why('SELECT * FROM t', 't', ''), 'updatable', 'with UpdateTable the columns are matched by name');
  CheckEq(Why('SELECT name FROM t', 't', ''), 'read-only: No key columns in the result set', 'the key is still needed');
  CheckEq(Why('SELECT rowid, code FROM nopk', 'nopk', ''), 'updatable', 'a column named rowid is the key');
  CheckEq(Why('SELECT * FROM tv', 'tv', ''), 'read-only: Update table is a view', 'a view is refused');
  Q := NewQuery('SELECT id, name AS n, status, upper(name) AS stamp FROM t');
  Q.UpdateTable := 't';
  Q.Open;
  CheckEqBool(Q.FieldByName('id').DataType = ftAutoInc, True, 'the key is an auto-increment field');
  Q.Append;
  CheckEq(Writable(Q, 'n'), 'Field ''n'' cannot be modified', 'a column under another name is not matched');
  Q.FieldByName('status').AsString := 'by-name';
  Q.Cancel;
  Q.Free;
  Q := NewQuery('SELECT id, name, status FROM t');
  Q.UpdateTable := 't';
  Q.Open;
  Q.Append;
  Q.FieldByName('name').AsString := 'Eve';
  Q.Post;
  CheckEqInt(Q.FieldByName('id').AsInteger, 1, 'an insert reads the key back');
  CheckEq(Q.FieldByName('status').AsString, 'new', 'and the default');
  Q.Edit;
  Q.FieldByName('status').AsString := 'edited';
  Q.Post;
  CheckEq(One('SELECT name || ''|'' || status FROM t'), 'Eve|edited', 'an update finds the row');
  Q.Delete;
  CheckEq(One('SELECT count(*) FROM t'), '0', 'and so does a delete');
  Q.Free;
end;

begin
  Calls := TCalls.Create;
  Errors := TErrors.Create;
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  C.Open;
  CreateSchema;
  TestDetect;
  TestInsert;
  TestUpdate;
  TestDelete;
  TestErrors;
  TestWithoutOrigins;
  C.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
