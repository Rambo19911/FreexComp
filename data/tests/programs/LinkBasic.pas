// covers: DAT-200, DAT-201, DAT-202, DAT-203, DAT-204, DAT-205, DAT-210, DAT-211, DAT-212, DAT-213, DAT-214, DAT-215, DAT-404
program LinkBasic;
{ The data source (registration, state, dispatch order, own events, Enabled, AutoEdit), the data link (active and
  editing tracking, default method chain, row window, ActiveRecord) and the field data link (field resolution,
  Edit, Modified, Reset, UpdateRecord, the push before CheckBrowseMode decides). }
uses
  SysUtils, Classes, DB, DataTestUtils, TestDataSet;

type
  { A link that records every notification: 'L1:DataSetChanged' }
  TRecLink = class(TDataLink)
  protected
    procedure ActiveChanged; override;
    procedure EditingChanged; override;
    procedure DataSetChanged; override;
    procedure DataSetScrolled(Distance: Integer); override;
    procedure LayoutChanged; override;
    procedure RecordChanged(Field: TField); override;
    procedure UpdateData; override;
    procedure CheckBrowseMode; override;
    procedure FocusControl(Field: TField); override;
  public
    Id: string;
    { the recorded methods also run the inherited default }
    Chain: Boolean;
    { UpdateData calls UpdateRecord again }
    Reenter: Boolean;
  end;

  TTestFieldLink = class(TFieldDataLink)
  public
    Id: string;
  end;

  { Handlers of the data-source and field-link events. }
  TRecorder = class
  public
    { what OnUpdateData of a field link writes into the field; '' = nothing }
    Pending: string;
    { OnUpdateData of a field link raises }
    FailUpdate: Boolean;
    procedure StateChange(Sender: TObject);
    procedure DataChange(Sender: TObject; Field: TField);
    procedure UpdateData(Sender: TObject);
    procedure LinkActiveChange(Sender: TObject);
    procedure LinkEditingChange(Sender: TObject);
    procedure LinkDataChange(Sender: TObject);
    procedure LinkUpdateData(Sender: TObject);
  end;

var
  Rec: TStringList;
  { the records go to the shared log of the test dataset, between the events of the dataset }
  Shared: Boolean;
  Handlers: TRecorder;
  Focused: string;

procedure Note(const Line: string);
begin
  if Shared then
    AddLog(Line)
  else
    Rec.Add(Line);
end;

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

function FieldLabel(Field: TField): string;
begin
  if Field = nil then
    Result := 'nil'
  else
    Result := Field.FieldName;
end;

{ TRecLink }

procedure TRecLink.ActiveChanged;
begin
  Note(Id + ':ActiveChanged ' + BoolStr(Active));
  if Chain then
    inherited ActiveChanged;
end;

procedure TRecLink.EditingChanged;
begin
  Note(Id + ':EditingChanged ' + BoolStr(Editing));
  if Chain then
    inherited EditingChanged;
end;

procedure TRecLink.DataSetChanged;
begin
  Note(Id + ':DataSetChanged');
  if Chain then
    inherited DataSetChanged;
end;

procedure TRecLink.DataSetScrolled(Distance: Integer);
begin
  Note(Id + ':DataSetScrolled ' + IntToStr(Distance));
  if Chain then
    inherited DataSetScrolled(Distance);
end;

procedure TRecLink.LayoutChanged;
begin
  Note(Id + ':LayoutChanged');
  if Chain then
    inherited LayoutChanged;
end;

procedure TRecLink.RecordChanged(Field: TField);
begin
  Note(Id + ':RecordChanged ' + FieldLabel(Field));
  if Chain then
    inherited RecordChanged(Field);
end;

procedure TRecLink.UpdateData;
begin
  Note(Id + ':UpdateData');
  if Reenter then
    UpdateRecord;
  if Chain then
    inherited UpdateData;
end;

procedure TRecLink.CheckBrowseMode;
begin
  Note(Id + ':CheckBrowseMode');
  if Chain then
    inherited CheckBrowseMode;
end;

procedure TRecLink.FocusControl(Field: TField);
begin
  Note(Id + ':FocusControl ' + FieldLabel(Field));
  if Chain then
    inherited FocusControl(Field);
end;

{ TRecorder }

procedure TRecorder.StateChange(Sender: TObject);
begin
  Note(TDataSource(Sender).Name + ':StateChange ' + StateName(TDataSource(Sender).State));
end;

procedure TRecorder.DataChange(Sender: TObject; Field: TField);
begin
  Note(TDataSource(Sender).Name + ':DataChange ' + FieldLabel(Field));
end;

procedure TRecorder.UpdateData(Sender: TObject);
begin
  Note(TDataSource(Sender).Name + ':UpdateData');
end;

procedure TRecorder.LinkActiveChange(Sender: TObject);
begin
  Note(TTestFieldLink(Sender).Id + ':OnActiveChange ' + BoolStr(TTestFieldLink(Sender).Active));
end;

procedure TRecorder.LinkEditingChange(Sender: TObject);
begin
  Note(TTestFieldLink(Sender).Id + ':OnEditingChange ' + BoolStr(TTestFieldLink(Sender).Editing));
end;

procedure TRecorder.LinkDataChange(Sender: TObject);
begin
  Note(TTestFieldLink(Sender).Id + ':OnDataChange ' + FieldLabel(TTestFieldLink(Sender).Field));
end;

procedure TRecorder.LinkUpdateData(Sender: TObject);
begin
  Note(TTestFieldLink(Sender).Id + ':OnUpdateData');
  if FailUpdate then
    raise Exception.Create('bad text');
  if Pending <> '' then
    TTestFieldLink(Sender).Field.AsString := Pending;
