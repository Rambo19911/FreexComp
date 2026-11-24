{ Conformance - the dataset conformance suite: one fixed script that every dataset class and driver must
  pass unchanged. The outputs of two dataset kinds differ only in their first line. Also the event recorder
  the suite uses, for the test programs that need one. }
unit Conformance;

interface

uses
  SysUtils, Classes, DB;

type
  { Records every dataset event of one dataset (by name) and every data event a data source receives from
    it (in brackets, a field change with the field name) in the order they happen. }
  TEventRecorder = class
  private
    FDataSet: TDataSet;
    FSource: TDataSource;
    FLog: TStringList;
    procedure HandleBeforeOpen(DataSet: TDataSet);
    procedure HandleAfterOpen(DataSet: TDataSet);
    procedure HandleBeforeClose(DataSet: TDataSet);
    procedure HandleAfterClose(DataSet: TDataSet);
    procedure HandleBeforeScroll(DataSet: TDataSet);
    procedure HandleAfterScroll(DataSet: TDataSet);
    procedure HandleBeforeInsert(DataSet: TDataSet);
    procedure HandleAfterInsert(DataSet: TDataSet);
    procedure HandleBeforeEdit(DataSet: TDataSet);
    procedure HandleAfterEdit(DataSet: TDataSet);
    procedure HandleBeforePost(DataSet: TDataSet);
    procedure HandleAfterPost(DataSet: TDataSet);
    procedure HandleBeforeCancel(DataSet: TDataSet);
    procedure HandleAfterCancel(DataSet: TDataSet);
    procedure HandleBeforeDelete(DataSet: TDataSet);
    procedure HandleAfterDelete(DataSet: TDataSet);
    procedure HandleBeforeRefresh(DataSet: TDataSet);
    procedure HandleAfterRefresh(DataSet: TDataSet);
    procedure HandleNewRecord(DataSet: TDataSet);
    procedure HandlePostError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
    procedure HandleDeleteError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
    procedure HandleEditError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
  public
    { Takes over every event of the dataset and attaches a data source to it. }
    constructor Create(ADataSet: TDataSet);
    { Detaches the data source and clears the events of the dataset. }
    destructor Destroy; override;
    procedure Add(const Text: string);
    procedure Clear;
    { What was recorded since the last call, as 'a,b,c'. }
    function Take: string;
  end;

{ Runs the suite on an open, empty, modifiable dataset with the fields id (auto-increment), name (string 50,
  required), amount (float), born (date), flag (boolean) and note (memo). Prints '== conformance <KindName>'
  and one ok / FAIL line per check; leaves the dataset open and empty. }
procedure RunConformance(DS: TDataSet; const KindName: string);

implementation

uses
  DataTestUtils;

type
  TRecorderSource = class(TDataSource)
  protected
    procedure DataEvent(Event: TDataEvent; Info: TObject); override;
  public
    Recorder: TEventRecorder;
  end;

  { the calls the suite expects to fail, as methods for CheckRaises }
  TConfCalls = class
  public
    DS: TDataSet;
    Mark: TBookmark;
    RaiseInFilter: Boolean;
    procedure RaisingFilter(DataSet: TDataSet; var Accept: Boolean);
    procedure RecNoTooBig;
    procedure WriteName;
    procedure WriteId;
    procedure Post;
    procedure Delete;
    procedure LocateCount;
    procedure BadFilter;
    procedure GotoMark;
    procedure AmountAsInteger;
    procedure LongName;
  end;

const
  EventNames: array[TDataEvent] of string = ('deFieldChange', 'deRecordChange', 'deDataSetChange',
    'deDataSetScroll', 'deLayoutChange', 'deUpdateRecord', 'deUpdateState', 'deCheckBrowseMode', 'deFocusControl');
  StateNames: array[TDataSetState] of string = ('dsInactive', 'dsBrowse', 'dsEdit', 'dsInsert');

{ TRecorderSource }

procedure TRecorderSource.DataEvent(Event: TDataEvent; Info: TObject);
begin
  if Info is TField then
    Recorder.Add('[' + EventNames[Event] + ' ' + TField(Info).FieldName + ']')
  else
    Recorder.Add('[' + EventNames[Event] + ']');
end;

{ TEventRecorder }

constructor TEventRecorder.Create(ADataSet: TDataSet);
var
  Source: TRecorderSource;
