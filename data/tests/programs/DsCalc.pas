// covers: DAT-007, DAT-045, DAT-046, DAT-047
program DsCalc;
{ Calculated fields: the per-row value cache filled lazily by OnCalcFields, the edit buffer's own values,
  AutoCalcFields, the restrictions while OnCalcFields runs, and reads through the active-row override. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  SCalcOp = 'Operation not allowed while calculating fields';
  PostEditSeq = '[deUpdateRecord dsEdit],BeforePost,ApplyUpdate,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';
  PostInsertSeq = '[deUpdateRecord dsInsert],BeforePost,ApplyInsert,[deUpdateState dsBrowse],[deDataSetChange dsBrowse],AfterPost';

type
  TCalls = class
    DS: TTestDataSet;
    Id, Name, Price, Qty, Total, Tag: TField;
    { what the handler does besides calculating }
    Probing: Boolean;
    Sink: Double;
    Failing: Boolean;
    procedure Calc(DataSet: TDataSet);
    procedure ProbeOp(const OpName: string; Proc: TTestMethod);
    procedure DoNext;
    procedure DoFirst;
    procedure DoEdit;
    procedure DoInsert;
    procedure DoPost;
    procedure DoCancel;
    procedure DoDelete;
    procedure DoRefresh;
    procedure DoClose;
    procedure DoOpen;
    procedure DoLocate;
    procedure DoRecordCount;
    procedure DoSetFilter;
    procedure DoSetFiltered;
    procedure WriteQty;
    procedure WriteTotal;
    procedure WriteName;
    procedure ReadTotal;
  end;

procedure TCalls.DoNext;
begin
  DS.Next;
end;

procedure TCalls.DoFirst;
begin
  DS.First;
end;

procedure TCalls.DoEdit;
begin
  DS.Edit;
end;

procedure TCalls.DoInsert;
begin
  DS.Insert;
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

procedure TCalls.DoClose;
begin
  DS.Close;
end;

procedure TCalls.DoOpen;
begin
  DS.Open;
end;

procedure TCalls.DoLocate;
begin
  DS.Locate('id', ['1'], []);
end;

procedure TCalls.DoRecordCount;
begin
  Sink := DS.RecordCount;
end;

procedure TCalls.DoSetFilter;
begin
  DS.Filter := 'id = 1';
end;

procedure TCalls.DoSetFiltered;
begin
  DS.Filtered := True;
end;

procedure TCalls.WriteQty;
begin
  Qty.AsInteger := 9;
end;

procedure TCalls.WriteTotal;
begin
  Total.AsFloat := 1;
end;

procedure TCalls.WriteName;
begin
  Name.AsString := 'x';
end;

procedure TCalls.ReadTotal;
begin
  Sink := Total.AsFloat;
end;

{ logs 'Op=refused' when the operation raises the calculating error, else what happened }
procedure TCalls.ProbeOp(const OpName: string; Proc: TTestMethod);
begin
  try
    Proc;
    AddLog(OpName + '=done');
  except
    on E: EDatabaseError do
      if E.Message = SCalcOp then
        AddLog(OpName + '=refused')
      else
        AddLog(OpName + '=' + E.Message);
  end;
end;

{ total = price * qty (null when either is null); tag = name#id }
procedure TCalls.Calc(DataSet: TDataSet);
begin
  if Total.IsNull and Tag.IsNull then
    AddLog('OnCalcFields(' + Id.AsString + ')')
  else
    AddLog('OnCalcFields(' + Id.AsString + ') not null');
  if Failing then
    raise Exception.Create('calc failed');
  if Probing then
  begin
    ProbeOp('Next', DoNext);
    ProbeOp('First', DoFirst);
    ProbeOp('Edit', DoEdit);
    ProbeOp('Insert', DoInsert);
    ProbeOp('Post', DoPost);
    ProbeOp('Cancel', DoCancel);
    ProbeOp('Delete', DoDelete);
    ProbeOp('Refresh', DoRefresh);
    ProbeOp('Close', DoClose);
    ProbeOp('Open', DoOpen);
    ProbeOp('Locate', DoLocate);
    ProbeOp('RecordCount', DoRecordCount);
    ProbeOp('Filter', DoSetFilter);
    ProbeOp('Filtered', DoSetFiltered);
    ProbeOp('data field', WriteQty);
    AddLog('state=' + StateName(DataSet.State));
  end;
  if not Price.IsNull and not Qty.IsNull then
    Total.AsFloat := Price.AsFloat * Qty.AsInteger;
  Tag.AsString := Name.AsString + '#' + Id.AsString;
end;

var
  Calls: TCalls;

function AddField(DS: TDataSet; FieldClass: TFieldClass; const FieldName: string; Kind: TFieldKind): TField;
begin
  Result := FieldClass.Create(DS);
  Result.FieldName := FieldName;
  Result.FieldKind := Kind;
  Result.DataSet := DS;
end;

{ id, name, price, qty + calculated total, tag; rows 1 one 1.5 2 / 2 two 10 NULL / 3 three 0.5 4; not opened }
function NewDS(WithHandler: Boolean): TTestDataSet;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddDef('price', ftFloat);
  Result.AddDef('qty', ftInteger);
  Result.AddRow([IntValue(1), StringValue('one'), FloatValue(1.5), IntValue(2)]);
  Result.AddRow([IntValue(2), StringValue('two'), FloatValue(10), NullValue]);
  Result.AddRow([IntValue(3), StringValue('three'), FloatValue(0.5), IntValue(4)]);
  Calls.DS := Result;
  Calls.Id := AddField(Result, TIntegerField, 'id', fkData);
  Calls.Name := AddField(Result, TStringField, 'name', fkData);
  Calls.Total := AddField(Result, TFloatField, 'total', fkCalculated);
  Calls.Price := AddField(Result, TFloatField, 'price', fkData);
  Calls.Qty := AddField(Result, TIntegerField, 'qty', fkData);
  Calls.Tag := AddField(Result, TStringField, 'tag', fkCalculated);
  Calls.Probing := False;
  Calls.Failing := False;
  if WithHandler then
    Result.OnCalcFields := Calls.Calc;
  Result.HookEvents;
end;

function OpenDS: TTestDataSet;
begin
  Result := NewDS(True);
  Result.Open;
  ClearLog;
end;

procedure TestLazy;
var
  DS: TTestDataSet;
begin
  Section('stored rows: lazy calculation');
  DS := NewDS(True);
  CheckEqBool(DS.AutoCalcFields, True, 'AutoCalcFields defaults to True');
  DS.Open;
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll',
    'Open calculates nothing');
  CheckEqInt(Calls.Total.FieldNo, -1, 'a calculated field has FieldNo -1');
  CheckEqBool(Calls.Total.CanModify, False, 'and cannot be modified');
  CheckEq(Calls.Name.AsString, 'one', 'reading a data field calculates nothing');
  CheckEq(TakeLog, '', 'no call yet');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '3', 'the first read of a calculated field');
  CheckEq(TakeLog, 'OnCalcFields(1)', 'calls OnCalcFields; the values are null before the handler runs');
  CheckEq(Calls.Tag.AsString, 'one#1', 'the handler fills every calculated field');
  CheckEq(Calls.Total.DisplayText, '3', 'DisplayText of a calculated field');
  CheckEq(TakeLog, '', 'the values are cached');
  DS.Next;
  ClearLog;
  CheckEqBool(Calls.Total.IsNull, True, 'a value the handler does not assign stays null');
  CheckEq(Calls.Tag.AsString, 'two#2', 'row 2');
  CheckEq(TakeLog, 'OnCalcFields(2)', 'one call per row');
  DS.First;
  ClearLog;
  CheckEq(Calls.Tag.AsString, 'one#1', 'back on row 1');
  CheckEq(TakeLog, '', 'the cache of a row survives navigation');
  DS.Refresh;
  ClearLog;
  CheckEq(Calls.Tag.AsString, 'one#1', 'after Refresh');
  CheckEq(TakeLog, 'OnCalcFields(1)', 'the reloaded row is calculated again');
  CheckEqBool(DS.Locate('tag', ['three#3'], []), True, 'Locate on a calculated field');
  CheckEq(TakeLog, 'OnCalcFields(2),OnCalcFields(3),BeforeScroll,[deDataSetScroll dsBrowse],AfterScroll',
    'calculates the rows it scans');
  DS.Free;

  Section('no handler');
  DS := NewDS(False);
  DS.Open;
  CheckEqBool(Calls.Total.IsNull, True, 'the values stay null');
  CheckEq(Calls.Tag.AsString, '', 'and read as defaults');
  DS.Edit;
  Calls.Qty.AsInteger := 7;
  DS.Post;
  CheckEqBool(Calls.Total.IsNull, True, 'also after an edit');
  DS.Free;