end;

procedure FocusHook(Control: TComponent);
begin
  Focused := Focused + Control.Name + ';';
end;

{ ---- fixtures ---- }

{ id, name; Count rows 1 n1, 2 n2, ...; not opened }
function NewDS(Count: Integer): TTestDataSet;
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  for I := 1 to Count do
    Result.AddRow([IntValue(I), StringValue('n' + IntToStr(I))]);
end;

function NewSource(const Name: string; DS: TDataSet): TDataSource;
begin
  Result := TDataSource.Create(nil);
  Result.Name := Name;
  Result.OnStateChange := Handlers.StateChange;
  Result.OnDataChange := Handlers.DataChange;
  Result.OnUpdateData := Handlers.UpdateData;
  Result.DataSet := DS;
end;

function NewLink(const Id: string; Source: TDataSource): TRecLink;
begin
  Result := TRecLink.Create;
  Result.Id := Id;
  Result.DataSource := Source;
end;

function NewFieldLink(const Id: string; Source: TDataSource; const FieldName: string): TTestFieldLink;
begin
  Result := TTestFieldLink.Create;
  Result.Id := Id;
  Result.OnActiveChange := Handlers.LinkActiveChange;
  Result.OnEditingChange := Handlers.LinkEditingChange;
  Result.OnDataChange := Handlers.LinkDataChange;
  Result.OnUpdateData := Handlers.LinkUpdateData;
  Result.FieldName := FieldName;
  Result.DataSource := Source;
end;

{ The ids of the window rows, the current one in brackets, the row being inserted as 'new': '3 [4] 5'.
  Reads every row through ActiveRecord and restores the previous value. }
function WindowText(Link: TDataLink): string;
var
  I, Saved: Integer;
  F: TField;
  Cell: string;
begin
  Result := '';
  F := Link.DataSet.FieldByName('id');
  Saved := Link.ActiveRecord;
  try
    for I := 0 to Link.RecordCount - 1 do
    begin
      Link.ActiveRecord := I;
      if F.IsNull then
        Cell := 'new'
      else
        Cell := F.AsString;
      if I = Saved then
        Cell := '[' + Cell + ']';
      if I > 0 then
        Result := Result + ' ';
      Result := Result + Cell;
    end;
  finally
    Link.ActiveRecord := Saved;
  end;
end;

{ ---- data source ---- }

procedure TestSourceBasics;
var
  DS, Other: TTestDataSet;
  S: TDataSource;
begin
  Section('data source: state, Edit, registration');
  S := TDataSource.Create(nil);
  CheckEqBool(S.AutoEdit, True, 'AutoEdit is True by default');
  CheckEqBool(S.Enabled, True, 'Enabled is True by default');
  Check(S.DataSet = nil, 'no dataset at first');
  CheckEq(StateName(S.State), 'dsInactive', 'State without a dataset');
  S.Edit;
  CheckEq(StateName(S.State), 'dsInactive', 'Edit without a dataset does nothing');
  DS := NewDS(3);
  Other := NewDS(1);
  S.DataSet := DS;
  CheckEqBool(S.IsLinkedTo(DS), True, 'IsLinkedTo its dataset');
  CheckEqBool(S.IsLinkedTo(Other), False, 'IsLinkedTo another dataset');
  CheckEqBool(S.IsLinkedTo(nil), False, 'IsLinkedTo(nil)');
  CheckEq(StateName(S.State), 'dsInactive', 'State of a closed dataset');
  S.Edit;
  CheckEq(StateName(DS.State), 'dsInactive', 'Edit on a closed dataset does nothing');
  DS.Open;
  CheckEq(StateName(S.State), 'dsBrowse', 'State follows the dataset');
  S.Enabled := False;
  CheckEq(StateName(S.State), 'dsInactive', 'State of a disabled source');
  S.Edit;
  CheckEq(StateName(DS.State), 'dsBrowse', 'Edit of a disabled source does nothing');
  S.Enabled := True;
  S.AutoEdit := False;
  S.Edit;
  CheckEq(StateName(DS.State), 'dsEdit', 'Edit enters dsEdit, also with AutoEdit = False');
  S.Edit;
  CheckEq(StateName(S.State), 'dsEdit', 'Edit in dsEdit does nothing');
  DS.Cancel;
  DS.Insert;
  S.Edit;
  CheckEq(StateName(S.State), 'dsInsert', 'Edit in dsInsert does nothing');
  DS.Cancel;
  S.Free;
  DS.Edit;
  CheckEq(StateName(DS.State), 'dsEdit', 'the dataset works after its source is destroyed');
  DS.Free;
  Other.Free;
end;

procedure TestDispatchOrder;
var
  DS: TTestDataSet;
  S, S2: TDataSource;
  L1, L2, L3: TRecLink;
