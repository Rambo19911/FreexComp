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
    procedure Note(const EventName: string);
  protected
    procedure DataEvent(Event: TDataEvent; Info: TObject); override;
    procedure DriverOpen; override;
    procedure DriverInitFieldDefs; override;
    procedure DriverLoadRows; override;
    procedure DriverClose; override;
    function DriverCanModify: Boolean; override;
    procedure ApplyInsert(Buffer: TDataRow); override;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); override;
    procedure ApplyDelete(Row: TDataRow); override;
  public
    { the driver step named here ('DriverOpen', 'DriverInitFieldDefs', 'DriverLoadRows') raises EDatabaseError }
    FailIn: string;
    { the event handler named here ('BeforeOpen', ...) raises Exception after logging }
    RaiseIn: string;
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
    { stand-ins for component streaming }
    procedure SimulateLoading;
    procedure SimulateLoaded;
    { row-model probes }
    function StoredCount: Integer;
    function StoredRowIds: string;
    function ViewRowIds: string;
    function CursorIndex: Integer;
  end;

{ The shared log as 'a,b,c'; the log is emptied. }
function TakeLog: string;
procedure ClearLog;

function StateName(State: TDataSetState): string;
function FieldTypeName(DataType: TFieldType): string;
function ValueKindName(Kind: TDBValueKind): string;
function DataEventName(Event: TDataEvent): string;
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
  Result := True;
end;

procedure TTestDataSet.ApplyInsert(Buffer: TDataRow);
begin
  Log.Add('ApplyInsert');
end;

procedure TTestDataSet.ApplyUpdate(OldRow, Buffer: TDataRow);
begin
  Log.Add('ApplyUpdate');
end;

procedure TTestDataSet.ApplyDelete(Row: TDataRow);
begin
  Log.Add('ApplyDelete');
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

initialization
  Log := TStringList.Create;
finalization
  Log.Free;
end.