end;

procedure TestWrites;
var
  DS: TTestDataSet;
begin
  Section('writes');
  DS := OpenDS;
  CheckRaises(Calls.WriteTotal, 'EDatabaseError', 'Field ''total'' cannot be modified', 'outside OnCalcFields: dsBrowse');
  DS.Edit;
  CheckRaises(Calls.WriteTotal, 'EDatabaseError', 'Field ''total'' cannot be modified', 'outside OnCalcFields: dsEdit');
  DS.Cancel;
  DS.Close;
  CheckRaises(Calls.WriteTotal, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'closed dataset');
  DS.Free;
end;

procedure TestEditBuffer;
var
  DS: TTestDataSet;
begin
  Section('edit buffer: AutoCalcFields');
  DS := OpenDS;
  CheckEq(FloatToStr(Calls.Total.AsFloat), '3', 'the stored row');
  ClearLog;
  DS.Edit;
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,OnCalcFields(1),[deUpdateState dsEdit],AfterEdit',
    'entering dsEdit calculates the buffer');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '3', 'the buffer has its own values');
  CheckEq(TakeLog, '', 'already calculated');
  Calls.Qty.AsInteger := 10;
  CheckEq(TakeLog, 'OnCalcFields(1),[deFieldChange dsEdit qty]', 'a field write recalculates before it notifies');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '15', 'the new value');
  Calls.Name.AsString := 'uno';
  CheckEq(TakeLog, 'OnCalcFields(1),[deFieldChange dsEdit name]', 'every write recalculates');
  CheckEq(Calls.Tag.AsString, 'uno#1', 'the other calculated field');
  DS.Cancel;
  ClearLog;
  CheckEq(FloatToStr(Calls.Total.AsFloat), '3', 'Cancel: the stored row still has its values');
  CheckEq(TakeLog, '', 'without a new call');
  DS.Edit;
  Calls.Qty.AsInteger := 10;
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostEditSeq, 'Post calculates nothing itself');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '15', 'the posted row');
  CheckEq(TakeLog, 'OnCalcFields(1)', 'is calculated again at the next read');
  Calls.ReadTotal;
  CheckEq(TakeLog, '', 'and cached');

  Section('edit buffer: AutoCalcFields = False');
  DS.AutoCalcFields := False;
  DS.Edit;
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,OnCalcFields(1),[deUpdateState dsEdit],AfterEdit',
    'entering dsEdit still calculates');
  Calls.Qty.AsInteger := 4;
  CheckEq(TakeLog, '[deFieldChange dsEdit qty]', 'a field write does not recalculate');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '15', 'the value is the one from entering the state');
  DS.Post;
  ClearLog;
  CheckEq(FloatToStr(Calls.Total.AsFloat), '6', 'after Post the row is calculated');
  CheckEq(TakeLog, 'OnCalcFields(1)', 'by one call');
  DS.AutoCalcFields := True;

  Section('insert');
  DS.Last;
  ClearLog;
  DS.Append;
  CheckEq(TakeLog, 'BeforeInsert,BeforeScroll,OnCalcFields(),[deUpdateState dsInsert],OnNewRecord,[deDataSetChange dsInsert],AfterInsert,AfterScroll',
    'entering dsInsert calculates the empty buffer');
  CheckEq(Calls.Tag.AsString, '#', 'calculated from nulls');
  Calls.Id.AsInteger := 4;
  Calls.Name.AsString := 'four';
  Calls.Price.AsFloat := 2;
  Calls.Qty.AsInteger := 3;
  CheckEq(Calls.Tag.AsString, 'four#4', 'recalculated after the writes');
  CheckEq(FloatToStr(Calls.Total.AsFloat), '6', 'total of the new row');
  ClearLog;
  DS.Post;
  CheckEq(TakeLog, PostInsertSeq, 'Post of an insert');
  CheckEq(Calls.Tag.AsString, 'four#4', 'the new stored row');
  CheckEq(TakeLog, 'OnCalcFields(4)', 'is calculated at the first read');
  DS.Prior;
  ClearLog;
  CheckEq(Calls.Tag.AsString, 'three#3', 'a row that was never read');
  CheckEq(TakeLog, 'OnCalcFields(3)', 'is calculated when it is read');
  DS.Free;