begin
  Section('data source: dispatch order and events');
  DS := NewDS(3);
  S := NewSource('S', DS);
  L1 := NewLink('L1', S);
  L2 := NewLink('L2', S);
  CheckEq(TakeRec, '', 'attaching to a closed dataset sends nothing');
  DS.Open;
  CheckEq(TakeRec, 'L1:ActiveChanged True,L2:ActiveChanged True,S:StateChange dsBrowse,S:DataChange nil',
    'Open: links in registration order, OnStateChange, then OnDataChange');
  DS.Next;
  CheckEq(TakeRec, 'L1:DataSetScrolled 0,L2:DataSetScrolled 0,S:DataChange nil', 'Next');
  DS.Edit;
  CheckEq(TakeRec, 'L1:EditingChanged True,L2:EditingChanged True,S:StateChange dsEdit', 'Edit');
  DS.FieldByName('name').AsString := 'zwei';
  CheckEq(TakeRec, 'L1:RecordChanged name,L2:RecordChanged name,S:DataChange name', 'a field write');
  DS.UpdateRecord;
  CheckEq(TakeRec, 'S:UpdateData,L1:UpdateData,L2:UpdateData', 'UpdateRecord: OnUpdateData before the links');
  DS.Post;
  CheckEq(TakeRec, 'S:UpdateData,L1:UpdateData,L2:UpdateData,L1:EditingChanged False,L2:EditingChanged False,' +
    'S:StateChange dsBrowse,L1:DataSetChanged,L2:DataSetChanged,S:DataChange nil', 'Post');
  DS.Insert;
  CheckEq(TakeRec, 'L1:EditingChanged True,L2:EditingChanged True,S:StateChange dsInsert,' +
    'L1:DataSetChanged,L2:DataSetChanged,S:DataChange nil', 'Insert');
  DS.ClearFields;
  CheckEq(TakeRec, 'L1:RecordChanged nil,L2:RecordChanged nil,S:DataChange nil', 'ClearFields (deRecordChange)');
  DS.First;
  CheckEq(TakeRec, 'L1:CheckBrowseMode,L2:CheckBrowseMode,S:UpdateData,L1:UpdateData,L2:UpdateData,' +
    'L1:EditingChanged False,L2:EditingChanged False,S:StateChange dsBrowse,L1:DataSetChanged,L2:DataSetChanged,' +
    'S:DataChange nil,L1:DataSetScrolled 0,L2:DataSetScrolled 0,S:DataChange nil',
    'navigation: deCheckBrowseMode reaches the links only, then the post and the scroll');
  DS.Edit;
  Rec.Clear;
  DS.CheckBrowseMode;
  CheckEq(TakeRec, 'L1:CheckBrowseMode,L2:CheckBrowseMode,L1:EditingChanged False,L2:EditingChanged False,' +
    'S:StateChange dsBrowse,L1:DataSetChanged,L2:DataSetChanged,S:DataChange nil', 'CheckBrowseMode cancels an unmodified record');
  DS.FieldByName('name').FocusControl;
  CheckEq(TakeRec, 'L1:FocusControl name,L2:FocusControl name', 'deFocusControl reaches the links only');
  DS.FieldByName('name').Index := 0;
  CheckEq(TakeRec, 'L1:LayoutChanged,L2:LayoutChanged,S:DataChange nil', 'deLayoutChange');
  DS.Delete;
  CheckEq(TakeRec, 'L1:DataSetChanged,L2:DataSetChanged,S:DataChange nil', 'Delete');
  DS.Refresh;
  CheckEq(TakeRec, 'L1:DataSetChanged,L2:DataSetChanged,S:DataChange nil', 'Refresh');

  S2 := NewSource('T', DS);
  CheckEq(TakeRec, 'T:StateChange dsBrowse,T:DataChange nil,T:DataChange nil',
    'attaching a source to an open dataset: a state change, then a dataset change');
  L3 := NewLink('L3', S2);
  CheckEq(TakeRec, 'L3:ActiveChanged True', 'a link attached to an active source becomes active');
  DS.Next;
  CheckEq(TakeRec, 'L1:DataSetScrolled 0,L2:DataSetScrolled 0,S:DataChange nil,L3:DataSetScrolled 0,T:DataChange nil',
    'two sources: in the order of attachment, each with its links first');
  L1.Free;
  DS.Next;
  CheckEq(TakeRec, 'L2:DataSetScrolled 0,S:DataChange nil,L3:DataSetScrolled 0,T:DataChange nil', 'a destroyed link is gone');
  L1 := NewLink('L1', S);
  Rec.Clear;
  DS.Prior;
  CheckEq(TakeRec, 'L2:DataSetScrolled 0,L1:DataSetScrolled 0,S:DataChange nil,L3:DataSetScrolled 0,T:DataChange nil',
    'a link registered again comes last');
  DS.DisableControls;
  DS.Next;
  DS.Edit;
  CheckEq(TakeRec, '', 'nothing while the controls are disabled');
  DS.EnableControls;
  CheckEq(TakeRec, 'L2:EditingChanged True,L1:EditingChanged True,S:StateChange dsEdit,L3:EditingChanged True,T:StateChange dsEdit,' +
    'L2:DataSetChanged,L1:DataSetChanged,S:DataChange nil,L3:DataSetChanged,T:DataChange nil', 'EnableControls');
  DS.Cancel;
  Rec.Clear;
  DS.Close;
  CheckEq(TakeRec, 'L2:ActiveChanged False,L1:ActiveChanged False,S:StateChange dsInactive,L3:ActiveChanged False,T:StateChange dsInactive', 'Close');
  L1.Free;
  L2.Free;
  L3.Free;
  S.Free;
  S2.Free;
  DS.Free;
end;

procedure TestWithDataSetEvents;
var
  DS: TTestDataSet;
  S: TDataSource;
  L: TRecLink;
