{ DB - the dataset abstraction: TDataSet (state machine, events, field management, navigation API),
  TRowDataSet (fully buffered row list + view, driver hooks), fields, field definitions, EDatabaseError.
  Must not use any UI-framework unit: console programs can do data access. }
unit DB;

interface

uses
  SysUtils, Classes;

resourcestring
  SDatClosed = 'Cannot perform this operation on a closed dataset';
  SDatEmpty = 'Cannot perform this operation on an empty dataset';
  SDatReadOnly = 'Cannot modify a read-only dataset';
  SDatNotEditing = 'Dataset not in edit or insert mode';
  SDatFieldNotFound = 'Field ''%s'' not found';
  SDatDuplicateField = 'Duplicate field name ''%s''';
  SDatFieldReadOnly = 'Field ''%s'' cannot be modified';
  SDatFieldRequired = 'Field ''%s'' must have a value';
  SDatFieldTooLong = 'Value too long for field ''%s'' (maximum %d characters)';
  SDatFieldRange = 'Value of field ''%s'' is out of range';
  SDatFieldAccess = 'Cannot access field ''%s'' as type %s';
  SDatFieldConvert = '''%s'' is not a valid %s value for field ''%s''';
  SDatFieldType = 'Field ''%s'' has an unsupported type';
  SDatTypeMismatch = 'Type mismatch for field ''%s''';
  SDatParamNotFound = 'Parameter ''%s'' not found';
  SDatRecNoRange = 'Record number out of range';
  SDatBadBookmark = 'Bookmark is not valid';
  SDatLocateCount = 'Locate: number of values does not match number of fields';
  SDatFilter = 'Filter error at position %d: %s';
  SDatCalcOp = 'Operation not allowed while calculating fields';
  SDatNotSupported = 'Not supported';
  SDatActiveChange = 'Cannot perform this operation on an open dataset';

