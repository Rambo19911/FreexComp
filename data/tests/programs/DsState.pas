// covers: DAT-001, DAT-002, DAT-003, DAT-004, DAT-010, DAT-011, DAT-012, DAT-013, DAT-020, DAT-021, DAT-070
program DsState;
{ Unit DB: exceptions and message texts, the row and value model, and the Open/Close state machine with the
  driver hooks of TRowDataSet (through the test driver TTestDataSet). }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

type
  TCalls = class
    DS: TTestDataSet;
    procedure RaiseError;
    procedure RaiseErrorFmt;
    procedure DoOpen;
    procedure DoClose;
    procedure GetRecordCount;
    procedure GetIsEmpty;
    procedure DoCheckBrowseMode;
  end;

procedure TCalls.RaiseError;
begin
  DatabaseError('plain text');
end;

procedure TCalls.RaiseErrorFmt;
begin
  DatabaseErrorFmt(SDatFieldTooLong, ['name', 50]);
end;

procedure TCalls.DoOpen;
begin
  DS.Open;
end;

procedure TCalls.DoClose;
begin
  DS.Close;
end;

procedure TCalls.GetRecordCount;
begin
  WriteLn(DS.RecordCount);
end;

procedure TCalls.GetIsEmpty;
begin
  WriteLn(DS.IsEmpty);
end;

procedure TCalls.DoCheckBrowseMode;
begin
  DS.CheckBrowseMode;
end;

var
  Calls: TCalls;

procedure TestErrors;
var
  E: EDatabaseError;
begin
  Section('errors');
  CheckRaises(Calls.RaiseError, 'EDatabaseError', 'plain text', 'DatabaseError raises EDatabaseError');
  CheckRaises(Calls.RaiseErrorFmt, 'EDatabaseError', 'Value too long for field ''name'' (maximum 50 characters)',
    'DatabaseErrorFmt formats the message');
  E := EDatabaseError.Create('x');
  Check(E is Exception, 'EDatabaseError descends from Exception');
  E.Free;
  WriteLn('SDatClosed=', SDatClosed);
  WriteLn('SDatEmpty=', SDatEmpty);
  WriteLn('SDatReadOnly=', SDatReadOnly);
  WriteLn('SDatNotEditing=', SDatNotEditing);
  WriteLn('SDatFieldNotFound=', SDatFieldNotFound);
  WriteLn('SDatDuplicateField=', SDatDuplicateField);
  WriteLn('SDatFieldReadOnly=', SDatFieldReadOnly);
  WriteLn('SDatFieldRequired=', SDatFieldRequired);
  WriteLn('SDatFieldTooLong=', SDatFieldTooLong);
  WriteLn('SDatFieldRange=', SDatFieldRange);
  WriteLn('SDatFieldAccess=', SDatFieldAccess);
  WriteLn('SDatFieldConvert=', SDatFieldConvert);
  WriteLn('SDatFieldType=', SDatFieldType);
  WriteLn('SDatTypeMismatch=', SDatTypeMismatch);
  WriteLn('SDatParamNotFound=', SDatParamNotFound);
  WriteLn('SDatRecNoRange=', SDatRecNoRange);
  WriteLn('SDatBadBookmark=', SDatBadBookmark);
  WriteLn('SDatLocateCount=', SDatLocateCount);
  WriteLn('SDatFilter=', SDatFilter);
  WriteLn('SDatCalcOp=', SDatCalcOp);
  WriteLn('SDatNotSupported=', SDatNotSupported);
  WriteLn('SDatActiveChange=', SDatActiveChange);
  Section('enumerations');
  CheckEqInt(Ord(dsInactive), 0, 'dsInactive is first');
  CheckEqInt(Ord(dsInsert), 3, 'dsInsert is last');
  CheckEqInt(Ord(ftUnknown), 0, 'ftUnknown is first');
  CheckEqInt(Ord(ftLargeint), 14, 'ftLargeint is last');
  CheckEqInt(Ord(fkCalculated), 1, 'fkCalculated');
  CheckEqInt(Ord(deFocusControl), 8, 'deFocusControl is last');
  CheckEqInt(Ord(daRetry), 2, 'daRetry is last');
  Check([loCaseInsensitive, loPartialKey] <> [], 'TLocateOptions is a set');
  Check([foCaseInsensitive, foNoPartialCompare] <> [], 'TFilterOptions is a set');
end;

procedure TestRowModel;
var
  V: TDBValue;
  B: TBytes;
  Row, Row2, Row3: TDataRow;
  All, View: TDataRowList;
  T: TFieldType;