begin
  Section('data source: order relative to the dataset events');
  DS := NewDS(2);
  DS.HookEvents;
  S := NewSource('S', DS);
  L := NewLink('L', S);
  Shared := True;
  ClearLog;
  DS.Open;
  CheckEq(TakeLog, 'BeforeOpen,DriverOpen,DriverInitFieldDefs,DriverLoadRows,[deUpdateState dsBrowse],L:ActiveChanged True,' +
    'S:StateChange dsBrowse,S:DataChange nil,AfterOpen,AfterScroll', 'Open');
  DS.Next;
  CheckEq(TakeLog, 'BeforeScroll,[deDataSetScroll dsBrowse],L:DataSetScrolled 0,S:DataChange nil,AfterScroll', 'Next');
  DS.Edit;
  CheckEq(TakeLog, 'BeforeEdit,DriverEdit,[deUpdateState dsEdit],L:EditingChanged True,S:StateChange dsEdit,AfterEdit', 'Edit');
  DS.FieldByName('name').AsString := 'x';
  CheckEq(TakeLog, '[deFieldChange dsEdit name],L:RecordChanged name,S:DataChange name', 'a field write');
  DS.Post;
  CheckEq(TakeLog, '[deUpdateRecord dsEdit],S:UpdateData,L:UpdateData,BeforePost,ApplyUpdate,[deUpdateState dsBrowse],' +
    'L:EditingChanged False,S:StateChange dsBrowse,[deDataSetChange dsBrowse],L:DataSetChanged,S:DataChange nil,AfterPost', 'Post');
  DS.Close;
  CheckEq(TakeLog, 'BeforeClose,DriverClose,[deUpdateState dsInactive],L:ActiveChanged False,S:StateChange dsInactive,AfterClose', 'Close');
  Shared := False;
  L.Free;
  S.Free;
  DS.Free;
end;

procedure TestEnabledAndDataSet;
var
  DS, DS2, Closed: TTestDataSet;
  S: TDataSource;
  L: TRecLink;
begin
  Section('data source: Enabled and DataSet changes');
  DS := NewDS(3);
  DS2 := NewDS(2);
  Closed := NewDS(1);
  DS.Open;
  DS2.Open;
  S := NewSource('S', DS);
  L := NewLink('L', S);
  Rec.Clear;
  S.Enabled := False;
  CheckEq(TakeRec, 'L:ActiveChanged False,S:StateChange dsInactive', 'Enabled := False: like closing');
  CheckEqBool(L.Active, False, 'the link of a disabled source is inactive');
  DS.Next;
  DS.Edit;
  DS.FieldByName('name').AsString := 'x';
  DS.Post;
  CheckEq(TakeRec, '', 'a disabled source dispatches nothing');
  S.Enabled := False;
  CheckEq(TakeRec, '', 'assigning the same Enabled does nothing');
  S.Enabled := True;
  CheckEq(TakeRec, 'L:ActiveChanged True,S:StateChange dsBrowse,S:DataChange nil,L:DataSetChanged,S:DataChange nil',
    'Enabled := True: a state change followed by a dataset change');
  DS.Edit;
  Rec.Clear;
  S.Enabled := False;
  CheckEq(TakeRec, 'L:ActiveChanged False,L:EditingChanged False,S:StateChange dsInactive', 'disabled while editing');
  S.Enabled := True;
  CheckEq(TakeRec, 'L:ActiveChanged True,L:EditingChanged True,S:StateChange dsEdit,L:DataSetChanged,S:DataChange nil',
    'enabled on an editing dataset: no extra OnDataChange (the state is not dsBrowse)');
  DS.Cancel;
  Rec.Clear;

  S.DataSet := DS;
  CheckEq(TakeRec, '', 'assigning the same dataset does nothing');
  S.DataSet := DS2;
  CheckEq(TakeRec, 'L:ActiveChanged False,S:StateChange dsInactive,L:ActiveChanged True,S:StateChange dsBrowse,S:DataChange nil,' +
    'L:DataSetChanged,S:DataChange nil', 'another open dataset: through inactive');
  Check(L.DataSet = DS2, 'the link reads the new dataset');
  DS.Next;
  CheckEq(TakeRec, '', 'the old dataset no longer notifies');
  DS2.Next;
  CheckEq(TakeRec, 'L:DataSetScrolled 0,S:DataChange nil', 'the new dataset notifies');
  S.DataSet := Closed;
  CheckEq(TakeRec, 'L:ActiveChanged False,S:StateChange dsInactive', 'a closed dataset');
  S.DataSet := nil;
  CheckEq(TakeRec, '', 'from a closed dataset to none: no state change');
  Check(L.DataSet = nil, 'DataSet of the link is nil-safe');
  S.DataSet := DS;
  CheckEq(TakeRec, 'L:ActiveChanged True,S:StateChange dsBrowse,S:DataChange nil,L:DataSetChanged,S:DataChange nil', 'from none to an open dataset');
  L.Free;
  S.Free;
  DS.Free;
  DS2.Free;
  Closed.Free;
end;

procedure TestFreeNotification;
var
  DS: TTestDataSet;
  Owner: TComponent;
  S, S2: TDataSource;
  L, L2: TRecLink;
  F: TTestFieldLink;
