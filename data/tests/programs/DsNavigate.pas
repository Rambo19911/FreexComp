// covers: DAT-005, DAT-010, DAT-014, DAT-015, DAT-016, DAT-017, DAT-018, DAT-020, DAT-021
program DsNavigate;
{ Navigation: the BOF/EOF table on 0, 1 and 3 rows, the canonical loops, MoveBy, RecNo and the scroll events. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

const
  Scroll = 'BeforeScroll,[deDataSetScroll dsBrowse],AfterScroll';

type
  TCalls = class
    DS: TTestDataSet;
    procedure DoFirst;
    procedure DoLast;
    procedure DoNext;
    procedure DoPrior;
    procedure DoMoveBy;
    procedure GetRecNo;
    procedure SetRecNoZero;
    procedure SetRecNoOne;
    procedure SetRecNoPastEnd;
  end;

procedure TCalls.DoFirst;
begin
  DS.First;
end;

procedure TCalls.DoLast;
begin
  DS.Last;
end;

procedure TCalls.DoNext;
begin
  DS.Next;
end;

procedure TCalls.DoPrior;
begin
  DS.Prior;
end;

procedure TCalls.DoMoveBy;
begin
  DS.MoveBy(1);
end;

procedure TCalls.GetRecNo;
begin
  WriteLn(DS.RecNo);
end;

procedure TCalls.SetRecNoZero;
begin
  DS.RecNo := 0;
end;

procedure TCalls.SetRecNoOne;
begin
  DS.RecNo := 1;
end;

procedure TCalls.SetRecNoPastEnd;
begin
  DS.RecNo := DS.RecordCount + 1;
end;

var
  Calls: TCalls;

function NewDataSet(RowCount: Integer): TTestDataSet;
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.HookEvents;
  Result.AddDef('id', ftInteger);
  for I := 1 to RowCount do
    Result.AddRow([IntValue(I)]);
  Result.Open;
  ClearLog;
  Calls.DS := Result;
end;

{ '<step>: recno=<n> <flags>' and whether the step fired exactly the scroll events }
procedure Show(DS: TTestDataSet; const Step: string);
var
  Events: string;
begin
  Events := TakeLog;
  if Events = Scroll then
    Events := 'scroll'
  else if Events = '' then
    Events := 'no events'
  else
    Events := 'UNEXPECTED ' + Events;
  WriteLn(Step, ': recno=', DS.RecNo, ' ', Flags(DS), ' (', Events, ')');
end;

procedure TestTable(RowCount: Integer);
var
  DS: TTestDataSet;
  I: Integer;
begin
  Section('table: ' + IntToStr(RowCount) + ' rows');
  DS := NewDataSet(RowCount);
  CheckEqInt(DS.RecordCount, RowCount, 'RecordCount');
  CheckEqBool(DS.IsEmpty, RowCount = 0, 'IsEmpty');
  Show(DS, 'Open');
  for I := 1 to RowCount + 1 do
  begin
    DS.Next;
    Show(DS, 'Next');
  end;
  for I := 1 to RowCount + 1 do
  begin
    DS.Prior;
    Show(DS, 'Prior');
  end;
  DS.Last;
  Show(DS, 'Last');
  DS.Prior;
  Show(DS, 'Prior');
  DS.Last;
  Show(DS, 'Last');
  DS.Next;
  Show(DS, 'Next');
  DS.First;
  Show(DS, 'First');
  DS.Next;
  Show(DS, 'Next');
  DS.First;
  Show(DS, 'First');
  DS.Prior;
  Show(DS, 'Prior');
  DS.Free;
end;

procedure TestLoops(RowCount: Integer);
var
  DS: TTestDataSet;
  Visited: string;
begin
  DS := NewDataSet(RowCount);
  Visited := '';
  DS.First;
  while not DS.Eof do
  begin
    Visited := Visited + IntToStr(DS.RecNo) + ' ';
    DS.Next;
  end;
  WriteLn(RowCount, ' rows forward: ', Visited);
  Visited := '';
  DS.Last;
  while not DS.Bof do
  begin
    Visited := Visited + IntToStr(DS.RecNo) + ' ';
    DS.Prior;
  end;
  WriteLn(RowCount, ' rows backward: ', Visited);
  DS.Free;
end;

procedure Move(DS: TTestDataSet; Distance: Integer);
var
  Moved: Integer;
begin
  Moved := DS.MoveBy(Distance);
  Show(DS, 'MoveBy(' + IntToStr(Distance) + ') = ' + IntToStr(Moved));
end;

procedure TestMoveBy;
var
  DS: TTestDataSet;
begin
  Section('MoveBy: 5 rows');
  DS := NewDataSet(5);
  Move(DS, 0);
  Move(DS, 2);
  Move(DS, 10);
  Move(DS, 0);
  Move(DS, -1);
  Move(DS, -10);
  Move(DS, 4);
  Move(DS, 1);
  Move(DS, -4);
  Move(DS, -1);
  Move(DS, 1);
  DS.Free;
  Section('MoveBy: 1 row');
  DS := NewDataSet(1);
  Move(DS, 1);
  Move(DS, -1);
  DS.First;
  ClearLog;
  Move(DS, -3);
  Move(DS, 0);
  DS.Free;
  Section('MoveBy: 0 rows');
  DS := NewDataSet(0);
  Move(DS, 3);
  Move(DS, -3);
  Move(DS, 0);
  DS.Free;
end;

procedure TestRecNo;
var
  DS: TTestDataSet;
begin
  Section('RecNo');
  DS := NewDataSet(3);
  CheckEqInt(DS.RecNo, 1, 'RecNo after Open');
  DS.RecNo := 3;
  Show(DS, 'RecNo := 3');
  CheckEqInt(DS.CursorIndex, 2, 'the cursor index is RecNo - 1');
  DS.RecNo := 1;
  Show(DS, 'RecNo := 1');
  DS.RecNo := 1;
  Show(DS, 'RecNo := 1 again');
  DS.Last;
  DS.Next;
  ClearLog;
  DS.RecNo := 3;
  Show(DS, 'RecNo := 3 at Eof');
  CheckRaises(Calls.SetRecNoZero, 'EDatabaseError', 'Record number out of range', 'RecNo := 0');
  CheckRaises(Calls.SetRecNoPastEnd, 'EDatabaseError', 'Record number out of range', 'RecNo := RecordCount + 1');
  Show(DS, 'after the errors');
  DS.Free;
  DS := NewDataSet(0);
  CheckEqInt(DS.RecNo, 0, 'RecNo of an empty dataset');
  CheckRaises(Calls.SetRecNoOne, 'EDatabaseError', 'Record number out of range', 'RecNo := 1 on an empty dataset');
  DS.Free;
end;

procedure TestHandlers;
var
  DS: TTestDataSet;
begin
  Section('scroll handlers that raise');
  DS := NewDataSet(3);
  DS.RaiseIn := 'BeforeScroll';
  CheckRaises(Calls.DoNext, 'Exception', 'stop in BeforeScroll', 'BeforeScroll raises');
  CheckEq(TakeLog, 'BeforeScroll', 'nothing happens after the handler');
  CheckEqInt(DS.RecNo, 1, 'the cursor did not move');
  CheckRaises(Calls.DoLast, 'Exception', 'stop in BeforeScroll', 'BeforeScroll raises in Last');
  CheckEq(Flags(DS), 'Bof', 'the flags are unchanged');
  DS.RaiseIn := 'AfterScroll';
  ClearLog;
  CheckRaises(Calls.DoNext, 'Exception', 'stop in AfterScroll', 'AfterScroll raises');
  CheckEq(TakeLog, Scroll, 'the move is complete');
  CheckEqInt(DS.RecNo, 2, 'the cursor moved');
  DS.RaiseIn := '';
  DS.Free;
end;

procedure TestClosed;
var
  DS: TTestDataSet;
begin
  Section('closed dataset');
  DS := NewDataSet(3);
  DS.Close;
  ClearLog;
  CheckRaises(Calls.DoFirst, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'First');
  CheckRaises(Calls.DoLast, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'Last');
  CheckRaises(Calls.DoNext, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'Next');
  CheckRaises(Calls.DoPrior, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'Prior');
  CheckRaises(Calls.DoMoveBy, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'MoveBy');
  CheckRaises(Calls.GetRecNo, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'RecNo');
  CheckRaises(Calls.SetRecNoOne, 'EDatabaseError', 'Cannot perform this operation on a closed dataset', 'RecNo :=');
  CheckEq(TakeLog, '', 'no events on a closed dataset');
  DS.Free;
end;

begin
  Calls := TCalls.Create;
  TestTable(0);
  TestTable(1);
  TestTable(3);
  Section('canonical loops');
  TestLoops(0);
  TestLoops(1);
  TestLoops(3);
  TestMoveBy;
  TestRecNo;
  TestHandlers;
  TestClosed;
  Calls.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
