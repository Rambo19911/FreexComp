// covers: DAT-005, DAT-006, DAT-012, DAT-013, DAT-014, DAT-015, DAT-019, DAT-020, DAT-021, DAT-022, DAT-024, DAT-072
program DsEvents;
{ The editing operations of a dataset and their exact event order: Open/Close, Edit/Post/Cancel, Insert/Append,
  Delete, the required-field check, CheckBrowseMode with auto-post/cancel on navigation, the abort and error
  protocol, and Refresh. One recorder (the test driver's log) receives every dataset event, every data event
  and every driver call. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  OpenSeq = 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll';
  CloseSeq = 'BeforeClose,DriverClose,[deUpdateState dsInactive],AfterClose';
  EditSeq = 'BeforeEdit,DriverEdit,[deUpdateState dsEdit],AfterEdit';
  PostEditSeq = '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';
  PostInsertSeq = '[deUpdateRecord dsInsert],BeforePost,ApplyInsert,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';
  CancelEditSeq = 'BeforeCancel,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterCancel';
  CancelInsertSeq = 'BeforeCancel,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterCancel,AfterScroll';
  InsertSeq = 'BeforeInsert,BeforeScroll,[deUpdateState dsInsert],OnNewRecord,[deDataSetChange dsInsert],AfterInsert,AfterScroll';
  DeleteSeq = 'BeforeDelete,BeforeScroll,ApplyDelete,[deDataSetChange dsBrowse],AfterDelete,AfterScroll';
  RefreshSeq = 'BeforeRefresh,DriverLoadRows,[deDataSetChange dsBrowse],AfterRefresh,AfterScroll';
  ScrollSeq = 'BeforeScroll,[deDataSetScroll dsBrowse],AfterScroll';
  SClosed = 'Cannot perform this operation on a closed dataset';

type
  TCalls = class
    DS: TTestDataSet;
    procedure DoEdit;
    procedure DoInsert;
    procedure DoAppend;
    procedure DoPost;
    procedure DoCancel;
    procedure DoDelete;
    procedure DoRefresh;
    procedure DoNext;
    procedure DoFirst;
    procedure DoCheckBrowseMode;
    procedure DoUpdateRecord;
    procedure SetRecNo9;
    { OnNewRecord handlers }
    procedure NewRecordDefaults(DataSet: TDataSet);
  end;

procedure TCalls.DoEdit;
begin
  DS.Edit;
end;

procedure TCalls.DoInsert;
begin
  DS.Insert;
end;

procedure TCalls.DoAppend;
begin
  DS.Append;
end;

procedure TCalls.DoPost;
begin
  DS.Post;
end;

procedure TCalls.DoCancel;
begin
  DS.Cancel;
end;

procedure TCalls.DoDelete;
begin
  DS.Delete;
end;

procedure TCalls.DoRefresh;
begin
  DS.Refresh;
end;

procedure TCalls.DoNext;
begin
  DS.Next;
end;

procedure TCalls.DoFirst;
begin
  DS.First;
end;

procedure TCalls.DoCheckBrowseMode;
begin
  DS.CheckBrowseMode;
end;

procedure TCalls.DoUpdateRecord;
begin
  DS.UpdateRecord;
end;

procedure TCalls.SetRecNo9;
begin
  DS.RecNo := 9;
end;

procedure TCalls.NewRecordDefaults(DataSet: TDataSet);
begin
  AddLog('OnNewRecord');
  DataSet.FieldByName('id').AsInteger := 100;
  DataSet.FieldByName('name').AsString := 'dflt';
end;

var
  Calls: TCalls;

{ id (integer), name (string 10); RowCount rows 1 'one', 2 'two', 3 'three'; opened, all events hooked }
function NewDS(RowCount: Integer): TTestDataSet;
const
  Names: array[1..3] of string = ('one', 'two', 'three');
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  for I := 1 to RowCount do
    Result.AddRow([IntValue(I), StringValue(Names[I])]);
  Result.HookEvents;
  Result.Open;
  ClearLog;
  Calls.DS := Result;
end;

{ 'dsEdit rec=1/3 Bof mod=False' }
function Snap(DS: TDataSet): string;
begin
  Result := StateName(DS.State) + ' rec=' + IntToStr(DS.RecNo) + '/' + IntToStr(DS.RecordCount) + ' ' + Flags(DS) +
    ' mod=' + BoolStr(DS.Modified);
end;

procedure TestOpenClose;
var
  DS: TTestDataSet;
begin
  Section('open and close: event order');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddRow([IntValue(1)]);
  DS.HookEvents;
  DS.HookErrors;
  ClearLog;
  DS.Open;
  CheckEq(TakeLog, OpenSeq, 'Open');
  DS.Close;
  CheckEq(TakeLog, CloseSeq, 'Close');
  Calls.DS := DS;
  CheckRaises(Calls.DoEdit, 'EDatabaseError', SClosed, 'Edit on a closed dataset');
  CheckRaises(Calls.DoInsert, 'EDatabaseError', SClosed, 'Insert on a closed dataset');
  CheckRaises(Calls.DoAppend, 'EDatabaseError', SClosed, 'Append on a closed dataset');
  CheckRaises(Calls.DoPost, 'EDatabaseError', SClosed, 'Post on a closed dataset');
  CheckRaises(Calls.DoCancel, 'EDatabaseError', SClosed, 'Cancel on a closed dataset');
  CheckRaises(Calls.DoDelete, 'EDatabaseError', SClosed, 'Delete on a closed dataset');
  CheckRaises(Calls.DoRefresh, 'EDatabaseError', SClosed, 'Refresh on a closed dataset');
  CheckRaises(Calls.DoUpdateRecord, 'EDatabaseError', SClosed, 'UpdateRecord on a closed dataset');
  CheckEqBool(DS.Modified, False, 'Modified of a closed dataset');
  CheckEqBool(DS.CanModify, False, 'CanModify of a closed dataset');
  CheckEq(TakeLog, '', 'no events on a closed dataset');
  DS.Free;
end;

procedure TestEdit;
var
  DS: TTestDataSet;
  Name: TField;
begin
  Section('edit: Edit');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  CheckEqBool(DS.CanModify, True, 'CanModify');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'before Edit');
  DS.Edit;
  CheckEq(TakeLog, EditSeq, 'Edit: event order');
  CheckEq(Snap(DS), 'dsEdit rec=1/3 Bof mod=False', 'after Edit');
  CheckEq(Name.AsString, 'one', 'the edit buffer is a copy of the current row');
  DS.Edit;
  CheckEq(TakeLog, '', 'Edit in dsEdit is a no-op');

  Section('edit: buffer and Post');
  Name.AsString := 'uno';
  CheckEq(TakeLog, '[deFieldChange dsEdit name]', 'a field write notifies');
  CheckEq(Name.AsString, 'uno', 'the field reads the buffer');
  CheckEqBool(DS.Modified, True, 'Modified after a write');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'the stored row is unchanged before Post');
  DS.Post;
  CheckEq(TakeLog, PostEditSeq, 'Post: event order');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'after Post');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'Post stores the buffer');
  CheckEq(DS.StoredText, '[1|uno] [2|two] [3|three]', 'in the row list too');
  CheckEq(DS.ViewRowIds, '1,2,3', 'the row keeps its id');
  CheckEq(DS.LastApply, 'update old=[1|one] new=[1|uno] assigned=2', 'the driver gets the old row, the buffer and the flags');
  DS.Post;
  CheckEq(TakeLog, '', 'Post in dsBrowse is a no-op');
  DS.Cancel;
  CheckEq(TakeLog, '', 'Cancel in dsBrowse is a no-op');

  Section('edit: Cancel');
  DS.Next;
  ClearLog;
  DS.Edit;
  CheckEq(Name.AsString, 'two', 'Edit copies the row under the cursor');
  Name.AsString := 'dos';
  DS.FieldByName('id').AsInteger := 22;
  CheckEqBool(DS.Modified, True, 'Modified');
  ClearLog;
  DS.Cancel;
  CheckEq(TakeLog, CancelEditSeq, 'Cancel: event order');
  CheckEq(Snap(DS), 'dsBrowse rec=2/3 - mod=False', 'after Cancel');
  CheckEq(Name.AsString, 'two', 'the row is unchanged');
  CheckEq(DS.StoredText, '[1|uno] [2|two] [3|three]', 'nothing stored');

  Section('edit: assigned flags');
  DS.Edit;
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[2|two] new=[2|two] assigned=', 'the flags are cleared when the buffer is created');
  CheckEq(TakeLog, EditSeq + ',' + PostEditSeq, 'an unmodified edit is posted when asked');
  DS.Edit;
  DS.FieldByName('id').AsInteger := 2;
  CheckEqBool(DS.Modified, True, 'writing the same value sets Modified');
  DS.Post;
  CheckEq(DS.LastApply, 'update old=[2|two] new=[2|two] assigned=1', 'and its flag');
  ClearLog;

  Section('edit: Close discards the edit');
  DS.Edit;
  Name.AsString := 'gone';
  ClearLog;
  DS.Close;
  CheckEq(TakeLog, CloseSeq, 'no cancel events');
  CheckEq(StateName(DS.State), 'dsInactive', 'closed');
  DS.Open;
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'reopened with the driver rows');
  CheckEqBool(DS.Modified, False, 'not Modified after reopening');
  DS.Free;

  Section('edit: empty dataset');
  DS := NewDS(0);
  DS.Edit;
  CheckEq(TakeLog, InsertSeq, 'Edit on an empty dataset behaves as Insert');
  CheckEq(Snap(DS), 'dsInsert rec=0/0 - mod=False', 'state');
  DS.Cancel;
  DS.Free;

  Section('edit: read-only dataset');
  DS := NewDS(3);
  DS.DriverReadOnly := True;
  CheckEqBool(DS.CanModify, False, 'CanModify');
  CheckRaises(Calls.DoEdit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit');
  CheckEq(TakeLog, '', 'no events');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'state unchanged');
  DS.Free;