begin
  Section('data source: free notification');
  DS := NewDS(2);
  S := NewSource('S', DS);
  L := NewLink('L', S);
  DS.Open;
  Rec.Clear;
  DS.Free;
  CheckEq(TakeRec, 'L:ActiveChanged False,S:StateChange dsInactive', 'destroying an open dataset closes it first');
  Check(S.DataSet = nil, 'destroying the dataset sets DataSet to nil');
  CheckEqBool(L.Active, False, 'the link is inactive');
  Check(L.DataSet = nil, 'the link has no dataset');
  CheckEq(StateName(S.State), 'dsInactive', 'State after the dataset is gone');

  DS := NewDS(2);
  DS.Open;
  S.DataSet := DS;
  L2 := NewLink('L2', S);
  Rec.Clear;
  S.Free;
  CheckEq(TakeRec, 'L2:ActiveChanged False,L:ActiveChanged False', 'destroying the source detaches every link');
  Check(L.DataSource = nil, 'DataSource of the first link is nil');
  Check(L2.DataSource = nil, 'DataSource of the second link is nil');
  DS.Next;
  CheckEq(TakeRec, '', 'the dataset no longer reaches the links');
  L.Free;
  L2.Free;
  DS.Free;

  { owner and dataset destroyed together: no events, and the links hold nothing of the dataset afterwards }
  Owner := TComponent.Create(nil);
  DS := TTestDataSet.Create(Owner);
  DS.AddDef('id', ftInteger);
  DS.AddRow([IntValue(1)]);
  S2 := NewSource('S', DS);
  F := NewFieldLink('F', S2, 'id');
  DS.Open;
  Check(F.Field <> nil, 'the field is resolved');
  Rec.Clear;
  Owner.Free;
  CheckEq(TakeRec, '', 'a dataset destroyed with its owner sends no events');
  Check(S2.DataSet = nil, 'the source is detached');
  CheckEqBool(F.Active, False, 'the field link is inactive');
  Check(F.Field = nil, 'the field link dropped its field');
  F.Free;
  S2.Free;
end;

{ ---- data link ---- }

procedure TestLinkTracking;
var
  DS: TTestDataSet;
  S: TDataSource;
  L: TRecLink;
begin
  Section('data link: active, editing, default methods');
  L := TRecLink.Create;
  L.Id := 'L';
  CheckEqBool(L.Active, False, 'a new link is inactive');
  CheckEqBool(L.Editing, False, 'a new link is not editing');
  CheckEqBool(L.ReadOnly, False, 'ReadOnly is False by default');
  CheckEqInt(L.BufferCount, 1, 'BufferCount is 1 by default');
  CheckEqInt(L.RecordCount, 0, 'RecordCount of an inactive link');
  CheckEqInt(L.ActiveRecord, 0, 'ActiveRecord of an inactive link');
  Check(L.DataSet = nil, 'DataSet without a source');
  CheckEqBool(L.Edit, False, 'Edit without a source');
  DS := NewDS(3);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  L.DataSource := S;
  Check(L.DataSet = DS, 'DataSet through the source');
  CheckEq(TakeRec, '', 'attached to a closed dataset');
  DS.Open;
  CheckEq(TakeRec, 'L:ActiveChanged True', 'Open');
  CheckEqBool(L.Active, True, 'Active');
  CheckEqInt(L.RecordCount, 1, 'RecordCount with BufferCount 1');
  DS.Edit;
  CheckEq(TakeRec, 'L:EditingChanged True', 'Edit');
  CheckEqBool(L.Editing, True, 'Editing');
  DS.Cancel;
  CheckEq(TakeRec, 'L:EditingChanged False,L:DataSetChanged', 'Cancel');
  DS.Insert;
  CheckEqBool(L.Editing, True, 'Editing in dsInsert');
  DS.Cancel;
  Rec.Clear;

  L.Chain := True;
  DS.Next;
  CheckEq(TakeRec, 'L:DataSetScrolled 0,L:DataSetChanged,L:RecordChanged nil', 'DataSetScrolled defaults to DataSetChanged, that to RecordChanged(nil)');
  DS.FieldByName('name').Index := 0;
  CheckEq(TakeRec, 'L:LayoutChanged,L:DataSetChanged,L:RecordChanged nil', 'LayoutChanged defaults to DataSetChanged');
  L.Chain := False;

  CheckEqBool(L.Edit, True, 'Edit enters edit mode');
  CheckEq(StateName(DS.State), 'dsEdit', 'the dataset is in dsEdit');
  CheckEq(TakeRec, 'L:EditingChanged True', 'Edit notifies');
  CheckEqBool(L.Edit, True, 'Edit while editing');
  CheckEq(TakeRec, '', 'Edit while editing does nothing');
  L.UpdateRecord;
  CheckEq(TakeRec, 'L:UpdateData', 'UpdateRecord calls UpdateData');
  L.Reenter := True;
  L.UpdateRecord;
  CheckEq(TakeRec, 'L:UpdateData', 'UpdateRecord from inside UpdateData is ignored');
  DS.UpdateRecord;
  CheckEq(TakeRec, 'L:UpdateData', 'deUpdateRecord: once');
  L.Reenter := False;
  DS.Cancel;
  L.ReadOnly := True;
  Rec.Clear;
  CheckEqBool(L.Edit, False, 'Edit of a read-only link');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the dataset stays in dsBrowse');
  L.ReadOnly := False;
  L.UpdateRecord;
  CheckEq(TakeRec, 'L:UpdateData', 'UpdateRecord in dsBrowse calls UpdateData as well');

  L.DataSource := nil;
  CheckEq(TakeRec, 'L:ActiveChanged False', 'DataSource := nil');
  CheckEqBool(L.Active, False, 'inactive without a source');
  DS.Next;
  CheckEq(TakeRec, '', 'a detached link is not notified');
  L.DataSource := S;
  CheckEq(TakeRec, 'L:ActiveChanged True', 'attached to an active source');
  L.Free;
  DS.Next;
  S.Free;
  DS.Free;
end;

procedure TestRowWindow;
var
  DS: TTestDataSet;
  S: TDataSource;
  L, One: TDataLink;
  Saved: Integer;