begin
  inherited Create;
  FLog := TStringList.Create;
  FDataSet := ADataSet;
  Source := TRecorderSource.Create(nil);
  Source.Recorder := Self;
  Source.DataSet := ADataSet;
  FSource := Source;
  ADataSet.BeforeOpen := HandleBeforeOpen;
  ADataSet.AfterOpen := HandleAfterOpen;
  ADataSet.BeforeClose := HandleBeforeClose;
  ADataSet.AfterClose := HandleAfterClose;
  ADataSet.BeforeScroll := HandleBeforeScroll;
  ADataSet.AfterScroll := HandleAfterScroll;
  ADataSet.BeforeInsert := HandleBeforeInsert;
  ADataSet.AfterInsert := HandleAfterInsert;
  ADataSet.BeforeEdit := HandleBeforeEdit;
  ADataSet.AfterEdit := HandleAfterEdit;
  ADataSet.BeforePost := HandleBeforePost;
  ADataSet.AfterPost := HandleAfterPost;
  ADataSet.BeforeCancel := HandleBeforeCancel;
  ADataSet.AfterCancel := HandleAfterCancel;
  ADataSet.BeforeDelete := HandleBeforeDelete;
  ADataSet.AfterDelete := HandleAfterDelete;
  ADataSet.BeforeRefresh := HandleBeforeRefresh;
  ADataSet.AfterRefresh := HandleAfterRefresh;
  ADataSet.OnNewRecord := HandleNewRecord;
  ADataSet.OnPostError := HandlePostError;
  ADataSet.OnDeleteError := HandleDeleteError;
  ADataSet.OnEditError := HandleEditError;
end;

destructor TEventRecorder.Destroy;
begin
  FSource.Free;
  FDataSet.BeforeOpen := nil;
  FDataSet.AfterOpen := nil;
  FDataSet.BeforeClose := nil;
  FDataSet.AfterClose := nil;
  FDataSet.BeforeScroll := nil;
  FDataSet.AfterScroll := nil;
  FDataSet.BeforeInsert := nil;
  FDataSet.AfterInsert := nil;
  FDataSet.BeforeEdit := nil;
  FDataSet.AfterEdit := nil;
  FDataSet.BeforePost := nil;
  FDataSet.AfterPost := nil;
  FDataSet.BeforeCancel := nil;
  FDataSet.AfterCancel := nil;
  FDataSet.BeforeDelete := nil;
  FDataSet.AfterDelete := nil;
  FDataSet.BeforeRefresh := nil;
  FDataSet.AfterRefresh := nil;
  FDataSet.OnNewRecord := nil;
  FDataSet.OnPostError := nil;
  FDataSet.OnDeleteError := nil;
  FDataSet.OnEditError := nil;
  FLog.Free;
  inherited Destroy;
end;

procedure TEventRecorder.Add(const Text: string);
begin
  FLog.Add(Text);
end;

procedure TEventRecorder.Clear;
begin
  FLog.Clear;
end;

function TEventRecorder.Take: string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to FLog.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + FLog[I];
  end;
  FLog.Clear;
end;

procedure TEventRecorder.HandleBeforeOpen(DataSet: TDataSet);
begin
  Add('BeforeOpen');
end;

procedure TEventRecorder.HandleAfterOpen(DataSet: TDataSet);
begin
  Add('AfterOpen');
end;

procedure TEventRecorder.HandleBeforeClose(DataSet: TDataSet);
begin
  Add('BeforeClose');
end;

procedure TEventRecorder.HandleAfterClose(DataSet: TDataSet);
begin
  Add('AfterClose');
end;

procedure TEventRecorder.HandleBeforeScroll(DataSet: TDataSet);
begin
  Add('BeforeScroll');
end;

procedure TEventRecorder.HandleAfterScroll(DataSet: TDataSet);
begin
  Add('AfterScroll');
end;

procedure TEventRecorder.HandleBeforeInsert(DataSet: TDataSet);
begin
  Add('BeforeInsert');
end;

procedure TEventRecorder.HandleAfterInsert(DataSet: TDataSet);
begin
  Add('AfterInsert');
end;

procedure TEventRecorder.HandleBeforeEdit(DataSet: TDataSet);
begin
  Add('BeforeEdit');
end;

procedure TEventRecorder.HandleAfterEdit(DataSet: TDataSet);
begin
  Add('AfterEdit');
end;

procedure TEventRecorder.HandleBeforePost(DataSet: TDataSet);
begin
  Add('BeforePost');
end;

procedure TEventRecorder.HandleAfterPost(DataSet: TDataSet);
begin
  Add('AfterPost');
end;

procedure TEventRecorder.HandleBeforeCancel(DataSet: TDataSet);
begin
  Add('BeforeCancel');
end;

procedure TEventRecorder.HandleAfterCancel(DataSet: TDataSet);
begin
  Add('AfterCancel');
end;

procedure TEventRecorder.HandleBeforeDelete(DataSet: TDataSet);
begin
  Add('BeforeDelete');
end;