end;

procedure TestInsert;
var
  DS: TTestDataSet;
  Id, Name: TField;
begin
  Section('insert: Insert');
  DS := NewDS(3);
  Id := DS.FieldByName('id');
  Name := DS.FieldByName('name');
  DS.Next;
  ClearLog;
  DS.Insert;
  CheckEq(TakeLog, InsertSeq, 'Insert: event order');
  CheckEq(Snap(DS), 'dsInsert rec=0/3 - mod=False', 'RecNo is 0, the new row is not counted');
  Check(Id.IsNull and Name.IsNull, 'the new row is all null');
  DS.Insert;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + CancelInsertSeq + ',' + InsertSeq, 'Insert in an untouched insert cancels it first');
  Id.AsInteger := 9;
  Name.AsString := 'new';
  CheckEqBool(DS.Modified, True, 'Modified');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'nothing stored before Post');
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostInsertSeq, 'Post: event order');
  CheckEq(DS.ViewText, '[1|one] [9|new] [2|two] [3|three]', 'inserted before the row that was current');
  CheckEq(DS.StoredText, '[1|one] [9|new] [2|two] [3|three]', 'in the row list too');
  CheckEq(DS.ViewRowIds, '1,4,2,3', 'a new row id');
  CheckEq(Snap(DS), 'dsBrowse rec=2/4 - mod=False', 'the new row is current');
  CheckEq(DS.LastApply, 'insert new=[9|new] assigned=1,2', 'the driver gets the buffer and the flags');

  Section('insert: Append');
  DS.First;
  ClearLog;
  DS.Append;
  CheckEq(TakeLog, InsertSeq, 'Append: event order');
  CheckEq(Snap(DS), 'dsInsert rec=0/4 - mod=False', 'state');
  Name.AsString := 'end';
  DS.Post;
  CheckEq(DS.ViewText, '[1|one] [9|new] [2|two] [3|three] [NULL|end]', 'appended after the last row');
  CheckEq(DS.ViewRowIds, '1,4,2,3,5', 'row ids');
  CheckEq(Snap(DS), 'dsBrowse rec=5/5 - mod=False', 'the new row is current');
  CheckEq(DS.LastApply, 'insert new=[NULL|end] assigned=2', 'only the written field is flagged');

  Section('insert: Cancel');
  DS.RecNo := 3;
  ClearLog;
  DS.Insert;
  Name.AsString := 'x';
  ClearLog;
  DS.Cancel;
  CheckEq(TakeLog, CancelInsertSeq, 'Cancel of an insert: event order');
  CheckEq(Snap(DS), 'dsBrowse rec=3/5 - mod=False', 'the cursor is back on the row that was current');
  CheckEq(Name.AsString, 'two', 'that row');
  CheckEq(DS.ViewRowIds, '1,4,2,3,5', 'nothing stored');
  DS.Append;
  DS.Cancel;
  CheckEq(Snap(DS), 'dsBrowse rec=3/5 - mod=False', 'the same after a cancelled Append');
  DS.First;
  DS.Insert;
  CheckEq(Flags(DS), '-', 'Bof and Eof are False while inserting');
  DS.Cancel;
  CheckEq(Snap(DS), 'dsBrowse rec=1/5 Bof mod=False', 'the flags are back too');
  DS.Insert;
  Name.AsString := 'top';
  DS.Post;
  CheckEq(DS.ViewRowIds, '6,1,4,2,3,5', 'a cancelled insert uses no row id; Insert on the first row goes before it');
  CheckEq(Snap(DS), 'dsBrowse rec=1/6 - mod=False', 'the new first row is current');
  DS.Free;

  Section('insert: empty dataset');
  DS := NewDS(0);
  DS.Insert;
  CheckEq(Snap(DS), 'dsInsert rec=0/0 - mod=False', 'Insert');
  DS.Cancel;
  CheckEq(Snap(DS), 'dsBrowse rec=0/0 Bof+Eof mod=False', 'cancelled');
  CheckEqInt(DS.CursorIndex, -1, 'no current row');
  DS.Insert;
  DS.FieldByName('name').AsString := 'a';
  DS.Post;
  CheckEq(Snap(DS), 'dsBrowse rec=1/1 - mod=False', 'posted');
  DS.Free;
  DS := NewDS(0);
  DS.Append;
  DS.FieldByName('name').AsString := 'a';
  DS.Post;
  DS.Append;
  DS.FieldByName('name').AsString := 'b';
  DS.Post;
  CheckEq(DS.ViewText, '[NULL|a] [NULL|b]', 'Append on an empty dataset, twice');
  CheckEq(Snap(DS), 'dsBrowse rec=2/2 - mod=False', 'state');
  DS.Free;

  Section('insert: OnNewRecord');
  DS := NewDS(3);
  DS.OnNewRecord := Calls.NewRecordDefaults;
  DS.Insert;
  CheckEq(TakeLog, 'BeforeInsert,BeforeScroll,[deUpdateState dsInsert],OnNewRecord,[deFieldChange dsInsert id],' +
    '[deFieldChange dsInsert name],[deDataSetChange dsInsert],AfterInsert,AfterScroll', 'event order with assignments');
  CheckEq(DS.FieldByName('name').AsString, 'dflt', 'the default is in the buffer');
  CheckEqBool(DS.Modified, False, 'defaults do not set Modified');
  DS.Post;
  CheckEq(DS.LastApply, 'insert new=[100|dflt] assigned=1,2', 'but their flags are kept for the driver');
  DS.Free;

  Section('insert: the driver may fill the buffer');
  DS := NewDS(1);
  DS.InsertId := 50;
  DS.Append;
  DS.FieldByName('name').AsString := 'auto';
  DS.Post;
  CheckEq(DS.ViewText, '[1|one] [50|auto]', 'the stored row has the value set by ApplyInsert');
  CheckEqInt(DS.FieldByName('id').AsInteger, 50, 'and the field reads it');
  DS.Free;

  Section('insert: read-only dataset');
  DS := NewDS(3);
  DS.DriverReadOnly := True;
  CheckRaises(Calls.DoInsert, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Insert');
  CheckRaises(Calls.DoAppend, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Append');
  CheckEq(TakeLog, '', 'no events');
  DS.Free;
end;

procedure TestDelete;
var
  DS: TTestDataSet;
begin
  Section('delete: Delete');
  DS := NewDS(3);
  DS.Next;
  ClearLog;
  DS.Delete;
  CheckEq(TakeLog, DeleteSeq, 'Delete: event order');
  CheckEq(DS.LastApply, 'delete row=[2|two]', 'the driver gets the row');
  CheckEq(DS.ViewText, '[1|one] [3|three]', 'the row is gone');
  CheckEq(DS.StoredText, '[1|one] [3|three]', 'from the row list too');
  CheckEq(Snap(DS), 'dsBrowse rec=2/2 - mod=False', 'the following row is current');
  DS.Delete;
  CheckEq(Snap(DS), 'dsBrowse rec=1/1 - mod=False', 'after deleting the last row the new last row is current');
  DS.First;
  CheckEq(Flags(DS), 'Bof', 'Bof after First');
  DS.Delete;
  CheckEq(Snap(DS), 'dsBrowse rec=0/0 Bof+Eof mod=False', 'empty after deleting the only row');
  CheckEqInt(DS.CursorIndex, -1, 'no current row');
  ClearLog;
  CheckRaises(Calls.DoDelete, 'EDatabaseError', 'Cannot perform this operation on an empty dataset', 'Delete on an empty dataset');
  CheckEq(TakeLog, '', 'no events');
  DS.Free;

  DS := NewDS(3);
  CheckEq(Flags(DS), 'Bof', 'at the first row');
  DS.Delete;
  CheckEq(Snap(DS), 'dsBrowse rec=1/2 - mod=False', 'Delete clears Bof');
  DS.Last;
  DS.Prior;
  DS.Last;
  CheckEq(Flags(DS), 'Eof', 'at the last row');
  DS.Delete;
  CheckEq(Snap(DS), 'dsBrowse rec=1/1 - mod=False', 'Delete clears Eof');
  DS.Free;

  Section('delete: in dsInsert and dsEdit');
  DS := NewDS(3);
  DS.Next;
  DS.Insert;
  DS.FieldByName('name').AsString := 'x';
  ClearLog;
  DS.Delete;
  CheckEq(TakeLog, CancelInsertSeq, 'Delete in dsInsert behaves as Cancel');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'nothing is deleted');
  CheckEq(Snap(DS), 'dsBrowse rec=2/3 - mod=False', 'state');
  DS.Edit;
  DS.FieldByName('name').AsString := 'x';
  ClearLog;
  DS.Delete;
  CheckEq(TakeLog, CancelEditSeq + ',' + DeleteSeq, 'Delete in dsEdit cancels the edit, then deletes');
  CheckEq(DS.ViewText, '[1|one] [3|three]', 'the row is gone');
  DS.Free;

  Section('delete: read-only dataset');
  DS := NewDS(3);
  DS.DriverReadOnly := True;
  CheckRaises(Calls.DoDelete, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Delete');
  CheckEq(TakeLog, '', 'no events');
  CheckEqInt(DS.RecordCount, 3, 'nothing deleted');
  DS.Free;
end;

procedure TestBrowseMode;
var
  DS: TTestDataSet;
  Name: TField;
begin
  Section('browse-mode: required fields');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftAutoInc, 0, True);
  DS.AddDef('name', ftString, 10, True);
  DS.AddDef('code', ftInteger, 0, True);
  DS.AddDef('note', ftString, 10);
  DS.AddRow([IntValue(1), StringValue('one'), IntValue(11), NullValue]);
  DS.HookEvents;
  DS.Open;
  Calls.DS := DS;
  DS.Append;
  ClearLog;
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'Field ''name'' must have a value', 'Post without the first required field');
  CheckEq(TakeLog, '[deUpdateRecord dsInsert]', 'the check comes before BeforePost');
  CheckEq(StateName(DS.State), 'dsInsert', 'still inserting');
  DS.FieldByName('name').AsString := 'n';
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'Field ''code'' must have a value', 'Post without the second required field');
  DS.FieldByName('code').AsInteger := 5;
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostInsertSeq, 'Post with all required values; the auto-increment field is exempt');
  CheckEq(DS.ViewText, '[1|one|11|NULL] [NULL|n|5|NULL]', 'stored');
  DS.Edit;
  DS.FieldByName('name').Clear;
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'Field ''name'' must have a value', 'Post of an edit with a cleared required field');
  CheckEq(StateName(DS.State), 'dsEdit', 'still editing');
  DS.FieldByName('name').AsString := '';
  DS.Post;
  CheckEq(DS.ViewText, '[1|one|11|NULL] [NULL||5|NULL]', 'an empty string is a value');
  DS.Free;

  Section('browse-mode: CheckBrowseMode');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  DS.CheckBrowseMode;
  CheckEq(TakeLog, '', 'nothing in dsBrowse');
  DS.Edit;
  ClearLog;
  DS.CheckBrowseMode;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + CancelEditSeq, 'an unmodified edit is cancelled');
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.CheckBrowseMode;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq, 'a modified edit is posted');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'stored');
  DS.Insert;
  ClearLog;
  DS.CheckBrowseMode;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + CancelInsertSeq, 'an unmodified insert is cancelled');
  DS.Insert;
  Name.AsString := 'new';
  ClearLog;
  DS.CheckBrowseMode;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + PostInsertSeq, 'a modified insert is posted');
  CheckEq(DS.ViewText, '[NULL|new] [1|uno] [2|two] [3|three]', 'stored');
  DS.Free;

  Section('browse-mode: navigation posts or cancels');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.Next;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'Next in a modified edit');
  CheckEq(Snap(DS), 'dsBrowse rec=2/3 - mod=False', 'moved');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'posted');
  DS.Edit;
  ClearLog;
  DS.Prior;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + CancelEditSeq + ',' + ScrollSeq, 'Prior in an unmodified edit');
  DS.Edit;
  Name.AsString := 'one';
  ClearLog;
  DS.Last;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'Last in a modified edit');
  DS.Edit;
  Name.AsString := 'tres';
  ClearLog;
  DS.First;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'First in a modified edit');
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  CheckEqInt(DS.MoveBy(0), 0, 'MoveBy(0)');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq, 'MoveBy(0) posts and does not scroll');
  DS.Edit;
  Name.AsString := 'one';
  ClearLog;
  DS.RecNo := 2;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + ScrollSeq, 'RecNo := in a modified edit');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|tres]', 'all posted');
  DS.Edit;
  Name.AsString := 'dos';
  ClearLog;
  CheckRaises(Calls.SetRecNo9, 'EDatabaseError', 'Record number out of range', 'RecNo out of range in an edit');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq, 'the edit is posted before the range check');
  DS.Insert;
  ClearLog;
  DS.Next;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + CancelInsertSeq + ',' + ScrollSeq, 'Next in an untouched insert');
  CheckEq(Snap(DS), 'dsBrowse rec=3/3 - mod=False', 'moved from the row that was current');
  DS.RecNo := 2;
  DS.Insert;
  Name.AsString := 'ins';
  ClearLog;
  DS.Next;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + PostInsertSeq + ',' + ScrollSeq, 'Next in a modified insert');
  CheckEq(DS.ViewText, '[1|one] [NULL|ins] [2|dos] [3|tres]', 'posted');
  CheckEq(Snap(DS), 'dsBrowse rec=3/4 - mod=False', 'moved from the new row');
  DS.Insert;
  Name.AsString := 'a';
  ClearLog;
  DS.Append;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + PostInsertSeq + ',' + InsertSeq, 'Append in a modified insert');
  DS.Cancel;
  CheckEq(DS.ViewText, '[1|one] [NULL|ins] [NULL|a] [2|dos] [3|tres]', 'the first insert was posted');
  CheckEq(Snap(DS), 'dsBrowse rec=3/5 - mod=False', 'the cancelled Append returns to it');
  DS.Free;

  Section('browse-mode: a failing Post stops the navigation');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10, True);
  DS.AddRow([IntValue(1), StringValue('one')]);
  DS.AddRow([IntValue(2), StringValue('two')]);
  DS.HookEvents;
  DS.Open;
  Calls.DS := DS;
  DS.Edit;
  DS.FieldByName('name').Clear;
  ClearLog;
  CheckRaises(Calls.DoNext, 'EDatabaseError', 'Field ''name'' must have a value', 'Next');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],[deUpdateRecord dsEdit]', 'no scroll events');
  CheckEq(Snap(DS), 'dsEdit rec=1/2 Bof mod=True', 'still editing the same row');
  ClearLog;
  CheckRaises(Calls.DoInsert, 'EDatabaseError', 'Field ''name'' must have a value', 'Insert');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],[deUpdateRecord dsEdit]', 'neither scroll nor insert events');
  CheckRaises(Calls.DoRefresh, 'EDatabaseError', 'Field ''name'' must have a value', 'Refresh');
  CheckEq(StateName(DS.State), 'dsEdit', 'still editing');
  DS.Cancel;
  DS.Free;

  Section('browse-mode: UpdateRecord');
  DS := NewDS(1);
  CheckRaises(Calls.DoUpdateRecord, 'EDatabaseError', 'Dataset not in edit or insert mode', 'UpdateRecord in dsBrowse');
  DS.Edit;
  ClearLog;
  DS.UpdateRecord;
  CheckEq(TakeLog, '[deUpdateRecord dsEdit]', 'UpdateRecord in dsEdit');
  DS.Free;