end;

procedure TestRestrictions;
var
  DS: TTestDataSet;
begin
  Section('restrictions while OnCalcFields runs');
  DS := OpenDS;
  Calls.Probing := True;
  CheckEq(FloatToStr(Calls.Total.AsFloat), '3', 'the value is calculated');
  CheckEq(TakeLog, 'OnCalcFields(1),Next=refused,First=refused,Edit=refused,Insert=refused,Post=refused,Cancel=refused,' +
    'Delete=refused,Refresh=refused,Close=refused,Open=refused,Locate=refused,RecordCount=refused,Filter=refused,' +
    'Filtered=refused,data field=refused,state=dsBrowse', 'every dataset method and data-field write is refused');
  Calls.Probing := False;
  CheckEq(Calls.Tag.AsString, 'one#1', 'the calculated fields were assigned');
  DS.Next;
  CheckEqInt(DS.RecNo, 2, 'afterwards the dataset works as before');

  Section('a failing handler');
  Calls.Failing := True;
  ClearLog;
  CheckRaises(Calls.ReadTotal, 'Exception', 'calc failed', 'the exception reaches the reader');
  CheckRaises(Calls.ReadTotal, 'Exception', 'calc failed', 'the row is not marked as calculated');
  CheckEq(TakeLog, 'OnCalcFields(2),OnCalcFields(2)', 'the handler ran twice');
  Calls.Failing := False;
  CheckEq(Calls.Tag.AsString, 'two#2', 'a later read calculates');
  DS.Edit;
  CheckEqInt(DS.RecNo, 2, 'the dataset is not left in the calculating mode');
  DS.Cancel;
  DS.Free;
