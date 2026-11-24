{ TestDataSet - a TRowDataSet driver for the dataset test programs: definitions and seed rows are given in
  code, every driver call, data event and dataset event is written to one shared log, and each driver step
  can be made to fail. }
unit TestDataSet;

interface

uses
  SysUtils, Classes, DB;

type
  TTestDataSet = class(TRowDataSet)
  private
    FTemplate: TFieldDefs;
    FSeed: TDataRowList;
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
    procedure NoteError(const EventName: string; E: EDatabaseError; var Action: TDataAction);
    procedure ApplyStep(const StepName: string);
    function AssignedText(Buffer: TDataRow): string;
    procedure Note(const EventName: string);
  protected
    procedure DataEvent(Event: TDataEvent; Info: TObject); override;
    procedure DriverOpen; override;
    procedure DriverInitFieldDefs; override;
    procedure DriverLoadRows; override;
    procedure DriverClose; override;
    function DriverCanModify: Boolean; override;
    function DriverFieldUpdatable(Field: TField): Boolean; override;
    procedure DriverEdit(Row: TDataRow); override;
    procedure ApplyInsert(Buffer: TDataRow); override;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); override;
    procedure ApplyDelete(Row: TDataRow); override;
  public
    { the driver step named here ('DriverOpen', 'DriverInitFieldDefs', 'DriverLoadRows') raises EDatabaseError }
    FailIn: string;
    { the event handler named here ('BeforeOpen', ...) raises Exception after logging }
    RaiseIn: string;
    { the event handler named here calls Abort after logging }
    AbortIn: string;
    { the next FailApply calls of DriverEdit / ApplyInsert / ApplyUpdate / ApplyDelete raise
      EDatabaseError('apply failed') (after logging) }
    FailApply: Integer;
    { what the error handlers of HookErrors answer }
    ErrorAction: TDataAction;
    { the driver reports the dataset as not modifiable }
    DriverReadOnly: Boolean;
    { the column the driver reports as not updatable }
    FixedField: string;
    { when > 0: ApplyInsert stores it in the first column of the buffer and increments it }
    InsertId: Int64;
    { what the last apply call received, e.g. 'update old=[1|one] new=[1|uno] assigned=2' }
    LastApply: string;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { the definitions the driver reports at open }
    procedure AddDef(const Name: string; DataType: TFieldType; Size: Integer = 0; Required: Boolean = False);
    procedure ClearDefs;
    { one row the driver loads at open; one value per definition }
    procedure AddRow(const Values: array of TDBValue);
    procedure ClearRows;
    { assigns a logging handler to every dataset event }
    procedure HookEvents;
    { assigns a logging handler to OnPostError, OnDeleteError and OnEditError; they answer ErrorAction }
    procedure HookErrors;
    { stand-ins for component streaming }
    procedure SimulateLoading;
    procedure SimulateLoaded;
    { the active-row override: field reads refer to this view row; -1 ends it }
    procedure OverrideRow(ViewIndex: Integer);
    { row-model probes }
    function StoredCount: Integer;
    function StoredRowIds: string;
    function ViewRowIds: string;
    function CursorIndex: Integer;
    { the stored rows / the rows of the view as '[1|one] [2|two]' }
    function StoredText: string;
    function ViewText: string;
    { kind and text of the value the field reads now, e.g. 'vkInt 7' }
    function Probe(Field: TField): string;
  end;

{ The shared log as 'a,b,c'; the log is emptied. }
function TakeLog: string;
procedure ClearLog;
procedure AddLog(const Text: string);

function StateName(State: TDataSetState): string;
function FieldTypeName(DataType: TFieldType): string;
function ValueKindName(Kind: TDBValueKind): string;
function DataEventName(Event: TDataEvent): string;
function DataActionName(Action: TDataAction): string;
{ 'NULL', an integer, a float, the string itself or 'bytes(n)' }
function ValueText(const V: TDBValue): string;
{ '[v1|v2|...]' }
function RowText(Row: TDataRow): string;
function BoolStr(Value: Boolean): string;
{ 'Bof' / 'Eof' / 'Bof+Eof' / '-' }
function Flags(DataSet: TDataSet): string;

implementation

var
  Log: TStringList;

function TakeLog: string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Log.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Log[I];
  end;
  Log.Clear;
end;

procedure ClearLog;
begin
  Log.Clear;
end;

procedure AddLog(const Text: string);
begin
  Log.Add(Text);
end;

function DataActionName(Action: TDataAction): string;
begin
  case Action of
    daFail: Result := 'daFail';
    daAbort: Result := 'daAbort';
    daRetry: Result := 'daRetry';
  else
    Result := '?';
  end;
