// covers: DAT-336, DAT-340, DAT-341, DAT-342, DAT-343
program FormStream;
{ A form whose text is embedded in the program: every data component is created from it, the references between
  them are resolved in any order (a control comes before its data source, a data source before its dataset, a
  dataset before and after its connection), the Columns collection of the grid and the FieldDefs collection of
  the in-memory table are read, and Active / Connected stored as True are applied when the form is loaded - not
  while the lines are read. A failure to open propagates out of the creation of the form. The field definitions
  as a collection: items, their properties, the checks. }
uses
  SysUtils, Classes, TypInfo, Controls, Forms, StdCtrls, DB, SQLiteData, MemData, DBCtrls, DBGrids, DataTestUtils,
  TestDataSet, CtlTestUtils;

type
  TStreamForm = class(TForm)
  published
    Grid: TDBGrid;
    NameEdit: TDBEdit;
    NameText: TDBText;
    Notes: TDBMemo;
    ActiveBox: TDBCheckBox;
    Kind: TDBComboBox;
    City: TDBLookupComboBox;
    Nav: TDBNavigator;
    MemEdit: TDBEdit;
    ContactsSource: TDataSource;
    CitySource: TDataSource;
    MemSource: TDataSource;
    Contacts: TSQLiteTable;
    Conn: TSQLiteConnection;
    Cities: TSQLiteQuery;
    Adults: TSQLiteQuery;
    Mem: TMemTable;
    Idle: TMemTable;
    procedure ConnAfterConnect(Sender: TObject);
    procedure DataSetAfterOpen(DataSet: TDataSet);
  end;

  TBadConnForm = class(TForm)
  published
    Conn: TSQLiteConnection;
  end;

  TBadTableForm = class(TForm)
  published
    Source: TDataSource;
    Table: TMemTable;
  end;

  TCalls = class
  public
    Defs: TFieldDefs;
    procedure CreateBadConn;
    procedure CreateBadTable;
    procedure AddDuplicate;
    procedure RenameDuplicate;
    procedure AddWhileActive;
    procedure RenameWhileActive;
    procedure ResizeWhileActive;
    procedure ClearWhileActive;
    procedure DeleteWhileActive;
  end;

{$FORM FormStream.form}
{$FORM FormStreamBadConn.form}
{$FORM FormStreamBadTable.form}

const
  DbName = 'formstream.db';

var
  Log: string;
  Calls: TCalls;

procedure TStreamForm.ConnAfterConnect(Sender: TObject);
begin
  Log := Log + 'connect;';
end;

procedure TStreamForm.DataSetAfterOpen(DataSet: TDataSet);
begin
  Log := Log + 'open ' + DataSet.Name + ';';
end;

procedure TCalls.CreateBadConn;
begin
  TBadConnForm.Create(nil);
end;

procedure TCalls.CreateBadTable;
begin
  TBadTableForm.Create(nil);
end;

procedure TCalls.AddDuplicate;
begin
  Defs.Add('CODE', ftInteger);
end;

procedure TCalls.RenameDuplicate;
begin
  Defs[1].Name := 'Code';
end;

procedure TCalls.AddWhileActive;
begin
  Defs.Add('more', ftInteger);
end;

procedure TCalls.RenameWhileActive;
begin
  Defs[0].Name := 'other';
end;

procedure TCalls.ResizeWhileActive;
begin
  Defs[0].Size := 9;
end;

procedure TCalls.ClearWhileActive;
begin
  Defs.Clear;
end;

procedure TCalls.DeleteWhileActive;
begin
  Defs.Delete(0);
end;

{ the database file beside the program, with two tables }
procedure CreateDatabase;
var
  C: TSQLiteConnection;
begin
  C := TSQLiteConnection.Create(nil);
  try
    C.Database := DbName;
    C.Open;
    C.ExecSQL('create table cities (id integer primary key, name varchar(20));' +
      'insert into cities values (1, ''Riga''), (2, ''Oslo'');' +
      'create table contacts (id integer primary key, name varchar(40) not null, age integer, active integer, ' +
      'kind varchar(10), city_id integer, notes text);' +
      'insert into contacts values (1, ''Bea'', 41, 1, ''work'', 2, ''first'' || char(10) || ''second'');' +
      'insert into contacts values (2, ''Al'', 17, 0, ''friend'', null, null);');
  finally
    C.Free;
  end;
end;

