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

  { One row: Values is indexed by data-field number - 1; RowId is unique within an open dataset.
    Flags are the "assigned in this edit" marks, one per value; only an edit buffer has them. }
  TDataRow = class
  public
    Values: array of TDBValue;
    Flags: array of Boolean;
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
    FOnChange: TFieldNotifyEvent;
    FOnValidate: TFieldNotifyEvent;
    FOnGetText: TFieldGetTextEvent;
    FOnSetText: TFieldSetTextEvent;
    function GetData: TDBValue;
    function GetCanModify: Boolean;
    function GetText: string;
    function GetDisplayText: string;
    function GetShownText(Display: Boolean): string;
    procedure SetAsString(const Value: string);
    procedure SetAsInteger(Value: Integer);
    procedure SetAsLargeInt(Value: Int64);
    procedure SetAsFloat(Value: Double);
    procedure SetAsBoolean(Value: Boolean);
    procedure SetAsDateTime(Value: TDateTime);
    procedure SetAsBytes(const Value: TBytes);
    procedure SetText(const Value: string);
    { steps 1 and 2 of an assignment: the dataset is open and editing, the field may be written }
    procedure CheckWrite;
    { steps 4 to 6 of an assignment: store, OnValidate (restore on failure), OnChange, notification }
    procedure StoreValue(const Value: TDBValue);
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
    { Conversions of a written value into the canonical kind of the field type (the mirror of the
      ValueAs... functions). The base class refuses every type; TypeName is 'Integer' or 'LargeInt'. }
    function StringToValue(const S: string): TDBValue; virtual;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; virtual;
    function FloatToValue(Value: Double): TDBValue; virtual;
    function BooleanToValue(Value: Boolean): TDBValue; virtual;
    function DateTimeToValue(Value: TDateTime): TDBValue; virtual;
    function BytesToValue(const Value: TBytes): TDBValue; virtual;
    { the value of a non-empty text assigned through Text }
    function TextToValue(const S: string): TDBValue; virtual;
    { whether Text := '' sets null (every field class except string and memo) }
    function EmptyTextIsNull: Boolean; virtual;
    { the text of a non-null value for DisplayText (Display) or Text }
    function DefaultText(const V: TDBValue; Display: Boolean): string; virtual;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function IsNull: Boolean;
    { Sets null; like every write it needs a dataset in dsEdit or dsInsert. }
    procedure Clear;
    { Asks the controls showing this field to take the focus. }
    procedure FocusControl;
    property AsString: string read GetAsString write SetAsString;
    property AsInteger: Integer read GetAsInteger write SetAsInteger;
    property AsLargeInt: Int64 read GetAsLargeInt write SetAsLargeInt;
    property AsFloat: Double read GetAsFloat write SetAsFloat;
    property AsBoolean: Boolean read GetAsBoolean write SetAsBoolean;
    property AsDateTime: TDateTime read GetAsDateTime write SetAsDateTime;
    property AsBytes: TBytes read GetAsBytes write SetAsBytes;
    { the edit text and the display text; both ask OnGetText when assigned }
    property Text: string read GetText write SetText;
    property DisplayText: string read GetDisplayText;
    property CanModify: Boolean read GetCanModify;
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
    property OnChange: TFieldNotifyEvent read FOnChange write FOnChange;
    property OnValidate: TFieldNotifyEvent read FOnValidate write FOnValidate;
    property OnGetText: TFieldGetTextEvent read FOnGetText write FOnGetText;
    property OnSetText: TFieldSetTextEvent read FOnSetText write FOnSetText;
  end;

  TFieldClass = class of TField;

  TStringField = class(TField)
  private
    { a string value, after the size check }
    function Sized(const S: string): TDBValue;
  protected
    function StringToValue(const S: string): TDBValue; override;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function BooleanToValue(Value: Boolean): TDBValue; override;
    function DateTimeToValue(Value: TDateTime): TDBValue; override;
    function BytesToValue(const Value: TBytes): TDBValue; override;
    function EmptyTextIsNull: Boolean; override;
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
  private
    FDisplayFormat: string;
  protected
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    { the number picture of DisplayText: placeholders 0 and #, a decimal point, ',' for digit grouping }
    property DisplayFormat: string read FDisplayFormat write FDisplayFormat;
  end;

  TIntegerField = class(TNumericField)
  protected
    function StringToValue(const S: string): TDBValue; override;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function BooleanToValue(Value: Boolean): TDBValue; override;
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
    function StringToValue(const S: string): TDBValue; override;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function BooleanToValue(Value: Boolean): TDBValue; override;
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TFloatField = class(TNumericField)
  private
    FPrecision: Integer;
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
    function StringToValue(const S: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function DateTimeToValue(Value: TDateTime): TDBValue; override;
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    { significant digits of DisplayText when DisplayFormat is empty }
    property Precision: Integer read FPrecision write FPrecision default 15;
  end;

  TBooleanField = class(TField)
  private
    FDisplayValues: string;
    { part 0 (True) or 1 (False) of DisplayValues }
    function DisplayPart(Index: Integer): string;
  protected
    function StringToValue(const S: string): TDBValue; override;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; override;
    function BooleanToValue(Value: Boolean): TDBValue; override;
    function TextToValue(const S: string): TDBValue; override;
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsInteger(const V: TDBValue): Integer; override;
    function ValueAsLargeInt(const V: TDBValue): Int64; override;
    function ValueAsBoolean(const V: TDBValue): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    { 'TrueText;FalseText' for Text and DisplayText; empty: True / False }
    property DisplayValues: string read FDisplayValues write FDisplayValues;
  end;

  TDateTimeField = class(TField)
  private
    FDisplayFormat: string;
  protected
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsFloat(const V: TDBValue): Double; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
    { the part of a written value the field keeps: all of it, the date or the time of day }
    function Normalize(Value: Double): Double; virtual;
    function ParseText(const S: string; out Value: TDateTime): Boolean; virtual;
    function StringToValue(const S: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function DateTimeToValue(Value: TDateTime): TDBValue; override;
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
  public
    constructor Create(AOwner: TComponent); override;
  published
    { the date/time picture of Text and DisplayText }
    property DisplayFormat: string read FDisplayFormat write FDisplayFormat;
  end;

  TDateField = class(TDateTimeField)
  protected
    function Normalize(Value: Double): Double; override;
    function ParseText(const S: string; out Value: TDateTime): Boolean; override;
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTimeField = class(TDateTimeField)
  protected
    function Normalize(Value: Double): Double; override;
    function ParseText(const S: string; out Value: TDateTime): Boolean; override;
    function ValueAsString(const V: TDBValue): string; override;
    function ValueAsDateTime(const V: TDBValue): TDateTime; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TBlobField = class(TField)
  protected
    function ValueAsBytes(const V: TDBValue): TBytes; override;
    function BytesToValue(const Value: TBytes): TDBValue; override;
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
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
    function StringToValue(const S: string): TDBValue; override;
    function IntToValue(Value: Int64; const TypeName: string): TDBValue; override;
    function FloatToValue(Value: Double): TDBValue; override;
    function BooleanToValue(Value: Boolean): TDBValue; override;
    function DateTimeToValue(Value: TDateTime): TDBValue; override;
    function BytesToValue(const Value: TBytes): TDBValue; override;
    function EmptyTextIsNull: Boolean; override;
    function DefaultText(const V: TDBValue; Display: Boolean): string; override;
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
    FModified: Boolean;
    FInNewRecord: Boolean;
    FSavedBof: Boolean;
    FSavedEof: Boolean;
    FBeforeInsert: TDataSetNotifyEvent;
    FAfterInsert: TDataSetNotifyEvent;
    FBeforeEdit: TDataSetNotifyEvent;
    FAfterEdit: TDataSetNotifyEvent;
    FBeforePost: TDataSetNotifyEvent;
    FAfterPost: TDataSetNotifyEvent;
    FBeforeCancel: TDataSetNotifyEvent;
    FAfterCancel: TDataSetNotifyEvent;
    FBeforeDelete: TDataSetNotifyEvent;
    FAfterDelete: TDataSetNotifyEvent;
    FBeforeRefresh: TDataSetNotifyEvent;
    FAfterRefresh: TDataSetNotifyEvent;
    FOnNewRecord: TDataSetNotifyEvent;
    FOnPostError: TDataSetErrorEvent;
    FOnDeleteError: TDataSetErrorEvent;
    FOnEditError: TDataSetErrorEvent;
    FBeforeOpen: TDataSetNotifyEvent;
    FAfterOpen: TDataSetNotifyEvent;
    FBeforeClose: TDataSetNotifyEvent;
    FAfterClose: TDataSetNotifyEvent;
    FBeforeScroll: TDataSetNotifyEvent;
    FAfterScroll: TDataSetNotifyEvent;
    function GetActive: Boolean;
    function GetCanModify: Boolean;
    function GetFieldCount: Integer;
    function GetRecNo: Integer;
    function GetRecordCount: Integer;
    procedure SetActive(Value: Boolean);
    procedure SetRecNo(Value: Integer);
    procedure BeginScroll;
    procedure EndScroll;
    procedure CloseSilently;
    procedure UnbindFields;
    procedure DoInsert(AtEnd: Boolean);
    procedure CheckRequiredFields;
    { A driver error in an apply step: asks the handler. True = repeat the step; False = the caller
      re-raises E; raises EAbort when the handler answers daAbort. }
    function RetryAfterError(E: EDatabaseError; Handler: TDataSetErrorEvent): Boolean;
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
    { ---- editing; the state machine and the events are in the public methods ---- }
    function InternalCanModify: Boolean; virtual; abstract;
    function FieldUpdatable(Field: TField): Boolean; virtual; abstract;
    { Stores a value and its assigned flag in the edit buffer. }
    procedure SetFieldValue(Field: TField; const Value: TDBValue; IsAssigned: Boolean); virtual; abstract;
    function GetFieldAssigned(Field: TField): Boolean; virtual; abstract;
    { Creates the edit buffer as a copy of the current row; may raise EDatabaseError (driver). }
    procedure InternalEdit; virtual; abstract;
    { Creates an all-null edit buffer and remembers the insert position: before the current row, or
      after the last row. }
    procedure InternalInsert(AtEnd: Boolean); virtual; abstract;
    { The driver apply step for the edit buffer; raises EDatabaseError and changes nothing on failure. }
    procedure InternalApply; virtual; abstract;
    { Stores the edit buffer and releases it; returns the view index of the posted row. }
    function InternalPost: Integer; virtual; abstract;
    { Releases the edit buffer. }
    procedure InternalCancel; virtual; abstract;
    { The driver apply step for deleting the current row. }
    procedure InternalApplyDelete; virtual; abstract;
    { Removes the current row from the row list and the view. }
    procedure InternalDelete; virtual; abstract;
    { Loads the rows again and rebuilds the view. }
    procedure InternalRefresh; virtual; abstract;
    property Cursor: Integer read FCursor;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure BeforeDestruction; override;
    procedure Open;
    procedure Close;
    { Loads the rows again; the record number is kept when it still exists. }
    procedure Refresh;
    { In dsEdit / dsInsert: posts a modified record, cancels an unmodified one. }
    procedure CheckBrowseMode;
    procedure Edit;
    procedure Insert;
    procedure Append;
    procedure Post;
    procedure Cancel;
    procedure Delete;
    { Asks the data links to write pending control text into their fields. }
    procedure UpdateRecord;
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
    { True after a field write in the current edit; False on a closed dataset. }
    property Modified: Boolean read FModified;
    { False on a closed dataset. }
    property CanModify: Boolean read GetCanModify;
    property Fields: TFields read FFields;
    property FieldDefs: TFieldDefs read FFieldDefs;
    property FieldCount: Integer read GetFieldCount;
    property BeforeOpen: TDataSetNotifyEvent read FBeforeOpen write FBeforeOpen;
    property AfterOpen: TDataSetNotifyEvent read FAfterOpen write FAfterOpen;
    property BeforeClose: TDataSetNotifyEvent read FBeforeClose write FBeforeClose;
    property AfterClose: TDataSetNotifyEvent read FAfterClose write FAfterClose;
    property BeforeScroll: TDataSetNotifyEvent read FBeforeScroll write FBeforeScroll;
    property AfterScroll: TDataSetNotifyEvent read FAfterScroll write FAfterScroll;
    property BeforeInsert: TDataSetNotifyEvent read FBeforeInsert write FBeforeInsert;
    property AfterInsert: TDataSetNotifyEvent read FAfterInsert write FAfterInsert;
    property BeforeEdit: TDataSetNotifyEvent read FBeforeEdit write FBeforeEdit;
    property AfterEdit: TDataSetNotifyEvent read FAfterEdit write FAfterEdit;
    property BeforePost: TDataSetNotifyEvent read FBeforePost write FBeforePost;
    property AfterPost: TDataSetNotifyEvent read FAfterPost write FAfterPost;
    property BeforeCancel: TDataSetNotifyEvent read FBeforeCancel write FBeforeCancel;
    property AfterCancel: TDataSetNotifyEvent read FAfterCancel write FAfterCancel;
    property BeforeDelete: TDataSetNotifyEvent read FBeforeDelete write FBeforeDelete;
    property AfterDelete: TDataSetNotifyEvent read FAfterDelete write FAfterDelete;
    property BeforeRefresh: TDataSetNotifyEvent read FBeforeRefresh write FBeforeRefresh;
    property AfterRefresh: TDataSetNotifyEvent read FAfterRefresh write FAfterRefresh;
    property OnNewRecord: TDataSetNotifyEvent read FOnNewRecord write FOnNewRecord;
    property OnPostError: TDataSetErrorEvent read FOnPostError write FOnPostError;
    property OnDeleteError: TDataSetErrorEvent read FOnDeleteError write FOnDeleteError;
    property OnEditError: TDataSetErrorEvent read FOnEditError write FOnEditError;
  end;

  { The fully buffered dataset: all rows in memory; drivers implement only the Driver*/Apply* methods
    and must not fire dataset events themselves. }
  TRowDataSet = class(TDataSet)
  private
    FRows: TDataRowList;
    FView: TDataRowList;
    FNextRowId: Int64;
    FDriverOpened: Boolean;
    FBuffer: TDataRow;
    FInsertIndex: Integer;
    procedure BuildView;
    function NewBuffer: TDataRow;
  protected
    function InternalCanModify: Boolean; override;
    function FieldUpdatable(Field: TField): Boolean; override;
    procedure SetFieldValue(Field: TField; const Value: TDBValue; IsAssigned: Boolean); override;
    function GetFieldAssigned(Field: TField): Boolean; override;
    procedure InternalEdit; override;
    procedure InternalInsert(AtEnd: Boolean); override;
    procedure InternalApply; override;
    function InternalPost: Integer; override;
    procedure InternalCancel; override;
    procedure InternalApplyDelete; override;
    procedure InternalDelete; override;
    procedure InternalRefresh; override;
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
    { Called by Edit before the edit buffer is created; may raise EDatabaseError (OnEditError). Default: nothing. }
    procedure DriverEdit(Row: TDataRow); virtual;
    procedure ApplyInsert(Buffer: TDataRow); virtual; abstract;
    procedure ApplyUpdate(OldRow, Buffer: TDataRow); virtual; abstract;
    procedure ApplyDelete(Row: TDataRow); virtual; abstract;
    { Appends an all-null row and returns it for filling; for DriverLoadRows. }
    function AddLoadedRow: TDataRow;
    { Whether the field with this number was written in the current edit; for the apply methods. }
    function IsAssigned(Buffer: TDataRow; FieldNo: Integer): Boolean;
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

{ The number picture of TNumericField.DisplayFormat: '0' = a digit, always shown; '#' = a digit, shown when
  significant; '.' = the decimal point; ',' = group the integer digits in threes. The text before the first
  and after the last of these characters is copied. Separators come from FormatSettings. }
function FormatNumber(const Picture: string; Value: Double): string;
var
  I, First, Last, IntZeros, Decimals, MinDecimals, PointPos: Integer;
  Grouping, AfterPoint, Negative: Boolean;
  C: Char;
  Digits, IntPart, DecPart, Grouped: string;
begin
  First := 0;
  Last := 0;
  for I := 1 to Length(Picture) do
  begin
    C := Picture[I];
    if (C = '0') or (C = '#') or (C = '.') then
    begin
      if First = 0 then
        First := I;
      Last := I;
    end;
  end;
  if First = 0 then
  begin
    Result := FloatToStr(Value);
    Exit;
  end;
  IntZeros := 0;
  Decimals := 0;
  MinDecimals := 0;
  Grouping := False;
  AfterPoint := False;
  for I := First to Last do
  begin
    C := Picture[I];
    if C = '.' then
      AfterPoint := True
    else if C = ',' then
      Grouping := True
    else if (C = '0') or (C = '#') then
      if AfterPoint then
      begin
        Decimals := Decimals + 1;
        if C = '0' then
          MinDecimals := Decimals;
      end
      else if C = '0' then
        IntZeros := IntZeros + 1;
  end;
  Digits := FloatToStrF(Abs(Value), ffFixed, 17, Decimals);
  if Pos('E', Digits) > 0 then
  begin
    Result := FloatToStr(Value);
    Exit;
  end;
  PointPos := Pos(FormatSettings.DecimalSeparator, Digits);
  if PointPos > 0 then
  begin
    IntPart := Copy(Digits, 1, PointPos - 1);
    DecPart := Copy(Digits, PointPos + 1, Length(Digits) - PointPos);
  end
  else
  begin
    IntPart := Digits;
    DecPart := '';
  end;
  while (Length(DecPart) > MinDecimals) and (DecPart[Length(DecPart)] = '0') do
    DecPart := Copy(DecPart, 1, Length(DecPart) - 1);
  while (IntPart <> '') and (IntPart[1] = '0') do
    IntPart := Copy(IntPart, 2, Length(IntPart) - 1);
  Negative := False;
  if Value < 0 then
    for I := 1 to Length(IntPart) + Length(DecPart) do
      if I <= Length(IntPart) then
        Negative := Negative or (IntPart[I] <> '0')
      else
        Negative := Negative or (DecPart[I - Length(IntPart)] <> '0');
  while Length(IntPart) < IntZeros do
    IntPart := '0' + IntPart;
  if Grouping then
  begin
    Grouped := '';
    for I := 1 to Length(IntPart) do
    begin
      if (I > 1) and ((Length(IntPart) - I + 1) mod 3 = 0) then
        Grouped := Grouped + FormatSettings.ThousandSeparator;
      Grouped := Grouped + IntPart[I];
    end;
    IntPart := Grouped;
  end;
  Result := Copy(Picture, 1, First - 1);
  if Negative then
    Result := '-' + Result;
  Result := Result + IntPart;
  if DecPart <> '' then
    Result := Result + FormatSettings.DecimalSeparator + DecPart;
  Result := Result + Copy(Picture, Last + 1, Length(Picture) - Last);
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

{ TField: write access (assignment sequence) }

function TField.GetCanModify: Boolean;
begin
  Result := (FDataSet <> nil) and FDataSet.GetCanModify and not FReadOnly and (FFieldKind = fkData) and
    FDataSet.FieldUpdatable(Self);
end;

procedure TField.CheckWrite;
begin
  if (FDataSet = nil) or (FDataSet.FState = dsInactive) then
    DatabaseError(SDatClosed);
  if FFieldKind = fkCalculated then
    DatabaseErrorFmt(SDatFieldReadOnly, [FFieldName]);
  if not (FDataSet.FState in [dsEdit, dsInsert]) then
    DatabaseError(SDatNotEditing);
  if not FDataSet.FInNewRecord and (FReadOnly or not FDataSet.FieldUpdatable(Self)) then
    DatabaseErrorFmt(SDatFieldReadOnly, [FFieldName]);
end;

procedure TField.StoreValue(const Value: TDBValue);
var
  OldValue: TDBValue;
  OldAssigned, OldModified: Boolean;
begin
  OldValue := FDataSet.GetFieldValue(Self);
  OldAssigned := FDataSet.GetFieldAssigned(Self);
  OldModified := FDataSet.FModified;
  FDataSet.SetFieldValue(Self, Value, True);
  FDataSet.FModified := True;
  if Assigned(FOnValidate) then
    try
      FOnValidate(Self);
    except
      FDataSet.SetFieldValue(Self, OldValue, OldAssigned);
      FDataSet.FModified := OldModified;
      raise;
    end;
  if Assigned(FOnChange) then
    FOnChange(Self);
  FDataSet.DataEvent(deFieldChange, Self);
end;

procedure TField.Clear;
begin
  CheckWrite;
  StoreValue(NullValue);
end;

procedure TField.FocusControl;
begin
  if FDataSet <> nil then
    FDataSet.DataEvent(deFocusControl, Self);
end;

procedure TField.SetAsString(const Value: string);
begin
  CheckWrite;
  StoreValue(StringToValue(Value));
end;

procedure TField.SetAsInteger(Value: Integer);
begin
  CheckWrite;
  StoreValue(IntToValue(Value, 'Integer'));
end;

procedure TField.SetAsLargeInt(Value: Int64);
begin
  CheckWrite;
  StoreValue(IntToValue(Value, 'LargeInt'));
end;

procedure TField.SetAsFloat(Value: Double);
begin
  CheckWrite;
  StoreValue(FloatToValue(Value));
end;

procedure TField.SetAsBoolean(Value: Boolean);
begin
  CheckWrite;
  StoreValue(BooleanToValue(Value));
end;

procedure TField.SetAsDateTime(Value: TDateTime);
begin
  CheckWrite;
  StoreValue(DateTimeToValue(Value));
end;

procedure TField.SetAsBytes(const Value: TBytes);
begin
  CheckWrite;
  StoreValue(BytesToValue(Value));
end;

function TField.StringToValue(const S: string): TDBValue;
begin
  Result := NullValue;
  AccessError('String');
end;

function TField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := NullValue;
  AccessError(TypeName);
end;

function TField.FloatToValue(Value: Double): TDBValue;
begin
  Result := NullValue;
  AccessError('Float');
end;

function TField.BooleanToValue(Value: Boolean): TDBValue;
begin
  Result := NullValue;
  AccessError('Boolean');
end;

function TField.DateTimeToValue(Value: TDateTime): TDBValue;
begin
  Result := NullValue;
  AccessError('DateTime');
end;

function TField.BytesToValue(const Value: TBytes): TDBValue;
begin
  Result := NullValue;
  AccessError('Bytes');
end;

{ TField: Text and DisplayText }

function TField.TextToValue(const S: string): TDBValue;
begin
  Result := StringToValue(S);
end;

function TField.EmptyTextIsNull: Boolean;
begin
  Result := True;
end;

function TField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  Result := ValueAsString(V);
end;

function TField.GetShownText(Display: Boolean): string;
var
  V: TDBValue;
begin
  Result := '';
  if Assigned(FOnGetText) then
    FOnGetText(Self, Result, Display)
  else
  begin
    V := GetData;
    if V.Kind <> vkNull then
      Result := DefaultText(V, Display);
  end;
end;

function TField.GetText: string;
begin
  Result := GetShownText(False);
end;

function TField.GetDisplayText: string;
begin
  Result := GetShownText(True);
end;

procedure TField.SetText(const Value: string);
begin
  if Assigned(FOnSetText) then
    FOnSetText(Self, Value)
  else
  begin
    CheckWrite;
    if (Value = '') and EmptyTextIsNull then
      StoreValue(NullValue)
    else
      StoreValue(TextToValue(Value));
  end;
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

function TStringField.Sized(const S: string): TDBValue;
begin
  if (FSize > 0) and (Length(S) > FSize) then
    DatabaseErrorFmt(SDatFieldTooLong, [FFieldName, FSize]);
  Result := StringValue(S);
end;

function TStringField.StringToValue(const S: string): TDBValue;
begin
  Result := Sized(S);
end;

function TStringField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := Sized(IntToStr(Value));
end;

function TStringField.FloatToValue(Value: Double): TDBValue;
begin
  Result := Sized(FloatToStr(Value));
end;

function TStringField.BooleanToValue(Value: Boolean): TDBValue;
begin
  if Value then
    Result := Sized('True')
  else
    Result := Sized('False');
end;

function TStringField.DateTimeToValue(Value: TDateTime): TDBValue;
begin
  Result := Sized(DateTimeToStr(Value));
end;

function TStringField.BytesToValue(const Value: TBytes): TDBValue;
begin
  Result := Sized(TEncoding.UTF8.GetString(Value));
end;

function TStringField.EmptyTextIsNull: Boolean;
begin
  Result := False;
end;

constructor TNumericField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FAlignment := taRightJustify;
end;

function TNumericField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  if Display and (FDisplayFormat <> '') then
    Result := FormatNumber(FDisplayFormat, ValueAsFloat(V))
  else
    Result := ValueAsString(V);
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

function TIntegerField.StringToValue(const S: string): TDBValue;
begin
  Result := IntValue(TextAsInteger(S));
end;

function TIntegerField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := IntValue(IntAsInteger(Value));
end;

{ only an integral value in range }
function TIntegerField.FloatToValue(Value: Double): TDBValue;
begin
  if (Value < -2147483648.0) or (Value > 2147483647.0) or (Frac(Value) <> 0) then
    RangeError;
  Result := IntValue(Trunc(Value));
end;

function TIntegerField.BooleanToValue(Value: Boolean): TDBValue;
begin
  Result := IntValue(Ord(Value));
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

function TLargeintField.StringToValue(const S: string): TDBValue;
begin
  Result := IntValue(TextAsLargeInt(S));
end;

function TLargeintField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := IntValue(Value);
end;

{ only an integral value in the 64-bit range }
function TLargeintField.FloatToValue(Value: Double): TDBValue;
begin
  if (Value < -9223372036854775808.0) or (Value >= 9223372036854775808.0) or (Frac(Value) <> 0) then
    RangeError;
  Result := IntValue(Trunc(Value));
end;

function TLargeintField.BooleanToValue(Value: Boolean): TDBValue;
begin
  Result := IntValue(Ord(Value));
end;

constructor TFloatField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftFloat;
  FPrecision := 15;
end;

function TFloatField.StringToValue(const S: string): TDBValue;
begin
  Result := FloatValue(TextAsFloat(S));
end;

function TFloatField.FloatToValue(Value: Double): TDBValue;
begin
  Result := FloatValue(Value);
end;

function TFloatField.DateTimeToValue(Value: TDateTime): TDBValue;
begin
  Result := FloatValue(Value);
end;

function TFloatField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  if not Display then
    Result := FloatToStr(V.FloatVal)
  else if FDisplayFormat <> '' then
    Result := FormatNumber(FDisplayFormat, V.FloatVal)
  else
    Result := FloatToStrF(V.FloatVal, ffGeneral, FPrecision, 0);
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

function TBooleanField.StringToValue(const S: string): TDBValue;
begin
  Result := IntValue(Ord(TextAsBoolean(S)));
end;

function TBooleanField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := IntValue(Ord(Value <> 0));
end;

function TBooleanField.BooleanToValue(Value: Boolean): TDBValue;
begin
  Result := IntValue(Ord(Value));
end;

function TBooleanField.DisplayPart(Index: Integer): string;
var
  P: Integer;
begin
  P := Pos(';', FDisplayValues);
  if P = 0 then
    P := Length(FDisplayValues) + 1;
  if Index = 0 then
    Result := Copy(FDisplayValues, 1, P - 1)
  else
    Result := Copy(FDisplayValues, P + 1, Length(FDisplayValues) - P);
end;

{ the parts of DisplayValues first, then the texts AsString accepts }
function TBooleanField.TextToValue(const S: string): TDBValue;
begin
  if (FDisplayValues <> '') and SameText(S, DisplayPart(0)) then
    Result := IntValue(1)
  else if (FDisplayValues <> '') and SameText(S, DisplayPart(1)) then
    Result := IntValue(0)
  else
    Result := StringToValue(S);
end;

function TBooleanField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  if FDisplayValues = '' then
    Result := ValueAsString(V)
  else if V.IntVal <> 0 then
    Result := DisplayPart(0)
  else
    Result := DisplayPart(1);
end;

constructor TDateTimeField.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FDataType := ftDateTime;
end;

function TDateTimeField.Normalize(Value: Double): Double;
begin
  Result := Value;
end;

function TDateTimeField.ParseText(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToDateTime(S, Value);
end;

function TDateTimeField.StringToValue(const S: string): TDBValue;
var
  D: TDateTime;
begin
  if not ParseText(S, D) then
    ConvertError(S, 'date-time');
  Result := FloatValue(Normalize(D));
end;

function TDateTimeField.FloatToValue(Value: Double): TDBValue;
begin
  Result := FloatValue(Normalize(Value));
end;

function TDateTimeField.DateTimeToValue(Value: TDateTime): TDBValue;
begin
  Result := FloatValue(Normalize(Value));
end;

function TDateTimeField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  if FDisplayFormat <> '' then
    Result := FormatDateTime(FDisplayFormat, V.FloatVal)
  else
    Result := ValueAsString(V);
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

{ the time part is dropped }
function TDateField.Normalize(Value: Double): Double;
begin
  Result := Trunc(Value);
end;

function TDateField.ParseText(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToDate(S, Value);
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

{ only the time of day is kept }
function TTimeField.Normalize(Value: Double): Double;
begin
  Result := Abs(Frac(Value));
end;

function TTimeField.ParseText(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToTime(S, Value);
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

function TBlobField.BytesToValue(const Value: TBytes): TDBValue;
begin
  Result := BytesValue(Value);
end;

function TBlobField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  Result := '(Blob)';
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

function TMemoField.StringToValue(const S: string): TDBValue;
begin
  Result := StringValue(S);
end;

function TMemoField.IntToValue(Value: Int64; const TypeName: string): TDBValue;
begin
  Result := StringValue(IntToStr(Value));
end;

function TMemoField.FloatToValue(Value: Double): TDBValue;
begin
  Result := StringValue(FloatToStr(Value));
end;

function TMemoField.BooleanToValue(Value: Boolean): TDBValue;
begin
  if Value then
    Result := StringValue('True')
  else
    Result := StringValue('False');
end;

function TMemoField.DateTimeToValue(Value: TDateTime): TDBValue;
begin
  Result := StringValue(DateTimeToStr(Value));
end;

function TMemoField.BytesToValue(const Value: TBytes): TDBValue;
begin
  Result := StringValue(TEncoding.UTF8.GetString(Value));
end;

function TMemoField.EmptyTextIsNull: Boolean;
begin
  Result := False;
end;

function TMemoField.DefaultText(const V: TDBValue; Display: Boolean): string;
begin
  Result := V.StrVal;
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
  FModified := False;
  FInNewRecord := False;
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
  if FState in [dsEdit, dsInsert] then
  begin
    DataEvent(deCheckBrowseMode, nil);
    if FModified then
      Post
    else
      Cancel;
  end;
end;

function TDataSet.GetCanModify: Boolean;
begin
  Result := (FState <> dsInactive) and InternalCanModify;
end;

function TDataSet.RetryAfterError(E: EDatabaseError; Handler: TDataSetErrorEvent): Boolean;
var
  Action: TDataAction;
begin
  Result := False;
  if not Assigned(Handler) then
    Exit;
  Action := daFail;
  Handler(Self, E, Action);
  if Action = daAbort then
    Abort;
  Result := Action = daRetry;
end;

procedure TDataSet.Edit;
var
  Done: Boolean;
begin
  CheckActive;
  if FState in [dsEdit, dsInsert] then
    Exit;
  if GetViewCount = 0 then
  begin
    Insert;
    Exit;
  end;
  if not GetCanModify then
    DatabaseError(SDatReadOnly);
  if Assigned(FBeforeEdit) then
    FBeforeEdit(Self);
  repeat
    Done := True;
    try
      InternalEdit;
    except
      on E: EDatabaseError do
      begin
        if not RetryAfterError(E, FOnEditError) then
          raise;
        Done := False;
      end;
    end;
  until Done;
  FModified := False;
  FState := dsEdit;
  DataEvent(deUpdateState, nil);
  if Assigned(FAfterEdit) then
    FAfterEdit(Self);
end;

procedure TDataSet.DoInsert(AtEnd: Boolean);
begin
  CheckBrowseMode;
  if not GetCanModify then
    DatabaseError(SDatReadOnly);
  if Assigned(FBeforeInsert) then
    FBeforeInsert(Self);
  if Assigned(FBeforeScroll) then
    FBeforeScroll(Self);
  InternalInsert(AtEnd);
  FSavedBof := FBof;
  FSavedEof := FEof;
  FBof := False;
  FEof := False;
  FModified := False;
  FState := dsInsert;
  DataEvent(deUpdateState, nil);
  { assignments made by OnNewRecord may write read-only fields and do not count as modifications }
  FInNewRecord := True;
  try
    try
      if Assigned(FOnNewRecord) then
        FOnNewRecord(Self);
    finally
      FInNewRecord := False;
      FModified := False;
    end;
  except
    InternalCancel;
    FState := dsBrowse;
    FBof := FSavedBof;
    FEof := FSavedEof;
    DataEvent(deUpdateState, nil);
    raise;
  end;
  DataEvent(deDataSetChange, nil);
  if Assigned(FAfterInsert) then
    FAfterInsert(Self);
  if Assigned(FAfterScroll) then
    FAfterScroll(Self);
end;

procedure TDataSet.Insert;
begin
  DoInsert(False);
end;

procedure TDataSet.Append;
begin
  DoInsert(True);
end;

procedure TDataSet.UpdateRecord;
begin
  CheckActive;
  if not (FState in [dsEdit, dsInsert]) then
    DatabaseError(SDatNotEditing);
  DataEvent(deUpdateRecord, nil);
end;

{ every required data field has a value; an auto-increment field gets its value from the driver }
procedure TDataSet.CheckRequiredFields;
var
  I: Integer;
  F: TField;
begin
  for I := 0 to FFields.Count - 1 do
  begin
    F := FFields[I];
    if F.FRequired and (F.FFieldKind = fkData) and (F.FDataType <> ftAutoInc) and
      (GetFieldValue(F).Kind = vkNull) then
      DatabaseErrorFmt(SDatFieldRequired, [F.FFieldName]);
  end;
end;

procedure TDataSet.Post;
var
  Done: Boolean;
begin
  CheckActive;
  if FState = dsBrowse then
    Exit;
  UpdateRecord;
  CheckRequiredFields;
  if Assigned(FBeforePost) then
    FBeforePost(Self);
  repeat
    Done := True;
    try
      InternalApply;
    except
      on E: EDatabaseError do
      begin
        if not RetryAfterError(E, FOnPostError) then
          raise;
        Done := False;
      end;
    end;
  until Done;
  FCursor := InternalPost;
  FModified := False;
  FState := dsBrowse;
  DataEvent(deUpdateState, nil);
  DataEvent(deDataSetChange, nil);
  if Assigned(FAfterPost) then
    FAfterPost(Self);
end;

procedure TDataSet.Cancel;
var
  WasInsert: Boolean;
begin
  CheckActive;
  if FState = dsBrowse then
    Exit;
  if Assigned(FBeforeCancel) then
    FBeforeCancel(Self);
  WasInsert := FState = dsInsert;
  InternalCancel;
  FModified := False;
  FState := dsBrowse;
  if WasInsert then
  begin
    { back on the row that was current before the insert, with the flags it had }
    FBof := FSavedBof;
    FEof := FSavedEof;
  end;
  DataEvent(deUpdateState, nil);
  DataEvent(deDataSetChange, nil);
  if Assigned(FAfterCancel) then
    FAfterCancel(Self);
  if WasInsert and Assigned(FAfterScroll) then
    FAfterScroll(Self);
end;

procedure TDataSet.Delete;
var
  Done: Boolean;
  N: Integer;
begin
  CheckActive;
  if FState = dsInsert then
  begin
    Cancel;
    Exit;
  end;
  if FState = dsEdit then
    Cancel;
  if GetViewCount = 0 then
    DatabaseError(SDatEmpty);
  if not GetCanModify then
    DatabaseError(SDatReadOnly);
  if Assigned(FBeforeDelete) then
    FBeforeDelete(Self);
  if Assigned(FBeforeScroll) then
    FBeforeScroll(Self);
  repeat
    Done := True;
    try
      InternalApplyDelete;
    except
      on E: EDatabaseError do
      begin
        if not RetryAfterError(E, FOnDeleteError) then
          raise;
        Done := False;
      end;
    end;
  until Done;
  InternalDelete;
  N := GetViewCount;
  if FCursor >= N then
    FCursor := N - 1;
  FBof := N = 0;
  FEof := N = 0;
  DataEvent(deDataSetChange, nil);
  if Assigned(FAfterDelete) then
    FAfterDelete(Self);
  if Assigned(FAfterScroll) then
    FAfterScroll(Self);
end;

procedure TDataSet.Refresh;
var
  OldRecNo, N: Integer;
begin
  CheckBrowseMode;
  if Assigned(FBeforeRefresh) then
    FBeforeRefresh(Self);
  OldRecNo := FCursor + 1;
  try
    InternalRefresh;
  finally
    { also after a failed reload the cursor refers to a row that exists }
    N := GetViewCount;
    if OldRecNo > N then
      FCursor := N - 1
    else if OldRecNo < 1 then
      FCursor := 0
    else
      FCursor := OldRecNo - 1;
    if N = 0 then
      FCursor := -1;
    FBof := N = 0;
    FEof := N = 0;
  end;
  DataEvent(deDataSetChange, nil);
  if Assigned(FAfterRefresh) then
    FAfterRefresh(Self);
  if Assigned(FAfterScroll) then
    FAfterScroll(Self);
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
  if FState = dsInsert then
    Result := 0
  else
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

procedure TRowDataSet.DriverEdit(Row: TDataRow);
begin
end;

function TRowDataSet.InternalCanModify: Boolean;
begin
  Result := DriverCanModify;
end;

function TRowDataSet.FieldUpdatable(Field: TField): Boolean;
begin
  Result := DriverFieldUpdatable(Field);
end;

{ In dsEdit / dsInsert the edit buffer, else the current row. }
function TRowDataSet.GetFieldValue(Field: TField): TDBValue;
begin
  if Field.FieldNo < 1 then
    Result := NullValue
  else if FBuffer <> nil then
    Result := FBuffer.Values[Field.FieldNo - 1]
  else if Cursor < 0 then
    Result := NullValue
  else
    Result := FView[Cursor].Values[Field.FieldNo - 1];
end;

procedure TRowDataSet.SetFieldValue(Field: TField; const Value: TDBValue; IsAssigned: Boolean);
begin
  if (FBuffer = nil) or (Field.FieldNo < 1) then
    Exit;
  FBuffer.Values[Field.FieldNo - 1] := Value;
  FBuffer.Flags[Field.FieldNo - 1] := IsAssigned;
end;

function TRowDataSet.GetFieldAssigned(Field: TField): Boolean;
begin
  Result := (FBuffer <> nil) and IsAssigned(FBuffer, Field.FieldNo);
end;

function TRowDataSet.IsAssigned(Buffer: TDataRow; FieldNo: Integer): Boolean;
begin
  Result := (FieldNo >= 1) and (FieldNo <= Length(Buffer.Flags)) and Buffer.Flags[FieldNo - 1];
end;

function TRowDataSet.NewBuffer: TDataRow;
var
  I: Integer;
begin
  Result := TDataRow.Create(FieldDefs.Count);
  SetLength(Result.Flags, FieldDefs.Count);
  for I := 0 to High(Result.Flags) do
    Result.Flags[I] := False;
end;

procedure TRowDataSet.InternalEdit;
var
  Row: TDataRow;
  I: Integer;
begin
  Row := FView[Cursor];
  DriverEdit(Row);
  FBuffer := NewBuffer;
  FBuffer.RowId := Row.RowId;
  for I := 0 to High(Row.Values) do
    FBuffer.Values[I] := Row.Values[I];
end;

procedure TRowDataSet.InternalInsert(AtEnd: Boolean);
begin
  if AtEnd or (Cursor < 0) then
    FInsertIndex := FView.Count
  else
    FInsertIndex := Cursor;
  FBuffer := NewBuffer;
end;

procedure TRowDataSet.InternalApply;
begin
  if State = dsInsert then
    ApplyInsert(FBuffer)
  else
    ApplyUpdate(FView[Cursor], FBuffer);
end;

function TRowDataSet.InternalPost: Integer;
var
  Row: TDataRow;
  I, StoreIndex: Integer;
begin
  if State = dsInsert then
  begin
    { the buffer becomes the new row, in the row list just before the view row it was inserted at }
    Row := FBuffer;
    FBuffer := nil;
    SetLength(Row.Flags, 0);
    Row.RowId := FNextRowId;
    FNextRowId := FNextRowId + 1;
    if FInsertIndex < FView.Count then
      StoreIndex := FRows.IndexOf(FView[FInsertIndex])
    else
      StoreIndex := FRows.Count;
    FRows.Insert(StoreIndex, Row);
    FView.Insert(FInsertIndex, Row);
    Result := FInsertIndex;
  end
  else
  begin
    Row := FView[Cursor];
    for I := 0 to High(Row.Values) do
      Row.Values[I] := FBuffer.Values[I];
    FBuffer.Free;
    FBuffer := nil;
    Result := Cursor;
  end;
end;

procedure TRowDataSet.InternalCancel;
begin
  FBuffer.Free;
  FBuffer := nil;
end;

procedure TRowDataSet.InternalApplyDelete;
begin
  ApplyDelete(FView[Cursor]);
end;

procedure TRowDataSet.InternalDelete;
var
  Row: TDataRow;
begin
  Row := FView[Cursor];
  FView.Delete(Cursor);
  FRows.Remove(Row);
end;

{ Row ids are not reused: the counter keeps running. }
procedure TRowDataSet.InternalRefresh;
begin
  FView.Clear;
  FRows.Clear;
  try
    DriverLoadRows;
  finally
    BuildView;
  end;
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
  FBuffer.Free;
  FBuffer := nil;
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
