// covers: DAT-012, DAT-018, DAT-065
program DsControlsOff;
{ Data-source notification: every dataset operation sends its data events to the attached data sources (a
  recording fake data source stands in for the real one), and DisableControls / EnableControls suppress and
  resume them. }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

type
  { A data source that only records what the dataset sends: 'A:deUpdateState dsBrowse' }
  TRecordingSource = class(TDataSource)
  protected
    procedure DataEvent(Event: TDataEvent; Info: TObject); override;
  public
    Id: string;
  end;

var
  Rec: TStringList;

procedure TRecordingSource.DataEvent(Event: TDataEvent; Info: TObject);
var
  Line: string;
begin
  Line := Id + ':' + DataEventName(Event) + ' ' + StateName(DataSet.State);
  if Info is TField then
    Line := Line + ' ' + TField(Info).FieldName;
  Rec.Add(Line);
end;

{ what the sources recorded as 'a,b,c'; the record is emptied }
function TakeRec: string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Rec.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Rec[I];
  end;
  Rec.Clear;
end;

function NewSource(const Id: string; DS: TDataSet): TRecordingSource;
begin
  Result := TRecordingSource.Create(nil);
  Result.Id := Id;
  Result.DataSet := DS;
end;

{ id, name; rows 1 one, 2 two, 3 three; not opened; dataset events hooked }
function NewDS: TTestDataSet;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  Result.AddRow([IntValue(1), StringValue('one')]);
  Result.AddRow([IntValue(2), StringValue('two')]);
  Result.AddRow([IntValue(3), StringValue('three')]);
  Result.HookEvents;
end;

procedure TestDispatch;
var
  DS: TTestDataSet;
  A, B: TRecordingSource;
  Mark: TBookmark;
begin
  Section('dispatch points');
  DS := NewDS;
  A := NewSource('A', DS);
  Check(A.DataSet = DS, 'the source is attached');
  DS.Open;
  CheckEq(TakeRec, 'A:deUpdateState dsBrowse', 'Open');
  Mark := DS.GetBookmark;
  DS.Next;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'Next');
  DS.Last;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'Last');
  DS.Next;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'Next at the end (no move)');
  DS.Prior;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'Prior');
  DS.First;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'First');
  DS.MoveBy(2);
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'MoveBy');
  DS.MoveBy(0);
  CheckEq(TakeRec, '', 'MoveBy(0)');
  DS.RecNo := 2;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'RecNo');
  DS.GotoBookmark(Mark);
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'GotoBookmark');
  DS.Locate('id', ['3'], []);
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'Locate, found');
  DS.Locate('id', ['9'], []);
  CheckEq(TakeRec, '', 'Locate, not found');
  DS.Edit;
  CheckEq(TakeRec, 'A:deUpdateState dsEdit', 'Edit');
  DS.FieldByName('name').AsString := 'drei';
  CheckEq(TakeRec, 'A:deFieldChange dsEdit name', 'a field write');
  DS.UpdateRecord;
  CheckEq(TakeRec, 'A:deUpdateRecord dsEdit', 'UpdateRecord');
  DS.Post;
  CheckEq(TakeRec, 'A:deUpdateRecord dsEdit,A:deUpdateState dsBrowse,A:deDataSetChange dsBrowse', 'Post');
  DS.Insert;
  CheckEq(TakeRec, 'A:deUpdateState dsInsert,A:deDataSetChange dsInsert', 'Insert');
  DS.Cancel;
  CheckEq(TakeRec, 'A:deUpdateState dsBrowse,A:deDataSetChange dsBrowse', 'Cancel');
  DS.Append;
  CheckEq(TakeRec, 'A:deUpdateState dsInsert,A:deDataSetChange dsInsert', 'Append');
  DS.FieldByName('id').AsInteger := 4;
  Rec.Clear;
  DS.First;
  CheckEq(TakeRec, 'A:deCheckBrowseMode dsInsert,A:deUpdateRecord dsInsert,A:deUpdateState dsBrowse,A:deDataSetChange dsBrowse,' +
    'A:deDataSetScroll dsBrowse', 'navigation posts a modified record');
  DS.Edit;
  Rec.Clear;
  DS.CheckBrowseMode;
  CheckEq(TakeRec, 'A:deCheckBrowseMode dsEdit,A:deUpdateState dsBrowse,A:deDataSetChange dsBrowse', 'CheckBrowseMode cancels an unmodified record');
  DS.Delete;
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'Delete');
  DS.Refresh;
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'Refresh');
  DS.Filter := 'id > 1';
  CheckEq(TakeRec, '', 'Filter without Filtered');
  DS.Filtered := True;
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'Filtered');
  DS.Filter := 'id > 2';
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'Filter while Filtered');
  DS.FilterOptions := [foCaseInsensitive];
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'FilterOptions while Filtered');
  DS.Filtered := False;
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse', 'Filtered off');
  DS.FieldByName('name').FocusControl;
  CheckEq(TakeRec, 'A:deFocusControl dsBrowse name', 'FocusControl');
  DS.FieldByName('name').Index := 0;
  CheckEq(TakeRec, 'A:deLayoutChange dsBrowse', 'a field is moved');
  DS.Close;
  CheckEq(TakeRec, 'A:deUpdateState dsInactive', 'Close');

  Section('several data sources');
  B := NewSource('B', DS);
  DS.Open;
  CheckEq(TakeRec, 'A:deUpdateState dsBrowse,B:deUpdateState dsBrowse', 'every source is notified, in the order of attachment');
  DS.Edit;
  DS.FieldByName('name').AsString := 'x';
  CheckEq(TakeRec, 'A:deUpdateState dsEdit,B:deUpdateState dsEdit,A:deFieldChange dsEdit name,B:deFieldChange dsEdit name',
    'each event goes to all sources before the next event');
  DS.Cancel;
  Rec.Clear;
  A.DataSet := nil;
  Check(A.DataSet = nil, 'detached');
  DS.Next;
  CheckEq(TakeRec, 'B:deDataSetScroll dsBrowse', 'a detached source gets nothing');
  A.DataSet := DS;
  DS.Next;
  CheckEq(TakeRec, 'B:deDataSetScroll dsBrowse,A:deDataSetScroll dsBrowse', 'attached again: now last');
  B.Free;
  DS.Next;
  CheckEq(TakeRec, 'A:deDataSetScroll dsBrowse', 'a destroyed source is detached');
  DS.Free;
  Check(A.DataSet = nil, 'destroying the dataset detaches its sources');
  Rec.Clear;
  A.Free;