end;

{ runs Proc with DS.RaiseIn = EventName; the exception class and message must be those of the test driver }
procedure CheckStop(Proc: TTestMethod; const EventName, Name: string);
begin
  Calls.DS.RaiseIn := EventName;
  CheckRaises(Proc, 'Exception', 'stop in ' + EventName, Name);
  Calls.DS.RaiseIn := '';
end;

procedure TestBeforeHandlers;
var
  DS: TTestDataSet;
  Name: TField;
begin
  Section('protocol: a Before handler cancels the operation');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  CheckStop(Calls.DoEdit, 'BeforeEdit', 'BeforeEdit raises');
  CheckEq(TakeLog, 'BeforeEdit', 'nothing after the handler');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'state unchanged');
  CheckStop(Calls.DoInsert, 'BeforeInsert', 'BeforeInsert raises');
  CheckEq(TakeLog, 'BeforeInsert', 'nothing after the handler');
  CheckStop(Calls.DoAppend, 'BeforeScroll', 'BeforeScroll raises in Append');
  CheckEq(TakeLog, 'BeforeInsert,BeforeScroll', 'nothing after the handler');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'state unchanged');
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  CheckStop(Calls.DoPost, 'BeforePost', 'BeforePost raises');
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost', 'nothing after the handler');
  CheckEq(Snap(DS), 'dsEdit rec=1/3 Bof mod=True', 'still editing');
  CheckEq(Name.AsString, 'uno', 'the buffer is kept');
  CheckStop(Calls.DoNext, 'BeforePost', 'BeforePost raises during Next');
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],[deUpdateRecord dsEdit],BeforePost', 'no scroll');
  CheckStop(Calls.DoCancel, 'BeforeCancel', 'BeforeCancel raises');
  CheckEq(TakeLog, 'BeforeCancel', 'nothing after the handler');
  CheckEq(Snap(DS), 'dsEdit rec=1/3 Bof mod=True', 'still editing');
  DS.Cancel;
  ClearLog;
  CheckStop(Calls.DoDelete, 'BeforeDelete', 'BeforeDelete raises');
  CheckEq(TakeLog, 'BeforeDelete', 'nothing after the handler');
  CheckStop(Calls.DoDelete, 'BeforeScroll', 'BeforeScroll raises in Delete');
  CheckEq(TakeLog, 'BeforeDelete,BeforeScroll', 'nothing after the handler');
  CheckEqInt(DS.RecordCount, 3, 'nothing deleted');
  CheckStop(Calls.DoRefresh, 'BeforeRefresh', 'BeforeRefresh raises');
  CheckEq(TakeLog, 'BeforeRefresh', 'nothing reloaded');
  CheckEq(DS.ViewRowIds, '1,2,3', 'the same rows');

  Section('protocol: Abort in a Before handler');
  DS.AbortIn := 'BeforeDelete';
  CheckRaises(Calls.DoDelete, 'EAbort', '', 'Abort in BeforeDelete');
  CheckEq(TakeLog, 'BeforeDelete', 'nothing after the handler');
  CheckEqInt(DS.RecordCount, 3, 'nothing deleted');
  DS.AbortIn := 'BeforeInsert';
  CheckRaises(Calls.DoInsert, 'EAbort', '', 'Abort in BeforeInsert');
  CheckEq(StateName(DS.State), 'dsBrowse', 'state unchanged');
  DS.AbortIn := 'BeforePost';
  DS.Edit;
  CheckRaises(Calls.DoPost, 'EAbort', '', 'Abort in BeforePost');
  CheckEq(StateName(DS.State), 'dsEdit', 'still editing');
  DS.AbortIn := '';
  DS.Cancel;
  DS.Free;
