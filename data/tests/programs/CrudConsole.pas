// covers: DAT-405
program CrudConsole;
{ The data operations of the CRUD sample without its user interface: the same steps on a TSQLiteTable over
  a memory database, with field assignments in place of typing and Post / Delete calls in place of the
  navigator. Every step prints '<step> <RecordCount> <State>'; the last lines are the rows of the table. }
uses
  SysUtils, Classes, DB, SQLiteData;

const
  Schema =
    'CREATE TABLE IF NOT EXISTS contacts (' +
    '  id     INTEGER PRIMARY KEY AUTOINCREMENT,' +
    '  name   VARCHAR(50) NOT NULL,' +
    '  email  VARCHAR(100),' +
    '  age    INTEGER CONSTRAINT age_range CHECK (age IS NULL OR (age >= 0 AND age <= 150)),' +
    '  active BOOLEAN NOT NULL DEFAULT 1,' +
    '  notes  TEXT' +
    ');';
  StateNames: array[TDataSetState] of string = ('dsInactive', 'dsBrowse', 'dsEdit', 'dsInsert');

type
  { the application logic of the sample's main form }
  THandlers = class
  public
    procedure NewRecord(DataSet: TDataSet);
    procedure BeforePost(DataSet: TDataSet);
  end;

procedure THandlers.NewRecord(DataSet: TDataSet);
begin
  DataSet.FieldByName('active').AsBoolean := True;
end;

procedure THandlers.BeforePost(DataSet: TDataSet);
begin
  if Trim(DataSet.FieldByName('name').AsString) = '' then
    DatabaseError('Name is required');
end;

var
  C: TSQLiteConnection;
  T: TSQLiteTable;
  Handlers: THandlers;

procedure Step(const Id: string);
begin
  WriteLn(Id, ' ', T.RecordCount, ' ', StateNames[T.State]);
end;

{ Posts; a failure prints '<step> error: <text>' and leaves the record in its edit state. }
procedure PostExpectingError(const Id: string);
begin
  try
    T.Post;
    WriteLn(Id, ' error: none');
  except
    on E: Exception do
      if Pos('CHECK constraint failed', E.Message) > 0 then
        WriteLn(Id, ' error: CHECK constraint failed')
      else
        WriteLn(Id, ' error: ', E.Message);
  end;
end;

procedure Dump;
var
  Q: TSQLiteQuery;
  I: Integer;
  F: TField;
  Line: string;
begin
  Q := TSQLiteQuery.Create(nil);
  Q.Connection := C;
  Q.SQL.Text := 'SELECT id, name, email, age, active, notes FROM contacts ORDER BY id';
  Q.Open;
  while not Q.Eof do
  begin
    Line := '';
    for I := 0 to Q.FieldCount - 1 do
    begin
      F := Q.Fields[I];
      if I > 0 then
        Line := Line + '|';
      if F.IsNull then
        Line := Line + 'NULL'
      else if F.DataType = ftBoolean then
        Line := Line + IntToStr(F.AsInteger)
      else
        Line := Line + F.AsString;
    end;
    WriteLn(Line);
    Q.Next;
  end;
  Q.Free;
end;

begin
  Handlers := THandlers.Create;
  C := TSQLiteConnection.Create(nil);
  C.Database := ':memory:';
  C.Open;
  C.ExecSQL(Schema);
  T := TSQLiteTable.Create(nil);
  T.Connection := C;
  T.TableName := 'contacts';
  T.IndexFieldNames := 'id';
  T.OnNewRecord := Handlers.NewRecord;
  T.BeforePost := Handlers.BeforePost;

  { C01: open }
  T.Open;
  Step('C01');

  { C02 - C04: three records }
  T.Insert;
  T.FieldByName('name').AsString := 'Ada Lovelace';
  T.FieldByName('email').AsString := 'ada@example.org';
  T.FieldByName('age').AsInteger := 36;
  T.FieldByName('notes').AsString := 'First';
  T.Post;
  Step('C02');
  T.Insert;
  T.FieldByName('name').AsString := 'Bob Stone';
  T.FieldByName('age').AsInteger := 41;
  T.FieldByName('active').AsBoolean := False;
  T.Post;
  Step('C03');
  T.Insert;
  T.FieldByName('name').AsString := 'Cy Young';
  T.FieldByName('email').AsString := 'cy@example.org';
  T.FieldByName('age').AsInteger := 29;
  T.Post;
  Step('C04');

  { C05: refresh orders by id; an edit is posted by moving on }
  T.Refresh;
  T.First;
  T.Edit;
  T.FieldByName('age').AsInteger := 37;
  T.Next;
  Step('C05');

  { C06: the rule of the application }
  T.Edit;
  T.FieldByName('name').AsString := ' ';
  PostExpectingError('C06');
  T.Cancel;
  Step('C06');

  { C07: the constraint of the database }
  T.Edit;
  T.FieldByName('age').AsInteger := 200;
  PostExpectingError('C07');
  T.Cancel;
  Step('C07');

  { C08: delete the last record }
  T.Last;
  T.Delete;
  Step('C08');

  { C09: a cancelled insert }
  T.Insert;
  T.FieldByName('name').AsString := 'Dan';
  T.Cancel;
  Step('C09');

  { C10: the key of a deleted record is not used again }
  T.Insert;
  T.FieldByName('name').AsString := 'Eve Moss';
  T.Post;
  WriteLn('C10 ', T.RecordCount, ' ', StateNames[T.State], ' id=', T.FieldByName('id').AsInteger);

  { C11: a rolled-back insert is gone after Refresh }
  C.StartTransaction;
  T.Insert;
  T.FieldByName('name').AsString := 'Tmp User';
  T.Post;
  C.Rollback;
  T.Refresh;
  Step('C11');

  { C12: locate and filter }
  if not T.Locate('name', ['eve moss'], [loCaseInsensitive]) then
    WriteLn('C12 error: record not found');
  T.Filter := 'age >= 40';
  T.Filtered := True;
  Step('C12');
  T.Filtered := False;
  Step('C12');

  Dump;
  T.Free;
  C.Free;
  Handlers.Free;
end.