type
  TDataSet = class;
  TField = class;

  EDatabaseError = class(Exception);

  TDataSetState = (dsInactive, dsBrowse, dsEdit, dsInsert);
  TFieldType = (ftUnknown, ftString, ftSmallint, ftInteger, ftWord, ftBoolean, ftFloat, ftCurrency,
    ftDate, ftTime, ftDateTime, ftAutoInc, ftBlob, ftMemo, ftLargeint);
  TFieldKind = (fkData, fkCalculated);
  TDataEvent = (deFieldChange, deRecordChange, deDataSetChange, deDataSetScroll, deLayoutChange,
    deUpdateRecord, deUpdateState, deCheckBrowseMode, deFocusControl);
  TDataAction = (daFail, daAbort, daRetry);
  TLocateOption = (loCaseInsensitive, loPartialKey);
  TLocateOptions = set of TLocateOption;
  TFilterOption = (foCaseInsensitive, foNoPartialCompare);
  TFilterOptions = set of TFilterOption;
  TBookmark = TBytes;

  TDataSetNotifyEvent = procedure(DataSet: TDataSet) of object;
  TDataSetErrorEvent = procedure(DataSet: TDataSet; E: EDatabaseError; var Action: TDataAction) of object;
  TFilterRecordEvent = procedure(DataSet: TDataSet; var Accept: Boolean) of object;
  TFieldNotifyEvent = procedure(Sender: TField) of object;
  TFieldGetTextEvent = procedure(Sender: TField; var Text: string; DisplayText: Boolean) of object;
  TFieldSetTextEvent = procedure(Sender: TField; const Text: string) of object;

  { ---- row and value model ---- }

  TDBValueKind = (vkNull, vkInt, vkFloat, vkString, vkBytes);

  { A stored value. Only the member selected by Kind is meaningful. }
  TDBValue = record
    Kind: TDBValueKind;
    IntVal: Int64;
    FloatVal: Double;
    StrVal: string;
    BytesVal: TBytes;
  end;

  { One row: Values is indexed by data-field number - 1; RowId is unique within an open dataset. }
  TDataRow = class
  public
    Values: array of TDBValue;
    RowId: Int64;
    constructor Create(ValueCount: Integer);
  end;

  { An ordered list of rows. The list of all rows owns them; a view refers to rows owned elsewhere. }
  TDataRowList = class
  private
    FList: TList;
    FOwnsRows: Boolean;
    function GetCount: Integer;
    function GetItem(Index: Integer): TDataRow;
  public
    constructor Create(AOwnsRows: Boolean);
    destructor Destroy; override;
    function Add(Row: TDataRow): Integer;
    procedure Insert(Index: Integer; Row: TDataRow);
    procedure Delete(Index: Integer);
    function Remove(Row: TDataRow): Integer;
    function IndexOf(Row: TDataRow): Integer;
    procedure Clear;
    property Count: Integer read GetCount;
    property Items[Index: Integer]: TDataRow read GetItem; default;
    property OwnsRows: Boolean read FOwnsRows;
  end;

  { ---- field definitions ---- }

  TFieldDef = class
  private
    FName: string;
    FDataType: TFieldType;
    FSize: Integer;
    FRequired: Boolean;
  public
    property Name: string read FName;
    property DataType: TFieldType read FDataType;
    property Size: Integer read FSize;
    property Required: Boolean read FRequired;
  end;

  TFieldDefs = class
  private
    FDataSet: TDataSet;
    FItems: TList;
    function GetCount: Integer;
    function GetItem(Index: Integer): TFieldDef;
    procedure CheckInactive;
  public
    constructor Create(ADataSet: TDataSet);
    destructor Destroy; override;
    function Add(const Name: string; DataType: TFieldType; Size: Integer = 0; Required: Boolean = False): TFieldDef;
    procedure Clear;
    function Find(const Name: string): TFieldDef;
    function IndexOf(const Name: string): Integer;
    { Asks the driver for the definitions; the dataset may be closed. Nothing to do on an open dataset. }
    procedure Update;
    property Count: Integer read GetCount;
    property Items[Index: Integer]: TFieldDef read GetItem; default;
    property DataSet: TDataSet read FDataSet;
  end;

  { ---- fields ---- }

  TField = class(TComponent)
  private
    FDataSet: TDataSet;
    FDataType: TFieldType;
    FFieldName: string;
    FFieldKind: TFieldKind;
    FFieldNo: Integer;
    FDisplayLabel: string;
    FDisplayWidth: Integer;
    FAlignment: TAlignment;
    FVisible: Boolean;
    FReadOnly: Boolean;
    FRequired: Boolean;
    FSize: Integer;
    function GetData: TDBValue;
    function GetAsString: string;
    function GetAsInteger: Integer;
    function GetAsLargeInt: Int64;
    function GetAsFloat: Double;
    function GetAsBoolean: Boolean;
    function GetAsDateTime: TDateTime;
    function GetAsBytes: TBytes;
    function GetDisplayLabel: string;
    function GetDisplayWidth: Integer;
    function GetIndex: Integer;
    function IsDisplayLabelStored: Boolean;
    function IsDisplayWidthStored: Boolean;
    procedure SetDataSet(Value: TDataSet);
    procedure SetFieldKind(Value: TFieldKind);
    procedure SetFieldName(const Value: string);
    procedure SetIndex(Value: Integer);
  protected
    procedure AccessError(const TypeName: string);
    procedure ConvertError(const Text, ValueKind: string);
    procedure RangeError;
    { Conversions of a non-null stored value (in the canonical kind of the field type). The base class
      refuses every type; each field class overrides the conversions it allows. }
    function ValueAsString(const V: TDBValue): string; virtual;
    function ValueAsInteger(const V: TDBValue): Integer; virtual;
    function ValueAsLargeInt(const V: TDBValue): Int64; virtual;
    function ValueAsFloat(const V: TDBValue): Double; virtual;
    function ValueAsBoolean(const V: TDBValue): Boolean; virtual;
    function ValueAsDateTime(const V: TDBValue): TDateTime; virtual;
    function ValueAsBytes(const V: TDBValue): TBytes; virtual;
    { text conversions shared by string and memo fields }
    function TextAsInteger(const S: string): Integer;
    function TextAsLargeInt(const S: string): Int64;
    function TextAsFloat(const S: string): Double;
    function TextAsBoolean(const S: string): Boolean;
    function TextAsDateTime(const S: string): TDateTime;
    { integer conversion shared by the integer field classes }
    function IntAsInteger(Value: Int64): Integer;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function IsNull: Boolean;
    property AsString: string read GetAsString;
    property AsInteger: Integer read GetAsInteger;
    property AsLargeInt: Int64 read GetAsLargeInt;
    property AsFloat: Double read GetAsFloat;
    property AsBoolean: Boolean read GetAsBoolean;
    property AsDateTime: TDateTime read GetAsDateTime;
    property AsBytes: TBytes read GetAsBytes;
    property DataSet: TDataSet read FDataSet write SetDataSet;
    property DataType: TFieldType read FDataType;
    property FieldNo: Integer read FFieldNo;
    property Index: Integer read GetIndex write SetIndex;
  published
    property FieldName: string read FFieldName write SetFieldName;
    property FieldKind: TFieldKind read FFieldKind write SetFieldKind default fkData;
    property DisplayLabel: string read GetDisplayLabel write FDisplayLabel stored IsDisplayLabelStored;
    property DisplayWidth: Integer read GetDisplayWidth write FDisplayWidth stored IsDisplayWidthStored;
    property Alignment: TAlignment read FAlignment write FAlignment;
    property Visible: Boolean read FVisible write FVisible default True;
    property ReadOnly: Boolean read FReadOnly write FReadOnly default False;
    property Required: Boolean read FRequired write FRequired default False;
    property Size: Integer read FSize write FSize default 0;
  end;

  TFieldClass = class of TField;

  TStringField = class(TField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
    function ValueAsBytes(const V: TDBValue): TBytes; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TNumericField = class(TField)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TIntegerField = class(TNumericField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TAutoIncField = class(TIntegerField)
  public
    constructor Create(AOwner: TComponent); override;
  published
    property ReadOnly default True;
  end;

  TLargeintField = class(TNumericField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TFloatField = class(TNumericField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TBooleanField = class(TField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TDateTimeField = class(TField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TDateField = class(TDateTimeField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTimeField = class(TDateTimeField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TBlobField = class(TField)
  protected
    function ValueAsBytes(const V: TDBValue): TBytes; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TMemoField = class(TBlobField)
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
    function ValueAsBytes(const V: TDBValue): TBytes; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  { The fields of one dataset, in creation order. }
  TFields = class
  private
    FDataSet: TDataSet;
    FList: TList;
    function GetCount: Integer;
    function GetField(Index: Integer): TField;
    procedure CheckDuplicate(const FieldName: string; Skip: TField);
  public
    constructor Create(ADataSet: TDataSet);
    destructor Destroy; override;
    function FieldByName(const FieldName: string): TField;
    function FindField(const FieldName: string): TField;
    function IndexOf(Field: TField): Integer;
    procedure GetFieldNames(List: TStrings);
    property Count: Integer read GetCount;
    property Fields[Index: Integer]: TField read GetField; default;
    property DataSet: TDataSet read FDataSet;
  end;

  { ---- datasets ---- }

  TDataSet = class(TComponent)
  private
    FState: TDataSetState;
    FFields: TFields;
    FFieldDefs: TFieldDefs;
    FDynamicFields: Boolean;
    FStreamedActive: Boolean;
    FCursor: Integer;
    FBof: Boolean;
    FEof: Boolean;
    FBeforeOpen: TDataSetNotifyEvent;
    FAfterOpen: TDataSetNotifyEvent;
    FBeforeClose: TDataSetNotifyEvent;
    FAfterClose: TDataSetNotifyEvent;
    FBeforeScroll: TDataSetNotifyEvent;
    FAfterScroll: TDataSetNotifyEvent;
    function GetActive: Boolean;
    function GetFieldCount: Integer;
    function GetRecNo: Integer;
    function GetRecordCount: Integer;
    procedure SetActive(Value: Boolean);
    procedure SetRecNo(Value: Integer);
    procedure BeginScroll;
    procedure EndScroll;
    procedure CloseSilently;
    procedure UnbindFields;
  protected
    procedure Loaded; override;
    { Notifies the attached data sources. Info is the field of deFieldChange, else nil. }
    procedure DataEvent(Event: TDataEvent; Info: TObject); virtual;
    procedure CheckActive;
    procedure CheckInactive;
    { Creates the dynamic fields or binds the persistent ones to FieldDefs; called by InternalOpen
      between building the definitions and loading the rows. }
    procedure BindFields;
    { Builds FieldDefs, calls BindFields, loads the rows. A failure may leave resources behind:
      InternalClose is called afterwards. }
    procedure InternalOpen; virtual; abstract;
    { Releases rows and resources; must not raise and must tolerate a half-opened dataset. }
    procedure InternalClose; virtual; abstract;
    procedure InternalInitFieldDefs; virtual; abstract;
    { Number of rows in the view. }
    function GetViewCount: Integer; virtual; abstract;
    { The value a field reads: that of the current row; null when there is no current row. }
    function GetFieldValue(Field: TField): TDBValue; virtual; abstract;
    property Cursor: Integer read FCursor;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure BeforeDestruction; override;
    procedure Open;
    procedure Close;
    procedure CheckBrowseMode;
    procedure First;
    procedure Last;
    procedure Next;
    procedure Prior;
    { Moves up to Distance rows (negative: towards the first row); returns the signed number of rows moved. }
    function MoveBy(Distance: Integer): Integer;
    function IsEmpty: Boolean;
    function FieldByName(const FieldName: string): TField;
    function FindField(const FieldName: string): TField;
    procedure GetFieldNames(List: TStrings);
    property Active: Boolean read GetActive write SetActive;
    property State: TDataSetState read FState;
    property Bof: Boolean read FBof;
    property Eof: Boolean read FEof;
    property RecordCount: Integer read GetRecordCount;
    property RecNo: Integer read GetRecNo write SetRecNo;
    property Fields: TFields read FFields;
    property FieldDefs: TFieldDefs read FFieldDefs;
    property FieldCount: Integer read GetFieldCount;
    property BeforeOpen: TDataSetNotifyEvent read FBeforeOpen write FBeforeOpen;
    property AfterOpen: TDataSetNotifyEvent read FAfterOpen write FAfterOpen;
    property BeforeClose: TDataSetNotifyEvent read FBeforeClose write FBeforeClose;
    property AfterClose: TDataSetNotifyEvent read FAfterClose write FAfterClose;
    property BeforeScroll: TDataSetNotifyEvent read FBeforeScroll write FBeforeScroll;
    property AfterScroll: TDataSetNotifyEvent read FAfterScroll write FAfterScroll;
  end;

  { The fully buffered dataset: all rows in memory; drivers implement only the Driver*/Apply* methods
    and must not fire dataset events themselves. }
  TRowDataSet = class(TDataSet)
  private
    FRows: TDataRowList;
    FView: TDataRowList;
    FNextRowId: Int64;
    FDriverOpened: Boolean;
    procedure BuildView;
  protected
    procedure InternalOpen; override;
    procedure InternalClose; override;
    procedure InternalInitFieldDefs; override;
    function GetViewCount: Integer; override;
    function GetFieldValue(Field: TField): TDBValue; override;
    { called by Open, in this order }
    procedure DriverOpen; virtual; abstract;            { acquire resources; raise on failure }
    procedure DriverInitFieldDefs; virtual; abstract;   { fill FieldDefs (must not touch rows) }
    procedure DriverLoadRows; virtual; abstract;        { call AddLoadedRow for every row }
    procedure DriverClose; virtual; abstract;           { release resources; must not raise }
    { editing }
    function DriverCanModify: Boolean; virtual; abstract;
    function DriverFieldUpdatable(Field: TField): Boolean; virtual;
    procedure ApplyInsert(Buffer: TDataRow); virtual; abstract;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); virtual; abstract;
    procedure ApplyDelete(Row: TDataRow); virtual; abstract;
    { Appends an all-null row and returns it for filling; for DriverLoadRows. }
    function AddLoadedRow: TDataRow;
    property Rows: TDataRowList read FRows;
    property View: TDataRowList read FView;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  end;

procedure DatabaseError(const Msg: string);
procedure DatabaseErrorFmt(const Msg: string; const Args: array of const);

function NullValue: TDBValue;
function IntValue(Value: Int64): TDBValue;
function FloatValue(Value: Double): TDBValue;
function StringValue(const Value: string): TDBValue;
function BytesValue(const Value: TBytes): TDBValue;
{ The kind in which a non-null value of a field of this type is stored. }
function CanonicalKind(DataType: TFieldType): TDBValueKind;
{ The field class that implements a field type; nil for ftUnknown. }
function FieldClassOf(DataType: TFieldType): TFieldClass;

implementation

uses
  Character;

procedure DatabaseError(const Msg: string);
begin
  raise EDatabaseError.Create(Msg);
end;

procedure DatabaseErrorFmt(const Msg: string; const Args: array of const);
begin
  raise EDatabaseError.Create(Format(Msg, Args));
end;

{ Field and definition names: case-insensitive, ordinal, invariant upper-casing. }
function SameName(const A, B: string): Boolean;
begin
  Result := (Length(A) = Length(B)) and (ToUpper(A) = ToUpper(B));
end;

function NullValue: TDBValue;
begin
  Result.Kind := vkNull;
  Result.IntVal := 0;
  Result.FloatVal := 0;
  Result.StrVal := '';
  Result.BytesVal := nil;
end;

function IntValue(Value: Int64): TDBValue;
begin
  Result := NullValue;
  Result.Kind := vkInt;
  Result.IntVal := Value;
end;

function FloatValue(Value: Double): TDBValue;
begin
  Result := NullValue;
  Result.Kind := vkFloat;
  Result.FloatVal := Value;
end;

function StringValue(const Value: string): TDBValue;
begin
  Result := NullValue;
  Result.Kind := vkString;
  Result.StrVal := Value;
end;

function BytesValue(const Value: TBytes): TDBValue;
begin
  Result := NullValue;
  Result.Kind := vkBytes;
  Result.BytesVal := Value;
end;

function CanonicalKind(DataType: TFieldType): TDBValueKind;
begin
  case DataType of
    ftSmallint, ftInteger, ftWord, ftLargeint, ftAutoInc, ftBoolean:
      Result := vkInt;
    ftFloat, ftCurrency, ftDate, ftTime, ftDateTime:
      Result := vkFloat;
    ftString, ftMemo:
      Result := vkString;
    ftBlob:
      Result := vkBytes;
  else
    Result := vkNull;
  end;
end;

function FieldClassOf(DataType: TFieldType): TFieldClass;
begin
  case DataType of
    ftString: Result := TStringField;
    ftSmallint, ftInteger, ftWord: Result := TIntegerField;
    ftAutoInc: Result := TAutoIncField;
    ftLargeint: Result := TLargeintField;
    ftFloat, ftCurrency: Result := TFloatField;
    ftBoolean: Result := TBooleanField;
    ftDateTime: Result := TDateTimeField;
    ftDate: Result := TDateField;
    ftTime: Result := TTimeField;
    ftBlob: Result := TBlobField;
    ftMemo: Result := TMemoField;
  else
    Result := nil;
  end;
end;

{ Persistent fields match definitions by type class: the integer types are mutually compatible, the
  two float types likewise; every other type only matches itself. }
function TypeGroup(DataType: TFieldType): TFieldType;
begin
  case DataType of
    ftSmallint, ftWord, ftAutoInc: Result := ftInteger;
    ftCurrency: Result := ftFloat;
  else
    Result := DataType;
  end;
end;

{ TDataRow }

constructor TDataRow.Create(ValueCount: Integer);
var
  I: Integer;
begin
  inherited Create;
  SetLength(Values, ValueCount);
  for I := 0 to ValueCount - 1 do
    Values[I] := NullValue;
end;

{ TDataRowList }

constructor TDataRowList.Create(AOwnsRows: Boolean);
begin
  inherited Create;
  FList := TList.Create;
  FOwnsRows := AOwnsRows;
end;

destructor TDataRowList.Destroy;
begin
  Clear;
  FList.Free;
  inherited Destroy;
end;

function TDataRowList.GetCount: Integer;
begin
  Result := FList.Count;
end;

function TDataRowList.GetItem(Index: Integer): TDataRow;
begin
  Result := TDataRow(FList[Index]);
end;

function TDataRowList.Add(Row: TDataRow): Integer;
begin
  Result := FList.Add(Row);
end;

procedure TDataRowList.Insert(Index: Integer; Row: TDataRow);
begin
  FList.Insert(Index, Row);
end;

procedure TDataRowList.Delete(Index: Integer);
var
  Row: TDataRow;
begin
  Row := TDataRow(FList[Index]);
  FList.Delete(Index);
  if FOwnsRows then
    Row.Free;
end;

function TDataRowList.Remove(Row: TDataRow): Integer;
begin
  Result := FList.IndexOf(Row);
  if Result >= 0 then
    Delete(Result);
end;

function TDataRowList.IndexOf(Row: TDataRow): Integer;
begin
  Result := FList.IndexOf(Row);
end;

procedure TDataRowList.Clear;
var
  I: Integer;
begin
  if FOwnsRows then
    for I := 0 to FList.Count - 1 do
      TDataRow(FList[I]).Free;
  FList.Clear;
end;

{ TFieldDefs }

constructor TFieldDefs.Create(ADataSet: TDataSet);
begin
  inherited Create;
  FDataSet := ADataSet;
  FItems := TList.Create;
end;

destructor TFieldDefs.Destroy;
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
    TFieldDef(FItems[I]).Free;
  FItems.Free;
  inherited Destroy;
end;

procedure TFieldDefs.CheckInactive;
begin
  if FDataSet <> nil then
    FDataSet.CheckInactive;
end;

function TFieldDefs.GetCount: Integer;
begin
  Result := FItems.Count;
end;

function TFieldDefs.GetItem(Index: Integer): TFieldDef;
begin
  Result := TFieldDef(FItems[Index]);
end;

function TFieldDefs.Add(const Name: string; DataType: TFieldType; Size: Integer; Required: Boolean): TFieldDef;
begin
  CheckInactive;
  if IndexOf(Name) >= 0 then
    DatabaseErrorFmt(SDatDuplicateField, [Name]);
  Result := TFieldDef.Create;
  Result.FName := Name;
  Result.FDataType := DataType;
  Result.FSize := Size;
  Result.FRequired := Required;
  FItems.Add(Result);
end;

procedure TFieldDefs.Clear;
var
  I: Integer;
begin
  CheckInactive;
  for I := 0 to FItems.Count - 1 do
    TFieldDef(FItems[I]).Free;
  FItems.Clear;
end;

function TFieldDefs.IndexOf(const Name: string): Integer;
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
    if SameName(TFieldDef(FItems[I]).FName, Name) then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

function TFieldDefs.Find(const Name: string): TFieldDef;
var
  I: Integer;
begin
  I := IndexOf(Name);
  if I >= 0 then
    Result := TFieldDef(FItems[I])
  else
    Result := nil;
end;

procedure TFieldDefs.Update;
begin
  if (FDataSet <> nil) and not FDataSet.Active then
    FDataSet.InternalInitFieldDefs;
end;

{ TField }

constructor TField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FVisible := True;
  FAlignment := taLeftJustify;
end;

destructor TField.Destroy;
begin
  if FDataSet <> nil then
  begin
    FDataSet.FFields.FList.Remove(Self);
    FDataSet := nil;
  end;
  inherited Destroy;
end;

function TField.GetDisplayLabel: string;
begin
  if FDisplayLabel <> '' then
    Result := FDisplayLabel
  else
    Result := FFieldName;
end;

function TField.IsDisplayLabelStored: Boolean;
begin
  Result := FDisplayLabel <> '';
end;

function TField.GetDisplayWidth: Integer;
begin
  if FDisplayWidth > 0 then
    Result := FDisplayWidth
  else
    case FDataType of
      ftString:
        if FSize = 0 then
          Result := 20
        else if FSize > 50 then
          Result := 50
        else
          Result := FSize;
      ftLargeint: Result := 15;
      ftBoolean: Result := 5;
      ftTime: Result := 8;
      ftDateTime: Result := 18;
    else
      Result := 10;
    end;
end;

function TField.IsDisplayWidthStored: Boolean;
begin
  Result := FDisplayWidth > 0;
end;

function TField.GetIndex: Integer;
begin
  if FDataSet <> nil then
    Result := FDataSet.FFields.FList.IndexOf(Self)
  else
    Result := -1;
end;

procedure TField.SetIndex(Value: Integer);
var
  Current: Integer;
begin
  Current := GetIndex;
  if (Current < 0) or (Current = Value) then
    Exit;
  FDataSet.FFields.FList.Move(Current, Value);
  FDataSet.DataEvent(deLayoutChange, nil);
end;

procedure TField.SetDataSet(Value: TDataSet);
begin
  if Value = FDataSet then
    Exit;
  if Value <> nil then
  begin
    Value.CheckInactive;
    Value.FFields.CheckDuplicate(FFieldName, Self);
  end;
  if FDataSet <> nil then
  begin
    FDataSet.CheckInactive;
    FDataSet.FFields.FList.Remove(Self);
    FDataSet := nil;
    FFieldNo := 0;
  end;
  if Value <> nil then
  begin
    Value.FFields.FList.Add(Self);
    FDataSet := Value;
  end;
end;

procedure TField.SetFieldKind(Value: TFieldKind);
begin
  if Value = FFieldKind then
    Exit;
  if FDataSet <> nil then
    FDataSet.CheckInactive;
  FFieldKind := Value;
end;

procedure TField.SetFieldName(const Value: string);
begin
  if Value = FFieldName then
    Exit;
  if FDataSet <> nil then
  begin
    FDataSet.CheckInactive;
    FDataSet.FFields.CheckDuplicate(Value, Self);
  end;
  FFieldName := Value;
end;

{ TField: value access }

function TField.GetData: TDBValue;
begin
  if (FDataSet = nil) or (FDataSet.FState = dsInactive) then
    DatabaseError(SDatClosed);
  Result := FDataSet.GetFieldValue(Self);
end;

function TField.IsNull: Boolean;
begin
  Result := GetData.Kind = vkNull;
end;

function TField.GetAsString: string;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := ''
  else
    Result := ValueAsString(V);
end;

function TField.GetAsInteger: Integer;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := 0
  else
    Result := ValueAsInteger(V);
end;

function TField.GetAsLargeInt: Int64;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := 0
  else
    Result := ValueAsLargeInt(V);
end;

function TField.GetAsFloat: Double;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := 0
  else
    Result := ValueAsFloat(V);
end;

function TField.GetAsBoolean: Boolean;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := False
  else
    Result := ValueAsBoolean(V);
end;

function TField.GetAsDateTime: TDateTime;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := 0
  else
    Result := ValueAsDateTime(V);
end;

function TField.GetAsBytes: TBytes;
var
  V: TDBValue;
begin
  V := GetData;
  if V.Kind = vkNull then
    Result := nil
  else
    Result := ValueAsBytes(V);
end;

procedure TField.AccessError(const TypeName: string);
begin
  DatabaseErrorFmt(SDatFieldAccess, [FFieldName, TypeName]);
end;

procedure TField.ConvertError(const Text, ValueKind: string);
begin
  DatabaseErrorFmt(SDatFieldConvert, [Text, ValueKind, FFieldName]);
end;

procedure TField.RangeError;
begin
  DatabaseErrorFmt(SDatFieldRange, [FFieldName]);
end;

function TField.ValueAsString(const V: TDBValue): string;
begin
  Result := '';
  AccessError('String');
end;

function TField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := 0;
  AccessError('Integer');
end;

function TField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := 0;
  AccessError('LargeInt');
end;

function TField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := 0;
  AccessError('Float');
end;

function TField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := False;
  AccessError('Boolean');
end;

function TField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := 0;
  AccessError('DateTime');
end;

function TField.ValueAsBytes(const V: TDBValue): TBytes;
begin
  Result := nil;
  AccessError('Bytes');
end;

function TField.TextAsInteger(const S: string): Integer;
begin
  if not TryStrToInt(S, Result) then
    ConvertError(S, 'integer');
end;

function TField.TextAsLargeInt(const S: string): Int64;
begin
  if not TryStrToInt64(S, Result) then
    ConvertError(S, 'integer');
end;

function TField.TextAsFloat(const S: string): Double;
begin
  if not TryStrToFloat(S, Result) then
    ConvertError(S, 'floating-point');
end;

function TField.TextAsBoolean(const S: string): Boolean;
begin
  Result := SameText(S, 'True') or (S = '1');
  if not Result and not SameText(S, 'False') and (S <> '0') then
    ConvertError(S, 'boolean');
end;

function TField.TextAsDateTime(const S: string): TDateTime;
begin
  if not TryStrToDateTime(S, Result) then
    ConvertError(S, 'date-time');
end;

function TField.IntAsInteger(Value: Int64): Integer;
begin
  if (Value < -2147483648) or (Value > 2147483647) then
    RangeError;
  Result := Integer(Value);
end;

{ field classes }

constructor TStringField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftString;
end;

function TStringField.ValueAsString(const V: TDBValue): string;
begin
  Result := V.StrVal;
end;

function TStringField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := TextAsInteger(V.StrVal);
end;

function TStringField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := TextAsLargeInt(V.StrVal);
end;

function TStringField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := TextAsFloat(V.StrVal);
end;

function TStringField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := TextAsBoolean(V.StrVal);
end;

function TStringField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := TextAsDateTime(V.StrVal);
end;

function TStringField.ValueAsBytes(const V: TDBValue): TBytes;
begin
  Result := TEncoding.UTF8.GetBytes(V.StrVal);
end;

constructor TNumericField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAlignment := taRightJustify;
end;

constructor TIntegerField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftInteger;
end;

function TIntegerField.ValueAsString(const V: TDBValue): string;
begin
  Result := IntToStr(V.IntVal);
end;

function TIntegerField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := IntAsInteger(V.IntVal);
end;

function TIntegerField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := V.IntVal;
end;

function TIntegerField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := V.IntVal;
end;

function TIntegerField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := V.IntVal <> 0;
end;

constructor TAutoIncField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftAutoInc;
  FReadOnly := True;
end;

constructor TLargeintField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftLargeint;
end;

function TLargeintField.ValueAsString(const V: TDBValue): string;
begin
  Result := IntToStr(V.IntVal);
end;

function TLargeintField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := IntAsInteger(V.IntVal);
end;

function TLargeintField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := V.IntVal;
end;

function TLargeintField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := V.IntVal;
end;

function TLargeintField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := V.IntVal <> 0;
end;

constructor TFloatField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftFloat;
end;

function TFloatField.ValueAsString(const V: TDBValue): string;
begin
  Result := FloatToStr(V.FloatVal);
end;

function TFloatField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := V.FloatVal;
end;

function TFloatField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := V.FloatVal;
end;

constructor TBooleanField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftBoolean;
  FAlignment := taCenter;
end;

function TBooleanField.ValueAsString(const V: TDBValue): string;
begin
  if V.IntVal <> 0 then
    Result := 'True'
  else
    Result := 'False';
end;

function TBooleanField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := Ord(V.IntVal <> 0);
end;

function TBooleanField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := Ord(V.IntVal <> 0);
end;

function TBooleanField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := V.IntVal <> 0;
end;

constructor TDateTimeField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftDateTime;
end;

function TDateTimeField.ValueAsString(const V: TDBValue): string;
begin
  Result := DateTimeToStr(V.FloatVal);
end;

function TDateTimeField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := V.FloatVal;
end;

function TDateTimeField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := V.FloatVal;
end;

constructor TDateField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftDate;
end;

function TDateField.ValueAsString(const V: TDBValue): string;
begin
  Result := DateToStr(V.FloatVal);
end;

{ the date part: the time part is 0 }
function TDateField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := Trunc(V.FloatVal);
end;

constructor TTimeField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftTime;
end;

function TTimeField.ValueAsString(const V: TDBValue): string;
begin
  Result := TimeToStr(V.FloatVal);
end;

{ the time of day: the date part is 0 }
function TTimeField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := Abs(Frac(V.FloatVal));
end;

constructor TBlobField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftBlob;
end;

function TBlobField.ValueAsBytes(const V: TDBValue): TBytes;
begin
  Result := V.BytesVal;
end;

constructor TMemoField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftMemo;
end;

function TMemoField.ValueAsString(const V: TDBValue): string;
begin
  Result := V.StrVal;
end;

function TMemoField.ValueAsInteger(const V: TDBValue): Integer;
begin
  Result := TextAsInteger(V.StrVal);
end;

function TMemoField.ValueAsLargeInt(const V: TDBValue): Int64;
begin
  Result := TextAsLargeInt(V.StrVal);
end;

function TMemoField.ValueAsFloat(const V: TDBValue): Double;
begin
  Result := TextAsFloat(V.StrVal);
end;

function TMemoField.ValueAsBoolean(const V: TDBValue): Boolean;
begin
  Result := TextAsBoolean(V.StrVal);
end;

function TMemoField.ValueAsDateTime(const V: TDBValue): TDateTime;
begin
  Result := TextAsDateTime(V.StrVal);
end;

function TMemoField.ValueAsBytes(const V: TDBValue): TBytes;
begin
  Result := TEncoding.UTF8.GetBytes(V.StrVal);
end;

{ TFields }

constructor TFields.Create(ADataSet: TDataSet);
begin
  inherited Create;
  FDataSet := ADataSet;
  FList := TList.Create;
end;

destructor TFields.Destroy;
begin
  FList.Free;
  inherited Destroy;
end;

function TFields.GetCount: Integer;
begin
  Result := FList.Count;
end;

function TFields.GetField(Index: Integer): TField;
begin
  Result := TField(FList[Index]);
end;

procedure TFields.CheckDuplicate(const FieldName: string; Skip: TField);
var
  Other: TField;
begin
  if FieldName = '' then
    Exit;
  Other := FindField(FieldName);
  if (Other <> nil) and (Other <> Skip) then
    DatabaseErrorFmt(SDatDuplicateField, [FieldName]);
end;

function TFields.FindField(const FieldName: string): TField;
var
  I: Integer;
begin
  for I := 0 to FList.Count - 1 do
  begin
    Result := TField(FList[I]);
    if SameName(Result.FFieldName, FieldName) then
      Exit;
  end;
  Result := nil;
end;

function TFields.FieldByName(const FieldName: string): TField;
begin
  Result := FindField(FieldName);
  if Result = nil then
    DatabaseErrorFmt(SDatFieldNotFound, [FieldName]);
end;

function TFields.IndexOf(Field: TField): Integer;
begin
  Result := FList.IndexOf(Field);
end;

procedure TFields.GetFieldNames(List: TStrings);
var
  I: Integer;
begin
  List.BeginUpdate;
  try
    List.Clear;
    for I := 0 to FList.Count - 1 do
      List.Add(TField(FList[I]).FFieldName);
  finally
    List.EndUpdate;
  end;
end;

{ TDataSet }

constructor TDataSet.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FFields := TFields.Create(Self);
  FFieldDefs := TFieldDefs.Create(Self);
  FCursor := -1;
  FBof := True;
  FEof := True;
end;

{ An active dataset is closed before any destructor runs, while the driver object is still intact.
  When the owner is being destroyed the dataset is already marked csDestroying: no events fire then. }
procedure TDataSet.BeforeDestruction;
begin
  if FState <> dsInactive then
    if csDestroying in ComponentState then
      CloseSilently
    else
      Close;
  inherited BeforeDestruction;
end;

destructor TDataSet.Destroy;
var
  I: Integer;
begin
  if FFields <> nil then
    for I := 0 to FFields.Count - 1 do
      FFields[I].FDataSet := nil;
  FFields.Free;
  FFields := nil;
  FFieldDefs.Free;
  FFieldDefs := nil;
  inherited Destroy;
end;

procedure TDataSet.Loaded;
begin
  inherited Loaded;
  if FStreamedActive then
  begin
    FStreamedActive := False;
    Open;
  end;
end;

procedure TDataSet.DataEvent(Event: TDataEvent; Info: TObject);
begin
end;

procedure TDataSet.CheckActive;
begin
  if FState = dsInactive then
    DatabaseError(SDatClosed);
end;

procedure TDataSet.CheckInactive;
begin
  if FState <> dsInactive then
    DatabaseError(SDatActiveChange);
end;

function TDataSet.GetActive: Boolean;
begin
  Result := FState <> dsInactive;
end;

procedure TDataSet.SetActive(Value: Boolean);
begin
  if csLoading in ComponentState then
    FStreamedActive := Value
  else if Value then
    Open
  else
    Close;
end;

function TDataSet.GetFieldCount: Integer;
begin
  Result := FFields.Count;
end;

function TDataSet.GetRecordCount: Integer;
begin
  CheckActive;
  Result := GetViewCount;
end;

function TDataSet.IsEmpty: Boolean;
begin
  CheckActive;
  Result := GetViewCount = 0;
end;

function TDataSet.FieldByName(const FieldName: string): TField;
begin
  Result := FFields.FieldByName(FieldName);
end;

function TDataSet.FindField(const FieldName: string): TField;
begin
  Result := FFields.FindField(FieldName);
end;

procedure TDataSet.GetFieldNames(List: TStrings);
begin
  FFields.GetFieldNames(List);
end;

procedure TDataSet.BindFields;
var
  I, DefIndex: Integer;
  Def: TFieldDef;
  F: TField;
begin
  for I := 0 to FFieldDefs.Count - 1 do
    if FFieldDefs[I].DataType = ftUnknown then
      DatabaseErrorFmt(SDatFieldType, [FFieldDefs[I].Name]);
  if FFields.Count = 0 then
  begin
    FDynamicFields := True;
    for I := 0 to FFieldDefs.Count - 1 do
    begin
      Def := FFieldDefs[I];
      F := FieldClassOf(Def.DataType).Create(Self);
      F.FDataType := Def.DataType;
      F.FFieldName := Def.Name;
      F.FSize := Def.Size;
      F.FRequired := Def.Required;
      F.FFieldNo := I + 1;
      F.FDataSet := Self;
      FFields.FList.Add(F);
    end;
  end
  else
    for I := 0 to FFields.Count - 1 do
    begin
      F := FFields[I];
      if F.FFieldKind = fkCalculated then
        F.FFieldNo := -1
      else
      begin
        DefIndex := FFieldDefs.IndexOf(F.FFieldName);
        if DefIndex < 0 then
          DatabaseErrorFmt(SDatFieldNotFound, [F.FFieldName]);
        if TypeGroup(FFieldDefs[DefIndex].DataType) <> TypeGroup(F.FDataType) then
          DatabaseErrorFmt(SDatTypeMismatch, [F.FFieldName]);
        F.FFieldNo := DefIndex + 1;
      end;
    end;
end;

{ Dynamic fields are destroyed; persistent fields stay and lose their binding. }
procedure TDataSet.UnbindFields;
var
  I: Integer;
begin
  if FDynamicFields then
  begin
    FDynamicFields := False;
    while FFields.Count > 0 do
      FFields[FFields.Count - 1].Free;
  end
  else
    for I := 0 to FFields.Count - 1 do
      FFields[I].FFieldNo := 0;
end;

procedure TDataSet.Open;
begin
  if FState <> dsInactive then
    Exit;
  if Assigned(FBeforeOpen) then
    FBeforeOpen(Self);
  try
    InternalOpen;
  except
    InternalClose;
    UnbindFields;
    raise;
  end;
  FState := dsBrowse;
  FBof := True;
  FEof := GetViewCount = 0;
  if FEof then
    FCursor := -1
  else
    FCursor := 0;
  DataEvent(deUpdateState, nil);
  if Assigned(FAfterOpen) then
    FAfterOpen(Self);
  if Assigned(FAfterScroll) then
    FAfterScroll(Self);
end;

procedure TDataSet.CloseSilently;
begin
  InternalClose;
  FState := dsInactive;
  UnbindFields;
  FCursor := -1;
  FBof := True;
  FEof := True;
end;

procedure TDataSet.Close;
begin
  if FState = dsInactive then
    Exit;
  if Assigned(FBeforeClose) then
    FBeforeClose(Self);
  CloseSilently;
  DataEvent(deUpdateState, nil);
  if Assigned(FAfterClose) then
    FAfterClose(Self);
end;

procedure TDataSet.CheckBrowseMode;
begin
  CheckActive;
end;

{ Every navigation step: CheckBrowseMode, BeforeScroll, the move, the scroll notification, AfterScroll -
  also when the cursor does not move. }
procedure TDataSet.BeginScroll;
begin
  CheckBrowseMode;
  if Assigned(FBeforeScroll) then
    FBeforeScroll(Self);
end;

procedure TDataSet.EndScroll;
begin
  DataEvent(deDataSetScroll, nil);
  if Assigned(FAfterScroll) then
    FAfterScroll(Self);
end;

procedure TDataSet.First;
var
  N: Integer;
begin
  BeginScroll;
  N := GetViewCount;
  if N = 0 then
    FCursor := -1
  else
    FCursor := 0;
  FBof := True;
  FEof := N = 0;
  EndScroll;
end;

procedure TDataSet.Last;
var
  N: Integer;
begin
  BeginScroll;
  N := GetViewCount;
  FCursor := N - 1;
  FBof := N = 0;
  FEof := True;
  EndScroll;
end;

procedure TDataSet.Next;
begin
  MoveBy(1);
end;

procedure TDataSet.Prior;
begin
  MoveBy(-1);
end;

function TDataSet.MoveBy(Distance: Integer): Integer;
var
  N, Available: Integer;
begin
  Result := 0;
  if Distance = 0 then
  begin
    CheckBrowseMode;
    Exit;
  end;
  BeginScroll;
  N := GetViewCount;
  if N > 0 then
    if Distance > 0 then
    begin
      Available := N - 1 - FCursor;
      if Distance <= Available then
        Result := Distance
      else
        Result := Available;
      FCursor := FCursor + Result;
      FEof := Result < Distance;
      if Result > 0 then
        FBof := False;
    end
    else
    begin
      Available := FCursor;
      if -Distance <= Available then
        Result := Distance
      else
        Result := -Available;
      FCursor := FCursor + Result;
      FBof := -Result < -Distance;
      if Result < 0 then
        FEof := False;
    end;
  EndScroll;
end;

function TDataSet.GetRecNo: Integer;
begin
  CheckActive;
  Result := FCursor + 1;
end;

procedure TDataSet.SetRecNo(Value: Integer);
begin
  CheckBrowseMode;
  if (Value < 1) or (Value > GetViewCount) then
    DatabaseError(SDatRecNoRange);
  if Assigned(FBeforeScroll) then
    FBeforeScroll(Self);
  FCursor := Value - 1;
  FBof := False;
  FEof := False;
  EndScroll;
end;

{ TRowDataSet }

constructor TRowDataSet.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FRows := TDataRowList.Create(True);
  FView := TDataRowList.Create(False);
end;

destructor TRowDataSet.Destroy;
begin
  FView.Free;
  FView := nil;
  FRows.Free;
  FRows := nil;
  inherited Destroy;
end;

function TRowDataSet.DriverFieldUpdatable(Field: TField): Boolean;
begin
  Result := True;
end;

function TRowDataSet.GetViewCount: Integer;
begin
  Result := FView.Count;
end;

function TRowDataSet.GetFieldValue(Field: TField): TDBValue;
begin
  if (Cursor < 0) or (Field.FieldNo < 1) then
    Result := NullValue
  else
    Result := FView[Cursor].Values[Field.FieldNo - 1];
end;

procedure TRowDataSet.InternalInitFieldDefs;
begin
  DriverInitFieldDefs;
end;

procedure TRowDataSet.InternalOpen;
begin
  DriverOpen;
  FDriverOpened := True;
  DriverInitFieldDefs;
  BindFields;
  FNextRowId := 1;
  DriverLoadRows;
  BuildView;
end;

procedure TRowDataSet.InternalClose;
begin
  FView.Clear;
  FRows.Clear;
  if FDriverOpened then
  begin
    FDriverOpened := False;
    DriverClose;
  end;
end;

procedure TRowDataSet.BuildView;
var
  I: Integer;
begin
  FView.Clear;
  for I := 0 to FRows.Count - 1 do
    FView.Add(FRows[I]);
end;

function TRowDataSet.AddLoadedRow: TDataRow;
begin
  Result := TDataRow.Create(FieldDefs.Count);
  Result.RowId := FNextRowId;
  FNextRowId := FNextRowId + 1;
  FRows.Add(Result);
end;

end.