end;

function ValueText(const V: TDBValue): string;
begin
  case V.Kind of
    vkNull: Result := 'NULL';
    vkInt: Result := IntToStr(V.IntVal);
    vkFloat: Result := FloatToStr(V.FloatVal);
    vkString: Result := V.StrVal;
    vkBytes: Result := 'bytes(' + IntToStr(Length(V.BytesVal)) + ')';
  else
    Result := '?';
  end;
end;

function RowText(Row: TDataRow): string;
var
  I: Integer;
begin
  Result := '[';
  for I := 0 to High(Row.Values) do
  begin
    if I > 0 then
      Result := Result + '|';
    Result := Result + ValueText(Row.Values[I]);
  end;
  Result := Result + ']';
end;

function RowsText(List: TDataRowList): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to List.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ' ';
    Result := Result + RowText(List[I]);
  end;
end;

function StateName(State: TDataSetState): string;
begin
  case State of
    dsInactive: Result := 'dsInactive';
    dsBrowse: Result := 'dsBrowse';
    dsEdit: Result := 'dsEdit';
    dsInsert: Result := 'dsInsert';
  else
    Result := '?';
  end;
end;

function FieldTypeName(DataType: TFieldType): string;
begin
  case DataType of
    ftUnknown: Result := 'ftUnknown';
    ftString: Result := 'ftString';
    ftSmallint: Result := 'ftSmallint';
    ftInteger: Result := 'ftInteger';
    ftWord: Result := 'ftWord';
    ftBoolean: Result := 'ftBoolean';
    ftFloat: Result := 'ftFloat';
    ftCurrency: Result := 'ftCurrency';
    ftDate: Result := 'ftDate';
    ftTime: Result := 'ftTime';
    ftDateTime: Result := 'ftDateTime';
    ftAutoInc: Result := 'ftAutoInc';
    ftBlob: Result := 'ftBlob';
    ftMemo: Result := 'ftMemo';
    ftLargeint: Result := 'ftLargeint';
  else
    Result := '?';
  end;
end;

function ValueKindName(Kind: TDBValueKind): string;
begin
  case Kind of
    vkNull: Result := 'vkNull';
    vkInt: Result := 'vkInt';
    vkFloat: Result := 'vkFloat';
    vkString: Result := 'vkString';
    vkBytes: Result := 'vkBytes';
  else
    Result := '?';
  end;
end;

function DataEventName(Event: TDataEvent): string;
begin
  case Event of
    deFieldChange: Result := 'deFieldChange';
    deRecordChange: Result := 'deRecordChange';
    deDataSetChange: Result := 'deDataSetChange';
    deDataSetScroll: Result := 'deDataSetScroll';
    deLayoutChange: Result := 'deLayoutChange';
    deUpdateRecord: Result := 'deUpdateRecord';
    deUpdateState: Result := 'deUpdateState';
    deCheckBrowseMode: Result := 'deCheckBrowseMode';
    deFocusControl: Result := 'deFocusControl';
  else
    Result := '?';
  end;
end;

function BoolStr(Value: Boolean): string;
begin
  if Value then
    Result := 'True'
  else
    Result := 'False';
end;

function Flags(DataSet: TDataSet): string;
begin
  if DataSet.Bof and DataSet.Eof then
    Result := 'Bof+Eof'
  else if DataSet.Bof then
    Result := 'Bof'
  else if DataSet.Eof then
    Result := 'Eof'
  else
    Result := '-';
end;

{ TTestDataSet }

constructor TTestDataSet.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FTemplate := TFieldDefs.Create(nil);
  FSeed := TDataRowList.Create(True);
end;

destructor TTestDataSet.Destroy;
begin
  FSeed.Free;
  FTemplate.Free;
  inherited Destroy;
end;

procedure TTestDataSet.AddDef(const Name: string; DataType: TFieldType; Size: Integer; Required: Boolean);
begin
  FTemplate.Add(Name, DataType, Size, Required);
end;

procedure TTestDataSet.ClearDefs;
begin
  FTemplate.Clear;
end;

procedure TTestDataSet.AddRow(const Values: array of TDBValue);
var
  Row: TDataRow;
  I: Integer;
begin
  Row := TDataRow.Create(Length(Values));
  for I := 0 to High(Values) do
    Row.Values[I] := Values[I];
  FSeed.Add(Row);
end;

procedure TTestDataSet.ClearRows;
begin
  FSeed.Clear;
end;