end;

procedure TestAfterHandlers;
var
  DS: TTestDataSet;
  Name: TField;
begin
  Section('protocol: an After handler does not undo the operation');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  CheckStop(Calls.DoEdit, 'AfterEdit', 'AfterEdit raises');
  CheckEq(TakeLog, EditSeq, 'the whole sequence ran');
  CheckEq(StateName(DS.State), 'dsEdit', 'editing');
  Name.AsString := 'uno';
  ClearLog;
  CheckStop(Calls.DoPost, 'AfterPost', 'AfterPost raises');
  CheckEq(TakeLog, PostEditSeq, 'the whole sequence ran');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'posted');
  CheckEq(StateName(DS.State), 'dsBrowse', 'browsing');
  DS.Edit;
  ClearLog;
  CheckStop(Calls.DoCancel, 'AfterCancel', 'AfterCancel raises');
  CheckEq(TakeLog, CancelEditSeq, 'the whole sequence ran');
  CheckEq(StateName(DS.State), 'dsBrowse', 'browsing');
  CheckStop(Calls.DoInsert, 'AfterInsert', 'AfterInsert raises');
  CheckEq(TakeLog, 'BeforeInsert,BeforeScroll,[deUpdateState dsInsert],OnNewRecord,[deDataSetChange dsInsert],AfterInsert',
    'AfterScroll does not fire');
  CheckEq(StateName(DS.State), 'dsInsert', 'inserting');
  CheckStop(Calls.DoCancel, 'AfterCancel', 'AfterCancel raises for an insert');
  CheckEq(TakeLog, CancelEditSeq, 'AfterScroll does not fire');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'cancelled');
  CheckStop(Calls.DoDelete, 'AfterDelete', 'AfterDelete raises');
  CheckEq(TakeLog, 'BeforeDelete,BeforeScroll,ApplyDelete,[deDataSetChange dsBrowse],AfterDelete', 'AfterScroll does not fire');
  CheckEq(DS.ViewText, '[2|two] [3|three]', 'deleted');
  CheckStop(Calls.DoRefresh, 'AfterRefresh', 'AfterRefresh raises');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'refreshed');
  ClearLog;

  Section('protocol: OnNewRecord raises');
  DS.Next;
  ClearLog;
  CheckStop(Calls.DoInsert, 'OnNewRecord', 'Insert');
  CheckEq(TakeLog, 'BeforeInsert,BeforeScroll,[deUpdateState dsInsert],OnNewRecord,[deUpdateState dsBrowse]',
    'the insert is undone; AfterInsert and AfterScroll do not fire');
  CheckEq(Snap(DS), 'dsBrowse rec=2/3 - mod=False', 'back on the row that was current');
  CheckEq(Name.AsString, 'two', 'that row');
  DS.First;
  CheckStop(Calls.DoAppend, 'OnNewRecord', 'Append');
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 Bof mod=False', 'the flags are back');
  DS.Free;