{ the caption of a control (TDBText does not publish Caption) }
function Cap(Control: TControl): string;
begin
  SetLength(Result, Control.GetTextLen);
  if Result <> '' then
    Control.GetTextBuf(PChar(Result), Length(Result) + 1);
end;

function StoredName(Instance: TObject; const PropName: string): string;
begin
  if not IsPublishedProp(Instance, PropName) then
    Result := 'not published'
  else if IsStoredProp(Instance, PropName) then
    Result := 'stored'
  else
    Result := 'not stored';
end;

function DefText(Defs: TFieldDefs): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Defs.Count - 1 do
  begin
    Result := Result + Defs[I].Name + ':' + FieldTypeName(Defs[I].DataType) + ':' + IntToStr(Defs[I].Size);
    if Defs[I].Required then
      Result := Result + ':required';
    Result := Result + ';';
  end;
end;

procedure TestForm;
var
  F: TStreamForm;
  P: TPersistent;
begin
  Section('loading');
  Log := '';
  F := TStreamForm.Create(nil);
  try
    CheckEq(Log, 'open Mem;connect;open Cities;open Contacts;',
      'Active and Connected are applied when the components are loaded, last component first');
    Check(not (csLoading in F.Contacts.ComponentState), 'loading is over');
    Check(not (csDesigning in F.NameEdit.ComponentState), 'never the design state');

    Section('connection');
    CheckEqBool(F.Conn.Connected, True, 'Connected');
    CheckEq(F.Conn.Database, DbName, 'Database');
    Check(F.Conn.OpenMode = omReadWrite, 'OpenMode');
    CheckEqInt(F.Conn.BusyTimeout, 1000, 'BusyTimeout');
    CheckEq(F.Conn.Params.CommaText, 'cache_size=100', 'Params');
    CheckEqInt(F.Conn.DataSetCount, 3, 'three datasets use the connection');
    CheckEqInt(F.Conn.DesignInfo, 304 shl 16 + 176, 'the design position is read');

    Section('table');
    Check(F.Contacts.Connection = F.Conn, 'Connection: a reference to a later component');
    CheckEqBool(F.Contacts.Active, True, 'Active');
    CheckEq(F.Contacts.TableName, 'contacts', 'TableName');
    CheckEq(F.Contacts.IndexFieldNames, 'name', 'IndexFieldNames');
    CheckEqInt(F.Contacts.RecordCount, 2, 'RecordCount');
    CheckEq(F.Contacts.FieldByName('name').AsString, 'Al', 'the first row in index order');
    CheckEq(StoredName(F.Contacts, 'TableName'), 'stored', 'TableName is stored');
    CheckEq(StoredName(F.Contacts, 'SQL'), 'not stored', 'the generated SQL of a table');
    CheckEq(StoredName(F.Contacts, 'Params'), 'not stored', 'Params of a table');
    CheckEq(StoredName(F.Contacts, 'UpdateTable'), 'not stored', 'UpdateTable of a table');
    CheckEq(StoredName(F.Contacts, 'KeyFields'), 'not stored', 'KeyFields of a table');

    Section('queries');
    Check(F.Cities.Connection = F.Conn, 'Connection: a reference to an earlier component');
    CheckEqBool(F.Cities.Active, True, 'Active');
    CheckEq(F.Cities.SQL.Text, 'select id, name from cities'#13#10'order by id'#13#10, 'SQL');
    CheckEqBool(F.Cities.ReadOnly, True, 'ReadOnly');
    CheckEqInt(F.Cities.RecordCount, 2, 'RecordCount');
    CheckEqBool(F.Adults.Active, False, 'a query without Active stays closed');
    CheckEq(StoredName(F.Adults, 'SQL'), 'stored', 'SQL of a query is stored');
    CheckEq(StoredName(F.Adults, 'Params'), 'stored', 'Params of a query are published');
    CheckEqInt(F.Adults.Params.Count, 1, 'the parameter of the SQL text');
    CheckEq(F.Adults.Params[0].Name, 'min', 'its name');
    F.Adults.Params[0].AsInteger := 18;
    F.Adults.Open;
    CheckEqInt(F.Adults.RecordCount, 1, 'opened by code');
    CheckEq(F.Adults.Fields[0].AsString, 'Bea', 'the row');
    CheckEq(Log, 'open Mem;connect;open Cities;open Contacts;open Adults;', 'events are bound to the form');

    Section('in-memory tables');
    P := F.Mem.FieldDefs;
    Check(P is TCollection, 'FieldDefs is a collection');
    CheckEq(StoredName(F.Mem, 'FieldDefs'), 'stored', 'FieldDefs of an in-memory table are stored');
    CheckEq(StoredName(F.Contacts, 'FieldDefs'), 'not published', 'FieldDefs of a table');
    CheckEq(DefText(F.Mem.FieldDefs), 'code:ftString:8:required;qty:ftInteger:0;', 'the definitions');
    CheckEqBool(F.Mem.Active, True, 'Active: opened empty from the definitions');
    CheckEqInt(F.Mem.RecordCount, 0, 'no rows');
    CheckEqInt(F.Mem.FieldCount, 2, 'the fields');
    CheckEqBool(F.Mem.FieldByName('code').Required, True, 'Required');
    F.Mem.Append;
    F.Mem.FieldByName('code').AsString := 'A1';
    F.Mem.FieldByName('qty').AsInteger := 3;
    F.Mem.Post;
    CheckEq(F.MemEdit.Text, 'A1', 'a control shows the appended row');
    CheckEqBool(F.Idle.Active, False, 'a table without Active stays closed');
    CheckEq(DefText(F.Idle.FieldDefs), 'n:ftAutoInc:0;', 'its definitions');
    CheckEqBool(F.Idle.ReadOnly, True, 'ReadOnly');

    Section('data sources');
    Check(F.ContactsSource.DataSet = F.Contacts, 'DataSet: a reference to a later component');
    Check(F.CitySource.DataSet = F.Cities, 'DataSet of the list source');
    CheckEqBool(F.CitySource.AutoEdit, False, 'AutoEdit');
    Check(F.MemSource.DataSet = F.Mem, 'DataSet of the third source');

    Section('grid');
    Check(F.Grid.DataSource = F.ContactsSource, 'DataSource: a reference to a later component');
    CheckEqInt(F.Grid.Columns.Count, 2, 'two columns');
    CheckEq(F.Grid.Columns[0].FieldName, 'name', 'column 0: FieldName');
    CheckEq(F.Grid.Columns[0].TitleText, 'Who', 'column 0: Title');
    CheckEqInt(F.Grid.Columns[0].Width, 120, 'column 0: Width');
    CheckEqBool(F.Grid.Columns[0].ReadOnly, False, 'column 0: ReadOnly');
    Check(F.Grid.Columns[0].Field = F.Contacts.FieldByName('name'), 'column 0: bound to the field');
    CheckEq(F.Grid.Columns[1].FieldName, 'age', 'column 1: FieldName');
    CheckEq(F.Grid.Columns[1].TitleText, 'age', 'column 1: the title of the field');
    CheckEqInt(F.Grid.Columns[1].Width, -1, 'column 1: Width');
    Check(F.Grid.Columns[1].Alignment = taCenter, 'column 1: Alignment');
    CheckEqBool(F.Grid.Columns[1].ReadOnly, True, 'column 1: ReadOnly');
    Check(F.Grid.Options = [dgEditing, dgTitles, dgRowLines], 'Options');
    CheckEqInt(F.Grid.ColCount, 2, 'the grid shows the two columns');

    Section('controls');
    Check(F.NameEdit.Field = F.Contacts.FieldByName('name'), 'edit: Field');
    CheckEq(F.NameEdit.Text, 'Al', 'edit');
    CheckEq(Cap(F.NameText), 'Al', 'text');
    CheckEq(F.Notes.Text, '', 'memo: null');
    CheckEqBool(F.ActiveBox.Checked, False, 'check box');
    CheckEq(F.ActiveBox.ValueChecked, '1;yes', 'check box: ValueChecked');
    Check(F.Kind.Style = csDropDownList, 'combo box: Style');
    CheckEqInt(F.Kind.ItemIndex, 0, 'combo box');
    Check(F.City.ListSource = F.CitySource, 'lookup combo box: ListSource');
    CheckEq(F.City.Text, '(none)', 'lookup combo box: null');
    Check(F.Nav.VisibleButtons = [nbFirst, nbPrior, nbNext, nbLast], 'navigator: VisibleButtons');
    CheckEqBool(F.Nav.ConfirmDelete, False, 'navigator: ConfirmDelete');
    CheckEq(F.Nav.ButtonHint(nbFirst), 'Top', 'navigator: Hints');
    CheckEqBool(F.Nav.ButtonEnabled(nbPrior), False, 'navigator: Prior is disabled on the first row');
    F.Nav.BtnClick(nbNext);
    CheckEq(F.NameEdit.Text, 'Bea', 'Next: edit');
    CheckEq(Cap(F.NameText), 'Bea', 'Next: text');
    CheckEq(Visible(F.Notes.Text), 'first\r\nsecond', 'Next: memo');
    CheckEqBool(F.ActiveBox.Checked, True, 'Next: check box');
    CheckEqInt(F.Kind.ItemIndex, 1, 'Next: combo box');
    CheckEq(F.City.Text, 'Oslo', 'Next: lookup combo box');
    CheckEqBool(F.Nav.ButtonEnabled(nbPrior), True, 'Next: the navigator follows');
  finally
    F.Free;
  end;
end;

procedure TestFailures;
begin
  Section('failures');
  CheckRaises(Calls.CreateBadConn, 'ESQLiteError', 'unable to open database file',
    'a connection that cannot be opened: the error leaves the creation of the form');
  CheckRaises(Calls.CreateBadTable, 'EDatabaseError', 'Cannot open a table without field definitions',
    'a dataset that cannot be opened: the error leaves the creation of the form');
end;

procedure TestDefs;
var
  T: TMemTable;
  D: TFieldDef;
  Item: TCollectionItem;
begin
  Section('field definitions as a collection');
  T := TMemTable.Create(nil);
  try
    Calls.Defs := T.FieldDefs;
    Check(T.FieldDefs.Owner = T, 'the dataset owns the collection');
    D := T.FieldDefs.Add('code', ftString, 8, True);
    Item := D;
    Check(Item.Collection = T.FieldDefs, 'a definition is an item of the collection');
    CheckEqInt(D.Index, 0, 'Index');
    Item := TCollection(T.FieldDefs).Add;
    CheckEqInt(T.FieldDefs.Count, 2, 'an item added without values');
    D := T.FieldDefs[1];
    Check(D = Item, 'is the second definition');
    CheckEq(DefText(T.FieldDefs), 'code:ftString:8:required;:ftUnknown:0;', 'with the default values');
    CheckRaises(Calls.RenameDuplicate, 'EDatabaseError', 'Duplicate field name ''Code''',
      'a name that exists is refused');
    D.Name := 'qty';
    D.DataType := ftInteger;
    D.Required := True;
    D.Size := 4;
    D.Name := 'QTY';
    CheckEq(DefText(T.FieldDefs), 'code:ftString:8:required;QTY:ftInteger:4:required;', 'the properties are assigned');
    CheckRaises(Calls.AddDuplicate, 'EDatabaseError', 'Duplicate field name ''CODE''', 'Add refuses a duplicate');
    T.Open;
    T.Append;
    T.FieldByName('code').AsString := 'x';
    T.FieldByName('qty').AsInteger := 1;
    T.Post;
    CheckRaises(Calls.AddWhileActive, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
      'open: Add');
    CheckRaises(Calls.RenameWhileActive, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
      'open: Name');
    CheckRaises(Calls.ResizeWhileActive, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
      'open: Size');
    CheckRaises(Calls.ClearWhileActive, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
      'open: Clear');
    CheckRaises(Calls.DeleteWhileActive, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
      'open: Delete');
    CheckEq(DefText(T.FieldDefs), 'code:ftString:8:required;QTY:ftInteger:4:required;', 'nothing changed');
    T.Close;
    T.Open;
    CheckEqInt(T.RecordCount, 1, 'the row survives Close and Open');
    T.Close;
    T.FieldDefs[1].Required := False;
    T.Open;
    CheckEqInt(T.RecordCount, 0, 'a changed definition discards the rows');
    T.Close;
    T.FieldDefs.Delete(1);
    CheckEq(DefText(T.FieldDefs), 'code:ftString:8:required;', 'Delete');
    T.FieldDefs.Clear;
    CheckEqInt(T.FieldDefs.Count, 0, 'Clear');
  finally
    T.Free;
  end;
end;

begin
  Application.Initialize;
  Calls := TCalls.Create;
  try
    CreateDatabase;
    try
      TestForm;
      TestFailures;
    finally
      DeleteFile(ExtractFilePath(ParamStr(0)) + DbName);
    end;
    TestDefs;
  finally
    Calls.Free;
  end;
  WriteLn('failed checks: ', FailedChecks);
end.