procedure TTestDataSet.Note(const EventName: string);
begin
  Log.Add(EventName);
  if RaiseIn = EventName then
    raise Exception.Create('stop in ' + EventName);
  if AbortIn = EventName then
    Abort;
end;

procedure TTestDataSet.NoteError(const EventName: string; E: EDatabaseError; var Action: TDataAction);
begin
  Log.Add(EventName + '(' + E.Message + ',' + DataActionName(Action) + ')');
  Action := ErrorAction;
end;

procedure TTestDataSet.HandleBeforeInsert(DataSet: TDataSet);
begin
  Note('BeforeInsert');
end;

procedure TTestDataSet.HandleAfterInsert(DataSet: TDataSet);
begin
  Note('AfterInsert');
end;

procedure TTestDataSet.HandleBeforeEdit(DataSet: TDataSet);
begin
  Note('BeforeEdit');
end;

procedure TTestDataSet.HandleAfterEdit(DataSet: TDataSet);
begin
  Note('AfterEdit');
end;

procedure TTestDataSet.HandleBeforePost(DataSet: TDataSet);
begin
  Note('BeforePost');
end;

procedure TTestDataSet.HandleAfterPost(DataSet: TDataSet);
begin
  Note('AfterPost');
end;

procedure TTestDataSet.HandleBeforeCancel(DataSet: TDataSet);
begin
  Note('BeforeCancel');
end;

procedure TTestDataSet.HandleAfterCancel(DataSet: TDataSet);
begin
  Note('AfterCancel');
end;

procedure TTestDataSet.HandleBeforeDelete(DataSet: TDataSet);
begin
  Note('BeforeDelete');
end;

procedure TTestDataSet.HandleAfterDelete(DataSet: TDataSet);
begin
  Note('AfterDelete');
end;

procedure TTestDataSet.HandleBeforeRefresh(DataSet: TDataSet);
begin
  Note('BeforeRefresh');
end;

procedure TTestDataSet.HandleAfterRefresh(DataSet: TDataSet);
begin
  Note('AfterRefresh');
end;

procedure TTestDataSet.HandleNewRecord(DataSet: TDataSet);
begin
  Note('OnNewRecord');
end;

procedure TTestDataSet.HandlePostError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  NoteError('OnPostError', E, Action);
end;

procedure TTestDataSet.HandleDeleteError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  NoteError('OnDeleteError', E, Action);
end;

procedure TTestDataSet.HandleEditError(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction);
begin
  NoteError('OnEditError', E, Action);
end;

procedure TTestDataSet.HandleBeforeOpen(DataSet: TDataSet);
begin
  Note('BeforeOpen');
end;

procedure TTestDataSet.HandleAfterOpen(DataSet: TDataSet);
begin
  Note('AfterOpen');
end;

procedure TTestDataSet.HandleBeforeClose(DataSet: TDataSet);
begin
  Note('BeforeClose');
end;

procedure TTestDataSet.HandleAfterClose(DataSet: TDataSet);
begin
  Note('AfterClose');
end;

procedure TTestDataSet.HandleBeforeScroll(DataSet: TDataSet);
begin
  Note('BeforeScroll');
end;

procedure TTestDataSet.HandleAfterScroll(DataSet: TDataSet);
begin
  Note('AfterScroll');
end;

procedure TTestDataSet.HookEvents;
begin
  BeforeOpen := HandleBeforeOpen;
  AfterOpen := HandleAfterOpen;
  BeforeClose := HandleBeforeClose;
  AfterClose := HandleAfterClose;
  BeforeScroll := HandleBeforeScroll;
  AfterScroll := HandleAfterScroll;
  BeforeInsert := HandleBeforeInsert;
  AfterInsert := HandleAfterInsert;
  BeforeEdit := HandleBeforeEdit;
  AfterEdit := HandleAfterEdit;
  BeforePost := HandleBeforePost;
  AfterPost := HandleAfterPost;
  BeforeCancel := HandleBeforeCancel;
  AfterCancel := HandleAfterCancel;
  BeforeDelete := HandleBeforeDelete;
  AfterDelete := HandleAfterDelete;
  BeforeRefresh := HandleBeforeRefresh;
  AfterRefresh := HandleAfterRefresh;
  OnNewRecord := HandleNewRecord;
end;

procedure TTestDataSet.HookErrors;
begin
  OnPostError := HandlePostError;
  OnDeleteError := HandleDeleteError;
  OnEditError := HandleEditError;
end;

procedure TTestDataSet.SimulateLoading;
begin
  Loading;
end;

procedure TTestDataSet.SimulateLoaded;
begin
  Loaded;
end;