end;

procedure TestDriverErrors;
var
  DS: TTestDataSet;
  Name: TField;
begin
  Section('protocol: driver error without a handler');
  DS := NewDS(3);
  Name := DS.FieldByName('name');
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'apply failed', 'Post');
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate', 'nothing after the apply step');
  CheckEq(Snap(DS), 'dsEdit rec=1/3 Bof mod=True', 'the edit is kept');
  CheckEq(Name.AsString, 'uno', 'with its buffer');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'nothing stored');
  DS.Post;
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'a second Post succeeds');
  DS.Append;
  Name.AsString := 'new';
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'apply failed', 'Post of an insert');
  CheckEq(TakeLog, '[deUpdateRecord dsInsert],BeforePost,ApplyInsert', 'nothing after the apply step');
  CheckEq(Snap(DS), 'dsInsert rec=0/3 - mod=True', 'the insert is kept');
  DS.Cancel;
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoDelete, 'EDatabaseError', 'apply failed', 'Delete');
  CheckEq(TakeLog, 'BeforeDelete,BeforeScroll,ApplyDelete', 'nothing after the apply step');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'nothing deleted');
  DS.FailApply := 1;
  CheckRaises(Calls.DoEdit, 'EDatabaseError', 'apply failed', 'Edit');
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit', 'nothing after the driver step');
  CheckEq(StateName(DS.State), 'dsBrowse', 'not editing');
  DS.Free;

  Section('protocol: daFail');
  DS := NewDS(3);
  DS.HookErrors;
  Name := DS.FieldByName('name');
  DS.ErrorAction := daFail;
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoPost, 'EDatabaseError', 'apply failed', 'Post');
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,OnPostError(apply failed,daFail)', 'the handler is called once');
  CheckEq(StateName(DS.State), 'dsEdit', 'the edit is kept');
  DS.Cancel;
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoDelete, 'EDatabaseError', 'apply failed', 'Delete');
  CheckEq(TakeLog, 'BeforeDelete,BeforeScroll,ApplyDelete,OnDeleteError(apply failed,daFail)', 'the handler is called once');
  DS.FailApply := 1;
  CheckRaises(Calls.DoEdit, 'EDatabaseError', 'apply failed', 'Edit');
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,OnEditError(apply failed,daFail)', 'the handler is called once');

  Section('protocol: daAbort');
  DS.ErrorAction := daAbort;
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoPost, 'EAbort', '', 'Post');
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,OnPostError(apply failed,daFail)', 'event order');
  CheckEq(Snap(DS), 'dsEdit rec=1/3 Bof mod=True', 'the edit is kept');
  DS.Cancel;
  ClearLog;
  DS.FailApply := 1;
  CheckRaises(Calls.DoDelete, 'EAbort', '', 'Delete');
  CheckEqInt(DS.RecordCount, 3, 'nothing deleted');
  DS.FailApply := 1;
  CheckRaises(Calls.DoEdit, 'EAbort', '', 'Edit');
  CheckEq(StateName(DS.State), 'dsBrowse', 'not editing');
  ClearLog;

  Section('protocol: daRetry');
  DS.ErrorAction := daRetry;
  DS.Edit;
  Name.AsString := 'uno';
  ClearLog;
  DS.FailApply := 2;
  DS.Post;
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,OnPostError(apply failed,daFail),ApplyUpdate,' +
    'OnPostError(apply failed,daFail),ApplyUpdate,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost',
    'Post repeats only the apply step');
  CheckEq(DS.ViewText, '[1|uno] [2|two] [3|three]', 'posted');
  DS.Append;
  Name.AsString := 'new';
  ClearLog;
  DS.FailApply := 1;
  DS.Post;
  CheckEq(TakeLog, '[deUpdateRecord dsInsert],BeforePost,ApplyInsert,OnPostError(apply failed,daFail),ApplyInsert,' +
    '[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost', 'Post of an insert');
  CheckEq(DS.ViewRowIds, '1,2,3,4', 'one new row');
  DS.FailApply := 1;
  DS.Delete;
  CheckEq(TakeLog, 'BeforeDelete,BeforeScroll,ApplyDelete,OnDeleteError(apply failed,daFail),ApplyDelete,' +
    '[deDataSetChange dsBrowse],AfterDelete,AfterScroll', 'Delete');
  CheckEq(DS.ViewRowIds, '1,2,3', 'deleted');
  DS.FailApply := 1;
  DS.Edit;
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,OnEditError(apply failed,daFail),DriverEdit,[deUpdateState dsEdit],AfterEdit', 'Edit');
  DS.Cancel;
  DS.Free;