procedure TEventRecorder.HandleAfterDelete(DataSet: TDataSet);
begin
  Add('AfterDelete');
end;

procedure TEventRecorder.HandleBeforeRefresh(DataSet: TDataSet);
begin
  Add('BeforeRefresh');
end;

procedure TEventRecorder.HandleAfterRefresh(DataSet: TDataSet);
begin
  Add('AfterRefresh');
end;

procedure TEventRecorder.HandleNewRecord(DataSet: TDataSet);
begin
  Add('OnNewRecord');
end;

procedure TEventRecorder.HandlePostError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  Add('OnPostError');
end;

procedure TEventRecorder.HandleDeleteError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  Add('OnDeleteError');
end;

procedure TEventRecorder.HandleEditError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  Add('OnEditError');
end;

{ TConfCalls }

procedure TConfCalls.RecNoTooBig;
begin
  DS.RecNo := DS.RecordCount + 1;
end;

procedure TConfCalls.WriteName;
begin
  DS.FieldByName('name').AsString := 'x';
end;

procedure TConfCalls.WriteId;
begin
  DS.FieldByName('id').AsInteger := 99;
end;

procedure TConfCalls.Post;
begin
  DS.Post;
end;

procedure TConfCalls.Delete;
begin
  DS.Delete;
end;

procedure TConfCalls.LocateCount;
begin
  DS.Locate('id;name', ['1'], []);
end;

{ accepts every row; raises once when RaiseInFilter is set }
procedure TConfCalls.RaisingFilter(DataSet: TDataSet; var Accept: Boolean);
begin
  if RaiseInFilter then
  begin
    RaiseInFilter := False;
    raise Exception.Create('filter failed');
  end;
end;

procedure TConfCalls.BadFilter;
begin
  DS.Filter := 'nosuch = 1';
end;

procedure TConfCalls.GotoMark;
begin
  DS.GotoBookmark(Mark);
end;

procedure TConfCalls.AmountAsInteger;
begin
  if DS.FieldByName('amount').AsInteger = 0 then
    Exit;
end;

procedure TConfCalls.LongName;
begin
  DS.FieldByName('name').AsString := StringOfChar('x', 51);
end;

{ the suite }

function Flags(DS: TDataSet): string;
begin
  if DS.Bof and DS.Eof then
    Result := 'Bof+Eof'
  else if DS.Bof then
    Result := 'Bof'
  else if DS.Eof then
    Result := 'Eof'
  else
    Result := '-';
end;

{ '<RecNo> <flags>', e.g. '1 Bof' }
function Position(DS: TDataSet): string;
begin
  Result := IntToStr(DS.RecNo) + ' ' + Flags(DS);
end;

{ the names met by the canonical forward loop, as 'a,b,c' }
function NamesForward(DS: TDataSet): string;
begin
  Result := '';
  DS.First;
  while not DS.Eof do
  begin
    if Result <> '' then
      Result := Result + ',';
    Result := Result + DS.FieldByName('name').AsString;
    DS.Next;
  end;
end;

function NamesBackward(DS: TDataSet): string;
begin
  Result := '';
  DS.Last;
  while not DS.Bof do
  begin
    if Result <> '' then
      Result := Result + ',';
    Result := Result + DS.FieldByName('name').AsString;
    DS.Prior;
  end;
end;

procedure AppendRow(DS: TDataSet; const Name: string; Amount: Double);
begin
  DS.Append;
  DS.FieldByName('name').AsString := Name;
  DS.FieldByName('amount').AsFloat := Amount;
  DS.Post;
end;

procedure RunConformance(DS: TDataSet; const KindName: string);
const
  InsertEvents = 'BeforeInsert,BeforeScroll,[deUpdateState],OnNewRecord,[deDataSetChange],AfterInsert,AfterScroll';
  PostEvents = '[deUpdateRecord],BeforePost,[deUpdateState],[deDataSetChange],AfterPost';
  CancelEvents = 'BeforeCancel,[deUpdateState],[deDataSetChange],AfterCancel';
  ScrollEvents = 'BeforeScroll,[deDataSetScroll],AfterScroll';
var
  Rec: TEventRecorder;
  Calls: TConfCalls;
  Id, Name, Amount, Born, Flag, Note: TField;
  First, Second, Old: TBookmark;