procedure TTestDataSet.DataEvent(Event: TDataEvent; Info: TObject);
begin
  if Info is TField then
    Log.Add('[' + DataEventName(Event) + ' ' + StateName(State) + ' ' + TField(Info).FieldName + ']')
  else
    Log.Add('[' + DataEventName(Event) + ' ' + StateName(State) + ']');
  inherited DataEvent(Event, Info);
end;

procedure TTestDataSet.DriverOpen;
begin
  Log.Add('DriverOpen');
  if FailIn = 'DriverOpen' then
    DatabaseError('driver open failed');
end;

procedure TTestDataSet.DriverInitFieldDefs;
var
  I: Integer;
begin
  Log.Add('DriverInitFieldDefs');
  if FailIn = 'DriverInitFieldDefs' then
    DatabaseError('driver definitions failed');
  FieldDefs.Clear;
  for I := 0 to FTemplate.Count - 1 do
    FieldDefs.Add(FTemplate[I].Name, FTemplate[I].DataType, FTemplate[I].Size, FTemplate[I].Required);
end;

procedure TTestDataSet.DriverLoadRows;
var
  I, J: Integer;
  Row: TDataRow;
begin
  Log.Add('DriverLoadRows');
  for I := 0 to FSeed.Count - 1 do
  begin
    if (FailIn = 'DriverLoadRows') and (I = 1) then
      DatabaseError('driver load failed');
    Row := AddLoadedRow;
    for J := 0 to High(Row.Values) do
      if J <= High(FSeed[I].Values) then
        Row.Values[J] := FSeed[I].Values[J];
  end;
end;

procedure TTestDataSet.DriverClose;
begin
  Log.Add('DriverClose');
end;

function TTestDataSet.DriverCanModify: Boolean;
begin
  Result := not DriverReadOnly;
end;

function TTestDataSet.DriverFieldUpdatable(Field: TField): Boolean;
begin
  Result := (FixedField = '') or not SameText(Field.FieldName, FixedField);
end;

procedure TTestDataSet.ApplyStep(const StepName: string);
begin
  Log.Add(StepName);
  if FailApply > 0 then
  begin
    FailApply := FailApply - 1;
    DatabaseError('apply failed');
  end;
end;

function TTestDataSet.AssignedText(Buffer: TDataRow): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(Buffer.Values) do
    if IsAssigned(Buffer, I) then
    begin
      if Result <> '' then
        Result := Result + ',';
      Result := Result + IntToStr(I);
    end;
end;

procedure TTestDataSet.DriverEdit(Row: TDataRow);
begin
  ApplyStep('DriverEdit');
end;

procedure TTestDataSet.ApplyInsert(Buffer: TDataRow);
begin
  ApplyStep('ApplyInsert');
  if InsertId > 0 then
  begin
    Buffer.Values[0] := IntValue(InsertId);
    InsertId := InsertId + 1;
  end;
  LastApply := 'insert new=' + RowText(Buffer) + ' assigned=' + AssignedText(Buffer);
end;

procedure TTestDataSet.ApplyUpdate(OldRow, Buffer: TDataRow);
begin
  ApplyStep('ApplyUpdate');
  LastApply := 'update old=' + RowText(OldRow) + ' new=' + RowText(Buffer) + ' assigned=' + AssignedText(Buffer);
end;

procedure TTestDataSet.ApplyDelete(Row: TDataRow);
begin
  ApplyStep('ApplyDelete');
  LastApply := 'delete row=' + RowText(Row);
end;

procedure TTestDataSet.OverrideRow(ViewIndex: Integer);
begin
  SetActiveRow(ViewIndex);
end;

function TTestDataSet.StoredCount: Integer;
begin
  Result := Rows.Count;
end;

function RowIds(List: TDataRowList): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to List.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(List[I].RowId);
  end;
end;

function TTestDataSet.StoredRowIds: string;
begin
  Result := RowIds(Rows);
end;

function TTestDataSet.ViewRowIds: string;
begin
  Result := RowIds(View);
end;

function TTestDataSet.CursorIndex: Integer;
begin
  Result := Cursor;
end;

function TTestDataSet.StoredText: string;
begin
  Result := RowsText(Rows);
end;

function TTestDataSet.ViewText: string;
begin
  Result := RowsText(View);
end;

function TTestDataSet.Probe(Field: TField): string;
var
  V: TDBValue;
begin
  V := GetFieldValue(Field);
  Result := ValueKindName(V.Kind);
  if V.Kind <> vkNull then
    Result := Result + ' ' + ValueText(V);
end;

initialization
  Log := TStringList.Create;
finalization
  Log.Free;
end.