end;

procedure TestOverride;
var
  DS: TTestDataSet;
begin
  Section('active-row override');
  DS := OpenDS;
  DS.OverrideRow(2);
  CheckEq(Calls.Name.AsString, 'three', 'data fields read the override row');
  CheckEq(TakeLog, '', 'no events');
  CheckEq(Calls.Tag.AsString, 'three#3', 'calculated fields too');
  CheckEq(TakeLog, 'OnCalcFields(3)', 'the override row is calculated lazily');
  CheckEqInt(DS.RecNo, 1, 'the cursor does not move');
  DS.OverrideRow(-1);
  CheckEq(Calls.Name.AsString, 'one', 'without the override: the current row');
  DS.Edit;
  Calls.Name.AsString := 'uno';
  DS.OverrideRow(1);
  CheckEq(Calls.Name.AsString, 'two', 'in dsEdit the override row is read, not the buffer');
  CheckRaises(Calls.WriteName, 'EDatabaseError', 'Dataset not in edit or insert mode', 'writes are refused while an override is set');
  DS.OverrideRow(0);
  CheckEq(Calls.Name.AsString, 'one', 'the stored row under the buffer');
  DS.OverrideRow(-1);
  CheckEq(Calls.Name.AsString, 'uno', 'the buffer again');
  Calls.Name.AsString := 'eins';
  DS.Post;
  CheckEq(DS.ViewText, '[1|eins|1.5|2] [2|two|10|NULL] [3|three|0.5|4]', 'the edit continues after the override');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  TestLazy;
  TestWrites;
  TestEditBuffer;
  TestRestrictions;
  TestOverride;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