end;

procedure TestDisable;
var
  DS: TTestDataSet;
  A, B: TRecordingSource;
begin
  Section('DisableControls');
  DS := NewDS;
  A := NewSource('A', DS);
  B := NewSource('B', DS);
  DS.Open;
  Rec.Clear;
  ClearLog;
  CheckEqBool(DS.ControlsDisabled, False, 'enabled at first');
  DS.DisableControls;
  CheckEqBool(DS.ControlsDisabled, True, 'ControlsDisabled');
  CheckEq(TakeRec, '', 'DisableControls itself sends nothing');
  DS.Next;
  CheckEq(TakeLog, 'BeforeScroll,AfterScroll', 'the events of the dataset still fire; no data event is sent');
  DS.Edit;
  DS.FieldByName('name').AsString := 'zwei';
  DS.Post;
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,AfterEdit,BeforePost,ApplyUpdate,AfterPost', 'editing while disabled');
  DS.Insert;
  DS.Cancel;
  DS.Last;
  DS.Delete;
  DS.Refresh;
  DS.Filtered := True;
  DS.Filtered := False;
  DS.FieldByName('name').FocusControl;
  ClearLog;
  CheckEq(TakeRec, '', 'nothing reached the data sources');
  CheckEq(DS.ViewText, '[1|one] [2|two] [3|three]', 'the operations themselves worked (the refresh reloaded the rows)');
  DS.EnableControls;
  CheckEqBool(DS.ControlsDisabled, False, 'enabled again');
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse,B:deDataSetChange dsBrowse', 'EnableControls: the state is the same, the data changed');
  CheckEq(TakeLog, '[deDataSetChange dsBrowse]', 'EnableControls fires no dataset events, only the data event');

  Section('EnableControls: what is sent');
  DS.DisableControls;
  DS.EnableControls;
  CheckEq(TakeRec, '', 'nothing was suppressed: nothing is sent');
  DS.DisableControls;
  DS.Edit;
  DS.EnableControls;
  CheckEq(TakeRec, 'A:deUpdateState dsEdit,B:deUpdateState dsEdit,A:deDataSetChange dsEdit,B:deDataSetChange dsEdit',
    'the state changed meanwhile: deUpdateState first');
  DS.FieldByName('name').AsString := 'uno';
  CheckEq(TakeRec, 'A:deFieldChange dsEdit name,B:deFieldChange dsEdit name', 'events flow again');
  DS.DisableControls;
  DS.Cancel;
  DS.Edit;
  DS.EnableControls;
  CheckEq(TakeRec, 'A:deDataSetChange dsEdit,B:deDataSetChange dsEdit', 'the state is the same as at DisableControls: no deUpdateState');
  DS.Cancel;
  Rec.Clear;

  Section('nesting');
  DS.DisableControls;
  DS.DisableControls;
  DS.Next;
  DS.EnableControls;
  CheckEqBool(DS.ControlsDisabled, True, 'one EnableControls of two: still disabled');
  CheckEq(TakeRec, '', 'and nothing is sent yet');
  DS.Prior;
  DS.EnableControls;
  CheckEqBool(DS.ControlsDisabled, False, 'the second EnableControls enables');
  CheckEq(TakeRec, 'A:deDataSetChange dsBrowse,B:deDataSetChange dsBrowse', 'and notifies once');
  DS.EnableControls;
  DS.EnableControls;
  CheckEq(TakeRec, '', 'EnableControls without DisableControls does nothing');
  DS.DisableControls;
  CheckEqBool(DS.ControlsDisabled, True, 'the counter never went below zero');
  DS.EnableControls;
  CheckEqBool(DS.ControlsDisabled, False, 'one EnableControls is enough');

  Section('closed dataset');
  DS.DisableControls;
  DS.Close;
  DS.EnableControls;
  CheckEq(TakeRec, 'A:deUpdateState dsInactive,B:deUpdateState dsInactive,A:deDataSetChange dsInactive,B:deDataSetChange dsInactive',
    'closed while disabled');
  DS.DisableControls;
  CheckEqBool(DS.ControlsDisabled, True, 'DisableControls works on a closed dataset');
  DS.Open;
  CheckEq(TakeRec, '', 'Open while disabled sends nothing');
  DS.EnableControls;
  CheckEq(TakeRec, 'A:deUpdateState dsBrowse,B:deUpdateState dsBrowse,A:deDataSetChange dsBrowse,B:deDataSetChange dsBrowse',
    'opened while disabled');
  A.Free;
  B.Free;
  DS.Free;
end;

begin
  Rec := TStringList.Create;
  TestDispatch;
  TestDisable;
  Rec.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