begin
  Section('conformance ' + KindName);
  Calls := TConfCalls.Create;
  Calls.DS := DS;

  Section('preconditions');
  Check(DS.Active, 'the dataset is open');
  CheckEq(StateNames[DS.State], 'dsBrowse', 'State');
  Check(DS.IsEmpty, 'the dataset is empty');
  CheckEqInt(DS.RecordCount, 0, 'RecordCount');
  CheckEq(Position(DS), '0 Bof+Eof', 'position of an empty dataset');
  Check(DS.CanModify, 'the dataset can be modified');
  Id := DS.FieldByName('id');
  Name := DS.FieldByName('name');
  Amount := DS.FieldByName('amount');
  Born := DS.FieldByName('born');
  Flag := DS.FieldByName('flag');
  Note := DS.FieldByName('note');
  Check(Id.DataType = ftAutoInc, 'id is an auto-increment field');
  Check((Name.DataType = ftString) and (Name.Size = 50) and Name.Required, 'name is a required string(50) field');
  Check(Amount.DataType = ftFloat, 'amount is a float field');
  Check(Born.DataType = ftDate, 'born is a date field');
  Check(Flag.DataType = ftBoolean, 'flag is a boolean field');
  Check(Note.DataType = ftMemo, 'note is a memo field');

  Section('insert');
  Rec := TEventRecorder.Create(DS);
  DS.Append;
  CheckEq(Rec.Take, InsertEvents, 'events of Append');
  CheckEq(StateNames[DS.State], 'dsInsert', 'State after Append');
  CheckEq(Position(DS), '0 -', 'position while inserting');
  Check(Id.IsNull and Name.IsNull and Amount.IsNull and Born.IsNull and Flag.IsNull and Note.IsNull,
    'a new record is all null');
  Name.AsString := 'Ada';
  CheckEq(Rec.Take, '[deFieldChange name]', 'events of a field assignment');
  Check(DS.Modified, 'Modified after an assignment');
  Amount.AsFloat := 9.5;
  Born.AsDateTime := EncodeDate(1815, 12, 10);
  Flag.AsBoolean := True;
  Note.AsString := 'first';
  Rec.Clear;
  DS.Post;
  CheckEq(Rec.Take, PostEvents, 'events of Post');
  Rec.Free;
  CheckEq(StateNames[DS.State], 'dsBrowse', 'State after Post');
  CheckEqInt(DS.RecordCount, 1, 'RecordCount after the first insert');
  CheckEqInt(DS.RecNo, 1, 'RecNo after the first insert');
  CheckEqInt(Id.AsInteger, 1, 'the first id');
  DS.Append;
  Name.AsString := 'Bob';
  Amount.AsFloat := 20.25;
  Flag.AsBoolean := False;
  DS.Post;
  CheckEqInt(Id.AsInteger, 2, 'the second id');
  DS.Append;
  Name.AsString := 'Cy';
  Amount.AsFloat := -3;
  Born.AsDateTime := EncodeDate(2000, 2, 29);
  Flag.AsBoolean := True;
  Note.AsString := 'third';
  DS.Post;
  CheckEqInt(Id.AsInteger, 3, 'the third id');
  CheckEqInt(DS.RecordCount, 3, 'RecordCount after three inserts');
  CheckEqInt(DS.RecNo, 3, 'an appended row is the last row and current');
  Check(not DS.IsEmpty, 'not empty');

  Section('navigation');
  DS.First;
  CheckEq(Position(DS), '1 Bof', 'First');
  DS.Prior;
  CheckEq(Position(DS), '1 Bof', 'Prior on the first row');
  DS.Next;
  CheckEq(Position(DS), '2 -', 'Next');
  DS.Next;
  CheckEq(Position(DS), '3 -', 'Next to the last row');
  DS.Next;
  CheckEq(Position(DS), '3 Eof', 'Next on the last row');
  DS.Prior;
  CheckEq(Position(DS), '2 -', 'Prior');
  DS.Last;
  CheckEq(Position(DS), '3 Eof', 'Last');
  CheckEqInt(DS.MoveBy(-5), -2, 'MoveBy(-5) moves two rows');
  CheckEq(Position(DS), '1 Bof', 'position after MoveBy(-5)');
  CheckEqInt(DS.MoveBy(2), 2, 'MoveBy(2)');
  CheckEq(Position(DS), '3 -', 'position after MoveBy(2)');
  CheckEqInt(DS.MoveBy(1), 0, 'MoveBy(1) on the last row');
  CheckEq(Position(DS), '3 Eof', 'position after MoveBy(1) on the last row');
  CheckEqInt(DS.MoveBy(0), 0, 'MoveBy(0)');
  CheckEq(Position(DS), '3 Eof', 'MoveBy(0) keeps the flags');
  DS.RecNo := 2;
  CheckEq(Position(DS), '2 -', 'RecNo := 2');
  CheckEq(Name.AsString, 'Bob', 'the row of RecNo 2');
  CheckRaises(Calls.RecNoTooBig, 'EDatabaseError', 'Record number out of range', 'RecNo beyond the last row');
  CheckEq(NamesForward(DS), 'Ada,Bob,Cy', 'the forward loop visits every row once');
  CheckEq(NamesBackward(DS), 'Cy,Bob,Ada', 'the backward loop visits every row once');

  Section('edit, cancel, post');
  DS.First;
  CheckRaises(Calls.WriteName, 'EDatabaseError', 'Dataset not in edit or insert mode', 'a write in dsBrowse');
  DS.Edit;
  CheckEq(StateNames[DS.State], 'dsEdit', 'State after Edit');
  Check(not DS.Modified, 'Edit starts unmodified');
  Name.AsString := 'Ada L';
  Check(DS.Modified, 'Modified after a write');
  CheckEq(Name.AsString, 'Ada L', 'the edit buffer is read');
  CheckRaises(Calls.WriteId, 'EDatabaseError', 'Field ''id'' cannot be modified', 'the auto-increment field is read-only');
  DS.Cancel;
  CheckEq(StateNames[DS.State], 'dsBrowse', 'State after Cancel');
  CheckEq(Name.AsString, 'Ada', 'Cancel restores the value');
  DS.Edit;
  Amount.AsFloat := 10.5;
  DS.Post;
  CheckEq(StateNames[DS.State], 'dsBrowse', 'State after Post');
  Check(Amount.AsFloat = 10.5, 'Post stores the value');
  CheckEqInt(Id.AsInteger, 1, 'the id is unchanged by an edit');
  DS.Edit;
  Note.AsString := 'changed';
  DS.Next;
  CheckEq(Position(DS), '2 -', 'Next posts a modified edit and moves');
  DS.Prior;
  CheckEq(Note.AsString, 'changed', 'the value posted by Next');
  DS.Edit;
  DS.Next;
  CheckEq(StateNames[DS.State] + ' ' + Position(DS), 'dsBrowse 2 -', 'Next cancels an unmodified edit and moves');
  DS.Append;
  Name.AsString := 'Tmp';
  DS.Cancel;
  CheckEqInt(DS.RecordCount, 3, 'a cancelled insert adds no row');
  CheckEq(Name.AsString, 'Bob', 'the cursor returns to the row before the insert');
  DS.Insert;
  CheckEq(StateNames[DS.State], 'dsInsert', 'State after Insert');
  DS.Cancel;
  CheckEq(Position(DS), '2 -', 'position after a cancelled Insert');

  Section('required field');
  DS.Append;
  Amount.AsFloat := 1;
  CheckRaises(Calls.Post, 'EDatabaseError', 'Field ''name'' must have a value', 'Post of an insert without the name');
  CheckEq(StateNames[DS.State], 'dsInsert', 'the failed Post keeps dsInsert');
  DS.Cancel;
  CheckEqInt(DS.RecordCount, 3, 'no row was added');
  DS.First;
  DS.Edit;
  Name.Clear;
  CheckRaises(Calls.Post, 'EDatabaseError', 'Field ''name'' must have a value', 'Post of an edit with a cleared name');
  CheckEq(StateNames[DS.State], 'dsEdit', 'the failed Post keeps dsEdit');
  DS.Cancel;
  CheckEq(Name.AsString, 'Ada', 'the name after Cancel');

  Section('locate');
  DS.First;
  Check(DS.Locate('name', ['Bob'], []), 'Locate by name');
  CheckEq(Position(DS), '2 -', 'position after Locate');
  Check(not DS.Locate('name', ['bob'], []), 'Locate is case-sensitive');
  CheckEq(Position(DS), '2 -', 'a failed Locate keeps the cursor');
  DS.First;
  Check(DS.Locate('name', ['bob'], [loCaseInsensitive]), 'Locate with loCaseInsensitive');
  CheckEqInt(DS.RecNo, 2, 'the row found without case');
  Check(DS.Locate('name', ['C'], [loPartialKey]), 'Locate with loPartialKey');
  CheckEqInt(DS.RecNo, 3, 'the row found by prefix');
  Check(DS.Locate('id', ['1'], []), 'Locate by id');
  CheckEq(Name.AsString, 'Ada', 'the row found by id');
  Check(DS.Locate('amount', ['20.25'], []), 'Locate by a float');
  CheckEq(Name.AsString, 'Bob', 'the row found by a float');
  Check(DS.Locate('id;flag', ['3', 'True'], []), 'Locate by two fields');
  CheckEq(Name.AsString, 'Cy', 'the row found by two fields');
  Check(not DS.Locate('id;flag', ['3', 'False'], []), 'Locate by two fields, one differs');
  Check(DS.Locate('born', [''], []), 'Locate of a null value');
  CheckEq(Name.AsString, 'Bob', 'the row with the null date');
  Check(not DS.Locate('name', ['Zed'], []), 'Locate of a missing value');
  CheckRaises(Calls.LocateCount, 'EDatabaseError', 'Locate: number of values does not match number of fields',
    'Locate with a wrong number of values');

  Section('filter');
  DS.Filter := 'amount > 0';
  CheckEqInt(DS.RecordCount, 3, 'a filter text alone filters nothing');
  DS.Filtered := True;
  CheckEqInt(DS.RecordCount, 2, 'RecordCount with amount > 0');
  CheckEq(NamesForward(DS), 'Ada,Bob', 'the rows with amount > 0');
  DS.Filter := 'name = ''C*''';
  CheckEq(NamesForward(DS), 'Cy', 'a prefix filter');
  DS.Filter := 'flag = FALSE AND note IS NULL';
  CheckEq(NamesForward(DS), 'Bob', 'a boolean and a null test');
  DS.Filter := 'born < ''1900-01-01'' OR NOT (id < 3)';
  CheckEq(NamesForward(DS), 'Ada,Cy', 'a date comparison, OR and NOT');
  DS.Filter := 'amount > 0';
  DS.Last;
  DS.Edit;
  Amount.AsFloat := -1;
  DS.Post;
  CheckEqInt(DS.RecordCount, 1, 'a posted row that fails the filter leaves the view');
  CheckEq(Name.AsString, 'Ada', 'the cursor moves to the last visible row');
  CheckRaises(Calls.BadFilter, 'EDatabaseError', 'Field ''nosuch'' not found', 'a filter with an unknown field');
  CheckEq(DS.Filter, 'amount > 0', 'the old filter is kept');
  DS.Filtered := False;
  CheckEqInt(DS.RecordCount, 3, 'RecordCount without the filter');
  DS.Filter := '';
  Check(DS.Locate('name', ['Bob'], []), 'the hidden row is back');
  DS.Edit;
  Amount.AsFloat := 20.25;
  DS.Post;

  Section('bookmarks');
  DS.First;
  First := DS.GetBookmark;
  DS.Next;
  Second := DS.GetBookmark;
  CheckEqInt(Length(Second), 8, 'a bookmark has 8 bytes');
  DS.Last;
  DS.GotoBookmark(Second);
  CheckEq(Position(DS) + ' ' + Name.AsString, '2 - Bob', 'GotoBookmark');
  Check(DS.BookmarkValid(Second), 'BookmarkValid');
  CheckEqInt(DS.CompareBookmarks(First, Second), -1, 'CompareBookmarks of rows 1 and 2');
  CheckEqInt(DS.CompareBookmarks(Second, First), 1, 'CompareBookmarks of rows 2 and 1');
  CheckEqInt(DS.CompareBookmarks(Second, Second), 0, 'CompareBookmarks of the same row');
  CheckEqInt(DS.CompareBookmarks(nil, First), -1, 'nil sorts before a bookmark');
  CheckEqInt(DS.CompareBookmarks(nil, nil), 0, 'two nil bookmarks are equal');
  SetLength(Calls.Mark, 3);
  Check(not DS.BookmarkValid(Calls.Mark), 'a bookmark of the wrong length is not valid');
  CheckRaises(Calls.GotoMark, 'EDatabaseError', 'Bookmark is not valid', 'GotoBookmark with an invalid bookmark');
  DS.Append;
  Check(DS.GetBookmark = nil, 'no bookmark while inserting');
  DS.Cancel;

  Section('event order');
  DS.First;
  Rec := TEventRecorder.Create(DS);
  DS.Edit;
  CheckEq(Rec.Take, 'BeforeEdit,[deUpdateState],AfterEdit', 'events of Edit');
  Note.AsString := 'again';
  Rec.Clear;
  DS.Post;
  CheckEq(Rec.Take, PostEvents, 'events of Post of an edit');
  DS.Edit;
  Rec.Clear;
  DS.Cancel;
  CheckEq(Rec.Take, CancelEvents, 'events of Cancel of an edit');
  DS.Append;
  Rec.Clear;
  DS.Cancel;
  CheckEq(Rec.Take, CancelEvents + ',AfterScroll', 'events of Cancel of an insert');
  DS.First;
  CheckEq(Rec.Take, ScrollEvents, 'events of First');
  DS.Prior;
  CheckEq(Rec.Take, ScrollEvents, 'events of Prior on the first row');
  DS.Edit;
  Note.AsString := 'changed';
  Rec.Clear;
  DS.Next;
  CheckEq(Rec.Take, '[deCheckBrowseMode],' + PostEvents + ',' + ScrollEvents, 'events of Next in a modified edit');
  DS.Edit;
  Rec.Clear;
  DS.Prior;
  CheckEq(Rec.Take, '[deCheckBrowseMode],' + CancelEvents + ',' + ScrollEvents,
    'events of Prior in an unmodified edit');
  Check(DS.Locate('name', ['Cy'], []), 'Locate for the event order');
  CheckEq(Rec.Take, ScrollEvents, 'events of a successful Locate');
  Check(not DS.Locate('name', ['Zed'], []), 'failed Locate for the event order');
  CheckEq(Rec.Take, '', 'a failed Locate fires no event');
  DS.Refresh;
  CheckEq(Rec.Take, 'BeforeRefresh,[deDataSetChange],AfterRefresh,AfterScroll', 'events of Refresh');
  DS.Append;
  Amount.AsFloat := 1;
  Rec.Clear;
  CheckRaises(Calls.Post, 'EDatabaseError', 'must have a value', 'a failing Post for the event order');
  CheckEq(Rec.Take, '[deUpdateRecord]', 'the required check comes before BeforePost');
  DS.Cancel;
  Rec.Free;

  Section('conversions of stored values');
  DS.First;
  CheckEq(Id.AsString, '1', 'id.AsString');
  Check(Id.AsFloat = 1, 'id.AsFloat');
  Check(Id.AsLargeInt = 1, 'id.AsLargeInt');
  Check(Id.AsBoolean, 'id.AsBoolean');
  CheckEq(Name.AsString, 'Ada', 'name.AsString');
  CheckEq(Amount.AsString, '10.5', 'amount.AsString');
  CheckRaises(Calls.AmountAsInteger, 'EDatabaseError', 'Cannot access field ''amount'' as type Integer',
    'amount.AsInteger');
  CheckEq(Flag.AsString, 'True', 'flag.AsString');
  CheckEqInt(Flag.AsInteger, 1, 'flag.AsInteger');
  CheckEq(Flag.DisplayText, 'True', 'flag.DisplayText');
  Check(Born.AsDateTime = EncodeDate(1815, 12, 10), 'born.AsDateTime');
  Check(Born.AsFloat = EncodeDate(1815, 12, 10), 'born.AsFloat');
  CheckEq(FormatDateTime('yyyy-mm-dd', Born.AsDateTime), '1815-12-10', 'born formatted');
  CheckEq(Note.AsString, 'changed', 'note.AsString');
  CheckEq(Note.DisplayText, 'changed', 'note.DisplayText');
  DS.Next;
  Check(Born.IsNull and Note.IsNull, 'the null values of the second row');
  Check(Born.AsDateTime = 0, 'a null date reads as 0');
  CheckEq(Note.AsString, '', 'a null memo reads as an empty string');
  CheckEq(Born.DisplayText, '', 'a null date has no text');
  Check(not Flag.AsBoolean and not Flag.IsNull, 'a stored False is not null');
  Check(Amount.AsFloat = 20.25, 'amount of the second row');
  DS.Edit;
  CheckRaises(Calls.LongName, 'EDatabaseError', 'Value too long for field ''name'' (maximum 50 characters)',
    'a name of 51 characters');
  Name.AsString := StringOfChar('y', 50);
  DS.Post;
  CheckEqInt(Length(Name.AsString), 50, 'a name of 50 characters is stored');
  DS.Edit;
  Name.AsString := 'Bob';
  Born.AsDateTime := EncodeDate(1990, 1, 2) + 0.75;
  DS.Post;
  Check(Born.AsDateTime = EncodeDate(1990, 1, 2), 'a date field drops the time of day');
  DS.Edit;
  Born.Clear;
  Note.AsString := '';
  DS.Post;
  Check(Born.IsNull, 'Clear stores null');
  Check(not Note.IsNull and (Note.AsString = ''), 'an empty memo is not null');
  DS.Edit;
  Note.Clear;
  DS.Post;

  Section('refresh');
  DS.RecNo := 2;
  Old := DS.GetBookmark;
  DS.Refresh;
  CheckEqInt(DS.RecordCount, 3, 'RecordCount after Refresh');
  CheckEq(Position(DS) + ' ' + Name.AsString, '2 - Bob', 'Refresh keeps the record number');
  Check(not DS.BookmarkValid(Old), 'a bookmark taken before Refresh is not valid');
  Check(Born.IsNull and Note.IsNull and (Amount.AsFloat = 20.25) and not Flag.AsBoolean and (Id.AsInteger = 2),
    'the second row after Refresh');
  DS.First;
  Check((Id.AsInteger = 1) and (Name.AsString = 'Ada') and (Amount.AsFloat = 10.5) and
    (Born.AsDateTime = EncodeDate(1815, 12, 10)) and Flag.AsBoolean and (Note.AsString = 'changed'),
    'the first row after Refresh');
  DS.Last;
  Check((Id.AsInteger = 3) and (Name.AsString = 'Cy') and (Amount.AsFloat = -3) and
    (Born.AsDateTime = EncodeDate(2000, 2, 29)) and Flag.AsBoolean and (Note.AsString = 'third'),
    'the third row after Refresh');
  CheckEq(NamesForward(DS), 'Ada,Bob,Cy', 'the rows after Refresh');

  Section('delete');
  AppendRow(DS, 'Dee', 4);
  CheckEqInt(Id.AsInteger, 4, 'the fourth id');
  AppendRow(DS, 'Eve', 5);
  CheckEqInt(Id.AsInteger, 5, 'the fifth id');
  DS.First;
  First := DS.GetBookmark;
  Rec := TEventRecorder.Create(DS);
  DS.Delete;
  CheckEq(Rec.Take, 'BeforeDelete,BeforeScroll,[deDataSetChange],AfterDelete,AfterScroll', 'events of Delete');
  Rec.Free;
  CheckEq(Position(DS) + ' ' + Name.AsString, '1 - Bob', 'after deleting the first row');
  CheckEqInt(DS.RecordCount, 4, 'RecordCount after deleting the first row');
  Check(not DS.BookmarkValid(First), 'the bookmark of a deleted row is not valid');
  DS.RecNo := 2;
  DS.Delete;
  CheckEq(Position(DS) + ' ' + Name.AsString, '2 - Dee', 'after deleting a middle row');
  DS.Last;
  DS.Delete;
  CheckEq(Position(DS) + ' ' + Name.AsString, '2 - Dee', 'after deleting the last row');
  CheckEq(NamesForward(DS), 'Bob,Dee', 'the rows left');
  DS.First;
  DS.Edit;
  Name.AsString := 'Bobby';
  DS.Delete;
  CheckEq(StateNames[DS.State] + ' ' + Name.AsString, 'dsBrowse Dee', 'Delete in dsEdit cancels the edit and deletes');
  DS.Append;
  Name.AsString := 'Tmp';
  DS.Delete;
  CheckEq(StateNames[DS.State] + ' ' + IntToStr(DS.RecordCount), 'dsBrowse 1', 'Delete in dsInsert only cancels');
  DS.Delete;
  Check(DS.IsEmpty, 'empty after deleting every row');
  CheckEq(Position(DS), '0 Bof+Eof', 'position of the emptied dataset');
  CheckRaises(Calls.Delete, 'EDatabaseError', 'Cannot perform this operation on an empty dataset',
    'Delete on an empty dataset');
  DS.Refresh;
  CheckEqInt(DS.RecordCount, 0, 'RecordCount after Refresh of the emptied dataset');
  CheckEq(StateNames[DS.State], 'dsBrowse', 'the dataset stays open');

  Section('filter handler raising during Post');
  Calls.RaiseInFilter := False;
  DS.OnFilterRecord := Calls.RaisingFilter;
  DS.Filtered := True;
  DS.Append;
  Name.AsString := 'Fay';
  Calls.RaiseInFilter := True;
  CheckRaises(Calls.Post, 'Exception', 'filter failed', 'Post of an insert: the exception reaches the caller');
  CheckEq(StateNames[DS.State] + ' ' + Position(DS), 'dsBrowse 0 Bof+Eof', 'browse state, the inserted row hidden');
  DS.Post;
  CheckEq(StateNames[DS.State], 'dsBrowse', 'a second Post after the insert does nothing');
  DS.Filtered := False;
  CheckEq(NamesForward(DS), 'Fay', 'the insert was applied');
  DS.Refresh;
  CheckEq(NamesForward(DS), 'Fay', 'the inserted row is in the store');
  DS.Filtered := True;
  DS.First;
  DS.Edit;
  Name.AsString := 'Gil';
  Calls.RaiseInFilter := True;
  CheckRaises(Calls.Post, 'Exception', 'filter failed', 'Post of an edit: the exception reaches the caller');
  CheckEq(StateNames[DS.State] + ' ' + Position(DS), 'dsBrowse 0 Bof+Eof', 'browse state, the edited row hidden');
  DS.Post;
  CheckEq(StateNames[DS.State], 'dsBrowse', 'a second Post after the edit does nothing');
  DS.Filtered := False;
  CheckEq(NamesForward(DS), 'Gil', 'the edit was applied');
  DS.Refresh;
  CheckEq(NamesForward(DS), 'Gil', 'the edited row is in the store');
  DS.OnFilterRecord := nil;
  DS.First;
  DS.Delete;
  Check(DS.IsEmpty, 'empty after the raising filter handler');
  Calls.Free;
end;

end.