begin
  Section('data link: row window');
  DS := NewDS(10);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  L := TDataLink.Create;
  L.BufferCount := 4;
  L.DataSource := S;
  One := TDataLink.Create;
  One.DataSource := S;
  DS.Open;
  CheckEqInt(L.RecordCount, 4, 'RecordCount = BufferCount when enough rows');
  CheckEqInt(L.ActiveRecord, 0, 'ActiveRecord defaults to the current row');
  CheckEq(WindowText(L), '[1] 2 3 4', 'after Open');
  CheckEq(WindowText(One), '[1]', 'a link with BufferCount 1 sees the current row');
  DS.Next;
  CheckEq(WindowText(L), '1 [2] 3 4', 'Next inside the window: the window stays');
  DS.MoveBy(2);
  CheckEq(WindowText(L), '1 2 3 [4]', 'the last window row');
  DS.Next;
  CheckEq(WindowText(L), '2 3 4 [5]', 'Next beyond the window: moves by one');
  CheckEq(WindowText(One), '[5]', 'BufferCount 1 follows');
  DS.MoveBy(3);
  CheckEq(WindowText(L), '5 6 7 [8]', 'MoveBy beyond the window: the minimum amount');
  DS.Prior;
  CheckEq(WindowText(L), '5 6 [7] 8', 'Prior inside the window');
  DS.MoveBy(-3);
  CheckEq(WindowText(L), '[4] 5 6 7', 'MoveBy above the window');
  DS.Last;
  CheckEq(WindowText(L), '7 8 9 [10]', 'Last');
  DS.First;
  CheckEq(WindowText(L), '[1] 2 3 4', 'First');
  DS.RecNo := 6;
  CheckEq(WindowText(L), '3 4 5 [6]', 'RecNo');
  DS.Locate('id', ['4'], []);
  CheckEq(WindowText(L), '3 [4] 5 6', 'Locate inside the window');

  DS.Edit;
  DS.FieldByName('id').AsInteger := 40;
  CheckEq(WindowText(L), '3 [40] 5 6', 'the current row reads the edit buffer');
  CheckEqInt(L.ActiveRecord, 1, 'ActiveRecord while editing');
  L.ActiveRecord := 2;
  CheckEqInt(L.ActiveRecord, 2, 'ActiveRecord reads back');
  CheckEq(DS.FieldByName('id').AsString, '5', 'fields read the active record');
  CheckRaises(DS.ClearFields, 'EDatabaseError', 'Dataset not in edit or insert mode', 'writes are refused under an override');
  L.ActiveRecord := 1;
  CheckEq(DS.FieldByName('id').AsString, '40', 'restored: the edit buffer again');
  DS.Cancel;
  CheckEq(WindowText(L), '3 [4] 5 6', 'after Cancel');

  DS.Insert;
  CheckEqInt(L.RecordCount, 4, 'RecordCount in dsInsert');
  CheckEq(WindowText(L), '3 [new] 4 5', 'Insert: the new row at its insert position');
  CheckEq(WindowText(One), '[new]', 'BufferCount 1 in dsInsert');
  DS.FieldByName('id').AsInteger := 35;
  DS.Post;
  CheckEq(WindowText(L), '3 [35] 4 5', 'after Post');
  DS.Append;
  CheckEq(WindowText(L), '8 9 10 [new]', 'Append: the new row after the last one');
  CheckEqInt(L.ActiveRecord, 3, 'ActiveRecord of the appended row');
  DS.Cancel;
  CheckEq(WindowText(L), '[35] 4 5 6', 'Cancel: back to the row before the insert');
  DS.Delete;
  CheckEq(WindowText(L), '[4] 5 6 7', 'Delete: the next row takes the position');
  DS.Last;
  DS.Delete;
  CheckEq(WindowText(L), '6 7 8 [9]', 'Delete of the last row: the position is kept');
  DS.First;
  DS.Insert;
  CheckEq(WindowText(L), '[new] 1 2 3', 'Insert before the first row');
  L.ActiveRecord := 3;
  CheckEq(DS.FieldByName('id').AsString, '3', 'rows after the insert position are read from the view');
  L.ActiveRecord := 0;
  Check(DS.FieldByName('id').IsNull, 'restored: the new row');
  DS.Cancel;
  CheckEq(WindowText(L), '[1] 2 3 4', 'after Cancel of the insert');

  DS.Last;
  DS.Filter := 'id > 6';
  DS.Filtered := True;
  CheckEq(WindowText(L), '7 8 [9]', 'filter: fewer rows than BufferCount');
  CheckEqInt(L.RecordCount, 3, 'RecordCount = number of rows');
  DS.Filtered := False;
  CheckEq(WindowText(L), '6 7 8 [9]', 'filter off: the window is filled again');
  DS.RecNo := 5;
  DS.Refresh;
  CheckEq(WindowText(L), '[5] 6 7 8', 'Refresh (the test driver loads its ten rows again) keeps the window position');
  L.BufferCount := 2;
  CheckEq(WindowText(L), '[5] 6', 'a smaller BufferCount');
  DS.Next;
  DS.Next;
  CheckEq(WindowText(L), '6 [7]', 'scrolling with the smaller window');
  L.BufferCount := 6;
  CheckEq(WindowText(L), '5 6 [7] 8 9 10', 'a larger BufferCount: no window row stays unused while rows exist');
  DS.Last;
  L.BufferCount := 3;
  L.BufferCount := 20;
  CheckEq(WindowText(L), '1 2 3 4 5 6 7 8 9 [10]', 'BufferCount beyond the row count: every row');
  CheckEqInt(L.RecordCount, 10, 'RecordCount = min(BufferCount, rows)');
  L.BufferCount := 0;
  CheckEqInt(L.BufferCount, 1, 'BufferCount is at least 1');
  L.BufferCount := 4;

  Saved := L.ActiveRecord;
  try
    L.ActiveRecord := 4;
    Check(False, 'ActiveRecord beyond RecordCount raises');
  except
    on E: EDatabaseError do
      CheckEq(E.Message, 'Record number out of range', 'ActiveRecord beyond RecordCount raises');
  end;
  try
    L.ActiveRecord := -1;
    Check(False, 'negative ActiveRecord raises');
  except
    on E: EDatabaseError do
      CheckEq(E.Message, 'Record number out of range', 'negative ActiveRecord raises');
  end;
  CheckEqInt(L.ActiveRecord, Saved, 'ActiveRecord unchanged after the errors');

  DS.Filter := 'id > 100';
  DS.Filtered := True;
  CheckEqInt(L.RecordCount, 0, 'RecordCount of an empty view');
  CheckEqInt(L.ActiveRecord, 0, 'ActiveRecord of an empty view');
  CheckEq(WindowText(L), '', 'no rows');
  DS.Filtered := False;
  DS.Insert;
  CheckEqInt(L.RecordCount, 4, 'RecordCount in dsInsert counts the new row');
  DS.Cancel;
  DS.Close;
  CheckEqInt(L.RecordCount, 0, 'RecordCount after Close');
  CheckEqInt(L.ActiveRecord, 0, 'ActiveRecord after Close');
  DS.ClearRows;
  DS.Open;
  CheckEqInt(L.RecordCount, 0, 'an empty dataset');
  DS.Insert;
  CheckEq(WindowText(L), '[new]', 'Insert into an empty dataset');
  CheckEqInt(L.RecordCount, 1, 'the new row is the only one');
  DS.Cancel;
  L.Free;
  One.Free;
  S.Free;
  DS.Free;