end;

procedure TestRefresh;
var
  DS: TTestDataSet;
begin
  Section('refresh: Refresh');
  DS := NewDS(3);
  DS.Next;
  ClearLog;
  DS.Refresh;
  CheckEq(TakeLog, RefreshSeq, 'Refresh: event order (no open or close events, definitions not rebuilt)');
  CheckEq(Snap(DS), 'dsBrowse rec=2/3 - mod=False', 'the record number is kept');
  CheckEq(DS.ViewRowIds, '4,5,6', 'the rows are new rows');
  CheckEq(DS.StoredRowIds, '4,5,6', 'in the row list too');
  DS.First;
  DS.Refresh;
  CheckEq(Snap(DS), 'dsBrowse rec=1/3 - mod=False', 'Bof is cleared');
  DS.AddRow([IntValue(4), StringValue('four')]);
  DS.Refresh;
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three] [4|four]', 'a row added to the store appears');
  CheckEq(Snap(DS), 'dsBrowse rec=1/4 - mod=False', 'state');
  DS.Last;
  DS.ClearRows;
  DS.AddRow([IntValue(7), StringValue('seven')]);
  DS.AddRow([IntValue(8), StringValue('eight')]);
  DS.Refresh;
  CheckEq(Snap(DS), 'dsBrowse rec=2/2 - mod=False', 'the cursor goes to the last row when the old number is gone');
  CheckEq(DS.FieldByName('name').AsString, 'eight', 'that row');
  DS.ClearRows;
  ClearLog;
  DS.Refresh;
  CheckEq(TakeLog, RefreshSeq, 'event order when the store became empty');
  CheckEq(Snap(DS), 'dsBrowse rec=0/0 Bof+Eof mod=False', 'empty');
  CheckEqInt(DS.CursorIndex, -1, 'no current row');
  DS.AddRow([IntValue(1), StringValue('one')]);
  DS.AddRow([IntValue(2), StringValue('two')]);
  DS.Refresh;
  CheckEq(Snap(DS), 'dsBrowse rec=1/2 - mod=False', 'from empty to the first row');

  Section('refresh: pending edit');
  DS.Edit;
  DS.FieldByName('name').AsString := 'uno';
  ClearLog;
  DS.Refresh;
  CheckEq(TakeLog, '[deCheckBrowseMode dsEdit],' + PostEditSeq + ',' + RefreshSeq, 'a modified edit is posted first');
  CheckEq(DS.LastApply, 'update old=[1|one] new=[1|uno] assigned=2', 'the driver got it');
  CheckEq(DS.ViewText, '[1|one] [2|two]', 'the rows are those the driver loads');
  DS.Insert;
  ClearLog;
  DS.Refresh;
  CheckEq(TakeLog, '[deCheckBrowseMode dsInsert],' + CancelInsertSeq + ',' + RefreshSeq, 'an untouched insert is cancelled first');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  TestOpenClose;
  TestEdit;
  TestInsert;
  TestDelete;
  TestBrowseMode;
  TestBeforeHandlers;
  TestAfterHandlers;
  TestDriverErrors;
  TestRefresh;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