begin
  Section('row model: values');
  V := NullValue;
  CheckEq(ValueKindName(V.Kind), 'vkNull', 'NullValue');
  V := IntValue(9000000000);
  Check((V.Kind = vkInt) and (V.IntVal = 9000000000), 'IntValue keeps 64 bits');
  V := FloatValue(1.5);
  Check((V.Kind = vkFloat) and (V.FloatVal = 1.5), 'FloatValue');
  V := StringValue('abc');
  Check((V.Kind = vkString) and (V.StrVal = 'abc'), 'StringValue');
  SetLength(B, 2);
  B[0] := 7;
  B[1] := 255;
  V := BytesValue(B);
  Check((V.Kind = vkBytes) and (Length(V.BytesVal) = 2) and (V.BytesVal[1] = 255), 'BytesValue');
  for T := ftUnknown to ftLargeint do
    WriteLn(FieldTypeName(T), ' -> ', ValueKindName(CanonicalKind(T)));

  Section('row model: rows and lists');
  Row := TDataRow.Create(3);
  CheckEqInt(Length(Row.Values), 3, 'a new row has one value per data field');
  Check((Row.Values[0].Kind = vkNull) and (Row.Values[1].Kind = vkNull) and (Row.Values[2].Kind = vkNull),
    'a new row is all null');
  Row.RowId := 1;
  Row2 := TDataRow.Create(3);
  Row2.RowId := 2;
  Row3 := TDataRow.Create(3);
  Row3.RowId := 3;
  All := TDataRowList.Create(True);
  View := TDataRowList.Create(False);
  Check(All.OwnsRows and not View.OwnsRows, 'the row list owns its rows, a view does not');
  CheckEqInt(All.Add(Row), 0, 'Add returns the index');
  All.Add(Row3);
  All.Insert(1, Row2);
  CheckEqInt(All.Count, 3, 'Count after Add and Insert');
  Check((All[0] = Row) and (All[1] = Row2) and (All[2] = Row3), 'Insert keeps the order');
  CheckEqInt(All.IndexOf(Row3), 2, 'IndexOf');
  View.Add(Row);
  View.Add(Row3);
  CheckEqInt(View.Remove(Row), 0, 'Remove returns the old index');
  CheckEqInt(View.Count, 1, 'the view lost one row');
  CheckEqInt(All.Count, 3, 'the row list is unaffected by the view');
  CheckEqInt(Row.RowId, 1, 'a row removed from a view still exists');
  All.Delete(1);
  CheckEqInt(All.Count, 2, 'Delete');
  CheckEqInt(All.IndexOf(Row2), -1, 'a deleted row is not found');
  View.Clear;
  CheckEqInt(View.Count, 0, 'Clear');
  View.Free;
  All.Free;
end;

procedure TestStateMachine;
var
  DS: TTestDataSet;
  Owner: TComponent;
