// covers: DAT-300, DAT-301, DAT-302, DAT-303, DAT-304, DAT-305, DAT-306, DAT-307
program MemBasic;
{ The in-memory table: creating and opening, rows surviving Close, the driver hooks, ReadOnly, the
  auto-increment counters, EmptyDataSet and CopyDataSet. }
uses
  SysUtils, Classes, DB, MemData, DataTestUtils, TestDataSet, Conformance;

type
  TCalls = class
  public
    T: TMemTable;
    Source: TDataSet;
    { what OnNewRecord stores in the id field; 0 = nothing }
    NewId: Integer;
    procedure Open;
    procedure CreateDataSet;
    procedure AddDef;
    procedure ClearDefs;
    procedure Edit;
    procedure Append;
    procedure Delete;
    procedure WriteId;
    procedure LongName;
    procedure Empty;
    procedure Copy;
    procedure HandleNewRecord(DataSet: TDataSet);
    procedure HandleCalc(DataSet: TDataSet);
  end;

var
  Calls: TCalls;

procedure TCalls.Open;
begin
  T.Open;
end;

procedure TCalls.CreateDataSet;
begin
  T.CreateDataSet;
end;

procedure TCalls.AddDef;
begin
  T.FieldDefs.Add('extra', ftInteger);
end;

procedure TCalls.ClearDefs;
begin
  T.FieldDefs.Clear;
end;

procedure TCalls.Edit;
begin
  T.Edit;
end;

procedure TCalls.Append;
begin
  T.Append;
end;

procedure TCalls.Delete;
begin
  T.Delete;
end;

procedure TCalls.WriteId;
begin
  T.FieldByName('id').AsInteger := 77;
end;

procedure TCalls.LongName;
begin
  T.FieldByName('name').AsString := 'more than ten';
end;

procedure TCalls.Empty;
begin
  T.EmptyDataSet;
end;

procedure TCalls.Copy;
begin
  T.CopyDataSet(Source);
end;

procedure TCalls.HandleNewRecord(DataSet: TDataSet);
begin
  if NewId <> 0 then
    DataSet.FieldByName('id').AsInteger := NewId;
end;

procedure TCalls.HandleCalc(DataSet: TDataSet);
begin
  DataSet.FieldByName('twice').AsInteger := DataSet.FieldByName('qty').AsInteger * 2;
end;

{ id (auto-increment), name (string 10, required), qty (integer); not opened }
function NewTable: TMemTable;
begin
  Result := TMemTable.Create(nil);
  Result.FieldDefs.Add('id', ftAutoInc);
  Result.FieldDefs.Add('name', ftString, 10, True);
  Result.FieldDefs.Add('qty', ftInteger);
end;

procedure Add(T: TDataSet; const Name: string; Qty: Integer);
begin
  T.Append;
  T.FieldByName('name').AsString := Name;
  T.FieldByName('qty').AsInteger := Qty;
  T.Post;
end;

{ the display texts of every row of the view as '1:one:10 2:two:NULL'; the cursor returns to its row }
function Rows(T: TDataSet): string;
var
  I, Keep: Integer;
  J: Integer;
  F: TField;
begin
  Result := '';
  Keep := T.RecNo;
  T.DisableControls;
  for I := 1 to T.RecordCount do
  begin
    T.RecNo := I;
    if I > 1 then
      Result := Result + ' ';
    for J := 0 to T.FieldCount - 1 do
    begin
      F := T.Fields[J];
      if J > 0 then
        Result := Result + ':';
      if F.IsNull then
        Result := Result + 'NULL'
      else
        Result := Result + F.DisplayText;
    end;
  end;
  if Keep >= 1 then
    T.RecNo := Keep;
  T.EnableControls;
end;

{ the definitions as 'id:ftAutoInc:0:False name:ftString:10:True' }
function Defs(T: TDataSet): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to T.FieldDefs.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + T.FieldDefs[I].Name + ':' + FieldTypeName(T.FieldDefs[I].DataType) + ':' +
      IntToStr(T.FieldDefs[I].Size) + ':' + BoolStr(T.FieldDefs[I].Required);
  end;