end;

{ ---- field data link ---- }

procedure TestFieldLink;
var
  DS, DS2: TTestDataSet;
  S, S2: TDataSource;
  F: TTestFieldLink;
  C: TComponent;
begin
  Section('field link: field resolution');
  DS := NewDS(3);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  F := NewFieldLink('F', S, 'name');
  CheckEq(F.FieldName, 'name', 'FieldName');
  Check(F.Field = nil, 'no field while inactive');
  CheckEqBool(F.CanModify, False, 'CanModify while inactive');
  CheckEqBool(F.Edit, False, 'Edit while inactive');
  CheckEq(TakeRec, '', 'nothing so far');
  DS.Open;
  CheckEq(TakeRec, 'F:OnActiveChange True,F:OnDataChange name', 'Open: OnActiveChange, then OnDataChange');
  Check(F.Field = DS.FieldByName('name'), 'the field is resolved');
  CheckEqBool(F.CanModify, True, 'CanModify');
  F.FieldName := 'nope';
  CheckEq(TakeRec, 'F:OnDataChange nil', 'an unknown field name: no exception, the control reloads');
  Check(F.Field = nil, 'Field is nil when not resolvable');
  CheckEqBool(F.CanModify, False, 'CanModify without a field');
  CheckEqBool(F.Edit, False, 'Edit without a field');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the dataset stays in dsBrowse');
  F.FieldName := 'ID';
  CheckEq(TakeRec, 'F:OnDataChange id', 'FieldName is resolved without regard to case');
  F.FieldName := 'ID';
  CheckEq(TakeRec, '', 'the same FieldName does nothing');
  F.FieldName := 'name';
  Rec.Clear;
  DS.FieldByName('name').Index := 0;
  CheckEq(TakeRec, 'F:OnDataChange name', 'a layout change resolves again and reloads');
  DS.Next;
  CheckEq(TakeRec, 'F:OnDataChange name', 'scrolling reloads');
  DS.Edit;
  CheckEq(TakeRec, 'F:OnEditingChange True', 'Edit: OnEditingChange');
  DS.FieldByName('id').AsInteger := 20;
  CheckEq(TakeRec, '', 'a change of another field is ignored');
  DS.FieldByName('name').AsString := 'zwei';
  CheckEq(TakeRec, 'F:OnDataChange name', 'a change of the field reloads');
  DS.Cancel;
  CheckEq(TakeRec, 'F:OnEditingChange False,F:OnDataChange name', 'Cancel');

  Section('field link: Edit');
  S.AutoEdit := False;
  CheckEqBool(F.Edit, False, 'AutoEdit = False: Edit is refused');
  CheckEq(StateName(DS.State), 'dsBrowse', 'no edit mode');
  DS.Edit;
  CheckEqBool(F.Edit, True, 'already editing: True, also with AutoEdit = False');
  DS.Cancel;
  S.AutoEdit := True;
  F.ReadOnly := True;
  CheckEqBool(F.CanModify, False, 'CanModify of a read-only link');
  CheckEqBool(F.Edit, False, 'Edit of a read-only link');
  F.ReadOnly := False;
  DS.FieldByName('name').ReadOnly := True;
  CheckEqBool(F.CanModify, False, 'CanModify of a read-only field');
  CheckEqBool(F.Edit, False, 'Edit on a read-only field');
  DS.FieldByName('name').ReadOnly := False;
  DS.DriverReadOnly := True;
  CheckEqBool(F.CanModify, False, 'CanModify on a read-only dataset');
  CheckEqBool(F.Edit, False, 'Edit on a read-only dataset');
  DS.DriverReadOnly := False;
  CheckEq(StateName(DS.State), 'dsBrowse', 'still dsBrowse');
  Rec.Clear;
  CheckEqBool(F.Edit, True, 'Edit enters edit mode');
  CheckEq(StateName(DS.State), 'dsEdit', 'dsEdit');
  CheckEq(TakeRec, 'F:OnEditingChange True', 'OnEditingChange');
  CheckEqBool(F.Editing, True, 'Editing');

  Section('field link: Modified, UpdateRecord, Reset');
  F.UpdateRecord;
  CheckEq(TakeRec, '', 'UpdateRecord without Modified does nothing');
  F.Modified;
  Handlers.Pending := 'typed';
  F.UpdateRecord;
  CheckEq(TakeRec, 'F:OnUpdateData,F:OnDataChange name', 'UpdateRecord after Modified: OnUpdateData writes the field');
  CheckEq(DS.FieldByName('name').AsString, 'typed', 'the field has the value');
  F.UpdateRecord;
  CheckEq(TakeRec, '', 'the flag is cleared');
  F.Modified;
  F.Reset;
  CheckEq(TakeRec, 'F:OnDataChange name', 'Reset reloads');
  F.UpdateRecord;
  CheckEq(TakeRec, '', 'Reset cleared the flag');
  F.Modified;
  DS.FieldByName('id').AsInteger := 21;
  Handlers.Pending := 'again';
  DS.UpdateRecord;
  CheckEq(TakeRec, 'F:OnUpdateData,F:OnDataChange name', 'a change of another field keeps the flag; deUpdateRecord writes');
  F.Modified;
  Handlers.FailUpdate := True;
  CheckRaises(F.UpdateRecord, 'Exception', 'bad text', 'an exception of OnUpdateData propagates');
  Handlers.FailUpdate := False;
  Handlers.Pending := 'fixed';
  Rec.Clear;
  F.UpdateRecord;
  CheckEq(TakeRec, 'F:OnUpdateData,F:OnDataChange name', 'the flag survived the failed write');
  DS.Post;
  CheckEq(TakeRec, 'F:OnEditingChange False,F:OnDataChange name', 'Post without pending text');
  CheckEq(DS.FieldByName('name').AsString, 'fixed', 'posted');
  F.Edit;
  F.Modified;
  Handlers.Pending := 'posted';
  Rec.Clear;
  DS.Post;
  CheckEq(TakeRec, 'F:OnUpdateData,F:OnDataChange name,F:OnEditingChange False,F:OnDataChange name', 'Post writes pending text first');
  CheckEq(DS.FieldByName('name').AsString, 'posted', 'posted with the text');

  Section('field link: push before CheckBrowseMode decides');
  F.Edit;
  F.Modified;
  Handlers.Pending := 'pushed';
  Rec.Clear;
  CheckEqBool(DS.Modified, False, 'the dataset is not modified yet');
  DS.Next;
  CheckEq(TakeRec, 'F:OnUpdateData,F:OnDataChange name,F:OnEditingChange False,F:OnDataChange name,F:OnDataChange name',
    'navigation: the text is written, the record posted, then the scroll');
  DS.Prior;
  CheckEq(DS.FieldByName('name').AsString, 'pushed', 'the pushed text was posted');
  F.Edit;
  Rec.Clear;
  DS.Next;
  CheckEq(TakeRec, 'F:OnEditingChange False,F:OnDataChange name,F:OnDataChange name', 'without Modified: nothing is written, the edit is cancelled');
  Handlers.Pending := '';

  Section('field link: focus, close, another source');
  C := TComponent.Create(nil);
  C.Name := 'Ctl';
  F.Control := C;
  Check(F.Control = C, 'Control');
  DataLinkFocusHook := FocusHook;
  DS.FieldByName('name').FocusControl;
  CheckEq(Focused, 'Ctl;', 'FocusControl of the field reaches the control');
  DS.FieldByName('id').FocusControl;
  CheckEq(Focused, 'Ctl;', 'another field is ignored');
  F.Control := nil;
  DS.FieldByName('name').FocusControl;
  CheckEq(Focused, 'Ctl;', 'no control: nothing');
  DataLinkFocusHook := nil;
  C.Free;
  Rec.Clear;
  DS.Close;
  CheckEq(TakeRec, 'F:OnActiveChange False,F:OnDataChange nil', 'Close');
  Check(F.Field = nil, 'no field after Close');
  DS.Open;
  CheckEq(TakeRec, 'F:OnActiveChange True,F:OnDataChange name', 'Open again');
  Check(F.Field = DS.FieldByName('name'), 'the new field object is resolved');
  DS2 := TTestDataSet.Create(nil);
  DS2.AddDef('id', ftInteger);
  DS2.AddRow([IntValue(7)]);
  DS2.Open;
  S2 := TDataSource.Create(nil);
  S2.DataSet := DS2;
  F.DataSource := S2;
  CheckEq(TakeRec, 'F:OnActiveChange False,F:OnDataChange nil,F:OnActiveChange True,F:OnDataChange nil',
    'another source: through inactive; the field does not exist there');
  F.FieldName := 'id';
  CheckEq(F.Field.AsString, '7', 'the field of the other dataset');
  Rec.Clear;
  S2.DataSet := DS;
  CheckEq(TakeRec, 'F:OnActiveChange False,F:OnDataChange nil,F:OnActiveChange True,F:OnDataChange id,F:OnDataChange id',
    'another dataset of the source: the field is resolved again');
  Check(F.Field = DS.FieldByName('id'), 'the field of the new dataset');
  F.Free;
  S.Free;
  S2.Free;
  DS.Free;
  DS2.Free;
end;

begin
  Rec := TStringList.Create;
  Handlers := TRecorder.Create;
  TestSourceBasics;
  TestDispatchOrder;
  TestWithDataSetEvents;
  TestEnabledAndDataSet;
  TestFreeNotification;
  TestLinkTracking;
  TestRowWindow;
  TestFieldLink;
  Handlers.Free;
  Rec.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