begin
  Section('state machine: open and close');
  DS := TTestDataSet.Create(nil);
  Calls.DS := DS;
  DS.HookEvents;
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 20);
  DS.AddRow([IntValue(1), StringValue('one')]);
  DS.AddRow([IntValue(2), StringValue('two')]);
  DS.AddRow([IntValue(3), NullValue]);
  CheckEq(StateName(DS.State), 'dsInactive', 'a new dataset is inactive');
  Check(not DS.Active, 'Active is False');
  DS.Close;
  CheckEq(TakeLog, '', 'Close on an inactive dataset does nothing');
  DS.Open;
  CheckEq(TakeLog,
    'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll',
    'Open sequence');
  CheckEq(StateName(DS.State), 'dsBrowse', 'an open dataset is in dsBrowse');
  Check(DS.Active, 'Active is True');
  CheckEqInt(DS.RecordCount, 3, 'RecordCount');
  Check(not DS.IsEmpty, 'IsEmpty is False');
  CheckEq(Flags(DS), 'Bof', 'after Open: Bof');
  CheckEqInt(DS.CursorIndex, 0, 'after Open the cursor is on the first row');
  DS.Open;
  CheckEq(TakeLog, '', 'Open on an active dataset does nothing');
  DS.Close;
  CheckEq(TakeLog, 'BeforeClose,DriverClose,[deUpdateState dsInactive],AfterClose', 'Close sequence');
  CheckEq(StateName(DS.State), 'dsInactive', 'a closed dataset is inactive');
  CheckEqInt(DS.FieldCount, 0, 'dynamic fields are gone after Close');
  CheckEqInt(DS.StoredCount, 0, 'rows are released by Close');
  DS.Active := True;
  CheckEq(TakeLog,
    'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll',
    'Active := True is Open');
  DS.Active := True;
  CheckEq(TakeLog, '', 'Active := True on an active dataset does nothing');
  DS.Active := False;
  CheckEq(TakeLog, 'BeforeClose,DriverClose,[deUpdateState dsInactive],AfterClose', 'Active := False is Close');

  Section('state machine: row ids and view');
  DS.Open;
  CheckEq(DS.StoredRowIds, '1,2,3', 'row ids count from 1 in load order');
  CheckEq(DS.ViewRowIds, '1,2,3', 'without a filter the view has the same rows');
  DS.Close;
  DS.Open;
  CheckEq(DS.StoredRowIds, '1,2,3', 'the row id counter restarts at Open');
  DS.Close;
  ClearLog;

  Section('state machine: closed dataset');
  CheckRaises(Calls.GetRecordCount, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'RecordCount on a closed dataset');
  CheckRaises(Calls.GetIsEmpty, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'IsEmpty on a closed dataset');
  CheckRaises(Calls.DoCheckBrowseMode, 'EDatabaseError', 'Cannot perform this operation on a closed dataset',
    'CheckBrowseMode on a closed dataset');
  Check(DS.Bof and DS.Eof, 'Bof and Eof can be read on a closed dataset');
  Check(DS.FindField('id') = nil, 'FindField on a closed dataset without persistent fields');
  CheckEqInt(DS.FieldDefs.Count, 2, 'FieldDefs can be read on a closed dataset');

  Section('state machine: empty dataset');
  DS.ClearRows;
  DS.Open;
  CheckEqInt(DS.RecordCount, 0, 'RecordCount of an empty dataset');
  Check(DS.IsEmpty, 'IsEmpty');
  CheckEq(Flags(DS), 'Bof+Eof', 'empty: Bof and Eof');
  CheckEqInt(DS.CursorIndex, -1, 'empty: no cursor row');
  DS.Close;
  DS.AddRow([IntValue(1), StringValue('one')]);
  DS.AddRow([IntValue(2), StringValue('two')]);
  ClearLog;

  Section('state machine: failures during open');
  DS.FailIn := 'DriverOpen';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'driver open failed', 'DriverOpen fails');
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen', 'nothing after the failing DriverOpen, no DriverClose');
  CheckEq(StateName(DS.State), 'dsInactive', 'the dataset stays inactive');
  DS.FailIn := 'DriverInitFieldDefs';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'driver definitions failed', 'DriverInitFieldDefs fails');
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverClose', 'resources are released');
  DS.FailIn := 'DriverLoadRows';
  CheckRaises(Calls.DoOpen, 'EDatabaseError', 'driver load failed', 'DriverLoadRows fails');
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,DriverClose',
    'resources are released after a failing load');
  CheckEq(StateName(DS.State), 'dsInactive', 'the dataset stays inactive after a failing load');
  CheckEqInt(DS.FieldCount, 0, 'dynamic fields created so far are destroyed');
  CheckEqInt(DS.StoredCount, 0, 'rows loaded so far are released');
  DS.FailIn := '';

  Section('state machine: handlers that raise');
  DS.RaiseIn := 'BeforeOpen';
  CheckRaises(Calls.DoOpen, 'Exception', 'stop in BeforeOpen', 'BeforeOpen raises');
  CheckEq(TakeLog, 'BeforeOpen', 'nothing happens after the handler');
  CheckEq(StateName(DS.State), 'dsInactive', 'the open was cancelled');
  DS.RaiseIn := 'AfterOpen';
  CheckRaises(Calls.DoOpen, 'Exception', 'stop in AfterOpen', 'AfterOpen raises');
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen',
    'the open itself is complete');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the dataset is open');
  DS.RaiseIn := 'BeforeClose';
  CheckRaises(Calls.DoClose, 'Exception', 'stop in BeforeClose', 'BeforeClose raises');
  CheckEq(TakeLog, 'BeforeClose', 'nothing happens after the handler');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the close was cancelled');
  DS.RaiseIn := 'AfterClose';
  CheckRaises(Calls.DoClose, 'Exception', 'stop in AfterClose', 'AfterClose raises');
  CheckEq(StateName(DS.State), 'dsInactive', 'the close itself is complete');
  DS.RaiseIn := '';
  ClearLog;

  Section('state machine: Active while loading');
  DS.SimulateLoading;
  DS.Active := True;
  CheckEq(TakeLog, '', 'Active := True while loading is only remembered');
  CheckEq(StateName(DS.State), 'dsInactive', 'still inactive while loading');
  DS.SimulateLoaded;
  CheckEq(TakeLog,
    'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],AfterOpen,AfterScroll',
    'Loaded opens the dataset');
  CheckEq(StateName(DS.State), 'dsBrowse', 'active after Loaded');

  Section('state machine: destruction');
  DS.Free;
  CheckEq(TakeLog, 'BeforeClose,DriverClose,[deUpdateState dsInactive],AfterClose',
    'destroying an active dataset closes it first');
  Owner := TComponent.Create(nil);
  DS := TTestDataSet.Create(Owner);
  DS.HookEvents;
  DS.AddDef('id', ftInteger);
  DS.Open;
  ClearLog;
  Owner.Free;
  CheckEq(TakeLog, 'DriverClose', 'no events when the owner is being destroyed');
  DS := TTestDataSet.Create(nil);
  DS.HookEvents;
  DS.Free;
  CheckEq(TakeLog, '', 'destroying an inactive dataset fires nothing');
end;

begin
  Calls := TCalls.Create;
  TestErrors;
  TestRowModel;
  TestStateMachine;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