end;

procedure TestOpenCreate;
var
  T: TMemTable;
  Rec: TEventRecorder;
begin
  Section('open and create');
  T := TMemTable.Create(nil);
  Calls.T := T;
  Check(not T.Active and not T.ReadOnly, 'a new table is closed and not read-only');
  CheckRaises(Calls.Open, 'EDatabaseError', 'Cannot open a table without field definitions', 'Open without definitions');
  CheckRaises(Calls.CreateDataSet, 'EDatabaseError', 'Cannot open a table without field definitions',
    'CreateDataSet without definitions');
  Check(not T.Active, 'the table stays closed');
  T.Free;

  T := NewTable;
  Calls.T := T;
  Rec := TEventRecorder.Create(T);
  T.CreateDataSet;
  CheckEq(Rec.Take, 'BeforeOpen,[deUpdateState],AfterOpen,AfterScroll', 'events of CreateDataSet on a closed table');
  Check(T.Active and T.IsEmpty and T.CanModify, 'the created table is open, empty and modifiable');
  CheckEqInt(T.FieldCount, 3, 'one field per definition');
  Check(T.FieldByName('id') is TAutoIncField, 'the class of the auto-increment field');
  Add(T, 'one', 10);
  Add(T, 'two', 20);
  Add(T, 'three', 30);
  CheckEq(Rows(T), '1:one:10 2:two:20 3:three:30', 'three rows');
  Rec.Clear;
  T.Open;
  CheckEq(Rec.Take, '', 'Open on an open table does nothing');

  Section('rows survive Close');
  T.RecNo := 2;
  Rec.Clear;
  T.Close;
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose', 'events of Close');
  Check(not T.Active, 'closed');
  CheckEqInt(T.FieldCount, 0, 'the dynamic fields are gone');
  T.Open;
  CheckEq(Rec.Take, 'BeforeOpen,[deUpdateState],AfterOpen,AfterScroll', 'events of Open');
  CheckEq(Rows(T), '1:one:10 2:two:20 3:three:30', 'the rows after Close and Open');
  CheckEqInt(T.RecNo, 1, 'a reopened table is on its first row');
  Add(T, 'four', 40);
  CheckEqInt(T.FieldByName('id').AsInteger, 4, 'the auto-increment counter survives Close');
  T.Edit;
  T.FieldByName('name').AsString := 'pending';
  T.Close;
  T.Open;
  CheckEq(Rows(T), '1:one:10 2:two:20 3:three:30 4:four:40', 'Close discards a pending edit only');
  T.Filter := 'qty > 15';
  T.Filtered := True;
  CheckEqInt(T.RecordCount, 3, 'the filtered view');
  T.Close;
  T.Filtered := False;
  T.Open;
  CheckEqInt(T.RecordCount, 4, 'rows hidden by a filter survive Close');
  T.Active := False;
  T.Active := True;
  CheckEqInt(T.RecordCount, 4, 'Active := False and True keeps the rows');

  Section('CreateDataSet on an open table');
  Rec.Clear;
  T.CreateDataSet;
  CheckEq(Rec.Take, 'BeforeClose,[deUpdateState],AfterClose,BeforeOpen,[deUpdateState],AfterOpen,AfterScroll',
    'events of CreateDataSet on an open table');
  Check(T.Active and T.IsEmpty, 'open and empty');
  Add(T, 'new', 1);
  CheckEqInt(T.FieldByName('id').AsInteger, 1, 'CreateDataSet resets the auto-increment counter');

  Section('field definitions');
  CheckRaises(Calls.AddDef, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'FieldDefs.Add on an open table');
  CheckRaises(Calls.ClearDefs, 'EDatabaseError', 'Cannot perform this operation on an open dataset',
    'FieldDefs.Clear on an open table');
  CheckEqInt(T.RecordCount, 1, 'the row is still there');
  T.Close;
  T.FieldDefs.Add('extra', ftInteger);
  T.Open;
  CheckEqInt(T.RecordCount, 0, 'adding a definition discards the rows');
  CheckEqInt(T.FieldCount, 4, 'the new definition has a field');
  Add(T, 'a', 1);
  CheckEqInt(T.FieldByName('id').AsInteger, 1, 'and resets the auto-increment counter');
  T.Close;
  T.FieldDefs.Clear;
  T.FieldDefs.Add('n', ftInteger);
  T.Open;
  CheckEq(Defs(T) + ' / ' + IntToStr(T.RecordCount), 'n:ftInteger:0:False / 0', 'new definitions, no rows');
  Rec.Free;
  T.Free;
end;

procedure TestPersistentFields;
var
  T: TMemTable;
  Code: TStringField;
  N, Twice: TIntegerField;
begin
  Section('definitions from persistent fields');
  T := TMemTable.Create(nil);
  Calls.T := T;
  Code := TStringField.Create(T);
  Code.FieldName := 'code';
  Code.Size := 4;
  Code.Required := True;
  Code.DataSet := T;
  Twice := TIntegerField.Create(T);
  Twice.FieldName := 'twice';
  Twice.FieldKind := fkCalculated;
  Twice.DataSet := T;
  N := TIntegerField.Create(T);
  N.FieldName := 'qty';
  N.DataSet := T;
  T.OnCalcFields := Calls.HandleCalc;
  T.Open;
  CheckEq(Defs(T), 'code:ftString:4:True qty:ftInteger:0:False', 'the definitions of the data fields');
  T.Append;
  Code.AsString := 'ab';
  N.AsInteger := 21;
  T.Post;
  CheckEq(Rows(T), 'ab:42:21', 'a row through the persistent fields');
  T.Close;
  T.Open;
  CheckEq(Rows(T), 'ab:42:21', 'the row after Close and Open');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  Twice := TIntegerField.Create(T);
  Twice.FieldName := 'twice';
  Twice.FieldKind := fkCalculated;
  Twice.DataSet := T;
  CheckRaises(Calls.Open, 'EDatabaseError', 'Cannot open a table without field definitions',
    'a calculated field alone gives no definition');
  T.Free;
end;

procedure TestDriver;
var
  T: TMemTable;
  Rec: TEventRecorder;
begin
  Section('driver hooks');
  T := NewTable;
  Calls.T := T;
  T.CreateDataSet;
  Add(T, 'one', 10);
  Add(T, 'three', 30);
  T.Insert;
  T.FieldByName('name').AsString := 'two';
  T.Post;
  CheckEq(Rows(T), '1:one:10 3:two:NULL 2:three:30', 'Insert stores the row before the current row');
  CheckEqInt(T.RecNo, 2, 'the inserted row is current');
  T.Edit;
  T.FieldByName('qty').AsInteger := 20;
  T.Post;
  T.First;
  T.Delete;
  CheckEq(Rows(T), '3:two:20 2:three:30', 'update and delete');
  T.Close;
  T.Open;
  CheckEq(Rows(T), '3:two:20 2:three:30', 'the row order survives Close');
  T.Edit;
  CheckRaises(Calls.LongName, 'EDatabaseError', 'Value too long for field ''name'' (maximum 10 characters)',
    'a string longer than Size');
  T.Cancel;

  Section('Refresh');
  T.Last;
  Rec := TEventRecorder.Create(T);
  T.Refresh;
  CheckEq(Rec.Take, 'BeforeRefresh,[deDataSetChange],AfterRefresh,AfterScroll', 'events of Refresh');
  Rec.Free;
  CheckEq(Rows(T), '3:two:20 2:three:30', 'Refresh keeps the rows');
  CheckEqInt(T.RecNo, 2, 'Refresh keeps the record number');
  Add(T, 'four', 40);
  CheckEqInt(T.FieldByName('id').AsInteger, 4, 'Refresh keeps the auto-increment counter');

  Section('ReadOnly');
  T.First;
  T.ReadOnly := True;
  Check(T.ReadOnly and not T.CanModify, 'CanModify of a read-only table');
  Check(not T.FieldByName('name').CanModify, 'CanModify of its fields');
  CheckRaises(Calls.Edit, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Edit on a read-only table');
  CheckRaises(Calls.Append, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Append on a read-only table');
  CheckRaises(Calls.Delete, 'EDatabaseError', 'Cannot modify a read-only dataset', 'Delete on a read-only table');
  CheckEq(Rows(T), '3:two:20 2:three:30 4:four:40', 'nothing changed');
  T.Next;
  CheckEqInt(T.RecNo, 2, 'a read-only table can be navigated');
  T.Close;
  T.Open;
  CheckEqInt(T.RecordCount, 3, 'a read-only table can be closed and opened');
  T.ReadOnly := False;
  Check(T.CanModify, 'CanModify after ReadOnly := False');
  T.Edit;
  T.FieldByName('qty').AsInteger := 21;
  T.Post;
  CheckEqInt(T.FieldByName('qty').AsInteger, 21, 'and the table can be edited again');
  T.Free;
end;

procedure TestAutoInc;
var
  T: TMemTable;
begin
  Section('auto-increment');
  T := NewTable;
  Calls.T := T;
  T.OnNewRecord := Calls.HandleNewRecord;
  T.CreateDataSet;
  Add(T, 'a', 0);
  Add(T, 'b', 0);
  Add(T, 'c', 0);
  CheckEq(Rows(T), '1:a:0 2:b:0 3:c:0', 'the counter starts at 1');
  T.Delete;
  Add(T, 'd', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 4, 'a deleted value is not used again');
  T.Append;
  Check(T.FieldByName('id').IsNull, 'the id is null until Post');
  T.FieldByName('name').AsString := 'x';
  T.Cancel;
  Add(T, 'e', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 5, 'a cancelled insert uses no value');
  T.Edit;
  CheckRaises(Calls.WriteId, 'EDatabaseError', 'Field ''id'' cannot be modified', 'the id cannot be written in an edit');
  T.FieldByName('name').AsString := 'e2';
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 5, 'Post of an edit keeps the id');
  Calls.NewId := 10;
  Add(T, 'f', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 10, 'a value assigned in OnNewRecord is kept');
  Calls.NewId := 0;
  Add(T, 'g', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 11, 'the counter continues after the assigned value');
  Calls.NewId := 7;
  Add(T, 'h', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 7, 'a smaller assigned value is kept too');
  Calls.NewId := 0;
  Add(T, 'i', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 12, 'and does not move the counter back');
  T.EmptyDataSet;
  Add(T, 'j', 0);
  CheckEqInt(T.FieldByName('id').AsInteger, 1, 'EmptyDataSet resets the counter');
  T.Free;

  T := TMemTable.Create(nil);
  Calls.T := T;
  T.FieldDefs.Add('id', ftAutoInc);
  T.FieldDefs.Add('name', ftString, 10);
  T.FieldDefs.Add('seq', ftAutoInc);
  T.OnNewRecord := Calls.HandleNewRecord;
  T.CreateDataSet;
  Calls.NewId := 5;
  T.Append;
  T.Post;
  Calls.NewId := 0;
  T.Append;
  T.Post;
  CheckEq(Rows(T), '5:NULL:1 6:NULL:2', 'every auto-increment field has its own counter');
  T.Free;
end;

procedure TestEmpty;
var
  T: TMemTable;
  Rec: TEventRecorder;
begin
  Section('EmptyDataSet');
  T := NewTable;
  Calls.T := T;
  CheckRaises(Calls.Empty, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'EmptyDataSet on a closed table');
  T.CreateDataSet;
  Add(T, 'one', 10);
  Add(T, 'two', 20);
  Rec := TEventRecorder.Create(T);
  T.EmptyDataSet;
  CheckEq(Rec.Take, '[deDataSetChange],AfterScroll', 'events of EmptyDataSet');
  Check(T.Active and T.IsEmpty and T.Bof and T.Eof, 'open and empty');
  CheckEq(StateName(T.State), 'dsBrowse', 'State');
  T.EmptyDataSet;
  CheckEq(Rec.Take, '[deDataSetChange],AfterScroll', 'events of EmptyDataSet on an empty table');
  Add(T, 'one', 10);
  Add(T, 'two', 20);
  T.Edit;
  T.FieldByName('qty').AsInteger := 99;
  Rec.Clear;
  T.EmptyDataSet;
  CheckEq(Rec.Take, 'BeforeCancel,[deUpdateState],[deDataSetChange],AfterCancel,[deDataSetChange],AfterScroll',
    'a pending edit is cancelled first');
  CheckEq(StateName(T.State) + ' ' + IntToStr(T.RecordCount), 'dsBrowse 0', 'State and RecordCount');
  Add(T, 'one', 10);
  T.Append;
  Rec.Clear;
  T.EmptyDataSet;
  CheckEq(Rec.Take,
    'BeforeCancel,[deUpdateState],[deDataSetChange],AfterCancel,AfterScroll,[deDataSetChange],AfterScroll',
    'a pending insert is cancelled first');
  CheckEqInt(T.RecordCount, 0, 'RecordCount after the cancelled insert');
  Rec.Free;
  Add(T, 'one', 10);
  Add(T, 'two', 20);
  T.Filter := 'qty > 15';
  T.Filtered := True;
  T.EmptyDataSet;
  T.Filtered := False;
  CheckEqInt(T.RecordCount, 0, 'the rows hidden by a filter are removed too');
  T.Close;
  T.Open;
  CheckEqInt(T.RecordCount, 0, 'still empty after Close and Open');
  Add(T, 'one', 10);
  T.ReadOnly := True;
  T.EmptyDataSet;
  CheckEqInt(T.RecordCount, 0, 'a read-only table can be emptied');
  T.Free;
end;

procedure TestCopy;
var
  S, T: TMemTable;
  D: TTestDataSet;
  Twice: TIntegerField;
  F: TField;
  Bytes: TBytes;
  SRec, TRec: TEventRecorder;
begin
  Section('CopyDataSet');
  S := NewTable;
  S.FieldDefs.Add('price', ftFloat);
  S.FieldDefs.Add('day', ftDate);
  S.FieldDefs.Add('data', ftBlob);
  T := TMemTable.Create(nil);
  Calls.T := T;
  Calls.Source := S;
  CheckRaises(Calls.Copy, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'CopyDataSet from a closed dataset');
  Check(not T.Active, 'the table is untouched');
  S.CreateDataSet;
  Add(S, 'one', 10);
  Add(S, 'two', 20);
  Add(S, 'three', 30);
  S.First;
  S.Delete;
  S.Edit;
  S.FieldByName('price').AsFloat := 0.1;
  S.FieldByName('day').AsDateTime := EncodeDate(2024, 2, 29);
  SetLength(Bytes, 3);
  Bytes[0] := 1;
  Bytes[1] := 2;
  Bytes[2] := 255;
  S.FieldByName('data').AsBytes := Bytes;
  S.Post;
  S.Last;
  SRec := TEventRecorder.Create(S);
  TRec := TEventRecorder.Create(T);
  T.CopyDataSet(S);
  CheckEq(TRec.Take, 'BeforeOpen,[deUpdateState],AfterOpen,AfterScroll,[deDataSetChange]',
    'events of CopyDataSet into a closed table');
  CheckEq(SRec.Take, '', 'the source fires no event');
  CheckEq(Defs(T),
    'id:ftInteger:0:False name:ftString:10:True qty:ftInteger:0:False price:ftFloat:0:False day:ftDate:0:False ' +
    'data:ftBlob:0:False', 'the definitions; the auto-increment field becomes an integer field');
  CheckEqInt(T.RecordCount, 2, 'RecordCount');
  CheckEq(IntToStr(T.RecNo) + ' ' + Flags(T), '1 Bof', 'the cursor is on the first row');
  CheckEq(IntToStr(S.RecNo) + ' ' + Flags(S), '2 Eof', 'the cursor of the source did not move');
  Check((T.FieldByName('id').AsInteger = 2) and (T.FieldByName('name').AsString = 'two') and
    (T.FieldByName('qty').AsInteger = 20) and (T.FieldByName('price').AsFloat = 0.1) and
    (T.FieldByName('day').AsDateTime = EncodeDate(2024, 2, 29)), 'the values of the first row');
  CheckEq(IntToStr(Length(T.FieldByName('data').AsBytes)) + ' ' + IntToStr(T.FieldByName('data').AsBytes[2]),
    '3 255', 'the bytes of the first row');
  T.Next;
  Check((T.FieldByName('id').AsInteger = 3) and (T.FieldByName('name').AsString = 'three') and
    T.FieldByName('price').IsNull and T.FieldByName('day').IsNull and T.FieldByName('data').IsNull,
    'the second row, with its null values');
  Add(T, 'more', 1);
  Check(T.FieldByName('id').IsNull, 'the copy has no auto-increment counter');
  T.Edit;
  T.FieldByName('id').AsInteger := 77;
  T.Post;
  CheckEqInt(T.FieldByName('id').AsInteger, 77, 'the id of the copy can be written');
  CheckEqInt(S.RecordCount, 2, 'the source is unchanged');

  S.Filter := 'qty > 25';
  S.Filtered := True;
  S.Edit;
  S.FieldByName('name').AsString := 'pending';
  SRec.Clear;
  TRec.Clear;
  T.CopyDataSet(S);
  CheckEq(TRec.Take,
    'BeforeClose,[deUpdateState],AfterClose,BeforeOpen,[deUpdateState],AfterOpen,AfterScroll,[deDataSetChange]',
    'events of CopyDataSet into an open table');
  CheckEq(SRec.Take, '', 'the source fires no event again');
  CheckEq(Rows(T), '3:three:30:NULL:NULL:NULL', 'the rows of the filtered view, as stored');
  CheckEq(StateName(S.State) + ' ' + S.FieldByName('name').AsString, 'dsEdit pending',
    'the pending edit of the source is untouched');
  S.Cancel;
  S.Filtered := False;
  SRec.Free;
  TRec.Free;

  T.Close;
  T.ReadOnly := True;
  T.Filter := 'qty < 25';
  T.Filtered := True;
  T.CopyDataSet(S);
  CheckEq(Rows(T), '2:two:20:0.1:2024-02-29:(Blob)', 'a read-only, filtered table shows the copied rows that pass');
  T.Filtered := False;
  CheckEqInt(T.RecordCount, 2, 'and holds them all');
  T.ReadOnly := False;
  T.Filter := '';

  T.CopyDataSet(T);
  CheckEq(Rows(T), '2:two:20:0.1:2024-02-29:(Blob) 3:three:30:NULL:NULL:NULL', 'a table copied from itself');
  S.Free;

  { any dataset can be the source: only its data fields are copied }
  D := TTestDataSet.Create(nil);
  D.AddDef('qty', ftInteger);
  D.AddDef('label', ftString, 5, True);
  D.AddDef('big', ftLargeint);
  D.AddRow([IntValue(1), StringValue('a'), IntValue(5000000000)]);
  D.AddRow([IntValue(2), StringValue('b'), NullValue]);
  F := TIntegerField.Create(D);
  F.FieldName := 'qty';
  F.DataSet := D;
  Twice := TIntegerField.Create(D);
  Twice.FieldName := 'twice';
  Twice.FieldKind := fkCalculated;
  Twice.DataSet := D;
  F := TLargeintField.Create(D);
  F.FieldName := 'big';
  F.DataSet := D;
  D.OnCalcFields := Calls.HandleCalc;
  D.Open;
  Calls.Source := D;
  T.CopyDataSet(D);
  CheckEq(Defs(T), 'qty:ftInteger:0:False big:ftLargeint:0:False', 'one definition per data field of the source');
  CheckEq(Rows(T), '1:5000000000 2:NULL', 'the rows of another dataset class');
  D.Free;
  T.Free;
end;

begin
  Calls := TCalls.Create;
  TestOpenCreate;
  TestPersistentFields;
  TestDriver;
  TestAutoInc;
  TestEmpty;
  TestCopy;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
