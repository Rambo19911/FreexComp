// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Classes - persistent objects, lists, strings, streams, components and form streaming (docs: rtl/classes.md). }
unit Classes;

interface

uses
  Types, SysUtils, SyncObjs, TypInfo;

{$POINTERMATH ON}

type
  { section 2: basic types }
  TNotifyEvent = procedure(Sender: TObject) of object;
  TGetStrProc = procedure(const S: string) of object;
  TAlignment = (taLeftJustify, taRightJustify, taCenter);
  TLeftRight = taLeftJustify..taRightJustify;
  TVerticalAlignment = (taAlignTop, taAlignBottom, taVerticalCenter);
  TBiDiMode = (bdLeftToRight, bdRightToLeft, bdRightToLeftNoAlign, bdRightToLeftReadingOnly);
  TShiftStateItem = (ssShift, ssAlt, ssCtrl, ssLeft, ssRight, ssMiddle, ssDouble);
  TShiftState = set of TShiftStateItem;
  THelpContext = -MaxInt..MaxInt;
  TShortCut = Low(Word)..High(Word);
  TSeekOrigin = (soBeginning, soCurrent, soEnd);
  TDuplicates = (dupIgnore, dupAccept, dupError);
  TOperation = (opInsert, opRemove);
  TListNotification = (lnAdded, lnExtracted, lnDeleted);
  TListAssignOp = (laCopy, laAnd, laOr, laXor, laSrcUnique, laDestUnique);
  TComponentName = string;

  { re-exported from Types (section 2) }
  TPoint = Types.TPoint;
  TRect = Types.TRect;
  TSmallPoint = Types.TSmallPoint;
  PPoint = Types.PPoint;
  PRect = Types.PRect;

  EListError = class(Exception);
  EStringListError = class(EListError);
  EStreamError = class(Exception);
  EFCreateError = class(EStreamError);
  EFOpenError = class(EStreamError);
  EFilerError = class(EStreamError);
  EReadError = class(EFilerError);
  EWriteError = class(EFilerError);
  { form-format.md section 5: every reader exception descends from EReadError }
  EClassNotFound = class(EReadError);
  EMethodNotFound = class(EReadError);
  EInvalidImage = class(EFilerError);
  EResNotFound = class(Exception);
  EComponentError = class(Exception);
  EInvalidOperation = class(Exception);
  EThread = class(Exception);
  EOutOfResources = class(EOutOfMemory);
  EBitsError = class(Exception);

  TFiler = class;
  TStream = class;

  { section 3 }
  {$M+}
  TPersistent = class(TObject)
  protected
    procedure AssignTo(Dest: TPersistent); virtual;
    procedure DefineProperties(Filer: TFiler); virtual;
    function GetOwner: TPersistent; virtual;
  public
    procedure Assign(Source: TPersistent); virtual;
    function GetNamePath: string; virtual;
  end;
  {$M-}
  TPersistentClass = class of TPersistent;

  { section 4 }
  PPointerList = ^TPointerList;
  TPointerList = array[0..134217727] of Pointer;
  TListSortCompare = function(Item1, Item2: Pointer): Integer;

  TList = class(TObject)
  private
    FList: PPointerList;
    FCount: Integer;
    FCapacity: Integer;
    function Get(Index: Integer): Pointer;
    procedure Put(Index: Integer; Item: Pointer);
    procedure SetCapacity(NewCapacity: Integer);
    procedure SetCount(NewCount: Integer);
    procedure CheckIndex(Index, Limit: Integer);
  protected
    procedure Notify(Ptr: Pointer; Action: TListNotification); virtual;
    procedure Grow; virtual;
  public
    destructor Destroy; override;
    function Add(Item: Pointer): Integer;
    procedure Clear; virtual;
    procedure Delete(Index: Integer);
    procedure Exchange(Index1, Index2: Integer);
    function Expand: TList;
    function Extract(Item: Pointer): Pointer;
    function First: Pointer;
    function Last: Pointer;
    function IndexOf(Item: Pointer): Integer;
    procedure Insert(Index: Integer; Item: Pointer);
    procedure Move(CurIndex, NewIndex: Integer);
    function Remove(Item: Pointer): Integer;
    procedure Pack;
    procedure Sort(Compare: TListSortCompare);
    procedure Assign(ListA: TList; AOperator: TListAssignOp = laCopy; ListB: TList = nil);
    property Capacity: Integer read FCapacity write SetCapacity;
    property Count: Integer read FCount write SetCount;
    property Items[Index: Integer]: Pointer read Get write Put; default;
    property List: PPointerList read FList;
  end;

  TThreadList = class(TObject)
  private
    FList: TList;
    FLock: TCriticalSection;
    FDuplicates: TDuplicates;
  public
    constructor Create;
    destructor Destroy; override;
    procedure Add(Item: Pointer);
    procedure Clear;
    function LockList: TList;
    procedure Remove(Item: Pointer);
    procedure UnlockList;
    property Duplicates: TDuplicates read FDuplicates write FDuplicates;
  end;

  { section 5.1 }
  TStrings = class(TPersistent)
  private
    FUpdateCount: Integer;
    FNameValueSeparator: Char;
    FQuoteChar: Char;
    FDelimiter: Char;
    FLineBreak: string;
    FStrictDelimiter: Boolean;
    FTrailingLineBreak: Boolean;
    FWriteBOM: Boolean;
    FDefaultEncoding: TEncoding;
    FEncoding: TEncoding;
    function GetName(Index: Integer): string;
    function GetValue(const Name: string): string;
    procedure SetValue(const Name, Value: string);
    function GetValueFromIndex(Index: Integer): string;
    procedure SetValueFromIndex(Index: Integer; const Value: string);
    function GetDelimitedText: string;
    procedure SetDelimitedText(const Value: string);
    function GetCommaText: string;
    procedure SetCommaText(const Value: string);
    procedure SetDefaultEncoding(Value: TEncoding);
  protected
    procedure Error(const Msg: string; Data: Integer);
    function Get(Index: Integer): string; virtual; abstract;
    function GetCount: Integer; virtual; abstract;
    procedure Put(Index: Integer; const S: string); virtual;
    function GetObject(Index: Integer): TObject; virtual;
    procedure PutObject(Index: Integer; AObject: TObject); virtual;
    function GetCapacity: Integer; virtual;
    procedure SetCapacity(NewCapacity: Integer); virtual;
    procedure SetUpdateState(Updating: Boolean); virtual;
    function GetTextStr: string; virtual;
    procedure SetTextStr(const Value: string); virtual;
    function CompareStrings(const S1, S2: string): Integer; virtual;
    property UpdateCount: Integer read FUpdateCount;
  public
    constructor Create;
    destructor Destroy; override;
    function Add(const S: string): Integer; virtual;
    function AddObject(const S: string; AObject: TObject): Integer; virtual;
    procedure Append(const S: string);
    procedure AddStrings(Strings: TStrings); overload; virtual;
    procedure AddStrings(const Strings: array of string); overload;
    procedure Assign(Source: TPersistent); override;
    procedure BeginUpdate;
    procedure EndUpdate;
    procedure Clear; virtual; abstract;
    procedure Delete(Index: Integer); virtual; abstract;
    function Equals(Strings: TStrings): Boolean; reintroduce; overload;
    procedure Exchange(Index1, Index2: Integer); virtual;
    function IndexOf(const S: string): Integer; virtual;
    function IndexOfName(const Name: string): Integer; virtual;
    function IndexOfObject(AObject: TObject): Integer; virtual;
    procedure Insert(Index: Integer; const S: string); virtual; abstract;
    procedure InsertObject(Index: Integer; const S: string; AObject: TObject); virtual;
    procedure Move(CurIndex, NewIndex: Integer); virtual;
    procedure LoadFromFile(const FileName: string); overload; virtual;
    procedure LoadFromFile(const FileName: string; Encoding: TEncoding); overload; virtual;
    procedure LoadFromStream(Stream: TStream); overload; virtual;
    procedure LoadFromStream(Stream: TStream; Encoding: TEncoding); overload; virtual;
    procedure SaveToFile(const FileName: string); overload; virtual;
    procedure SaveToFile(const FileName: string; Encoding: TEncoding); overload; virtual;
    procedure SaveToStream(Stream: TStream); overload; virtual;
    procedure SaveToStream(Stream: TStream; Encoding: TEncoding); overload; virtual;
    function ToStringArray: TArray<string>;
    property Capacity: Integer read GetCapacity write SetCapacity;
    property Count: Integer read GetCount;
    property Strings[Index: Integer]: string read Get write Put; default;
    property Objects[Index: Integer]: TObject read GetObject write PutObject;
    property Names[Index: Integer]: string read GetName;
    property Values[const Name: string]: string read GetValue write SetValue;
    property ValueFromIndex[Index: Integer]: string read GetValueFromIndex write SetValueFromIndex;
    property NameValueSeparator: Char read FNameValueSeparator write FNameValueSeparator;
    property Delimiter: Char read FDelimiter write FDelimiter;
    property QuoteChar: Char read FQuoteChar write FQuoteChar;
    property StrictDelimiter: Boolean read FStrictDelimiter write FStrictDelimiter;
    property DelimitedText: string read GetDelimitedText write SetDelimitedText;
    property CommaText: string read GetCommaText write SetCommaText;
    property LineBreak: string read FLineBreak write FLineBreak;
    property TrailingLineBreak: Boolean read FTrailingLineBreak write FTrailingLineBreak;
    property WriteBOM: Boolean read FWriteBOM write FWriteBOM;
    property DefaultEncoding: TEncoding read FDefaultEncoding write SetDefaultEncoding;
    property Encoding: TEncoding read FEncoding;
    property Text: string read GetTextStr write SetTextStr;
  end;

  { section 5.2 }
  TStringList = class;
  TStringListSortCompare = function(List: TStringList; Index1, Index2: Integer): Integer;

  TStringItem = record
    FString: string;
    FObject: TObject;
  end;

  TStringList = class(TStrings)
  private
    FList: array of TStringItem;
    FCount: Integer;
    FSorted: Boolean;
    FDuplicates: TDuplicates;
    FCaseSensitive: Boolean;
    FOwnsObjects: Boolean;
    FOnChange: TNotifyEvent;
    FOnChanging: TNotifyEvent;
    procedure SetSorted(Value: Boolean);
    procedure SetCaseSensitive(Value: Boolean);
    procedure CheckIndex(Index, Limit: Integer);
    procedure CheckUnsorted;
    procedure InsertItem(Index: Integer; const S: string; AObject: TObject);
    procedure Grow;
    procedure SortBy(Compare: TStringListSortCompare);
  protected
    procedure Changed; virtual;
    procedure Changing; virtual;
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    function GetObject(Index: Integer): TObject; override;
    procedure Put(Index: Integer; const S: string); override;
    procedure PutObject(Index: Integer; AObject: TObject); override;
    function GetCapacity: Integer; override;
    procedure SetCapacity(NewCapacity: Integer); override;
    procedure SetUpdateState(Updating: Boolean); override;
    function CompareStrings(const S1, S2: string): Integer; override;
  public
    constructor Create; overload;
    constructor Create(AOwnsObjects: Boolean); overload;
    constructor Create(ADuplicates: TDuplicates; ASorted, ACaseSensitive: Boolean); overload;
    destructor Destroy; override;
    function Add(const S: string): Integer; override;
    function AddObject(const S: string; AObject: TObject): Integer; override;
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    procedure Exchange(Index1, Index2: Integer); override;
    function Find(const S: string; var Index: Integer): Boolean; virtual;
    function IndexOf(const S: string): Integer; override;
    procedure Insert(Index: Integer; const S: string); override;
    procedure InsertObject(Index: Integer; const S: string; AObject: TObject); override;
    procedure Move(CurIndex, NewIndex: Integer); override;
    procedure Sort; virtual;
    procedure CustomSort(Compare: TStringListSortCompare); virtual;
    property Duplicates: TDuplicates read FDuplicates write FDuplicates;
    property Sorted: Boolean read FSorted write SetSorted;
    property CaseSensitive: Boolean read FCaseSensitive write SetCaseSensitive;
    property OwnsObjects: Boolean read FOwnsObjects write FOwnsObjects;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnChanging: TNotifyEvent read FOnChanging write FOnChanging;
  end;

  { section 7 }
  TCollection = class;
  TCollectionNotification = (cnAdded, cnExtracting, cnDeleting);

  TCollectionItem = class(TPersistent)
  private
    FCollection: TCollection;
    FID: Integer;
    function GetIndex: Integer;
  protected
    procedure Changed(AllItems: Boolean);
    function GetOwner: TPersistent; override;
    function GetDisplayName: string; virtual;
    procedure SetCollection(Value: TCollection); virtual;
    procedure SetIndex(Value: Integer); virtual;
    procedure SetDisplayName(const Value: string); virtual;
  public
    constructor Create(Collection: TCollection); virtual;
    destructor Destroy; override;
    function GetNamePath: string; override;
    property Collection: TCollection read FCollection write SetCollection;
    property ID: Integer read FID;
    property Index: Integer read GetIndex write SetIndex;
    property DisplayName: string read GetDisplayName write SetDisplayName;
  end;
  TCollectionItemClass = class of TCollectionItem;

  TCollection = class(TPersistent)
  private
    FItemClass: TCollectionItemClass;
    FItems: TList;
    FUpdateCount: Integer;
    FNextID: Integer;
    function GetCount: Integer;
    procedure InsertItem(Item: TCollectionItem);
    procedure RemoveItem(Item: TCollectionItem);
  protected
    procedure Changed;
    function GetItem(Index: Integer): TCollectionItem;
    procedure SetItem(Index: Integer; Value: TCollectionItem);
    procedure Update(Item: TCollectionItem); virtual;
    procedure Notify(Item: TCollectionItem; Action: TCollectionNotification); virtual;
    property UpdateCount: Integer read FUpdateCount;
  public
    constructor Create(ItemClass: TCollectionItemClass);
    destructor Destroy; override;
    function Owner: TPersistent;
    function Add: TCollectionItem;
    function Insert(Index: Integer): TCollectionItem;
    procedure Delete(Index: Integer);
    procedure Clear;
    procedure Assign(Source: TPersistent); override;
    procedure BeginUpdate; virtual;
    procedure EndUpdate; virtual;
    function FindItemID(ID: Integer): TCollectionItem;
    property Count: Integer read GetCount;
    property ItemClass: TCollectionItemClass read FItemClass;
    property Items[Index: Integer]: TCollectionItem read GetItem write SetItem;
  end;

  TOwnedCollection = class(TCollection)
  private
    FOwner: TPersistent;
  protected
    function GetOwner: TPersistent; override;
  public
    constructor Create(AOwner: TPersistent; ItemClass: TCollectionItemClass);
  end;

  { section 6.1 }
  TStream = class(TObject)
  private
    function GetPosition: Int64;
    procedure SetPosition(const Pos: Int64);
    procedure SetSize64(const NewSize: Int64);
  protected
    function GetSize: Int64; virtual;
    procedure SetSize(const NewSize: Int64); virtual;
  public
    function Read(var Buffer; Count: Integer): Integer; virtual;
    function Write(const Buffer; Count: Integer): Integer; virtual;
    function Seek(const Offset: Int64; Origin: TSeekOrigin): Int64; virtual; abstract;
    procedure ReadBuffer(var Buffer; Count: Integer);
    procedure WriteBuffer(const Buffer; Count: Integer);
    function CopyFrom(Source: TStream; Count: Int64): Int64;
    property Position: Int64 read GetPosition write SetPosition;
    property Size: Int64 read GetSize write SetSize64;
  end;

  { section 6.2 }
  THandleStream = class(TStream)
  protected
    FHandle: THandle;
    procedure SetSize(const NewSize: Int64); override;
  public
    constructor Create(AHandle: THandle);
    function Read(var Buffer; Count: Integer): Integer; override;
    function Write(const Buffer; Count: Integer): Integer; override;
    function Seek(const Offset: Int64; Origin: TSeekOrigin): Int64; override;
    property Handle: THandle read FHandle;
  end;

  TFileStream = class(THandleStream)
  private
    FFileName: string;
  public
    constructor Create(const AFileName: string; Mode: Word);
    destructor Destroy; override;
    property FileName: string read FFileName;
  end;

  TCustomMemoryStream = class(TStream)
  private
    FMemory: Pointer;
    FSize: NativeInt;
    FPosition: NativeInt;
  protected
    procedure SetPointer(Ptr: Pointer; ASize: NativeInt);
  public
    function Read(var Buffer; Count: Integer): Integer; override;
    function Seek(const Offset: Int64; Origin: TSeekOrigin): Int64; override;
    procedure SaveToStream(Stream: TStream);
    procedure SaveToFile(const FileName: string);
    property Memory: Pointer read FMemory;
  end;

  TMemoryStream = class(TCustomMemoryStream)
  private
    FCapacity: NativeInt;
    procedure SetCapacity(NewCapacity: NativeInt);
  protected
    function Realloc(var NewCapacity: NativeInt): Pointer; virtual;
    procedure SetSize(const NewSize: Int64); override;
    property Capacity: NativeInt read FCapacity write SetCapacity;
  public
    destructor Destroy; override;
    procedure Clear;
    procedure LoadFromStream(Stream: TStream);
    procedure LoadFromFile(const FileName: string);
    function Write(const Buffer; Count: Integer): Integer; override;
  end;

  { RTL-1052 ... RTL-1054: a read-only view of a Win32 resource of a loaded module. The memory belongs to the module
    image: nothing is copied and nothing is freed. Instance 0 is the program; code that wants the resources of its
    own module passes HInstance. ResType is a name or one of the RT_ identifiers of Windows. }
  TResourceStream = class(TCustomMemoryStream)
  private
    procedure Open(Instance: THandle; Name, ResType: PChar; const Shown: string);
  protected
    procedure SetSize(const NewSize: Int64); override;
  public
    constructor Create(Instance: THandle; const ResName: string; ResType: PChar);
    constructor CreateFromID(Instance: THandle; ResID: Integer; ResType: PChar);
    function Write(const Buffer; Count: Integer): Integer; override;
  end;

  TBytesStream = class(TMemoryStream)
  private
    FBytes: TBytes;
  protected
    function Realloc(var NewCapacity: NativeInt): Pointer; override;
  public
    constructor Create(const ABytes: TBytes);
    property Bytes: TBytes read FBytes;
  end;

  TStringStream = class(TBytesStream)
  private
    FEncoding: TEncoding;
    function GetDataString: string;
  public
    constructor Create(const AString: string = ''; AEncoding: TEncoding = nil);
    function ReadString(Count: Integer): string;
    procedure WriteString(const AString: string);
    property DataString: string read GetDataString;
    property Encoding: TEncoding read FEncoding;
  end;

  { section 8 }
  TReader = class;
  TWriter = class;
  TComponent = class;
  TBasicAction = class;
  TBasicActionLink = class;

  TComponentStateItem = (csLoading, csReading, csWriting, csDestroying, csDesigning, csAncestor, csUpdating, csFixups,
    csFreeNotification, csInline, csDesignInstance);
  TComponentState = set of TComponentStateItem;
  TComponentStyleItem = (csInheritable, csCheckPropAvail, csSubComponent, csTransient);
  TComponentStyle = set of TComponentStyleItem;
  TGetChildProc = procedure(Child: TComponent) of object;

  TComponent = class(TPersistent)
  private
    FOwner: TComponent;
    FName: TComponentName;
    FTag: NativeInt;
    FComponents: TList;
    FFreeNotifies: TList;
    FDesignInfo: Integer;
    FComponentState: TComponentState;
    FComponentStyle: TComponentStyle;
    function GetComponent(Index: Integer): TComponent;
    function GetComponentCount: Integer;
    function GetComponentIndex: Integer;
    procedure SetComponentIndex(Value: Integer);
    procedure Insert(AComponent: TComponent);
    procedure Remove(AComponent: TComponent);
    procedure RemoveNotification(AComponent: TComponent);
    procedure BindField(const AName: string; Bind: Boolean);
    procedure ReadDesignLeft(Reader: TReader);
    procedure ReadDesignTop(Reader: TReader);
    procedure WriteDesignLeft(Writer: TWriter);
    procedure WriteDesignTop(Writer: TWriter);
  protected
    procedure Loaded; virtual;
    procedure Notification(AComponent: TComponent; Operation: TOperation); virtual;
    procedure ReadState(Reader: TReader); virtual;
    procedure WriteState(Writer: TWriter); virtual;
    procedure SetName(const NewName: TComponentName); virtual;
    procedure ValidateRename(AComponent: TComponent; const CurName, NewName: string); virtual;
    procedure ValidateContainer(AComponent: TComponent); virtual;
    procedure ValidateInsert(AComponent: TComponent); virtual;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); virtual;
    function GetChildOwner: TComponent; virtual;
    function GetChildParent: TComponent; virtual;
    procedure SetParentComponent(Value: TComponent); virtual;
    procedure DefineProperties(Filer: TFiler); override;
    procedure SetDesigning(Value: Boolean; SetChildren: Boolean = True);
    procedure SetAncestor(Value: Boolean);
    procedure Loading;
    procedure Updating;
    procedure Updated;
  public
    constructor Create(AOwner: TComponent); virtual;
    destructor Destroy; override;
    procedure BeforeDestruction; override;
    procedure DestroyComponents;
    procedure Destroying;
    function FindComponent(const AName: string): TComponent;
    procedure FreeNotification(AComponent: TComponent);
    procedure RemoveFreeNotification(AComponent: TComponent);
    function GetParentComponent: TComponent; virtual;
    function HasParent: Boolean; virtual;
    procedure InsertComponent(AComponent: TComponent);
    procedure RemoveComponent(AComponent: TComponent);
    procedure SetSubComponent(IsSubComponent: Boolean);
    function ExecuteAction(Action: TBasicAction): Boolean; virtual;
    function UpdateAction(Action: TBasicAction): Boolean; virtual;
    function GetNamePath: string; override;
    property Components[Index: Integer]: TComponent read GetComponent;
    property ComponentCount: Integer read GetComponentCount;
    property ComponentIndex: Integer read GetComponentIndex write SetComponentIndex;
    property ComponentState: TComponentState read FComponentState;
    property ComponentStyle: TComponentStyle read FComponentStyle;
    property DesignInfo: Integer read FDesignInfo write FDesignInfo;
    property Owner: TComponent read FOwner;
  published
    property Name: TComponentName read FName write SetName stored False;
    property Tag: NativeInt read FTag write FTag default 0;
  end;
  TComponentClass = class of TComponent;

  { section 8.2 }
  TBasicAction = class(TComponent)
  private
    FOnExecute: TNotifyEvent;
    FOnUpdate: TNotifyEvent;
    FOnChange: TNotifyEvent;
    FClients: TList;
    FActionComponent: TComponent;
    procedure SetActionComponent(Value: TComponent);
  protected
    procedure Change; virtual;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    function HandlesTarget(Target: TObject): Boolean; virtual;
    procedure UpdateTarget(Target: TObject); virtual;
    procedure ExecuteTarget(Target: TObject); virtual;
    function Execute: Boolean; virtual;
    function Update: Boolean; virtual;
    procedure RegisterChanges(Value: TBasicActionLink);
    procedure UnRegisterChanges(Value: TBasicActionLink);
    property ActionComponent: TComponent read FActionComponent write SetActionComponent;
    property OnExecute: TNotifyEvent read FOnExecute write FOnExecute;
    property OnUpdate: TNotifyEvent read FOnUpdate write FOnUpdate;
  end;
  TBasicActionClass = class of TBasicAction;

  TBasicActionLink = class(TObject)
  private
    FAction: TBasicAction;
    FOnChange: TNotifyEvent;
  protected
    procedure AssignClient(AClient: TObject); virtual;
    procedure Change; virtual;
    function IsOnExecuteLinked: Boolean; virtual;
    procedure SetAction(Value: TBasicAction); virtual;
    procedure SetOnExecute(Value: TNotifyEvent); virtual;
  public
    constructor Create(AClient: TObject); virtual;
    destructor Destroy; override;
    function Execute(AComponent: TComponent = nil): Boolean; virtual;
    function Update: Boolean; virtual;
    property Action: TBasicAction read FAction write SetAction;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
  end;
  TBasicActionLinkClass = class of TBasicActionLink;

  { section 9 }
  TIdentToInt = function(const Ident: string; var Int: Integer): Boolean;
  TIntToIdent = function(Int: Integer; var Ident: string): Boolean;
  TIdentMapEntry = record
    Value: Integer;
    Name: string;
  end;

  { section 10.3 }
  TValueType = (vaNull, vaList, vaInt8, vaInt16, vaInt32, vaExtended, vaString, vaIdent, vaFalse, vaTrue,
    vaBinary, vaSet, vaLString, vaNil, vaCollection, vaSingle, vaCurrency, vaDate, vaWString,
    vaInt64, vaUTF8String, vaDouble);
  TReaderProc = procedure(Reader: TReader) of object;
  TWriterProc = procedure(Writer: TWriter) of object;
  TStreamProc = procedure(Stream: TStream) of object;
  TFindMethodEvent = procedure(Reader: TReader; const MethodName: string; var Address: Pointer;
    var Error: Boolean) of object;
  TSetNameEvent = procedure(Reader: TReader; Component: TComponent; var Name: string) of object;
  TReaderError = procedure(Reader: TReader; const Message: string; var Handled: Boolean) of object;
  TFindComponentClassEvent = procedure(Reader: TReader; const ClassName: string;
    var ComponentClass: TComponentClass) of object;
  TCreateComponentEvent = procedure(Reader: TReader; ComponentClass: TComponentClass;
    var Component: TComponent) of object;
  TReferenceNameEvent = procedure(Reader: TReader; var Name: string) of object;

  TFiler = class(TObject)
  private
    FStream: TStream;
    FRoot: TComponent;
    FLookupRoot: TComponent;
    FAncestor: TPersistent;
  public
    constructor Create(Stream: TStream; BufSize: Integer);
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); virtual; abstract;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); virtual; abstract;
    property Root: TComponent read FRoot write FRoot;
    property LookupRoot: TComponent read FLookupRoot;
    property Ancestor: TPersistent read FAncestor write FAncestor;
  end;

  { the token kinds of the form text lexer (RTL-485 ... RTL-491) }
  TFormToken = (ftEOF, ftLineEnd, ftIdent, ftInteger, ftFloat, ftString, ftSymbol);
  TFormLexState = record
    Pos, Line, Col: Integer;
    Tok: TFormToken;
    TokText: string;
    TokStr: string;
    TokInt: Int64;
    TokHex32: Boolean;
    TokFloat: Double;
    TokLine, TokCol, TokPos: Integer;
  end;

  TReader = class(TFiler)
  private
    FText: string;
    FBytes: TBytes;
    FLex: TFormLexState;
    FOwner: TComponent;
    FParent: TComponent;
    FSourceName: string;
    FLoaded: TList;
    FCreated: TList;
    FFixups: TList;
    FPropName: string;
    FPropLine, FPropCol: Integer;
    FValueLine, FValueCol: Integer;
    FMatchName: string;
    FMatched: Boolean;
    FRootCreated: Boolean;
    FScope: TComponent;
    FFormScope: Boolean;
    FApplyRootName: Boolean;
    FRootHeader: string;
    FDeferRootLoaded: Boolean;
    FLevel: Integer;
    FLevelClass: TClass;
    FAncestorClass: TClass;
    FDeferLoaded: Boolean;
    FOnFindMethod: TFindMethodEvent;
    FOnSetName: TSetNameEvent;
    FOnError: TReaderError;
    FOnFindComponentClass: TFindComponentClassEvent;
    FOnCreateComponent: TCreateComponentEvent;
    FOnReferenceName: TReferenceNameEvent;
    function GetLine: Integer;
    function GetColumn: Integer;
    { lexer }
    procedure DecodeText;
    function Peek(Offset: Integer): Char;
    procedure Advance;
    procedure NextToken;
    procedure ScanNumber;
    procedure ScanString;
    function TokenDesc: string;
    procedure SyntaxError(const Detail: string);
    procedure LexError(const Detail: string);
    procedure Unexpected(const What: string);
    procedure Expect(const Symbol: string);
    procedure ExpectLineEnd;
    procedure SkipLineEnds;
    function IsSymbol(const Symbol: string): Boolean;
    function IsKeyword(const Keyword: string): Boolean;
    function ReadIdentToken: string;
    function ReadQualIdent: string;
    procedure ReadBinaryBlock(Stream: TStream);
    { errors }
    procedure Error(const Code, Text: string; Line, Col: Integer);
    procedure TokenValueError;
    { parser }
    procedure ReadProperty(Instance: TPersistent);
    procedure ReadPropValue(Instance: TPersistent; const Path: string);
    procedure ApplyValue(Instance: TPersistent; PropInfo: PPropInfo; const Path: string);
    function ReadOrdinalValue(PropInfo: PPropInfo): Int64;
    procedure ReadReference(Instance: TPersistent; PropInfo: PPropInfo);
    function ResolveReference(const Name: string; Scope: TComponent): TComponent;
    function NamesRoot(const Name: string): Boolean;
    function FieldClass(const Name, TypeName: string): TPersistentClass;
    procedure ResolveFixups;
    procedure ReadStringsValue(Strings: TStrings);
    procedure ReadComponentObject;
    procedure ReadOverrideBlock;
    procedure ReadItemProperties(Item: TPersistent);
    procedure Cleanup;
  public
    constructor Create(Stream: TStream; BufSize: Integer);
    destructor Destroy; override;
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); override;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); override;
    function ReadRootComponent(Root: TComponent): TComponent;
    procedure ReadData(Instance: TComponent);
    function NextValue: TValueType;
    function ReadInteger: Integer;
    function ReadInt64: Int64;
    function ReadFloat: Double;
    function ReadString: string;
    function ReadBoolean: Boolean;
    function ReadIdent: string;
    function ReadChar: Char;
    function ReadSet(SetType: Pointer): Integer;
    procedure ReadListBegin;
    procedure ReadListEnd;
    function EndOfList: Boolean;
    procedure ReadCollection(Collection: TCollection);
    procedure SkipValue;
    procedure PropValueError;
    property Owner: TComponent read FOwner write FOwner;
    property Parent: TComponent read FParent write FParent;
    property SourceName: string read FSourceName write FSourceName;
    property Line: Integer read GetLine;
    property Column: Integer read GetColumn;
    property OnFindMethod: TFindMethodEvent read FOnFindMethod write FOnFindMethod;
    property OnSetName: TSetNameEvent read FOnSetName write FOnSetName;
    property OnError: TReaderError read FOnError write FOnError;
    property OnFindComponentClass: TFindComponentClassEvent read FOnFindComponentClass write FOnFindComponentClass;
    property OnCreateComponent: TCreateComponentEvent read FOnCreateComponent write FOnCreateComponent;
    property OnReferenceName: TReferenceNameEvent read FOnReferenceName write FOnReferenceName;
  end;

  { section 11 }
  TThreadMethod = procedure of object;

  TThread = class(TObject)
  private
    FHandle: THandle;
    FThreadID: Cardinal;
    FCreateSuspended: Boolean;
    FStarted: Boolean;
    FTerminated: Boolean;
    FFinished: Boolean;
    FFreeOnTerminate: Boolean;
    FReturnValue: Integer;
    FFatalException: TObject;
    FOnTerminate: TNotifyEvent;
    procedure CallOnTerminate;
  protected
    procedure Execute; virtual; abstract;
    property Terminated: Boolean read FTerminated;
    property ReturnValue: Integer read FReturnValue write FReturnValue;
  public
    constructor Create(CreateSuspended: Boolean = False);
    destructor Destroy; override;
    procedure AfterConstruction; override;
    procedure Start;
    procedure Terminate;
    function WaitFor: Cardinal;
    class procedure Synchronize(AThread: TThread; AMethod: TThreadMethod); overload;
    procedure Synchronize(AMethod: TThreadMethod); overload;
    class procedure Queue(AThread: TThread; AMethod: TThreadMethod);
    class function CurrentThread: TThread;
    class procedure Sleep(Milliseconds: Integer);
    property FreeOnTerminate: Boolean read FFreeOnTerminate write FFreeOnTerminate;
    property Handle: THandle read FHandle;
    property ThreadID: Cardinal read FThreadID;
    property Finished: Boolean read FFinished;
    property FatalException: TObject read FFatalException;
    property OnTerminate: TNotifyEvent read FOnTerminate write FOnTerminate;
  end;

  { RTL-481: the run-time writer is Phase 8+; declared so that component code compiles }
  TWriter = class(TFiler)
  public
    procedure DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
      HasData: Boolean); override;
    procedure DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc;
      HasData: Boolean); override;
    procedure WriteRootComponent(Root: TComponent);
  end;

const
  fmCreate = $FF00;

function Point(X, Y: Integer): TPoint;
function Rect(Left, Top, Right, Bottom: Integer): TRect;
function Bounds(Left, Top, Width, Height: Integer): TRect;
function SmallPoint(X, Y: SmallInt): TSmallPoint;

{ section 9: class registry and identifier maps }
procedure RegisterClass(AClass: TPersistentClass);
procedure RegisterClasses(const AClasses: array of TPersistentClass);
procedure RegisterClassAlias(AClass: TPersistentClass; const Alias: string);
procedure UnRegisterClass(AClass: TPersistentClass);
procedure UnRegisterClasses(const AClasses: array of TPersistentClass);
function GetClass(const AClassName: string): TPersistentClass;
function FindClass(const ClassName: string): TPersistentClass;
procedure RegisterIntegerConsts(AIntegerType: Pointer; IdentToIntFn: TIdentToInt; IntToIdentFn: TIntToIdent);
function FindIdentToInt(AIntegerType: Pointer): TIdentToInt;
function FindIntToIdent(AIntegerType: Pointer): TIntToIdent;
function IdentToInt(const Ident: string; var Int: Integer; const Map: array of TIdentMapEntry): Boolean;
function IntToIdent(Int: Integer; var Ident: string; const Map: array of TIdentMapEntry): Boolean;

{ section 10.2: embedded forms }
function InitInheritedComponent(Instance: TComponent; RootAncestor: TClass; ApplyRootName: Boolean = True): Boolean;
function InitComponentRes(const ResName: string; Instance: TComponent): Boolean;
function ReadComponentFromText(const Text: string; Instance: TComponent; const SourceName: string = ''): TComponent;

{ section 11: the main thread's side of Synchronize / Queue (RTL-513) }
function CheckSynchronize(Timeout: Integer = 0): Boolean;

var
  { RTL-498: set by Forms to find forms and data modules by name for qualified references }
  FindGlobalComponent: function(const Name: string): TComponent;
  { RTL-511: called by Synchronize / Queue after an entry was added (Forms posts a message to wake its loop) }
  WakeMainThread: TNotifyEvent;
  { RTL-512: receives exceptions of queued methods (set by Forms) }
  ApplicationHandleException: procedure(Sender: TObject) of object;

implementation

uses
  Windows, RTLConsts;

function Point(X, Y: Integer): TPoint;
begin
  Result := Types.Point(X, Y);
end;

function Rect(Left, Top, Right, Bottom: Integer): TRect;
begin
  Result := Types.Rect(Left, Top, Right, Bottom);
end;

function Bounds(Left, Top, Width, Height: Integer): TRect;
begin
  Result := Types.Bounds(Left, Top, Width, Height);
end;

function SmallPoint(X, Y: SmallInt): TSmallPoint;
begin
  Result := Types.SmallPoint(X, Y);
end;

{ ---------------------------------------------------------------- TPersistent (RTL-410, RTL-411) }

procedure TPersistent.AssignTo(Dest: TPersistent);
begin
  raise EConvertError.CreateFmt(SAssignError, [ClassName, Dest.ClassName]);
end;

procedure TPersistent.DefineProperties(Filer: TFiler);
begin
end;

function TPersistent.GetOwner: TPersistent;
begin
  Result := nil;
end;

procedure TPersistent.Assign(Source: TPersistent);
begin
  if Source <> nil then
    Source.AssignTo(Self)
  else
    raise EConvertError.CreateFmt(SAssignNil, [ClassName]);
end;

function TPersistent.GetNamePath: string;
var
  Owner: TPersistent;
begin
  Owner := GetOwner;
  if Owner = nil then
    Result := ClassName
  else
    Result := Owner.GetNamePath + '.' + ClassName;
end;

{ ---------------------------------------------------------------- TList (RTL-415 ... RTL-419) }

destructor TList.Destroy;
begin
  Clear;
  inherited Destroy;
end;

procedure TList.CheckIndex(Index, Limit: Integer);
begin
  if (Index < 0) or (Index > Limit) then
    raise EListError.CreateFmt(SListIndexError, [Index]);
end;

function TList.Get(Index: Integer): Pointer;
begin
  CheckIndex(Index, FCount - 1);
  Result := FList^[Index];
end;

procedure TList.Put(Index: Integer; Item: Pointer);
var
  Old: Pointer;
begin
  CheckIndex(Index, FCount - 1);
  Old := FList^[Index];
  if Item <> Old then
  begin
    FList^[Index] := Item;
    if Old <> nil then
      Notify(Old, lnDeleted);
    if Item <> nil then
      Notify(Item, lnAdded);
  end;
end;

procedure TList.SetCapacity(NewCapacity: Integer);
begin
  if (NewCapacity < FCount) or (NewCapacity > High(TPointerList) + 1) then
    raise EListError.CreateFmt(SListCapacityError, [NewCapacity]);
  if NewCapacity <> FCapacity then
  begin
    ReallocMem(FList, NewCapacity * SizeOf(Pointer));
    FCapacity := NewCapacity;
  end;
end;

procedure TList.SetCount(NewCount: Integer);
var
  I: Integer;
  Item: Pointer;
begin
  if (NewCount < 0) or (NewCount > High(TPointerList) + 1) then
    raise EListError.CreateFmt(SListCountError, [NewCount]);
  if NewCount > FCapacity then
    SetCapacity(NewCount);
  if NewCount > FCount then
    FillChar(FList^[FCount], (NewCount - FCount) * SizeOf(Pointer), 0)
  else
    for I := FCount - 1 downto NewCount do
    begin
      FCount := I;
      Item := FList^[I];
      if Item <> nil then
        Notify(Item, lnDeleted);
    end;
  FCount := NewCount;
end;

procedure TList.Notify(Ptr: Pointer; Action: TListNotification);
begin
end;

procedure TList.Grow;
var
  Delta: Integer;
begin
  if FCapacity > 64 then
    Delta := FCapacity div 4
  else if FCapacity > 8 then
    Delta := 16
  else
    Delta := 4;
  SetCapacity(FCapacity + Delta);
end;

function TList.Add(Item: Pointer): Integer;
begin
  Result := FCount;
  if Result = FCapacity then
    Grow;
  FList^[Result] := Item;
  FCount := FCount + 1;
  if Item <> nil then
    Notify(Item, lnAdded);
end;

procedure TList.Clear;
begin
  SetCount(0);
  SetCapacity(0);
end;

procedure TList.Delete(Index: Integer);
var
  Item: Pointer;
begin
  CheckIndex(Index, FCount - 1);
  Item := FList^[Index];
  FCount := FCount - 1;
  if Index < FCount then
    System.Move(FList^[Index + 1], FList^[Index], (FCount - Index) * SizeOf(Pointer));
  if Item <> nil then
    Notify(Item, lnDeleted);
end;

procedure TList.Exchange(Index1, Index2: Integer);
var
  Item: Pointer;
begin
  CheckIndex(Index1, FCount - 1);
  CheckIndex(Index2, FCount - 1);
  Item := FList^[Index1];
  FList^[Index1] := FList^[Index2];
  FList^[Index2] := Item;
end;

function TList.Expand: TList;
begin
  if FCount = FCapacity then
    Grow;
  Result := Self;
end;

function TList.Extract(Item: Pointer): Pointer;
var
  I: Integer;
begin
  Result := nil;
  I := IndexOf(Item);
  if I >= 0 then
  begin
    Result := Item;
    FCount := FCount - 1;
    if I < FCount then
      System.Move(FList^[I + 1], FList^[I], (FCount - I) * SizeOf(Pointer));
    Notify(Result, lnExtracted);
  end;
end;

function TList.First: Pointer;
begin
  Result := Get(0);
end;

function TList.Last: Pointer;
begin
  Result := Get(FCount - 1);
end;

function TList.IndexOf(Item: Pointer): Integer;
begin
  for Result := 0 to FCount - 1 do
    if FList^[Result] = Item then
      Exit;
  Result := -1;
end;

procedure TList.Insert(Index: Integer; Item: Pointer);
begin
  CheckIndex(Index, FCount);
  if FCount = FCapacity then
    Grow;
  if Index < FCount then
    System.Move(FList^[Index], FList^[Index + 1], (FCount - Index) * SizeOf(Pointer));
  FList^[Index] := Item;
  FCount := FCount + 1;
  if Item <> nil then
    Notify(Item, lnAdded);
end;

procedure TList.Move(CurIndex, NewIndex: Integer);
var
  Item: Pointer;
begin
  if CurIndex <> NewIndex then
  begin
    CheckIndex(NewIndex, FCount - 1);
    Item := Get(CurIndex);
    FList^[CurIndex] := nil;
    Delete(CurIndex);
    Insert(NewIndex, nil);
    FList^[NewIndex] := Item;
  end;
end;

function TList.Remove(Item: Pointer): Integer;
begin
  Result := IndexOf(Item);
  if Result >= 0 then
    Delete(Result);
end;

procedure TList.Pack;
var
  I: Integer;
begin
  for I := FCount - 1 downto 0 do
    if FList^[I] = nil then
      Delete(I);
end;

{ RTL-418: stable merge sort (bottom-up, one scratch buffer) }
procedure MergeSortPointers(List: PPointerList; Count: Integer; Compare: TListSortCompare);
var
  Tmp, Src, Dst, Swap: PPointerList;
  Width, Lo, Mid, Hi, I, J, K: Integer;
begin
  if Count < 2 then
    Exit;
  GetMem(Tmp, Count * SizeOf(Pointer));
  try
    Src := List;
    Dst := Tmp;
    Width := 1;
    while Width < Count do
    begin
      Lo := 0;
      while Lo < Count do
      begin
        Mid := Lo + Width;
        if Mid > Count then
          Mid := Count;
        Hi := Lo + 2 * Width;
        if Hi > Count then
          Hi := Count;
        I := Lo;
        J := Mid;
        K := Lo;
        while (I < Mid) and (J < Hi) do
        begin
          if Compare(Src^[J], Src^[I]) < 0 then
          begin
            Dst^[K] := Src^[J];
            J := J + 1;
          end
          else
          begin
            Dst^[K] := Src^[I];
            I := I + 1;
          end;
          K := K + 1;
        end;
        while I < Mid do
        begin
          Dst^[K] := Src^[I];
          I := I + 1;
          K := K + 1;
        end;
        while J < Hi do
        begin
          Dst^[K] := Src^[J];
          J := J + 1;
          K := K + 1;
        end;
        Lo := Hi;
      end;
      Swap := Src;
      Src := Dst;
      Dst := Swap;
      Width := Width * 2;
    end;
    if Src <> List then
      System.Move(Src^[0], List^[0], Count * SizeOf(Pointer));
  finally
    FreeMem(Tmp);
  end;
end;

procedure TList.Sort(Compare: TListSortCompare);
begin
  if FCount > 1 then
    MergeSortPointers(FList, FCount, Compare);
end;

procedure TList.Assign(ListA: TList; AOperator: TListAssignOp; ListB: TList);
var
  I: Integer;
  Source: TList;
  Temp: TList;
begin
  if ListB <> nil then
  begin
    Assign(ListA);
    Source := ListB;
  end
  else
    Source := ListA;
  case AOperator of
    laCopy:
      begin
        Clear;
        Capacity := Source.Capacity;
        for I := 0 to Source.Count - 1 do
          Add(Source.FList^[I]);
      end;
    laAnd:
      for I := FCount - 1 downto 0 do
        if Source.IndexOf(FList^[I]) = -1 then
          Delete(I);
    laOr:
      for I := 0 to Source.Count - 1 do
        if IndexOf(Source.FList^[I]) = -1 then
          Add(Source.FList^[I]);
    laXor:
      begin
        Temp := TList.Create;
        try
          for I := 0 to Source.Count - 1 do
            if IndexOf(Source.FList^[I]) = -1 then
              Temp.Add(Source.FList^[I]);
          for I := FCount - 1 downto 0 do
            if Source.IndexOf(FList^[I]) <> -1 then
              Delete(I);
          for I := 0 to Temp.Count - 1 do
            Add(Temp.FList^[I]);
        finally
          Temp.Free;
        end;
      end;
    laSrcUnique:
      for I := FCount - 1 downto 0 do
        if Source.IndexOf(FList^[I]) <> -1 then
          Delete(I);
    laDestUnique:
      begin
        Temp := TList.Create;
        try
          for I := Source.Count - 1 downto 0 do
            if IndexOf(Source.FList^[I]) = -1 then
              Temp.Add(Source.FList^[I]);
          Clear;
          for I := Temp.Count - 1 downto 0 do
            Add(Temp.FList^[I]);
        finally
          Temp.Free;
        end;
      end;
  end;
end;

{ ---------------------------------------------------------------- TThreadList }

constructor TThreadList.Create;
begin
  inherited Create;
  FLock := SyncObjs.TCriticalSection.Create;
  FList := TList.Create;
  FDuplicates := dupIgnore;
end;

destructor TThreadList.Destroy;
begin
  LockList;
  try
    FList.Free;
  finally
    UnlockList;
  end;
  FLock.Free;
  inherited Destroy;
end;

procedure TThreadList.Add(Item: Pointer);
begin
  LockList;
  try
    if (FDuplicates = dupAccept) or (FList.IndexOf(Item) = -1) then
      FList.Add(Item)
    else if FDuplicates = dupError then
      raise EListError.CreateFmt(SDuplicateItem, [NativeInt(Item)]);
  finally
    UnlockList;
  end;
end;

procedure TThreadList.Clear;
begin
  LockList;
  try
    FList.Clear;
  finally
    UnlockList;
  end;
end;

function TThreadList.LockList: TList;
begin
  FLock.Enter;
  Result := FList;
end;

procedure TThreadList.Remove(Item: Pointer);
begin
  LockList;
  try
    FList.Remove(Item);
  finally
    UnlockList;
  end;
end;

procedure TThreadList.UnlockList;
begin
  FLock.Leave;
end;

{ ---------------------------------------------------------------- TStrings (section 5.1) }

constructor TStrings.Create;
begin
  inherited Create;
  FNameValueSeparator := '=';
  FQuoteChar := '"';
  FDelimiter := ',';
  FLineBreak := sLineBreak;
  FTrailingLineBreak := True;
  FDefaultEncoding := TEncoding.UTF8;
end;

destructor TStrings.Destroy;
begin
  inherited Destroy;
end;

procedure TStrings.SetDefaultEncoding(Value: TEncoding);
begin
  if Value = nil then
    FDefaultEncoding := TEncoding.UTF8
  else
    FDefaultEncoding := Value;
end;

{ RTL-425 }
procedure TStrings.Error(const Msg: string; Data: Integer);
begin
  raise EStringListError.CreateFmt(Msg, [Data]);
end;

procedure TStrings.Put(Index: Integer; const S: string);
var
  Obj: TObject;
begin
  Obj := GetObject(Index);
  Delete(Index);
  InsertObject(Index, S, Obj);
end;

function TStrings.GetObject(Index: Integer): TObject;
begin
  Result := nil;
end;

procedure TStrings.PutObject(Index: Integer; AObject: TObject);
begin
end;

function TStrings.GetCapacity: Integer;
begin
  Result := GetCount;
end;

procedure TStrings.SetCapacity(NewCapacity: Integer);
begin
end;

procedure TStrings.SetUpdateState(Updating: Boolean);
begin
end;

function TStrings.CompareStrings(const S1, S2: string): Integer;
begin
  Result := CompareText(S1, S2);
end;

function TStrings.Add(const S: string): Integer;
begin
  Result := GetCount;
  Insert(Result, S);
end;

function TStrings.AddObject(const S: string; AObject: TObject): Integer;
begin
  Result := Add(S);
  PutObject(Result, AObject);
end;

procedure TStrings.Append(const S: string);
begin
  Add(S);
end;

procedure TStrings.AddStrings(Strings: TStrings);
var
  I: Integer;
begin
  BeginUpdate;
  try
    for I := 0 to Strings.Count - 1 do
      AddObject(Strings[I], Strings.Objects[I]);
  finally
    EndUpdate;
  end;
end;

procedure TStrings.AddStrings(const Strings: array of string);
var
  I: Integer;
begin
  BeginUpdate;
  try
    for I := 0 to High(Strings) do
      Add(Strings[I]);
  finally
    EndUpdate;
  end;
end;

procedure TStrings.Assign(Source: TPersistent);
var
  S: TStrings;
begin
  if Source is TStrings then
  begin
    S := TStrings(Source);
    BeginUpdate;
    try
      Clear;
      FNameValueSeparator := S.FNameValueSeparator;
      FQuoteChar := S.FQuoteChar;
      FDelimiter := S.FDelimiter;
      FLineBreak := S.FLineBreak;
      FStrictDelimiter := S.FStrictDelimiter;
      AddStrings(S);
    finally
      EndUpdate;
    end;
  end
  else
    inherited Assign(Source);
end;

procedure TStrings.BeginUpdate;
begin
  if FUpdateCount = 0 then
    SetUpdateState(True);
  FUpdateCount := FUpdateCount + 1;
end;

procedure TStrings.EndUpdate;
begin
  FUpdateCount := FUpdateCount - 1;
  if FUpdateCount = 0 then
    SetUpdateState(False);
end;

function TStrings.Equals(Strings: TStrings): Boolean;
var
  I: Integer;
begin
  Result := False;
  if (Strings = nil) or (Strings.Count <> GetCount) then
    Exit;
  for I := 0 to GetCount - 1 do
    if Get(I) <> Strings.Get(I) then
      Exit;
  Result := True;
end;

procedure TStrings.Exchange(Index1, Index2: Integer);
var
  S: string;
  Obj: TObject;
begin
  BeginUpdate;
  try
    S := Strings[Index1];
    Obj := Objects[Index1];
    Strings[Index1] := Strings[Index2];
    Objects[Index1] := Objects[Index2];
    Strings[Index2] := S;
    Objects[Index2] := Obj;
  finally
    EndUpdate;
  end;
end;

function TStrings.IndexOf(const S: string): Integer;
begin
  for Result := 0 to GetCount - 1 do
    if CompareStrings(Get(Result), S) = 0 then
      Exit;
  Result := -1;
end;

{ the part of S before the first separator, or -1 in P when there is none }
function SplitName(const S: string; Sep: Char; out P: Integer): string;
begin
  P := Pos(Sep, S);
  if P > 0 then
    Result := Copy(S, 1, P - 1)
  else
    Result := '';
end;

function TStrings.ToStringArray: TArray<string>;
var
  I: Integer;
begin
  SetLength(Result, Count);
  for I := 0 to Count - 1 do
    Result[I] := Get(I);
end;

function TStrings.IndexOfName(const Name: string): Integer;
var
  P: Integer;
  N: string;
begin
  for Result := 0 to GetCount - 1 do
  begin
    N := SplitName(Get(Result), FNameValueSeparator, P);
    if (P > 0) and (CompareStrings(N, Name) = 0) then
      Exit;
  end;
  Result := -1;
end;

function TStrings.IndexOfObject(AObject: TObject): Integer;
begin
  for Result := 0 to GetCount - 1 do
    if GetObject(Result) = AObject then
      Exit;
  Result := -1;
end;

procedure TStrings.InsertObject(Index: Integer; const S: string; AObject: TObject);
begin
  Insert(Index, S);
  PutObject(Index, AObject);
end;

procedure TStrings.Move(CurIndex, NewIndex: Integer);
var
  S: string;
  Obj: TObject;
begin
  if CurIndex <> NewIndex then
  begin
    BeginUpdate;
    try
      S := Get(CurIndex);
      Obj := GetObject(CurIndex);
      PutObject(CurIndex, nil);
      Delete(CurIndex);
      InsertObject(NewIndex, S, Obj);
    finally
      EndUpdate;
    end;
  end;
end;

{ RTL-422: the rest of the stream from its position; no encoding: a BOM decides, else DefaultEncoding; an explicit
  encoding skips its own preamble when present }
procedure TStrings.LoadFromStream(Stream: TStream);
begin
  LoadFromStream(Stream, nil);
end;

procedure TStrings.LoadFromStream(Stream: TStream; Encoding: TEncoding);
var
  Buffer: TBytes;
  N: Int64;
  Skip: Integer;
begin
  N := Stream.Size - Stream.Position;
  if N < 0 then
    N := 0;
  SetLength(Buffer, Integer(N));
  if N > 0 then
    Stream.ReadBuffer(Buffer[0], Integer(N));
  Skip := TEncoding.GetBufferEncoding(Buffer, Encoding, FDefaultEncoding);
  FEncoding := Encoding;
  SetTextStr(Encoding.GetString(Buffer, Skip, Length(Buffer) - Skip));
end;

procedure TStrings.LoadFromFile(const FileName: string);
begin
  LoadFromFile(FileName, nil);
end;

procedure TStrings.LoadFromFile(const FileName: string; Encoding: TEncoding);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    LoadFromStream(S, Encoding);
  finally
    S.Free;
  end;
end;

{ RTL-423: the parameter, else the encoding of the last load, else DefaultEncoding; the preamble only with WriteBOM }
procedure TStrings.SaveToStream(Stream: TStream);
begin
  SaveToStream(Stream, nil);
end;

procedure TStrings.SaveToStream(Stream: TStream; Encoding: TEncoding);
var
  Bytes: TBytes;
begin
  if Encoding = nil then
    Encoding := FEncoding;
  if Encoding = nil then
    Encoding := FDefaultEncoding;
  if FWriteBOM then
  begin
    Bytes := Encoding.GetPreamble;
    if Length(Bytes) > 0 then
      Stream.WriteBuffer(Bytes[0], Length(Bytes));
  end;
  Bytes := Encoding.GetBytes(GetTextStr);
  if Length(Bytes) > 0 then
    Stream.WriteBuffer(Bytes[0], Length(Bytes));
end;

procedure TStrings.SaveToFile(const FileName: string);
begin
  SaveToFile(FileName, nil);
end;

procedure TStrings.SaveToFile(const FileName: string; Encoding: TEncoding);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmCreate);
  try
    SaveToStream(S, Encoding);
  finally
    S.Free;
  end;
end;

function TStrings.GetName(Index: Integer): string;
var
  P: Integer;
begin
  Result := SplitName(Get(Index), FNameValueSeparator, P);
end;

function TStrings.GetValue(const Name: string): string;
var
  I: Integer;
begin
  I := IndexOfName(Name);
  if I >= 0 then
    Result := GetValueFromIndex(I)
  else
    Result := '';
end;

{ '' deletes the entry; a missing name with a non-empty value appends Name=Value }
procedure TStrings.SetValue(const Name, Value: string);
var
  I: Integer;
begin
  I := IndexOfName(Name);
  if Value <> '' then
  begin
    if I < 0 then
      Add(Name + FNameValueSeparator + Value)
    else
      Put(I, Name + FNameValueSeparator + Value);
  end
  else if I >= 0 then
    Delete(I);
end;

function TStrings.GetValueFromIndex(Index: Integer): string;
var
  S: string;
  P: Integer;
begin
  S := Get(Index);
  SplitName(S, FNameValueSeparator, P);
  if P > 0 then
    Result := Copy(S, P + 1, MaxInt)
  else
    Result := '';
end;

procedure TStrings.SetValueFromIndex(Index: Integer; const Value: string);
begin
  if Value <> '' then
    Put(Index, GetName(Index) + FNameValueSeparator + Value)
  else
    Delete(Index);
end;

{ RTL-420 }
function TStrings.GetTextStr: string;
var
  I, N: Integer;
  B: TStringBuilder;
begin
  N := GetCount;
  B := TStringBuilder.Create;
  try
    for I := 0 to N - 1 do
    begin
      B.Append(Get(I));
      if (I < N - 1) or FTrailingLineBreak then
        B.Append(FLineBreak);
    end;
    Result := B.ToString;
  finally
    B.Free;
  end;
end;

{ RTL-421: with the default LineBreak a line ends at CR LF, a lone LF or a lone CR; a custom LineBreak alone separates
  lines; a final break adds no empty line }
procedure TStrings.SetTextStr(const Value: string);
var
  P, Start, Len, BL: Integer;
begin
  BeginUpdate;
  try
    Clear;
    Len := Length(Value);
    P := 1;
    Start := 1;
    if FLineBreak = sLineBreak then
    begin
      while P <= Len do
      begin
        if (Value[P] = #13) or (Value[P] = #10) then
        begin
          Add(Copy(Value, Start, P - Start));
          if (Value[P] = #13) and (P < Len) and (Value[P + 1] = #10) then
            P := P + 1;
          Start := P + 1;
        end;
        P := P + 1;
      end;
    end
    else if FLineBreak <> '' then
    begin
      BL := Length(FLineBreak);
      while P <= Len - BL + 1 do
        if Copy(Value, P, BL) = FLineBreak then
        begin
          Add(Copy(Value, Start, P - Start));
          P := P + BL;
          Start := P;
        end
        else
          P := P + 1;
    end;
    if Start <= Len then
      Add(Copy(Value, Start, Len - Start + 1));
  finally
    EndUpdate;
  end;
end;

{ RTL-424 }
function TStrings.GetDelimitedText: string;
var
  I, K: Integer;
  S: string;
  Quote: Boolean;
begin
  Result := '';
  for I := 0 to GetCount - 1 do
  begin
    S := Get(I);
    Quote := S = '';
    for K := 1 to Length(S) do
      if (S[K] = FQuoteChar) or (S[K] = FDelimiter) or (not FStrictDelimiter and (S[K] <= ' ')) then
        Quote := True;
    if Quote then
      S := AnsiQuotedStr(S, FQuoteChar);
    if I > 0 then
      Result := Result + FDelimiter;
    Result := Result + S;
  end;
end;

procedure TStrings.SetDelimitedText(const Value: string);
var
  P, Len: Integer;
  Item: string;

  function IsBlank(C: Char): Boolean;
  begin
    Result := not FStrictDelimiter and (C <= ' ');
  end;

  procedure SkipBlanks;
  begin
    while (P <= Len) and IsBlank(Value[P]) do
      P := P + 1;
  end;

begin
  BeginUpdate;
  try
    Clear;
    Len := Length(Value);
    P := 1;
    SkipBlanks;
    while P <= Len do
    begin
      Item := '';
      if Value[P] = FQuoteChar then
      begin
        P := P + 1;
        while P <= Len do
        begin
          if Value[P] = FQuoteChar then
          begin
            if (P < Len) and (Value[P + 1] = FQuoteChar) then
            begin
              Item := Item + FQuoteChar;
              P := P + 2;
            end
            else
            begin
              P := P + 1;
              Break;
            end;
          end
          else
          begin
            Item := Item + Value[P];
            P := P + 1;
          end;
        end;
      end;
      while (P <= Len) and (Value[P] <> FDelimiter) and not IsBlank(Value[P]) do
      begin
        Item := Item + Value[P];
        P := P + 1;
      end;
      Add(Item);
      SkipBlanks;
      if (P <= Len) and (Value[P] = FDelimiter) then
      begin
        P := P + 1;
        SkipBlanks;
        if P > Len then
          Add('');
      end;
    end;
  finally
    EndUpdate;
  end;
end;

function TStrings.GetCommaText: string;
var
  D, Q: Char;
  S: Boolean;
begin
  D := FDelimiter;
  Q := FQuoteChar;
  S := FStrictDelimiter;
  FDelimiter := ',';
  FQuoteChar := '"';
  FStrictDelimiter := False;
  try
    Result := GetDelimitedText;
  finally
    FDelimiter := D;
    FQuoteChar := Q;
    FStrictDelimiter := S;
  end;
end;

procedure TStrings.SetCommaText(const Value: string);
var
  D, Q: Char;
  S: Boolean;
begin
  D := FDelimiter;
  Q := FQuoteChar;
  S := FStrictDelimiter;
  FDelimiter := ',';
  FQuoteChar := '"';
  FStrictDelimiter := False;
  try
    SetDelimitedText(Value);
  finally
    FDelimiter := D;
    FQuoteChar := Q;
    FStrictDelimiter := S;
  end;
end;

{ ---------------------------------------------------------------- TStringList (section 5.2) }

constructor TStringList.Create;
begin
  inherited Create;
end;

constructor TStringList.Create(AOwnsObjects: Boolean);
begin
  inherited Create;
  FOwnsObjects := AOwnsObjects;
end;

constructor TStringList.Create(ADuplicates: TDuplicates; ASorted, ACaseSensitive: Boolean);
begin
  inherited Create;
  FDuplicates := ADuplicates;
  FCaseSensitive := ACaseSensitive;
  FSorted := ASorted;
end;

destructor TStringList.Destroy;
begin
  FOnChange := nil;
  FOnChanging := nil;
  Clear;
  inherited Destroy;
end;

procedure TStringList.CheckIndex(Index, Limit: Integer);
begin
  if (Index < 0) or (Index > Limit) then
    Error(SListIndexError, Index);
end;

procedure TStringList.CheckUnsorted;
begin
  if FSorted then
    raise EStringListError.Create(SSortedListError);
end;

procedure TStringList.Changed;
begin
  if (UpdateCount = 0) and Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TStringList.Changing;
begin
  if (UpdateCount = 0) and Assigned(FOnChanging) then
    FOnChanging(Self);
end;

procedure TStringList.SetUpdateState(Updating: Boolean);
begin
  if Updating then
    Changing
  else
    Changed;
end;

function TStringList.CompareStrings(const S1, S2: string): Integer;
begin
  if FCaseSensitive then
    Result := CompareStr(S1, S2)
  else
    Result := CompareText(S1, S2);
end;

function TStringList.Get(Index: Integer): string;
begin
  CheckIndex(Index, FCount - 1);
  Result := FList[Index].FString;
end;

function TStringList.GetCount: Integer;
begin
  Result := FCount;
end;

function TStringList.GetObject(Index: Integer): TObject;
begin
  CheckIndex(Index, FCount - 1);
  Result := FList[Index].FObject;
end;

procedure TStringList.Put(Index: Integer; const S: string);
begin
  CheckUnsorted;
  CheckIndex(Index, FCount - 1);
  Changing;
  FList[Index].FString := S;
  Changed;
end;

procedure TStringList.PutObject(Index: Integer; AObject: TObject);
var
  Old: TObject;
begin
  CheckIndex(Index, FCount - 1);
  Changing;
  Old := FList[Index].FObject;
  FList[Index].FObject := AObject;
  if FOwnsObjects and (Old <> nil) and (Old <> AObject) then
    Old.Free;
  Changed;
end;

function TStringList.GetCapacity: Integer;
begin
  Result := Length(FList);
end;

procedure TStringList.SetCapacity(NewCapacity: Integer);
begin
  if NewCapacity < FCount then
    Error(SListCapacityError, NewCapacity);
  SetLength(FList, NewCapacity);
end;

{ RTL-416 growth steps, as TList }
procedure TStringList.Grow;
var
  Cap, Delta: Integer;
begin
  Cap := Length(FList);
  if Cap > 64 then
    Delta := Cap div 4
  else if Cap > 8 then
    Delta := 16
  else
    Delta := 4;
  SetLength(FList, Cap + Delta);
end;

procedure TStringList.InsertItem(Index: Integer; const S: string; AObject: TObject);
begin
  Changing;
  if FCount = Length(FList) then
    Grow;
  if Index < FCount then
  begin
    { the strings move by their references; the vacated slot is cleared without finalising }
    System.Move(FList[Index], FList[Index + 1], (FCount - Index) * SizeOf(TStringItem));
    FillChar(FList[Index], SizeOf(TStringItem), 0);
  end;
  FList[Index].FString := S;
  FList[Index].FObject := AObject;
  FCount := FCount + 1;
  Changed;
end;

function TStringList.Add(const S: string): Integer;
begin
  Result := AddObject(S, nil);
end;

function TStringList.AddObject(const S: string; AObject: TObject): Integer;
begin
  if not FSorted then
    Result := FCount
  else if Find(S, Result) then
    case FDuplicates of
      dupIgnore:
        Exit;
      dupError:
        raise EStringListError.Create(SDuplicateString);
      dupAccept:
        while (Result < FCount) and (CompareStrings(FList[Result].FString, S) = 0) do
          Result := Result + 1;
    end;
  InsertItem(Result, S, AObject);
end;

procedure TStringList.Clear;
var
  I: Integer;
  Obj: TObject;
begin
  if FCount = 0 then
    Exit;
  Changing;
  for I := FCount - 1 downto 0 do
  begin
    Obj := FList[I].FObject;
    FList[I].FObject := nil;
    if FOwnsObjects then
      Obj.Free;
  end;
  FCount := 0;
  SetLength(FList, 0);
  Changed;
end;

procedure TStringList.Delete(Index: Integer);
var
  Obj: TObject;
begin
  CheckIndex(Index, FCount - 1);
  Changing;
  Obj := FList[Index].FObject;
  FList[Index].FString := '';
  FCount := FCount - 1;
  if Index < FCount then
  begin
    System.Move(FList[Index + 1], FList[Index], (FCount - Index) * SizeOf(TStringItem));
    FillChar(FList[FCount], SizeOf(TStringItem), 0);
  end
  else
    FList[Index].FObject := nil;
  if FOwnsObjects then
    Obj.Free;
  Changed;
end;

procedure TStringList.Exchange(Index1, Index2: Integer);
var
  T: TStringItem;
begin
  CheckUnsorted;
  CheckIndex(Index1, FCount - 1);
  CheckIndex(Index2, FCount - 1);
  Changing;
  T := FList[Index1];
  FList[Index1] := FList[Index2];
  FList[Index2] := T;
  Changed;
end;

{ RTL-426: binary search; the lowest matching index, or the insertion position when absent }
function TStringList.Find(const S: string; var Index: Integer): Boolean;
var
  Lo, Hi, Mid, C: Integer;
begin
  Result := False;
  Lo := 0;
  Hi := FCount - 1;
  while Lo <= Hi do
  begin
    Mid := (Lo + Hi) shr 1;
    C := CompareStrings(FList[Mid].FString, S);
    if C < 0 then
      Lo := Mid + 1
    else
    begin
      Hi := Mid - 1;
      if C = 0 then
        Result := True;
    end;
  end;
  Index := Lo;
end;

function TStringList.IndexOf(const S: string): Integer;
begin
  if FSorted then
  begin
    if not Find(S, Result) then
      Result := -1;
  end
  else
    Result := inherited IndexOf(S);
end;

procedure TStringList.Insert(Index: Integer; const S: string);
begin
  InsertObject(Index, S, nil);
end;

procedure TStringList.InsertObject(Index: Integer; const S: string; AObject: TObject);
begin
  CheckUnsorted;
  CheckIndex(Index, FCount);
  InsertItem(Index, S, AObject);
end;

procedure TStringList.Move(CurIndex, NewIndex: Integer);
var
  T: TStringItem;
begin
  CheckUnsorted;
  if CurIndex = NewIndex then
    Exit;
  CheckIndex(CurIndex, FCount - 1);
  CheckIndex(NewIndex, FCount - 1);
  Changing;
  T := FList[CurIndex];
  if CurIndex < NewIndex then
    System.Move(FList[CurIndex + 1], FList[CurIndex], (NewIndex - CurIndex) * SizeOf(TStringItem))
  else
    System.Move(FList[NewIndex], FList[NewIndex + 1], (CurIndex - NewIndex) * SizeOf(TStringItem));
  { the slot at NewIndex still holds a moved reference: overwrite it without finalising }
  FillChar(FList[NewIndex], SizeOf(TStringItem), 0);
  FList[NewIndex] := T;
  Changed;
end;

{ RTL-418: a stable merge sort of an index permutation; Compare sees the unsorted list throughout }
procedure TStringList.SortBy(Compare: TStringListSortCompare);
var
  Order, Tmp: array of Integer;
  Items: array of TStringItem;
  Width, Lo, Mid, Hi, I, J, K: Integer;
begin
  if FCount < 2 then
    Exit;
  Changing;
  SetLength(Order, FCount);
  SetLength(Tmp, FCount);
  for I := 0 to FCount - 1 do
    Order[I] := I;
  Width := 1;
  while Width < FCount do
  begin
    Lo := 0;
    while Lo < FCount do
    begin
      Mid := Lo + Width;
      if Mid > FCount then
        Mid := FCount;
      Hi := Lo + 2 * Width;
      if Hi > FCount then
        Hi := FCount;
      I := Lo;
      J := Mid;
      K := Lo;
      while (I < Mid) and (J < Hi) do
      begin
        if Compare(Self, Order[J], Order[I]) < 0 then
        begin
          Tmp[K] := Order[J];
          J := J + 1;
        end
        else
        begin
          Tmp[K] := Order[I];
          I := I + 1;
        end;
        K := K + 1;
      end;
      while I < Mid do
      begin
        Tmp[K] := Order[I];
        I := I + 1;
        K := K + 1;
      end;
      while J < Hi do
      begin
        Tmp[K] := Order[J];
        J := J + 1;
        K := K + 1;
      end;
      Lo := Hi;
    end;
    for I := 0 to FCount - 1 do
      Order[I] := Tmp[I];
    Width := Width * 2;
  end;
  SetLength(Items, FCount);
  for I := 0 to FCount - 1 do
    Items[I] := FList[Order[I]];
  for I := 0 to FCount - 1 do
    FList[I] := Items[I];
  Changed;
end;

function CompareByStrings(List: TStringList; Index1, Index2: Integer): Integer;
begin
  Result := List.CompareStrings(List.FList[Index1].FString, List.FList[Index2].FString);
end;

procedure TStringList.Sort;
begin
  if not FSorted then
    SortBy(CompareByStrings);
end;

procedure TStringList.CustomSort(Compare: TStringListSortCompare);
begin
  if not FSorted then
    SortBy(Compare);
end;

procedure TStringList.SetSorted(Value: Boolean);
begin
  if Value <> FSorted then
  begin
    if Value then
      SortBy(CompareByStrings);
    FSorted := Value;
  end;
end;

procedure TStringList.SetCaseSensitive(Value: Boolean);
begin
  if Value <> FCaseSensitive then
  begin
    FCaseSensitive := Value;
    if FSorted then
      SortBy(CompareByStrings);
  end;
end;

{ ---------------------------------------------------------------- TCollectionItem, TCollection (section 7) }

constructor TCollectionItem.Create(Collection: TCollection);
begin
  inherited Create;
  SetCollection(Collection);
end;

destructor TCollectionItem.Destroy;
begin
  SetCollection(nil);
  inherited Destroy;
end;

procedure TCollectionItem.SetCollection(Value: TCollection);
begin
  if Value <> FCollection then
  begin
    if FCollection <> nil then
      FCollection.RemoveItem(Self);
    if Value <> nil then
      Value.InsertItem(Self);
  end;
end;

{ RTL-440: an item change reaches Update(Self), a structural one Update(nil); nothing while an update is pending }
procedure TCollectionItem.Changed(AllItems: Boolean);
begin
  if (FCollection <> nil) and (FCollection.FUpdateCount = 0) then
    if AllItems then
      FCollection.Update(nil)
    else
      FCollection.Update(Self);
end;

function TCollectionItem.GetOwner: TPersistent;
begin
  Result := FCollection;
end;

function TCollectionItem.GetIndex: Integer;
begin
  if FCollection <> nil then
    Result := FCollection.FItems.IndexOf(Pointer(Self))
  else
    Result := -1;
end;

procedure TCollectionItem.SetIndex(Value: Integer);
var
  Cur: Integer;
begin
  Cur := GetIndex;
  if (Cur >= 0) and (Cur <> Value) then
  begin
    FCollection.FItems.Move(Cur, Value);
    Changed(True);
  end;
end;

function TCollectionItem.GetDisplayName: string;
begin
  Result := ClassName;
end;

procedure TCollectionItem.SetDisplayName(const Value: string);
begin
  Changed(False);
end;

{ RTL-411: <collection path>[<Index>] }
function TCollectionItem.GetNamePath: string;
begin
  if FCollection <> nil then
    Result := FCollection.GetNamePath + '[' + IntToStr(GetIndex) + ']'
  else
    Result := ClassName;
end;

constructor TCollection.Create(ItemClass: TCollectionItemClass);
begin
  inherited Create;
  FItemClass := ItemClass;
  FItems := TList.Create;
end;

destructor TCollection.Destroy;
begin
  FUpdateCount := 1;
  if FItems <> nil then
    Clear;
  FItems.Free;
  inherited Destroy;
end;

function TCollection.Owner: TPersistent;
begin
  Result := GetOwner;
end;

function TCollection.GetCount: Integer;
begin
  Result := FItems.Count;
end;

procedure TCollection.InsertItem(Item: TCollectionItem);
begin
  FItems.Add(Pointer(Item));
  Item.FCollection := Self;
  Item.FID := FNextID;
  FNextID := FNextID + 1;
  Notify(Item, cnAdded);
  Changed;
end;

procedure TCollection.RemoveItem(Item: TCollectionItem);
begin
  Notify(Item, cnExtracting);
  FItems.Remove(Pointer(Item));
  Item.FCollection := nil;
  Changed;
end;

procedure TCollection.Changed;
begin
  if FUpdateCount = 0 then
    Update(nil);
end;

function TCollection.GetItem(Index: Integer): TCollectionItem;
begin
  Result := TCollectionItem(FItems[Index]);
end;

procedure TCollection.SetItem(Index: Integer; Value: TCollectionItem);
begin
  TCollectionItem(FItems[Index]).Assign(Value);
end;

procedure TCollection.Update(Item: TCollectionItem);
begin
end;

procedure TCollection.Notify(Item: TCollectionItem; Action: TCollectionNotification);
begin
end;

function TCollection.Add: TCollectionItem;
begin
  Result := FItemClass.Create(Self);
end;

function TCollection.Insert(Index: Integer): TCollectionItem;
begin
  if (Index < 0) or (Index > FItems.Count) then
    raise EListError.CreateFmt(SListIndexError, [Index]);
  Result := Add;
  Result.Index := Index;
end;

procedure TCollection.Delete(Index: Integer);
var
  Item: TCollectionItem;
begin
  Item := TCollectionItem(FItems[Index]);
  Notify(Item, cnDeleting);
  Item.Free;
end;

{ frees the items last to first inside one update }
procedure TCollection.Clear;
var
  I: Integer;
begin
  if FItems.Count = 0 then
    Exit;
  BeginUpdate;
  try
    for I := FItems.Count - 1 downto 0 do
      Delete(I);
  finally
    EndUpdate;
  end;
end;

procedure TCollection.Assign(Source: TPersistent);
var
  I: Integer;
  S: TCollection;
begin
  if Source is TCollection then
  begin
    S := TCollection(Source);
    BeginUpdate;
    try
      Clear;
      for I := 0 to S.Count - 1 do
        Add.Assign(S.Items[I]);
    finally
      EndUpdate;
    end;
  end
  else
    inherited Assign(Source);
end;

procedure TCollection.BeginUpdate;
begin
  FUpdateCount := FUpdateCount + 1;
end;

{ RTL-440: the outermost EndUpdate reports one structural change }
procedure TCollection.EndUpdate;
begin
  FUpdateCount := FUpdateCount - 1;
  Changed;
end;

function TCollection.FindItemID(ID: Integer): TCollectionItem;
var
  I: Integer;
begin
  for I := 0 to FItems.Count - 1 do
  begin
    Result := TCollectionItem(FItems[I]);
    if Result.FID = ID then
      Exit;
  end;
  Result := nil;
end;

constructor TOwnedCollection.Create(AOwner: TPersistent; ItemClass: TCollectionItemClass);
begin
  FOwner := AOwner;
  inherited Create(ItemClass);
end;

function TOwnedCollection.GetOwner: TPersistent;
begin
  Result := FOwner;
end;

{ ---------------------------------------------------------------- TStream (section 6.1) }

function TStream.GetPosition: Int64;
begin
  Result := Seek(0, soCurrent);
end;

{ RTL-433 }
procedure TStream.SetPosition(const Pos: Int64);
begin
  Seek(Pos, soBeginning);
end;

{ RTL-432: seek to the end and back }
function TStream.GetSize: Int64;
var
  Pos: Int64;
begin
  Pos := Seek(0, soCurrent);
  Result := Seek(0, soEnd);
  Seek(Pos, soBeginning);
end;

procedure TStream.SetSize(const NewSize: Int64);
begin
end;

procedure TStream.SetSize64(const NewSize: Int64);
begin
  SetSize(NewSize);
end;

function TStream.Read(var Buffer; Count: Integer): Integer;
begin
  raise EStreamError.CreateFmt(SStreamNoRead, [ClassName]);
end;

function TStream.Write(const Buffer; Count: Integer): Integer;
begin
  raise EStreamError.CreateFmt(SStreamNoWrite, [ClassName]);
end;

{ RTL-430 }
procedure TStream.ReadBuffer(var Buffer; Count: Integer);
var
  Total, N: Integer;
begin
  Total := 0;
  while Total < Count do
  begin
    N := Read((PByte(@Buffer) + Total)^, Count - Total);
    if N <= 0 then
      raise EReadError.Create(SReadError);
    Total := Total + N;
  end;
end;

procedure TStream.WriteBuffer(const Buffer; Count: Integer);
var
  Total, N: Integer;
begin
  Total := 0;
  while Total < Count do
  begin
    N := Write((PByte(@Buffer) + Total)^, Count - Total);
    if N <= 0 then
      raise EWriteError.Create(SWriteError);
    Total := Total + N;
  end;
end;

{ RTL-431: Count 0 copies all of Source from position 0; 64 KiB chunks }
function TStream.CopyFrom(Source: TStream; Count: Int64): Int64;
const
  ChunkSize = 65536;
var
  Buffer: PByte;
  N: Integer;
begin
  if Count = 0 then
  begin
    Source.Position := 0;
    Count := Source.Size;
  end;
  Result := Count;
  if Count <= 0 then
    Exit;
  GetMem(Buffer, ChunkSize);
  try
    while Count > 0 do
    begin
      if Count > ChunkSize then
        N := ChunkSize
      else
        N := Integer(Count);
      Source.ReadBuffer(Buffer^, N);
      WriteBuffer(Buffer^, N);
      Count := Count - N;
    end;
  finally
    FreeMem(Buffer);
  end;
end;

{ ---------------------------------------------------------------- THandleStream, TFileStream (section 6.2) }

constructor THandleStream.Create(AHandle: THandle);
begin
  inherited Create;
  FHandle := AHandle;
end;

function THandleStream.Read(var Buffer; Count: Integer): Integer;
begin
  Result := FileRead(FHandle, Buffer, Count);
  if Result = -1 then
    Result := 0;
end;

function THandleStream.Write(const Buffer; Count: Integer): Integer;
begin
  Result := FileWrite(FHandle, Buffer, Count);
  if Result = -1 then
    Result := 0;
end;

function THandleStream.Seek(const Offset: Int64; Origin: TSeekOrigin): Int64;
begin
  Result := FileSeek(FHandle, Offset, Ord(Origin));
end;

procedure THandleStream.SetSize(const NewSize: Int64);
begin
  Seek(NewSize, soBeginning);
  if not SetEndOfFile(FHandle) then
    raise EStreamError.Create(SStreamSetSize);
end;

constructor TFileStream.Create(const AFileName: string; Mode: Word);
var
  H: THandle;
begin
  if (Mode and fmCreate) = fmCreate then
  begin
    H := FileCreate(AFileName, Mode and $FF);
    if H = INVALID_HANDLE_VALUE then
      raise EFCreateError.CreateFmt(SFCreateErrorEx, [ExpandFileName(AFileName), SysErrorMessage(GetLastError)]);
  end
  else
  begin
    H := FileOpen(AFileName, Mode);
    if H = INVALID_HANDLE_VALUE then
      raise EFOpenError.CreateFmt(SFOpenErrorEx, [ExpandFileName(AFileName), SysErrorMessage(GetLastError)]);
  end;
  inherited Create(H);
  FFileName := AFileName;
end;

destructor TFileStream.Destroy;
begin
  if (FHandle <> 0) and (FHandle <> INVALID_HANDLE_VALUE) then
    FileClose(FHandle);
  inherited Destroy;
end;

{ ---------------------------------------------------------------- memory streams (section 6.2, RTL-435) }

procedure TCustomMemoryStream.SetPointer(Ptr: Pointer; ASize: NativeInt);
begin
  FMemory := Ptr;
  FSize := ASize;
end;

{ reading past the end returns fewer bytes }
function TCustomMemoryStream.Read(var Buffer; Count: Integer): Integer;
var
  N: NativeInt;
begin
  Result := 0;
  if (Count <= 0) or (FPosition < 0) or (FPosition >= FSize) then
    Exit;
  N := FSize - FPosition;
  if N > Count then
    N := Count;
  System.Move((PByte(FMemory) + FPosition)^, Buffer, N);
  FPosition := FPosition + N;
  Result := Integer(N);
end;

{ RTL-435: a negative position becomes 0; the result is the new position }
function TCustomMemoryStream.Seek(const Offset: Int64; Origin: TSeekOrigin): Int64;
var
  P: Int64;
begin
  case Origin of
    soBeginning: P := Offset;
    soCurrent: P := FPosition + Offset;
  else
    P := FSize + Offset;
  end;
  if P < 0 then
    P := 0;
  FPosition := NativeInt(P);
  Result := P;
end;

procedure TCustomMemoryStream.SaveToStream(Stream: TStream);
begin
  if FSize > 0 then
    Stream.WriteBuffer(FMemory^, Integer(FSize));
end;

procedure TCustomMemoryStream.SaveToFile(const FileName: string);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmCreate);
  try
    SaveToStream(S);
  finally
    S.Free;
  end;
end;

{ TResourceStream }

{ Shown is the name the error message carries }
procedure TResourceStream.Open(Instance: THandle; Name, ResType: PChar; const Shown: string);
var
  Info: HRSRC;
  Data: HGLOBAL;
  P: Pointer;
begin
  P := nil;
  Data := 0;
  Info := FindResourceW(HMODULE(Instance), Name, ResType);
  if Info <> 0 then
    Data := LoadResource(HMODULE(Instance), Info);
  if Data <> 0 then
    P := LockResource(Data);
  if P = nil then
    raise EResNotFound.CreateFmt(SResNotFound, [Shown]);
  SetPointer(P, SizeofResource(HMODULE(Instance), Info));
end;

constructor TResourceStream.Create(Instance: THandle; const ResName: string; ResType: PChar);
begin
  inherited Create;
  Open(Instance, PChar(ResName), ResType, ResName);
end;

constructor TResourceStream.CreateFromID(Instance: THandle; ResID: Integer; ResType: PChar);
begin
  inherited Create;
  Open(Instance, PChar(NativeUInt(ResID)), ResType, IntToStr(ResID));
end;

function TResourceStream.Write(const Buffer; Count: Integer): Integer;
begin
  raise EStreamError.Create(SCantWriteResourceStreamError);
end;

procedure TResourceStream.SetSize(const NewSize: Int64);
begin
  raise EStreamError.Create(SCantWriteResourceStreamError);
end;

const
  MemoryDelta = 8192;

destructor TMemoryStream.Destroy;
begin
  Clear;
  inherited Destroy;
end;

{ the block for NewCapacity bytes (rounded up to a multiple of MemoryDelta); 0 frees it }
function TMemoryStream.Realloc(var NewCapacity: NativeInt): Pointer;
begin
  if NewCapacity > 0 then
    NewCapacity := (NewCapacity + MemoryDelta - 1) div MemoryDelta * MemoryDelta;
  Result := FMemory;
  if NewCapacity <> FCapacity then
  begin
    if NewCapacity = 0 then
    begin
      FreeMem(Result);
      Result := nil;
    end
    else
      ReallocMem(Result, NewCapacity);
  end;
end;

procedure TMemoryStream.SetCapacity(NewCapacity: NativeInt);
begin
  SetPointer(Realloc(NewCapacity), FSize);
  FCapacity := NewCapacity;
end;

procedure TMemoryStream.SetSize(const NewSize: Int64);
var
  Old: NativeInt;
begin
  Old := FSize;
  SetCapacity(NativeInt(NewSize));
  FSize := NativeInt(NewSize);
  if FSize > Old then
    FillChar((PByte(FMemory) + Old)^, FSize - Old, 0);
  if FPosition > FSize then
    FPosition := FSize;
end;

procedure TMemoryStream.Clear;
begin
  SetCapacity(0);
  FSize := 0;
  FPosition := 0;
end;

procedure TMemoryStream.LoadFromStream(Stream: TStream);
var
  Count: Int64;
begin
  Stream.Position := 0;
  Count := Stream.Size;
  SetSize(Count);
  if Count <> 0 then
    Stream.ReadBuffer(FMemory^, Integer(Count));
  FPosition := 0;
end;

procedure TMemoryStream.LoadFromFile(const FileName: string);
var
  S: TFileStream;
begin
  S := TFileStream.Create(FileName, fmOpenRead or fmShareDenyWrite);
  try
    LoadFromStream(S);
  finally
    S.Free;
  end;
end;

{ grows to the next multiple of MemoryDelta, at least 1.25 times the old capacity; a gap after the end is zeroed }
function TMemoryStream.Write(const Buffer; Count: Integer): Integer;
var
  NewPos, Cap: NativeInt;
begin
  Result := 0;
  if (Count <= 0) or (FPosition < 0) then
    Exit;
  NewPos := FPosition + Count;
  if NewPos > FSize then
  begin
    if NewPos > FCapacity then
    begin
      Cap := FCapacity + FCapacity div 4;
      if Cap < NewPos then
        Cap := NewPos;
      SetCapacity(Cap);
    end;
    if FPosition > FSize then
      FillChar((PByte(FMemory) + FSize)^, FPosition - FSize, 0);
    FSize := NewPos;
  end;
  System.Move(Buffer, (PByte(FMemory) + FPosition)^, Count);
  FPosition := NewPos;
  Result := Count;
end;

constructor TBytesStream.Create(const ABytes: TBytes);
begin
  inherited Create;
  FBytes := ABytes;
  SetPointer(Pointer(FBytes), Length(FBytes));
  FCapacity := Length(FBytes);
end;

{ the memory is the TBytes buffer itself (exact capacity) }
function TBytesStream.Realloc(var NewCapacity: NativeInt): Pointer;
begin
  if NewCapacity <> Length(FBytes) then
    SetLength(FBytes, NewCapacity);
  Result := Pointer(FBytes);
end;

constructor TStringStream.Create(const AString: string; AEncoding: TEncoding);
begin
  if AEncoding = nil then
    AEncoding := TEncoding.UTF8;
  FEncoding := AEncoding;
  inherited Create(AEncoding.GetBytes(AString));
end;

function TStringStream.GetDataString: string;
begin
  Result := FEncoding.GetString(FBytes, 0, Integer(FSize));
end;

{ Count bytes from the position (fewer at the end), decoded }
function TStringStream.ReadString(Count: Integer): string;
var
  N: NativeInt;
begin
  N := FSize - FPosition;
  if N > Count then
    N := Count;
  if N <= 0 then
  begin
    Result := '';
    Exit;
  end;
  Result := FEncoding.GetString(FBytes, Integer(FPosition), Integer(N));
  FPosition := FPosition + N;
end;

procedure TStringStream.WriteString(const AString: string);
var
  B: TBytes;
begin
  B := FEncoding.GetBytes(AString);
  if Length(B) > 0 then
    WriteBuffer(B[0], Length(B));
end;

{ ---------------------------------------------------------------- TComponent (section 8) }

type
  PComponent = ^TComponent;

{ RTL-450 }
constructor TComponent.Create(AOwner: TComponent);
begin
  inherited Create;
  FComponentStyle := [csInheritable];
  if AOwner <> nil then
    AOwner.InsertComponent(Self);
end;

{ RTL-453 }
destructor TComponent.Destroy;
var
  Other: TComponent;
begin
  Destroying;
  if FFreeNotifies <> nil then
  begin
    while FFreeNotifies.Count > 0 do
    begin
      Other := TComponent(FFreeNotifies[0]);
      FFreeNotifies.Delete(0);
      Other.RemoveNotification(Self);
      Other.Notification(Self, opRemove);
    end;
    FFreeNotifies.Free;
    FFreeNotifies := nil;
  end;
  DestroyComponents;
  if FOwner <> nil then
    FOwner.RemoveComponent(Self);
  FComponents.Free;
  FComponents := nil;
  inherited Destroy;
end;

procedure TComponent.BeforeDestruction;
begin
  if not (csDestroying in FComponentState) then
    Destroying;
end;

procedure TComponent.Destroying;
var
  I: Integer;
begin
  if csDestroying in FComponentState then
    Exit;
  Include(FComponentState, csDestroying);
  if FComponents <> nil then
    for I := 0 to FComponents.Count - 1 do
      TComponent(FComponents[I]).Destroying;
end;

{ the last owned component first: removed, then destroyed }
procedure TComponent.DestroyComponents;
var
  C: TComponent;
begin
  while (FComponents <> nil) and (FComponents.Count > 0) do
  begin
    C := TComponent(FComponents.Last);
    RemoveComponent(C);
    C.Destroy;
  end;
end;

function TComponent.GetComponent(Index: Integer): TComponent;
begin
  if FComponents = nil then
    raise EListError.CreateFmt(SListIndexError, [Index]);
  Result := TComponent(FComponents[Index]);
end;

function TComponent.GetComponentCount: Integer;
begin
  if FComponents = nil then
    Result := 0
  else
    Result := FComponents.Count;
end;

function TComponent.GetComponentIndex: Integer;
begin
  if (FOwner <> nil) and (FOwner.FComponents <> nil) then
    Result := FOwner.FComponents.IndexOf(Pointer(Self))
  else
    Result := -1;
end;

{ RTL-460: clamped to 0..Count-1 }
procedure TComponent.SetComponentIndex(Value: Integer);
var
  Cur, N: Integer;
begin
  if FOwner = nil then
    Exit;
  Cur := GetComponentIndex;
  if Cur < 0 then
    Exit;
  N := FOwner.FComponents.Count;
  if Value < 0 then
    Value := 0;
  if Value >= N then
    Value := N - 1;
  if Value <> Cur then
    FOwner.FComponents.Move(Cur, Value);
end;

procedure TComponent.Insert(AComponent: TComponent);
begin
  if FComponents = nil then
    FComponents := TList.Create;
  FComponents.Add(Pointer(AComponent));
  AComponent.FOwner := Self;
end;

procedure TComponent.Remove(AComponent: TComponent);
begin
  FComponents.Remove(Pointer(AComponent));
  AComponent.FOwner := nil;
end;

{ RTL-451 }
procedure TComponent.InsertComponent(AComponent: TComponent);
begin
  AComponent.ValidateContainer(Self);
  if AComponent.FOwner <> nil then
    AComponent.FOwner.RemoveComponent(AComponent);
  ValidateRename(AComponent, '', AComponent.FName);
  Insert(AComponent);
  if csDesigning in FComponentState then
    AComponent.SetDesigning(True);
  Notification(AComponent, opInsert);
end;

{ RTL-452 }
procedure TComponent.RemoveComponent(AComponent: TComponent);
var
  P: PComponent;
begin
  ValidateRename(AComponent, AComponent.FName, '');
  Notification(AComponent, opRemove);
  Remove(AComponent);
  if AComponent.FName <> '' then
  begin
    P := PComponent(FieldAddress(AComponent.FName));
    if (P <> nil) and (P^ = AComponent) then
      P^ := nil;
  end;
end;

procedure TComponent.ValidateContainer(AComponent: TComponent);
begin
  AComponent.ValidateInsert(Self);
end;

procedure TComponent.ValidateInsert(AComponent: TComponent);
begin
end;

{ RTL-455: another owned component with the same name (ASCII case-insensitive) }
procedure TComponent.ValidateRename(AComponent: TComponent; const CurName, NewName: string);
var
  Found: TComponent;
begin
  if (NewName <> '') and (CompareText(CurName, NewName) <> 0) then
  begin
    Found := FindComponent(NewName);
    if (Found <> nil) and (Found <> AComponent) then
      raise EComponentError.CreateFmt(SDuplicateName, [NewName]);
  end;
end;

{ RTL-456: the owner's published field named AName receives (Bind) or loses Self }
procedure TComponent.BindField(const AName: string; Bind: Boolean);
var
  P: PComponent;
begin
  if (FOwner = nil) or (AName = '') then
    Exit;
  P := PComponent(FOwner.FieldAddress(AName));
  if P = nil then
    Exit;
  if Bind then
    P^ := Self
  else if P^ = Self then
    P^ := nil;
end;

{ RTL-455 }
procedure TComponent.SetName(const NewName: TComponentName);
begin
  if FName = NewName then
    Exit;
  if (NewName <> '') and not IsValidIdent(NewName) then
    raise EComponentError.CreateFmt(SInvalidName, [NewName]);
  if FOwner <> nil then
    FOwner.ValidateRename(Self, FName, NewName)
  else
    ValidateRename(nil, FName, NewName);
  BindField(FName, False);
  BindField(NewName, True);
  FName := NewName;
end;

{ RTL-459 }
function TComponent.FindComponent(const AName: string): TComponent;
var
  I: Integer;
begin
  if (AName <> '') and (FComponents <> nil) then
    for I := 0 to FComponents.Count - 1 do
    begin
      Result := TComponent(FComponents[I]);
      if CompareText(Result.FName, AName) = 0 then
        Exit;
    end;
  Result := nil;
end;

{ RTL-458 }
procedure TComponent.FreeNotification(AComponent: TComponent);
begin
  if (AComponent = nil) or (AComponent = Self) then
    Exit;
  if (FOwner = nil) or (AComponent.FOwner <> FOwner) then
  begin
    if FFreeNotifies = nil then
      FFreeNotifies := TList.Create;
    if FFreeNotifies.IndexOf(Pointer(AComponent)) < 0 then
      FFreeNotifies.Add(Pointer(AComponent));
    if AComponent.FFreeNotifies = nil then
      AComponent.FFreeNotifies := TList.Create;
    if AComponent.FFreeNotifies.IndexOf(Pointer(Self)) < 0 then
      AComponent.FFreeNotifies.Add(Pointer(Self));
    Include(FComponentState, csFreeNotification);
    Include(AComponent.FComponentState, csFreeNotification);
  end;
end;

procedure TComponent.RemoveNotification(AComponent: TComponent);
begin
  if FFreeNotifies <> nil then
    FFreeNotifies.Remove(Pointer(AComponent));
end;

procedure TComponent.RemoveFreeNotification(AComponent: TComponent);
begin
  RemoveNotification(AComponent);
  if AComponent <> nil then
    AComponent.RemoveNotification(Self);
end;

{ RTL-454 }
procedure TComponent.Notification(AComponent: TComponent; Operation: TOperation);
var
  I: Integer;
begin
  if Operation = opRemove then
    RemoveNotification(AComponent);
  if FComponents <> nil then
  begin
    I := 0;
    while I < FComponents.Count do
    begin
      TComponent(FComponents[I]).Notification(AComponent, Operation);
      I := I + 1;
    end;
  end;
end;

{ RTL-461 }
procedure TComponent.Loaded;
begin
  Exclude(FComponentState, csLoading);
end;

procedure TComponent.ReadState(Reader: TReader);
begin
  Reader.ReadData(Self);
end;

procedure TComponent.WriteState(Writer: TWriter);
begin
end;

procedure TComponent.GetChildren(Proc: TGetChildProc; Root: TComponent);
begin
end;

function TComponent.GetChildOwner: TComponent;
begin
  Result := nil;
end;

function TComponent.GetChildParent: TComponent;
begin
  Result := Self;
end;

procedure TComponent.SetParentComponent(Value: TComponent);
begin
end;

function TComponent.GetParentComponent: TComponent;
begin
  Result := nil;
end;

function TComponent.HasParent: Boolean;
begin
  Result := False;
end;

{ RTL-457: DesignInfo low word = left, high word = top }
procedure TComponent.ReadDesignLeft(Reader: TReader);
begin
  FDesignInfo := Integer((Cardinal(FDesignInfo) and $FFFF0000) or (Cardinal(Reader.ReadInteger) and $FFFF));
end;

procedure TComponent.ReadDesignTop(Reader: TReader);
begin
  FDesignInfo := Integer((Cardinal(FDesignInfo) and $FFFF) or ((Cardinal(Reader.ReadInteger) and $FFFF) shl 16));
end;

procedure TComponent.WriteDesignLeft(Writer: TWriter);
begin
end;

procedure TComponent.WriteDesignTop(Writer: TWriter);
begin
end;

procedure TComponent.DefineProperties(Filer: TFiler);
begin
  inherited DefineProperties(Filer);
  Filer.DefineProperty('DesignLeft', ReadDesignLeft, WriteDesignLeft, (FDesignInfo and $FFFF) <> 0);
  Filer.DefineProperty('DesignTop', ReadDesignTop, WriteDesignTop, (FDesignInfo shr 16) <> 0);
end;

procedure TComponent.SetDesigning(Value: Boolean; SetChildren: Boolean);
var
  I: Integer;
begin
  if Value then
    Include(FComponentState, csDesigning)
  else
    Exclude(FComponentState, csDesigning);
  if SetChildren and (FComponents <> nil) then
    for I := 0 to FComponents.Count - 1 do
      TComponent(FComponents[I]).SetDesigning(Value, True);
end;

procedure TComponent.SetAncestor(Value: Boolean);
var
  I: Integer;
begin
  if Value then
    Include(FComponentState, csAncestor)
  else
    Exclude(FComponentState, csAncestor);
  if FComponents <> nil then
    for I := 0 to FComponents.Count - 1 do
      TComponent(FComponents[I]).SetAncestor(Value);
end;

procedure TComponent.Loading;
begin
  Include(FComponentState, csLoading);
end;

procedure TComponent.Updating;
begin
  Include(FComponentState, csUpdating);
end;

procedure TComponent.Updated;
begin
  Exclude(FComponentState, csUpdating);
end;

procedure TComponent.SetSubComponent(IsSubComponent: Boolean);
begin
  if IsSubComponent then
    Include(FComponentStyle, csSubComponent)
  else
    Exclude(FComponentStyle, csSubComponent);
end;

{ RTL-462 }
function TComponent.ExecuteAction(Action: TBasicAction): Boolean;
begin
  Result := Action.HandlesTarget(Self);
  if Result then
    Action.ExecuteTarget(Self);
end;

function TComponent.UpdateAction(Action: TBasicAction): Boolean;
begin
  Result := Action.HandlesTarget(Self);
  if Result then
    Action.UpdateTarget(Self);
end;

function TComponent.GetNamePath: string;
begin
  Result := FName;
end;

{ ---------------------------------------------------------------- TBasicAction, TBasicActionLink (section 8.2) }

constructor TBasicAction.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FClients := TList.Create;
end;

destructor TBasicAction.Destroy;
begin
  while FClients.Count > 0 do
    UnRegisterChanges(TBasicActionLink(FClients.Last));
  FClients.Free;
  FClients := nil;
  inherited Destroy;
end;

function TBasicAction.HandlesTarget(Target: TObject): Boolean;
begin
  Result := False;
end;

procedure TBasicAction.UpdateTarget(Target: TObject);
begin
end;

procedure TBasicAction.ExecuteTarget(Target: TObject);
begin
end;

function TBasicAction.Execute: Boolean;
begin
  Result := Assigned(FOnExecute);
  if Result then
    FOnExecute(Self);
end;

function TBasicAction.Update: Boolean;
begin
  Result := Assigned(FOnUpdate);
  if Result then
    FOnUpdate(Self);
end;

procedure TBasicAction.RegisterChanges(Value: TBasicActionLink);
begin
  Value.FAction := Self;
  FClients.Add(Pointer(Value));
end;

procedure TBasicAction.UnRegisterChanges(Value: TBasicActionLink);
begin
  if FClients.Remove(Pointer(Value)) >= 0 then
    Value.FAction := nil;
end;

procedure TBasicAction.Change;
var
  I: Integer;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
  for I := 0 to FClients.Count - 1 do
    TBasicActionLink(FClients[I]).Change;
end;

procedure TBasicAction.SetActionComponent(Value: TComponent);
begin
  if Value <> FActionComponent then
  begin
    if FActionComponent <> nil then
      FActionComponent.RemoveFreeNotification(Self);
    FActionComponent := Value;
    if Value <> nil then
      Value.FreeNotification(Self);
  end;
end;

procedure TBasicAction.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (AComponent = FActionComponent) then
    FActionComponent := nil;
end;

constructor TBasicActionLink.Create(AClient: TObject);
begin
  inherited Create;
  AssignClient(AClient);
end;

destructor TBasicActionLink.Destroy;
begin
  if FAction <> nil then
    FAction.UnRegisterChanges(Self);
  inherited Destroy;
end;

procedure TBasicActionLink.AssignClient(AClient: TObject);
begin
end;

procedure TBasicActionLink.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(FAction);
end;

function TBasicActionLink.IsOnExecuteLinked: Boolean;
begin
  Result := True;
end;

procedure TBasicActionLink.SetOnExecute(Value: TNotifyEvent);
begin
end;

procedure TBasicActionLink.SetAction(Value: TBasicAction);
begin
  if Value <> FAction then
  begin
    if FAction <> nil then
      FAction.UnRegisterChanges(Self);
    if Value <> nil then
      Value.RegisterChanges(Self);
  end;
end;

{ the action runs with ActionComponent = AComponent; False without an action }
function TBasicActionLink.Execute(AComponent: TComponent): Boolean;
var
  A: TBasicAction;
begin
  A := FAction;
  Result := False;
  if A = nil then
    Exit;
  A.ActionComponent := AComponent;
  try
    Result := A.Execute;
  finally
    if FAction = A then
      A.ActionComponent := nil;
  end;
end;

function TBasicActionLink.Update: Boolean;
begin
  Result := (FAction <> nil) and FAction.Update;
end;

{ ---------------------------------------------------------------- class registry (section 9) }

type
  TClassEntry = record
    Name: string;
    AClass: TPersistentClass;
  end;
  TIntConstEntry = record
    TypeInfo: Pointer;
    ToInt: TIdentToInt;
    ToIdent: TIntToIdent;
  end;

var
  RegistryLock: SyncObjs.TCriticalSection;
  ClassTable: array of TClassEntry;
  ClassCount: Integer;
  IntConstTable: array of TIntConstEntry;
  IntConstCount: Integer;

function FindClassEntry(const AName: string): Integer;
begin
  for Result := 0 to ClassCount - 1 do
    if CompareText(ClassTable[Result].Name, AName) = 0 then
      Exit;
  Result := -1;
end;

{ the same class twice is a no-op; another class under the name raises EFilerError }
procedure AddClassEntry(AClass: TPersistentClass; const AName: string);
var
  I: Integer;
begin
  RegistryLock.Enter;
  try
    I := FindClassEntry(AName);
    if I >= 0 then
    begin
      if ClassTable[I].AClass <> AClass then
        raise EFilerError.CreateFmt(SDuplicateClass, [AName]);
      Exit;
    end;
    if ClassCount = Length(ClassTable) then
      SetLength(ClassTable, ClassCount * 2 + 16);
    ClassTable[ClassCount].Name := AName;
    ClassTable[ClassCount].AClass := AClass;
    ClassCount := ClassCount + 1;
  finally
    RegistryLock.Leave;
  end;
end;

procedure RegisterClass(AClass: TPersistentClass);
begin
  AddClassEntry(AClass, AClass.ClassName);
end;

procedure RegisterClasses(const AClasses: array of TPersistentClass);
var
  I: Integer;
begin
  for I := 0 to High(AClasses) do
    RegisterClass(AClasses[I]);
end;

procedure RegisterClassAlias(AClass: TPersistentClass; const Alias: string);
begin
  AddClassEntry(AClass, Alias);
end;

{ removes the class under every name it is registered with }
procedure UnRegisterClass(AClass: TPersistentClass);
var
  I, K: Integer;
begin
  RegistryLock.Enter;
  try
    K := 0;
    for I := 0 to ClassCount - 1 do
      if ClassTable[I].AClass <> AClass then
      begin
        if K <> I then
          ClassTable[K] := ClassTable[I];
        K := K + 1;
      end;
    for I := K to ClassCount - 1 do
      ClassTable[I].Name := '';
    ClassCount := K;
  finally
    RegistryLock.Leave;
  end;
end;

procedure UnRegisterClasses(const AClasses: array of TPersistentClass);
var
  I: Integer;
begin
  for I := 0 to High(AClasses) do
    UnRegisterClass(AClasses[I]);
end;

function GetClass(const AClassName: string): TPersistentClass;
var
  I: Integer;
begin
  RegistryLock.Enter;
  try
    I := FindClassEntry(AClassName);
    if I >= 0 then
      Result := ClassTable[I].AClass
    else
      Result := nil;
  finally
    RegistryLock.Leave;
  end;
end;

function FindClass(const ClassName: string): TPersistentClass;
begin
  Result := GetClass(ClassName);
  if Result = nil then
    raise EClassNotFound.CreateFmt(SClassNotFound, [ClassName]);
end;

procedure RegisterIntegerConsts(AIntegerType: Pointer; IdentToIntFn: TIdentToInt; IntToIdentFn: TIntToIdent);
begin
  RegistryLock.Enter;
  try
    if IntConstCount = Length(IntConstTable) then
      SetLength(IntConstTable, IntConstCount * 2 + 8);
    IntConstTable[IntConstCount].TypeInfo := AIntegerType;
    IntConstTable[IntConstCount].ToInt := IdentToIntFn;
    IntConstTable[IntConstCount].ToIdent := IntToIdentFn;
    IntConstCount := IntConstCount + 1;
  finally
    RegistryLock.Leave;
  end;
end;

{ the latest registration for the type wins }
function FindIntConstEntry(AIntegerType: Pointer): Integer;
begin
  for Result := IntConstCount - 1 downto 0 do
    if IntConstTable[Result].TypeInfo = AIntegerType then
      Exit;
  Result := -1;
end;

function FindIdentToInt(AIntegerType: Pointer): TIdentToInt;
var
  I: Integer;
begin
  Result := nil;
  RegistryLock.Enter;
  try
    I := FindIntConstEntry(AIntegerType);
    if I >= 0 then
      Result := IntConstTable[I].ToInt;
  finally
    RegistryLock.Leave;
  end;
end;

function FindIntToIdent(AIntegerType: Pointer): TIntToIdent;
var
  I: Integer;
begin
  Result := nil;
  RegistryLock.Enter;
  try
    I := FindIntConstEntry(AIntegerType);
    if I >= 0 then
      Result := IntConstTable[I].ToIdent;
  finally
    RegistryLock.Leave;
  end;
end;

function IdentToInt(const Ident: string; var Int: Integer; const Map: array of TIdentMapEntry): Boolean;
var
  I: Integer;
begin
  for I := 0 to High(Map) do
    if CompareText(Map[I].Name, Ident) = 0 then
    begin
      Int := Map[I].Value;
      Result := True;
      Exit;
    end;
  Result := False;
end;

function IntToIdent(Int: Integer; var Ident: string; const Map: array of TIdentMapEntry): Boolean;
var
  I: Integer;
begin
  for I := 0 to High(Map) do
    if Map[I].Value = Int then
    begin
      Ident := Map[I].Name;
      Result := True;
      Exit;
    end;
  Result := False;
end;

{ ---------------------------------------------------------------- TFiler, TReader, TWriter (section 10) }

type
  { a recorded component reference resolved after the root's end (RTL-498) }
  TFixup = class
  public
    Instance: TObject;
    PropInfo: PPropInfo;
    Name: string;
    Path: string;
    Line, Col: Integer;
    Scope: TComponent;
  end;

  { raised after OnError handled a reader error: the property line is skipped (RTL-500) }
  ESkipProperty = class(Exception);

  { RTL-1096: one class whose resources InitInheritedComponent is reading on this thread }
  PFrameReadEntry = ^TFrameReadEntry;
  TFrameReadEntry = record
    Cls: TClass;
    Next: PFrameReadEntry;
  end;

threadvar
  { the classes being read, innermost first }
  FrameReadChain: PFrameReadEntry;
  { set by a reader while the constructor of an "inline" object runs: the reader and the object's keyword }
  PendingInlineReader: TReader;
  PendingInlineLine, PendingInlineCol: Integer;

type
  { a read-only stream over the bytes of an embedded form }
  TPointerStream = class(TCustomMemoryStream)
  public
    constructor Create(P: Pointer; ASize: NativeInt);
  end;

constructor TPointerStream.Create(P: Pointer; ASize: NativeInt);
begin
  inherited Create;
  SetPointer(P, ASize);
end;

constructor TFiler.Create(Stream: TStream; BufSize: Integer);
begin
  inherited Create;
  FStream := Stream;
end;

{ BufSize is ignored: the rest of the stream is loaded at once }
constructor TReader.Create(Stream: TStream; BufSize: Integer);
var
  N: Int64;
begin
  inherited Create(Stream, BufSize);
  N := Stream.Size - Stream.Position;
  if N < 0 then
    N := 0;
  SetLength(FBytes, Integer(N));
  if N > 0 then
    Stream.ReadBuffer(FBytes[0], Integer(N));
  FLoaded := TList.Create;
  FCreated := TList.Create;
  FFixups := TList.Create;
  FApplyRootName := True;
  FLex.Line := 1;
  FLex.Col := 1;
  FLex.Pos := 1;
  FLex.TokLine := 1;
  FLex.TokCol := 1;
end;

destructor TReader.Destroy;
var
  I: Integer;
begin
  if FFixups <> nil then
    for I := 0 to FFixups.Count - 1 do
      TFixup(FFixups[I]).Free;
  FFixups.Free;
  FCreated.Free;
  FLoaded.Free;
  inherited Destroy;
end;

function TReader.GetLine: Integer;
begin
  Result := FLex.TokLine;
end;

function TReader.GetColumn: Integer;
begin
  Result := FLex.TokCol;
end;

{ ---- errors (RTL-500) }

procedure TReader.Error(const Code, Text: string; Line, Col: Integer);
var
  Msg: string;
  Handled: Boolean;
begin
  Msg := FSourceName + '(' + IntToStr(Line) + ',' + IntToStr(Col) + '): error ' + Code + ': ' + Text;
  if (Code <> 'F0001') and (Code <> 'F0101') and (Code <> 'F0106') and (Code <> 'F0107') and (Code <> 'F0109') and
    (Code <> 'F0110') and (Code <> 'F0112') and (Code <> 'F0113') and Assigned(FOnError) then
  begin
    Handled := False;
    FOnError(Self, Msg, Handled);
    if Handled then
      raise ESkipProperty.Create(Msg);
  end;
  if Code = 'F0101' then
    raise EClassNotFound.Create(Msg);
  if Code = 'F0104' then
    raise EMethodNotFound.Create(Msg);
  raise EReadError.Create(Msg);
end;

procedure TReader.SyntaxError(const Detail: string);
begin
  Error('F0001', Format(SFormSyntaxError, [Detail]), FLex.TokLine, FLex.TokCol);
end;

{ a lexical error at the current character }
procedure TReader.LexError(const Detail: string);
begin
  FLex.TokLine := FLex.Line;
  FLex.TokCol := FLex.Col;
  SyntaxError(Detail);
end;

function TReader.TokenDesc: string;
begin
  case FLex.Tok of
    ftEOF:
      Result := 'end of file';
    ftLineEnd:
      Result := 'end of line';
  else
    Result := '"' + Copy(FText, FLex.TokPos, FLex.Pos - FLex.TokPos) + '"';
  end;
end;

procedure TReader.Unexpected(const What: string);
begin
  if FLex.Tok = ftEOF then
    SyntaxError('unexpected end of file')
  else
    SyntaxError('expected ' + What + ' but found ' + TokenDesc);
end;

{ F0103 at the current token; a missing value at the end of the file is a syntax error }
procedure TReader.TokenValueError;
begin
  if FLex.Tok = ftEOF then
    SyntaxError('unexpected end of file');
  Error('F0103', Format(SFormInvalidPropertyValue, [FPropName]), FLex.TokLine, FLex.TokCol);
end;

{ R-203: F0103 for the property being read, at the first token of its value - for the read procs of defined
  properties, also after they consumed the value (a binary property's stream proc reaches the reader through the
  Filer passed to DefineProperties). OnError may handle it: the property line is then skipped. }
procedure TReader.PropValueError;
begin
  Error('F0103', Format(SFormInvalidPropertyValue, [FPropName]), FValueLine, FValueCol);
end;

{ ---- lexer (RTL-485 ... RTL-491) }

{ RTL-485: UTF-8 with an optional BOM; invalid sequences, overlong forms and surrogate code points are F0001 }
procedure TReader.DecodeText;
var
  I, N, K, J, Len, Line, Col: Integer;
  B, C: Byte;
  CP, MinCP: Cardinal;
  S: string;
  Bad: Boolean;
begin
  N := Length(FBytes);
  I := 0;
  if (N >= 3) and (FBytes[0] = $EF) and (FBytes[1] = $BB) and (FBytes[2] = $BF) then
    I := 3;
  SetLength(S, N);
  K := 0;
  Line := 1;
  Col := 1;
  while I < N do
  begin
    B := FBytes[I];
    Bad := False;
    Len := 1;
    MinCP := 0;
    if B < $80 then
      CP := B
    else if (B and $E0) = $C0 then
    begin
      CP := B and $1F;
      Len := 2;
      MinCP := $80;
    end
    else if (B and $F0) = $E0 then
    begin
      CP := B and $0F;
      Len := 3;
      MinCP := $800;
    end
    else if (B and $F8) = $F0 then
    begin
      CP := B and $07;
      Len := 4;
      MinCP := $10000;
    end
    else
    begin
      CP := 0;
      Bad := True;
    end;
    if not Bad and (I + Len > N) then
      Bad := True;
    J := 1;
    while not Bad and (J < Len) do
    begin
      C := FBytes[I + J];
      if (C and $C0) <> $80 then
        Bad := True
      else
        CP := (CP shl 6) or (C and $3F);
      J := J + 1;
    end;
    if not Bad and (Len > 1) and ((CP < MinCP) or (CP > $10FFFF) or ((CP >= $D800) and (CP <= $DFFF))) then
      Bad := True;
    if Bad then
    begin
      FLex.TokLine := Line;
      FLex.TokCol := Col;
      SyntaxError('invalid UTF-8');
    end;
    if CP < $10000 then
    begin
      K := K + 1;
      S[K] := Char(CP);
      Col := Col + 1;
    end
    else
    begin
      CP := CP - $10000;
      S[K + 1] := Char($D800 + (CP shr 10));
      S[K + 2] := Char($DC00 + (CP and $3FF));
      K := K + 2;
      Col := Col + 2;
    end;
    if CP = 10 then
    begin
      Line := Line + 1;
      Col := 1;
    end;
    I := I + Len;
  end;
  SetLength(S, K);
  FText := S;
end;

function TReader.Peek(Offset: Integer): Char;
begin
  if FLex.Pos + Offset <= Length(FText) then
    Result := FText[FLex.Pos + Offset]
  else
    Result := #0;
end;

procedure TReader.Advance;
begin
  if FText[FLex.Pos] = #10 then
  begin
    FLex.Line := FLex.Line + 1;
    FLex.Col := 1;
  end
  else
    FLex.Col := FLex.Col + 1;
  FLex.Pos := FLex.Pos + 1;
end;

function IsFormDigit(C: Char): Boolean;
begin
  Result := (C >= '0') and (C <= '9');
end;

function IsFormLetter(C: Char): Boolean;
begin
  Result := ((C >= 'A') and (C <= 'Z')) or ((C >= 'a') and (C <= 'z')) or (C = '_');
end;

function HexValue(C: Char): Integer;
begin
  if (C >= '0') and (C <= '9') then
    Result := Ord(C) - Ord('0')
  else if (C >= 'A') and (C <= 'F') then
    Result := Ord(C) - Ord('A') + 10
  else if (C >= 'a') and (C <= 'f') then
    Result := Ord(C) - Ord('a') + 10
  else
    Result := -1;
end;

function IsBlankChar(C: Char): Boolean;
begin
  Result := (C = ' ') or (C = #9);
end;

procedure TReader.NextToken;
var
  C: Char;
begin
  while IsBlankChar(Peek(0)) do
    Advance;
  FLex.TokLine := FLex.Line;
  FLex.TokCol := FLex.Col;
  FLex.TokPos := FLex.Pos;
  FLex.TokText := '';
  if FLex.Pos > Length(FText) then
  begin
    FLex.Tok := ftEOF;
    Exit;
  end;
  C := Peek(0);
  if (C = #10) or ((C = #13) and (Peek(1) = #10)) then   { RTL-486 }
  begin
    FLex.Tok := ftLineEnd;
    if C = #13 then
      Advance;
    Advance;
  end
  else if IsFormLetter(C) then
  begin
    while IsFormLetter(Peek(0)) or IsFormDigit(Peek(0)) do
      Advance;
    FLex.Tok := ftIdent;
    FLex.TokText := Copy(FText, FLex.TokPos, FLex.Pos - FLex.TokPos);
  end
  else if IsFormDigit(C) or (C = '$') or ((C = '-') and IsFormDigit(Peek(1))) then
    ScanNumber
  else if (C = '''') or (C = '#') then
    ScanString
  else if (C = '=') or (C = ':') or (C = '.') or (C = ',') or (C = '[') or (C = ']') or (C = '(') or (C = ')')
    or (C = '{') or (C = '}') or (C = '<') or (C = '>') then
  begin
    FLex.Tok := ftSymbol;
    FLex.TokText := C;
    Advance;
  end
  else
    LexError('invalid character');   { RTL-491 }
end;

{ RTL-490: decimal within Int64; $ with 1-16 hex digits, up to 8 digits a 32-bit pattern; floats contain "." }
procedure TReader.ScanNumber;
var
  Neg, Overflow: Boolean;
  U: UInt64;
  Digits, D: Integer;
  F: Double;
begin
  FLex.TokHex32 := False;
  if Peek(0) = '$' then
  begin
    Advance;
    U := 0;
    Digits := 0;
    while HexValue(Peek(0)) >= 0 do
    begin
      if Digits = 16 then
        SyntaxError('integer out of range');
      U := (U shl 4) or UInt64(HexValue(Peek(0)));
      Digits := Digits + 1;
      Advance;
    end;
    if Digits = 0 then
      LexError('invalid character');
    FLex.Tok := ftInteger;
    if Digits <= 8 then
    begin
      FLex.TokHex32 := True;
      FLex.TokInt := Integer(Cardinal(U));
    end
    else
      FLex.TokInt := Int64(U);
    Exit;
  end;
  Neg := Peek(0) = '-';
  if Neg then
    Advance;
  U := 0;
  Overflow := False;
  while IsFormDigit(Peek(0)) do
  begin
    D := Ord(Peek(0)) - Ord('0');
    if U > 922337203685477580 then
      Overflow := True
    else
      U := U * 10 + UInt64(D);
    Advance;
  end;
  if (Peek(0) = '.') and IsFormDigit(Peek(1)) then
  begin
    Advance;
    while IsFormDigit(Peek(0)) do
      Advance;
    if ((Peek(0) = 'e') or (Peek(0) = 'E')) and (IsFormDigit(Peek(1))
      or (((Peek(1) = '+') or (Peek(1) = '-')) and IsFormDigit(Peek(2)))) then
    begin
      Advance;
      if (Peek(0) = '+') or (Peek(0) = '-') then
        Advance;
      while IsFormDigit(Peek(0)) do
        Advance;
    end;
    if not TryStrToFloat(Copy(FText, FLex.TokPos, FLex.Pos - FLex.TokPos), F, InvariantFormatSettings) then
      SyntaxError('float out of range');
    FLex.Tok := ftFloat;
    FLex.TokFloat := F;
    Exit;
  end;
  if Overflow or (((U shr 63) <> 0) and not (Neg and (U = UInt64(1) shl 63))) then
    SyntaxError('integer out of range');
  FLex.Tok := ftInteger;
  if Neg then
    FLex.TokInt := Int64(UInt64(0) - U)
  else
    FLex.TokInt := Int64(U);
end;

{ RTL-489: quoted parts, #n / #$h code units, "+" at the end of a line continues on the next line }
procedure TReader.ScanString;
var
  S: string;
  V, Digits, H, PlusLine, PlusCol: Integer;
  More: Boolean;
begin
  S := '';
  repeat
    while (Peek(0) = '''') or (Peek(0) = '#') do
      if Peek(0) = '''' then
      begin
        Advance;
        while True do
        begin
          if (FLex.Pos > Length(FText)) or (Peek(0) = #10) or ((Peek(0) = #13) and (Peek(1) = #10)) then
            SyntaxError('unterminated string');
          if Peek(0) = '''' then
          begin
            Advance;
            if Peek(0) <> '''' then
              Break;
            S := S + '''';
            Advance;
          end
          else
          begin
            S := S + Peek(0);
            Advance;
          end;
        end;
      end
      else
      begin
        Advance;
        V := 0;
        Digits := 0;
        if Peek(0) = '$' then
        begin
          Advance;
          H := HexValue(Peek(0));
          while H >= 0 do
          begin
            if V <= $FFFF then
              V := V * 16 + H;
            Digits := Digits + 1;
            Advance;
            H := HexValue(Peek(0));
          end;
        end
        else
          while IsFormDigit(Peek(0)) do
          begin
            if V <= $FFFF then
              V := V * 10 + Ord(Peek(0)) - Ord('0');
            Digits := Digits + 1;
            Advance;
          end;
        if Digits = 0 then
          LexError('invalid character');
        if V > $FFFF then
          SyntaxError('integer out of range');
        S := S + Char(V);
      end;
    More := False;
    while IsBlankChar(Peek(0)) do
      Advance;
    if Peek(0) = '+' then
    begin
      PlusLine := FLex.Line;
      PlusCol := FLex.Col;
      Advance;
      while IsBlankChar(Peek(0)) do
        Advance;
      if not ((Peek(0) = #10) or ((Peek(0) = #13) and (Peek(1) = #10))) then
      begin
        FLex.TokLine := PlusLine;
        FLex.TokCol := PlusCol;
        SyntaxError('invalid character');
      end;
      while IsBlankChar(Peek(0)) or (Peek(0) = #10) or ((Peek(0) = #13) and (Peek(1) = #10)) do
        Advance;
      if (Peek(0) <> '''') and (Peek(0) <> '#') then
      begin
        if FLex.Pos > Length(FText) then
          LexError('unexpected end of file')
        else
          LexError('expected string but found "' + Peek(0) + '"');
      end;
      More := True;
    end;
  until not More;
  FLex.Tok := ftString;
  FLex.TokStr := S;
end;

function TReader.IsSymbol(const Symbol: string): Boolean;
begin
  Result := (FLex.Tok = ftSymbol) and (FLex.TokText = Symbol);
end;

{ RTL-488: keywords are ASCII case-insensitive }
function TReader.IsKeyword(const Keyword: string): Boolean;
begin
  Result := (FLex.Tok = ftIdent) and (CompareText(FLex.TokText, Keyword) = 0);
end;

procedure TReader.Expect(const Symbol: string);
begin
  if not IsSymbol(Symbol) then
    Unexpected('"' + Symbol + '"');
  NextToken;
end;

{ one or more line ends; the end of the file also ends a line }
procedure TReader.ExpectLineEnd;
begin
  if FLex.Tok = ftLineEnd then
    SkipLineEnds
  else if FLex.Tok <> ftEOF then
    Unexpected('end of line');
end;

procedure TReader.SkipLineEnds;
begin
  while FLex.Tok = ftLineEnd do
    NextToken;
end;

function TReader.ReadIdentToken: string;
begin
  if FLex.Tok <> ftIdent then
    Unexpected('identifier');
  Result := FLex.TokText;
  NextToken;
end;

function TReader.ReadQualIdent: string;
begin
  Result := ReadIdentToken;
  while IsSymbol('.') do
  begin
    NextToken;
    Result := Result + '.' + ReadIdentToken;
  end;
end;

(* RTL-505: hex digits in any case and any line structure up to the closing brace; the current token is the opening one *)
procedure TReader.ReadBinaryBlock(Stream: TStream);
var
  Bytes: TBytes;
  Count, Hi, H: Integer;
  C: Char;
begin
  SetLength(Bytes, 0);
  Count := 0;
  Hi := -1;
  while True do
  begin
    if FLex.Pos > Length(FText) then
      LexError('unexpected end of file');
    C := Peek(0);
    if C = '}' then
      Break;
    if IsBlankChar(C) or (C = #10) or ((C = #13) and (Peek(1) = #10)) then
      Advance
    else
    begin
      H := HexValue(C);
      if H < 0 then
        LexError('invalid character');
      if Hi < 0 then
        Hi := H
      else
      begin
        if Count = Length(Bytes) then
          SetLength(Bytes, Count * 2 + 64);
        Bytes[Count] := Byte(Hi * 16 + H);
        Count := Count + 1;
        Hi := -1;
      end;
      Advance;
    end;
  end;
  if Hi >= 0 then
    LexError('invalid character');
  Advance;
  if (Stream <> nil) and (Count > 0) then
    Stream.WriteBuffer(Bytes[0], Count);
  NextToken;
end;

{ ---- value access for DefineProperty read procs (RTL-505) }

function TReader.NextValue: TValueType;
begin
  case FLex.Tok of
    ftInteger:
      if FLex.TokHex32 then
        Result := vaInt32
      else if (FLex.TokInt >= -128) and (FLex.TokInt <= 127) then
        Result := vaInt8
      else if (FLex.TokInt >= -32768) and (FLex.TokInt <= 32767) then
        Result := vaInt16
      else if (FLex.TokInt >= -2147483647 - 1) and (FLex.TokInt <= 2147483647) then
        Result := vaInt32
      else
        Result := vaInt64;
    ftFloat:
      Result := vaExtended;
    ftString:
      Result := vaString;
    ftIdent:
      if IsKeyword('True') then
        Result := vaTrue
      else if IsKeyword('False') then
        Result := vaFalse
      else if IsKeyword('nil') then
        Result := vaNil
      else
        Result := vaIdent;
    ftSymbol:
      if IsSymbol('[') then
        Result := vaSet
      else if IsSymbol('(') then
        Result := vaList
      else if IsSymbol('{') then
        Result := vaBinary
      else if IsSymbol('<') then
        Result := vaCollection
      else
        Result := vaNull;
  else
    Result := vaNull;
  end;
end;

function TReader.ReadInteger: Integer;
begin
  if FLex.Tok <> ftInteger then
    TokenValueError;
  if not FLex.TokHex32 and ((FLex.TokInt < -2147483647 - 1) or (FLex.TokInt > 2147483647)) then
    TokenValueError;
  Result := Integer(FLex.TokInt);
  NextToken;
end;

{ a $ literal of up to 8 digits is an unsigned 32-bit value here }
function TReader.ReadInt64: Int64;
begin
  if FLex.Tok <> ftInteger then
    TokenValueError;
  Result := FLex.TokInt;
  if FLex.TokHex32 then
    Result := Result and $FFFFFFFF;
  NextToken;
end;

function TReader.ReadFloat: Double;
begin
  if FLex.Tok = ftFloat then
    Result := FLex.TokFloat
  else if FLex.Tok = ftInteger then
    Result := FLex.TokInt
  else
  begin
    TokenValueError;
    Result := 0;
  end;
  NextToken;
end;

function TReader.ReadString: string;
begin
  if FLex.Tok <> ftString then
    TokenValueError;
  Result := FLex.TokStr;
  NextToken;
end;

function TReader.ReadBoolean: Boolean;
begin
  if IsKeyword('True') then
    Result := True
  else if IsKeyword('False') then
    Result := False
  else
  begin
    TokenValueError;
    Result := False;
  end;
  NextToken;
end;

function TReader.ReadIdent: string;
begin
  if FLex.Tok <> ftIdent then
    TokenValueError;
  Result := ReadQualIdent;
end;

function TReader.ReadChar: Char;
begin
  if (FLex.Tok <> ftString) or (Length(FLex.TokStr) <> 1) then
    TokenValueError;
  Result := FLex.TokStr[1];
  NextToken;
end;

(* "[" ident { "," ident } "]" over the element type of the set type SetType *)
function TReader.ReadSet(SetType: Pointer): Integer;
var
  CompType: PTypeInfo;
  Names: string;
begin
  if not IsSymbol('[') then
    TokenValueError;
  CompType := GetTypeData(PTypeInfo(SetType))^.CompType;
  NextToken;
  Names := '';
  while not IsSymbol(']') do
  begin
    if (FLex.Tok <> ftIdent) or (GetEnumValue(CompType, FLex.TokText) < 0) then
      TokenValueError;
    if Names <> '' then
      Names := Names + ',';
    Names := Names + FLex.TokText;
    NextToken;
    if IsSymbol(',') then
      NextToken
    else if not IsSymbol(']') then
      Unexpected('"]"');
  end;
  NextToken;
  Result := StringToSet(PTypeInfo(SetType), Names);
end;

procedure TReader.ReadListBegin;
begin
  if not IsSymbol('(') then
    TokenValueError;
  NextToken;
  SkipLineEnds;
end;

function TReader.EndOfList: Boolean;
begin
  SkipLineEnds;
  Result := IsSymbol(')');
end;

procedure TReader.ReadListEnd;
begin
  SkipLineEnds;
  if not IsSymbol(')') then
    Unexpected('")"');
  NextToken;
end;

procedure TReader.SkipValue;
var
  Depth: Integer;
begin
  case FLex.Tok of
    ftInteger, ftFloat, ftString:
      NextToken;
    ftIdent:
      ReadQualIdent;
    ftSymbol:
      if IsSymbol('[') or IsSymbol('(') then
      begin
        if IsSymbol('[') then
          Depth := 1
        else
          Depth := 2;
        NextToken;
        while not ((Depth = 1) and IsSymbol(']')) and not ((Depth = 2) and IsSymbol(')')) do
        begin
          if FLex.Tok = ftEOF then
            SyntaxError('unexpected end of file');
          NextToken;
        end;
        NextToken;
      end
      else if IsSymbol('{') then
        ReadBinaryBlock(nil)
      else if IsSymbol('<') then
      begin
        Depth := 1;
        NextToken;
        while Depth > 0 do
        begin
          if FLex.Tok = ftEOF then
            SyntaxError('unexpected end of file');
          if IsSymbol('{') then
            ReadBinaryBlock(nil)
          else
          begin
            if IsSymbol('<') then
              Depth := Depth + 1
            else if IsSymbol('>') then
              Depth := Depth - 1;
            NextToken;
          end;
        end;
      end
      else
        Unexpected('value');
  else
    Unexpected('value');
  end;
end;

procedure TReader.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
  if (FMatchName <> '') and not FMatched and (CompareText(Name, FMatchName) = 0) then
  begin
    FMatched := True;
    if Assigned(ReadData) then
      ReadData(Self)
    else
      SkipValue;
  end;
end;

procedure TReader.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc; HasData: Boolean);
var
  M: TMemoryStream;
begin
  if (FMatchName <> '') and not FMatched and (CompareText(Name, FMatchName) = 0) then
  begin
    FMatched := True;
    if not IsSymbol('{') then
      TokenValueError;
    M := TMemoryStream.Create;
    try
      ReadBinaryBlock(M);
      M.Position := 0;
      if Assigned(ReadData) then
        ReadData(M);
    finally
      M.Free;
    end;
  end;
end;

{ ---- parser (RTL-495 ... RTL-499) }

procedure TReader.ReadStringsValue(Strings: TStrings);
begin
  NextToken;
  SkipLineEnds;
  Strings.BeginUpdate;
  try
    Strings.Clear;
    while not IsSymbol(')') do
    begin
      if FLex.Tok <> ftString then
        TokenValueError;
      Strings.Add(FLex.TokStr);
      NextToken;
      SkipLineEnds;
    end;
    NextToken;
  finally
    Strings.EndUpdate;
  end;
end;

(* "<" { "item" NL { property } "end" NL } ">": the collection is cleared, then one Add per item *)
procedure TReader.ReadCollection(Collection: TCollection);
var
  Item: TCollectionItem;
begin
  if not IsSymbol('<') then
    TokenValueError;
  NextToken;
  SkipLineEnds;
  Collection.BeginUpdate;
  try
    Collection.Clear;
    while IsKeyword('item') do
    begin
      NextToken;
      ExpectLineEnd;
      Item := Collection.Add;
      ReadItemProperties(Item);
    end;
    if not IsSymbol('>') then
      Unexpected('"item" or ">"');
    NextToken;
  finally
    Collection.EndUpdate;
  end;
end;

procedure TReader.ReadItemProperties(Item: TPersistent);
begin
  while not IsKeyword('end') do
    ReadProperty(Item);
  NextToken;
  ExpectLineEnd;
end;

{ RTL-1104: the root's name or the header name of the file being read, also when the header name was not applied
  (a frame read with an owner, RTL-1099) }
function TReader.NamesRoot(const Name: string): Boolean;
begin
  Result := (FLookupRoot <> nil) and (((FLookupRoot.Name <> '') and (CompareText(FLookupRoot.Name, Name) = 0)) or
    ((FRootHeader <> '') and (CompareText(FRootHeader, Name) = 0)));
end;

{ the components named by the dotted Path below Start, each part a component of the previous one; nil when a part
  is missing }
function FindComponentPath(Start: TComponent; const Path: string): TComponent;
var
  P: Integer;
  Rest, Part: string;
begin
  Result := Start;
  Rest := Path;
  while (Result <> nil) and (Rest <> '') do
  begin
    P := Pos('.', Rest);
    if P = 0 then
    begin
      Part := Rest;
      Rest := '';
    end
    else
    begin
      Part := Copy(Rest, 1, P - 1);
      Rest := Copy(Rest, P + 1, MaxInt);
    end;
    Result := Result.FindComponent(Part);
  end;
end;

{ RTL-1093: the first part of a reference is looked up in the innermost frame instance first (the instance itself by
  name, then its components), else among the names of the file (RTL-1104: the root's name or header name is the
  lookup root, else a component of the lookup root); each further part is a component of the previous part's
  instance (Outer1.Home.StreetEdit). A qualified reference not found so goes through FindGlobalComponent (RTL-498) }
function TReader.ResolveReference(const Name: string; Scope: TComponent): TComponent;
var
  P: Integer;
  First, Rest: string;
begin
  Result := nil;
  P := Pos('.', Name);
  if P = 0 then
  begin
    First := Name;
    Rest := '';
  end
  else
  begin
    First := Copy(Name, 1, P - 1);
    Rest := Copy(Name, P + 1, MaxInt);
  end;
  if Scope <> nil then
  begin
    if CompareText(Scope.Name, First) = 0 then
      Result := Scope
    else
      Result := Scope.FindComponent(First);
  end;
  if (Result = nil) and (FLookupRoot <> nil) then
  begin
    if NamesRoot(First) then
      Result := FLookupRoot
    else
      Result := FLookupRoot.FindComponent(First);
  end;
  Result := FindComponentPath(Result, Rest);
  if (Result = nil) and (P > 0) and Assigned(FindGlobalComponent) then
    Result := FindComponentPath(FindGlobalComponent(First), Rest);
end;

{ RTL-496 component reference: now if possible, else a fix-up resolved after the root's end }
procedure TReader.ReadReference(Instance: TPersistent; PropInfo: PPropInfo);
var
  RefLine, RefCol: Integer;
  Name: string;
  C: TComponent;
  F: TFixup;
begin
  RefLine := FLex.TokLine;
  RefCol := FLex.TokCol;
  Name := ReadQualIdent;
  if Assigned(FOnReferenceName) then
    FOnReferenceName(Self, Name);
  C := ResolveReference(Name, FScope);
  if C = nil then
  begin
    F := TFixup.Create;
    F.Instance := Instance;
    F.PropInfo := PropInfo;
    F.Name := Name;
    F.Path := FPropName;
    F.Line := RefLine;
    F.Col := RefCol;
    F.Scope := FScope;
    FFixups.Add(Pointer(F));
  end
  else if not C.InheritsFrom(GetObjectPropClass(PropInfo)) then
    Error('F0103', Format(SFormInvalidPropertyValue, [FPropName]), RefLine, RefCol)
  else
    SetObjectProp(Instance, PropInfo, C);
end;

{ RTL-498 }
procedure TReader.ResolveFixups;
var
  I: Integer;
  F: TFixup;
  C: TComponent;
begin
  for I := 0 to FFixups.Count - 1 do
  begin
    F := TFixup(FFixups[I]);
    try
      C := ResolveReference(F.Name, F.Scope);
      if C = nil then
        Error('F0105', Format(SFormComponentNotFound, [F.Name]), F.Line, F.Col)
      else if not C.InheritsFrom(GetObjectPropClass(F.PropInfo)) then
        Error('F0103', Format(SFormInvalidPropertyValue, [F.Path]), F.Line, F.Col)
      else
        SetObjectProp(F.Instance, F.PropInfo, C);
    except
      on E: ESkipProperty do
        ;
    end;
  end;
end;

{ RTL-496 step 2 for an integer property: a number or an identifier of the type's RegisterIntegerConsts map }
function TReader.ReadOrdinalValue(PropInfo: PPropInfo): Int64;
var
  TD: PTypeData;
  ToInt: TIdentToInt;
  I32: Integer;
begin
  TD := GetTypeData(PropInfo^.PropType);
  Result := 0;
  if FLex.Tok = ftInteger then
  begin
    Result := FLex.TokInt;
    if FLex.TokHex32 and (TD^.OrdType = otULong) then
      Result := Result and $FFFFFFFF;
  end
  else if FLex.Tok = ftIdent then
  begin
    ToInt := FindIdentToInt(PropInfo^.PropType);
    I32 := 0;
    if not Assigned(ToInt) or not ToInt(FLex.TokText, I32) then
      TokenValueError;
    Result := I32;
  end
  else
    TokenValueError;
  if (Result < TD^.MinValue) or (Result > TD^.MaxValue) then
    TokenValueError;
  NextToken;
end;

{ RTL-496 step 3 }
procedure TReader.ApplyValue(Instance: TPersistent; PropInfo: PPropInfo; const Path: string);
var
  TI: PTypeInfo;
  V: Integer;
  F: Double;
  Sub: TObject;
  Name: string;
  Addr: Pointer;
  Err: Boolean;
  M: TMethod;
begin
  TI := PropInfo^.PropType;
  case TI^.Kind of
    tkInteger:
      SetOrdProp(Instance, PropInfo, ReadOrdinalValue(PropInfo));
    tkInt64:
      SetInt64Prop(Instance, PropInfo, ReadInt64);
    tkChar, tkWChar:
      SetOrdProp(Instance, PropInfo, Ord(ReadChar));
    tkEnumeration:
      begin
        if FLex.Tok <> ftIdent then
          TokenValueError;
        V := GetEnumValue(TI, FLex.TokText);
        if V < 0 then
          TokenValueError;
        SetOrdProp(Instance, PropInfo, V);
        NextToken;
      end;
    tkFloat:
      begin
        F := ReadFloat;
        SetFloatProp(Instance, PropInfo, F);
      end;
    tkUString, tkAString, tkSString:
      SetStrProp(Instance, PropInfo, ReadString);
    tkSet:
      SetOrdProp(Instance, PropInfo, ReadSet(TI));
    tkClass:
      if IsKeyword('nil') then
      begin
        SetObjectProp(Instance, PropInfo, nil);
        NextToken;
      end
      else if IsSymbol('(') then
      begin
        Sub := GetObjectProp(Instance, PropInfo);
        if not (Sub is TStrings) then
          TokenValueError;
        ReadStringsValue(TStrings(Sub));
      end
      else if IsSymbol('<') then
      begin
        Sub := GetObjectProp(Instance, PropInfo);
        if not (Sub is TCollection) then
          TokenValueError;
        ReadCollection(TCollection(Sub));
      end
      else if FLex.Tok = ftIdent then
        ReadReference(Instance, PropInfo)
      else
        TokenValueError;
    tkMethod:
      begin
        if FLex.Tok <> ftIdent then
          TokenValueError;
        Name := FLex.TokText;
        Addr := FRoot.MethodAddress(Name);
        Err := Addr = nil;
        if Assigned(FOnFindMethod) then
          FOnFindMethod(Self, Name, Addr, Err);
        if Addr = nil then
          Error('F0104', Format(SFormMethodNotFound, [Name, FRoot.ClassName]), FLex.TokLine, FLex.TokCol);
        M.Code := Addr;
        M.Data := Pointer(FRoot);
        SetMethodProp(Instance, PropInfo, M);
        NextToken;
      end;
  else
    TokenValueError;
  end;
end;

{ RTL-496 steps 1 and 2: the path into sub-objects, then a published property or a defined one }
procedure TReader.ReadPropValue(Instance: TPersistent; const Path: string);
var
  Obj, Sub: TObject;
  Rest, Part: string;
  P: Integer;
  Info: PPropInfo;
begin
  Obj := Instance;
  Rest := Path;
  P := Pos('.', Rest);
  while P > 0 do
  begin
    Part := Copy(Rest, 1, P - 1);
    Rest := Copy(Rest, P + 1, MaxInt);
    Info := GetPropInfo(Obj, Part);
    Sub := nil;
    if (Info <> nil) and (Info^.PropType^.Kind = tkClass) then
      Sub := GetObjectProp(Obj, Info);
    if Sub = nil then
      Error('F0102', Format(SFormPropertyNotFound, [Path, Instance.ClassName]), FPropLine, FPropCol);
    Obj := Sub;
    P := Pos('.', Rest);
  end;
  Info := GetPropInfo(Obj, Rest);
  if Info = nil then
  begin
    if Obj is TPersistent then
    begin
      FMatchName := Rest;
      FMatched := False;
      try
        TPersistent(Obj).DefineProperties(Self);
      finally
        FMatchName := '';
      end;
      if FMatched then
        Exit;
    end;
    Error('F0102', Format(SFormPropertyNotFound, [Path, Instance.ClassName]), FPropLine, FPropCol);
  end;
  ApplyValue(TPersistent(Obj), Info, Path);
end;

{ qualIdent "=" value NL; an error handled by OnError skips the value }
procedure TReader.ReadProperty(Instance: TPersistent);
var
  PathLine, PathCol: Integer;
  Path: string;
  Saved: TFormLexState;
begin
  PathLine := FLex.TokLine;
  PathCol := FLex.TokCol;
  Path := ReadQualIdent;
  Expect('=');
  if (FLex.Tok = ftLineEnd) or (FLex.Tok = ftEOF) then
    Unexpected('value');
  FPropName := Path;
  FPropLine := PathLine;
  FPropCol := PathCol;
  FValueLine := FLex.TokLine;
  FValueCol := FLex.TokCol;
  Saved := FLex;
  try
    ReadPropValue(Instance, Path);
  except
    on E: ESkipProperty do
    begin
      FLex := Saved;
      SkipValue;
    end;
  end;
  ExpectLineEnd;
end;

{ property lines, then nested objects with Parent = Instance, then "end" (RTL-499) }
procedure TReader.ReadData(Instance: TComponent);
var
  OldParent: TComponent;
begin
  while not (IsKeyword('object') or IsKeyword('inherited') or IsKeyword('inline') or IsKeyword('end')) do
    ReadProperty(Instance);
  OldParent := FParent;
  FParent := Instance;
  try
    while IsKeyword('object') or IsKeyword('inherited') or IsKeyword('inline') do
      ReadComponentObject;
  finally
    FParent := OldParent;
  end;
  if not IsKeyword('end') then
    Unexpected('"end"');
  NextToken;
  ExpectLineEnd;
end;

{ RTL-1105: the component class of an object line whose class GetClass does not know, from the published fields
  (Visibility 3, ABI-144) of the class chain of the root of this file (a form, or a frame instance reading its own
  file): the field named Name whose class is named TypeName first, then the first field whose class is named
  TypeName; nil when there is none. Override blocks create nothing, nested object lines in an instance are F0110 and
  the new components of a descendant file are owned by the root (RTL-1126), so the root is the component that owns
  every file scope this lookup is asked from; its class chain holds the fields of every level (RTL-1129). }
function TReader.FieldClass(const Name, TypeName: string): TPersistentClass;
var
  Pass, I, Count: Integer;
  VMT: TClass;
  F: PFieldInfo;
  C: TClass;
begin
  Result := nil;
  if FRoot = nil then
    Exit;
  for Pass := 0 to 1 do
  begin
    VMT := FRoot.ClassType;
    while VMT <> nil do
    begin
      F := GetFieldTable(PTypeInfo(VMT.ClassInfo), Count);
      for I := 0 to Count - 1 do
      begin
        if (F[I].Visibility = mvPublished) and (F[I].FieldType^.Kind = tkClass) and
          ((Pass = 1) or (CompareText(F[I].Name, Name) = 0)) then
        begin
          C := GetTypeData(F[I].FieldType)^.ClassType;
          if (C <> nil) and C.InheritsFrom(TComponent) and (CompareText(C.ClassName, TypeName) = 0) then
          begin
            Result := TPersistentClass(C);
            Exit;
          end;
        end;
      end;
      VMT := VMT.ClassParent;
    end;
  end;
end;

{ RTL-497; an "inline" object is a frame instance (RTL-1090), an "inherited" one an override block (RTL-1091,
  RTL-1125); inside an instance nothing can be added (F0110, RTL-1094), the form scope of a descendant file is open
  (RTL-1126), an "inherited" object without a scope is F0107 (RTL-1133) }
procedure TReader.ReadComponentObject;
var
  KeyLine, KeyCol, NameLine, NameCol, TypeLine, TypeCol: Integer;
  Name, TypeName, Msg: string;
  Cls: TPersistentClass;
  CompClass: TComponentClass;
  C, OldScope: TComponent;
  IsInline, OldFormScope: Boolean;
  OldPending: TReader;
  OldLine, OldCol: Integer;
begin
  if IsKeyword('inherited') then
  begin
    if (FScope = nil) and not FFormScope then
      Error('F0107', Format(SFormObjectsNotSupported, [LowerCase(FLex.TokText)]), FLex.TokLine, FLex.TokCol);
    ReadOverrideBlock;
    Exit;
  end;
  if FScope <> nil then
    Error('F0110', Format(SFormFrameNoNewChildren, [FScope.Name]), FLex.TokLine, FLex.TokCol);
  IsInline := IsKeyword('inline');
  KeyLine := FLex.TokLine;
  KeyCol := FLex.TokCol;
  NextToken;
  NameLine := FLex.TokLine;
  NameCol := FLex.TokCol;
  Name := ReadIdentToken;
  Expect(':');
  TypeLine := FLex.TokLine;
  TypeCol := FLex.TokCol;
  TypeName := ReadIdentToken;
  ExpectLineEnd;
  Cls := GetClass(TypeName);
  if Cls = nil then
    Cls := FieldClass(Name, TypeName);
  CompClass := nil;
  if (Cls <> nil) and Cls.InheritsFrom(TComponent) then
    CompClass := TComponentClass(Cls);
  if Assigned(FOnFindComponentClass) then
    FOnFindComponentClass(Self, TypeName, CompClass);
  if CompClass = nil then
    Error('F0101', Format(SFormClassNotFound, [TypeName]), TypeLine, TypeCol);
  C := nil;
  OldPending := PendingInlineReader;
  OldLine := PendingInlineLine;
  OldCol := PendingInlineCol;
  if IsInline then
  begin
    PendingInlineReader := Self;
    PendingInlineLine := KeyLine;
    PendingInlineCol := KeyCol;
  end
  else
    PendingInlineReader := nil;
  try
    if Assigned(FOnCreateComponent) then
      FOnCreateComponent(Self, CompClass, C);
    if C = nil then
      C := CompClass.Create(FOwner);
  finally
    PendingInlineReader := OldPending;
    PendingInlineLine := OldLine;
    PendingInlineCol := OldCol;
  end;
  FCreated.Add(Pointer(C));
  if IsInline then
    Include(C.FComponentState, csInline);
  Include(C.FComponentState, csLoading);
  Include(C.FComponentState, csReading);
  FLoaded.Add(Pointer(C));
  { R-210: the name is set before SetParentComponent, so a refused parent can name the component }
  if Assigned(FOnSetName) then
    FOnSetName(Self, C, Name);
  Msg := '';
  try
    C.Name := Name;
  except
    on E: EComponentError do
      Msg := E.Message;
  end;
  if Msg <> '' then
    Error('F0106', Msg, NameLine, NameCol);
  { an EInvalidOperation from SetParentComponent is F0103 at the object's name; when OnError handles it the
    component stays without a parent and reading continues }
  Msg := '';
  try
    C.SetParentComponent(FParent);
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  if Msg <> '' then
  try
    Error('F0103', Msg, NameLine, NameCol);
  except
    on E: ESkipProperty do
      ;
  end;
  { RTL-1126: a new instance is a closed scope; the objects nested in a component this file creates with "object"
    have no scope }
  OldScope := FScope;
  OldFormScope := FFormScope;
  if csInline in C.FComponentState then
    FScope := C
  else
    FFormScope := False;
  try
    C.ReadState(Self);
  finally
    FScope := OldScope;
    FFormScope := OldFormScope;
  end;
end;

{ RTL-1091: "inherited Name: TypeName" inside a frame instance names a component the frame created; its lines are
  applied, nothing is created, named or re-parented; a nested instance is the scope of its own blocks (RTL-1092).
  RTL-1125, 1127, 1128: in the form scope of a descendant file the block names a component that an earlier level
  created (owned by the root), wherever it is nested; the form scope stays open inside the block }
procedure TReader.ReadOverrideBlock;
var
  NameLine, NameCol: Integer;
  Name, TypeName, ScopeName: string;
  C, OldScope: TComponent;
begin
  NextToken;
  NameLine := FLex.TokLine;
  NameCol := FLex.TokCol;
  Name := ReadIdentToken;
  Expect(':');
  TypeName := ReadIdentToken;
  ExpectLineEnd;
  if FScope <> nil then
  begin
    C := FScope.FindComponent(Name);
    ScopeName := FScope.ClassName;
  end
  else
  begin
    C := FRoot.FindComponent(Name);
    if FAncestorClass <> nil then
      ScopeName := FAncestorClass.ClassName
    else
      ScopeName := FRoot.ClassParent.ClassName;
  end;
  if (C = nil) or (CompareText(C.ClassName, TypeName) <> 0) then
    Error('F0109', Format(SFormFrameChildNotFound, [Name, TypeName, ScopeName]), NameLine, NameCol);
  Include(C.FComponentState, csLoading);
  Include(C.FComponentState, csReading);
  if FLoaded.IndexOf(Pointer(C)) < 0 then
    FLoaded.Add(Pointer(C));
  OldScope := FScope;
  if csInline in C.FComponentState then
    FScope := C;
  try
    C.ReadState(Self);
  finally
    FScope := OldScope;
  end;
end;

{ RTL-495 step 7 }
procedure TReader.Cleanup;
var
  I: Integer;
begin
  { RTL-1098: components reached through override blocks are not destroyed here; their flags are cleared }
  for I := 0 to FLoaded.Count - 1 do
    if FCreated.IndexOf(FLoaded[I]) < 0 then
    begin
      Exclude(TComponent(FLoaded[I]).FComponentState, csLoading);
      Exclude(TComponent(FLoaded[I]).FComponentState, csReading);
    end;
  for I := FCreated.Count - 1 downto 0 do
    TComponent(FCreated[I]).Free;
  FCreated.Clear;
  FLoaded.Clear;
  if FRoot <> nil then
  begin
    Exclude(FRoot.FComponentState, csLoading);
    Exclude(FRoot.FComponentState, csReading);
    if FRootCreated then
    begin
      FRoot.Free;
      FRoot := nil;
    end;
  end;
end;

{ RTL-495; a nil Root is created from the header's class (GetClass); the root of an "inherited" file is the form
  scope (RTL-1125); a level of a chain checks its root keyword (F0112, F0113) and, unless it is the last one, leaves
  Loaded and csLoading to the last level (RTL-1129) }
function TReader.ReadRootComponent(Root: TComponent): TComponent;
var
  NameLine, NameCol, TypeLine, TypeCol, I: Integer;
  Name, TypeName, Msg: string;
  Cls: TPersistentClass;
  IsInherited: Boolean;
begin
  try
    DecodeText;
    NextToken;
    SkipLineEnds;
    if IsKeyword('inline') then
      Error('F0107', Format(SFormObjectsNotSupported, ['inline']), FLex.TokLine, FLex.TokCol);
    if not (IsKeyword('object') or IsKeyword('inherited')) then
      Unexpected('"object"');
    IsInherited := IsKeyword('inherited');
    if (FLevel > 0) and IsInherited and (FAncestorClass = nil) then
      Error('F0112', Format(SFormNoAncestorForm, [FLevelClass.ClassName]), FLex.TokLine, FLex.TokCol);
    if (FLevel > 1) and not IsInherited then
      Error('F0113', Format(SFormMustInherit, [FLevelClass.ClassName, FAncestorClass.ClassName]), FLex.TokLine,
        FLex.TokCol);
    NextToken;
    NameLine := FLex.TokLine;
    NameCol := FLex.TokCol;
    Name := ReadIdentToken;
    Expect(':');
    TypeLine := FLex.TokLine;
    TypeCol := FLex.TokCol;
    TypeName := ReadIdentToken;
    ExpectLineEnd;
    if Root = nil then
    begin
      Cls := GetClass(TypeName);
      if (Cls = nil) or not Cls.InheritsFrom(TComponent) then
        Error('F0101', Format(SFormClassNotFound, [TypeName]), TypeLine, TypeCol);
      Root := TComponentClass(Cls).Create(nil);
      FRootCreated := True;
    end;
    FRoot := Root;
    FLookupRoot := Root;
    FRootHeader := Name;
    FOwner := Root;
    FParent := nil;
    FScope := nil;
    FFormScope := IsInherited;
    Include(Root.FComponentState, csLoading);
    Include(Root.FComponentState, csReading);
    if FLoaded.IndexOf(Pointer(Root)) < 0 then
      FLoaded.Add(Pointer(Root));
    { RTL-1099: a frame read with an owner keeps the name its instance gives it }
    if FApplyRootName and Assigned(FOnSetName) then
      FOnSetName(Self, Root, Name);
    if FApplyRootName and (Name <> '') then
    begin
      Msg := '';
      try
        Root.Name := Name;
      except
        on E: EComponentError do
          Msg := E.Message;
      end;
      if Msg <> '' then
        Error('F0106', Msg, NameLine, NameCol);
    end;
    Root.ReadState(Self);
    SkipLineEnds;
    if FLex.Tok <> ftEOF then
      Unexpected('end of file');
    ResolveFixups;
    for I := FLoaded.Count - 1 downto 0 do
      Exclude(TComponent(FLoaded[I]).FComponentState, csReading);
    { RTL-1129 (b): a level before the last keeps its loaded list, with csLoading, for the next level }
    if not FDeferLoaded then
    begin
      { RTL-1097: the root of a frame constructed for an instance gets its Loaded from the reader of the instance }
      for I := FLoaded.Count - 1 downto 0 do
        if not FDeferRootLoaded or (TComponent(FLoaded[I]) <> Root) then
          TComponent(FLoaded[I]).Loaded;
      FLoaded.Clear;
    end;
    FCreated.Clear;
  except
    Cleanup;
    raise;
  end;
  Result := Root;
end;

procedure TWriter.DefineProperty(const Name: string; ReadData: TReaderProc; WriteData: TWriterProc;
  HasData: Boolean);
begin
end;

procedure TWriter.DefineBinaryProperty(const Name: string; ReadData, WriteData: TStreamProc; HasData: Boolean);
begin
end;

{ RTL-481 }
procedure TWriter.WriteRootComponent(Root: TComponent);
begin
  raise ENotImplemented.Create(SWriteRootNotImplemented);
end;

{ ---------------------------------------------------------------- embedded forms (section 10.2) }

function ReadFormData(Data: Pointer; Size: NativeInt; Instance: TComponent; const SourceName: string;
  ApplyRootName, DeferRootLoaded: Boolean): TComponent;
var
  S: TPointerStream;
  R: TReader;
begin
  S := TPointerStream.Create(Data, Size);
  try
    R := TReader.Create(S, 4096);
    try
      R.SourceName := SourceName;
      R.FApplyRootName := ApplyRootName;
      R.FDeferRootLoaded := DeferRootLoaded;
      Result := R.ReadRootComponent(Instance);
    finally
      R.Free;
    end;
  finally
    S.Free;
  end;
end;

function ReadComponentFromText(const Text: string; Instance: TComponent; const SourceName: string): TComponent;
var
  S: TBytesStream;
  R: TReader;
begin
  S := TBytesStream.Create(TEncoding.UTF8.GetBytes(Text));
  try
    R := TReader.Create(S, 4096);
    try
      R.SourceName := SourceName;
      Result := R.ReadRootComponent(Instance);
    finally
      R.Free;
    end;
  finally
    S.Free;
  end;
end;

{ RTL-1129: level Level (1-based) of a chain, read into Instance from the form of LevelClass, whose ancestor with a
  form is AncestorClass (nil: none). Deferred holds the components the earlier levels loaded; unless this is the
  last level it receives this level's loaded list. Only the last level applies its header name and calls Loaded. }
procedure ReadFormLevel(Data: Pointer; Size: NativeInt; Instance: TComponent; LevelClass, AncestorClass: TClass;
  Level: Integer; IsLast, ApplyRootName, DeferRootLoaded: Boolean; Deferred: TList);
var
  S: TPointerStream;
  R: TReader;
  I: Integer;
begin
  S := TPointerStream.Create(Data, Size);
  try
    R := TReader.Create(S, 4096);
    try
      R.SourceName := LevelClass.ClassName + '.form';
      R.FApplyRootName := ApplyRootName and IsLast;
      R.FDeferRootLoaded := DeferRootLoaded;
      R.FDeferLoaded := not IsLast;
      R.FLevel := Level;
      R.FLevelClass := LevelClass;
      R.FAncestorClass := AncestorClass;
      for I := 0 to Deferred.Count - 1 do
        R.FLoaded.Add(Deferred[I]);
      R.ReadRootComponent(Instance);
      if not IsLast then
      begin
        Deferred.Clear;
        for I := 0 to R.FLoaded.Count - 1 do
          Deferred.Add(R.FLoaded[I]);
      end;
    finally
      R.Free;
    end;
  finally
    S.Free;
  end;
end;

{ RTL-483, RTL-1129: the levels, from the base-most class below RootAncestor with a form to the most derived one }
function ReadInheritedResources(Instance: TComponent; RootAncestor: TClass;
  ApplyRootName, DeferRootLoaded: Boolean): Boolean;
var
  Chain, Levels: array of TClass;
  N, Count, I: Integer;
  C, Ancestor: TClass;
  Data: Pointer;
  Size: NativeInt;
  Deferred: TList;
begin
  N := 0;
  C := Instance.ClassType;
  while (C <> nil) and (C <> RootAncestor) do
  begin
    SetLength(Chain, N + 1);
    Chain[N] := C;
    N := N + 1;
    C := C.ClassParent;
  end;
  Count := 0;
  for I := N - 1 downto 0 do
    if FindResourceData(Chain[I].ClassName, Data, Size) then
    begin
      SetLength(Levels, Count + 1);
      Levels[Count] := Chain[I];
      Count := Count + 1;
    end;
  Result := Count > 0;
  if not Result then
    Exit;
  { RTL-1128, 1131: the ancestor with a form of the first level, also one that RootAncestor excludes }
  Ancestor := Levels[0].ClassParent;
  while (Ancestor <> nil) and not FindResourceData(Ancestor.ClassName, Data, Size) do
    Ancestor := Ancestor.ClassParent;
  Deferred := TList.Create;
  try
    for I := 0 to Count - 1 do
    begin
      if I > 0 then
        Ancestor := Levels[I - 1];
      FindResourceData(Levels[I].ClassName, Data, Size);
      ReadFormLevel(Data, Size, Instance, Levels[I], Ancestor, I + 1, I = Count - 1, ApplyRootName,
        DeferRootLoaded, Deferred);
    end;
  finally
    Deferred.Free;
  end;
end;

{ RTL-483; RTL-1096: a class whose resources are already being read on this thread is F0111 at the "inline" object
  that started the nested construction; a frame constructed for an instance leaves its root's Loaded to the reader of
  the instance (RTL-1097); RTL-1099: ApplyRootName }
function InitInheritedComponent(Instance: TComponent; RootAncestor: TClass; ApplyRootName: Boolean = True): Boolean;
var
  Pending: TReader;
  Line, Col: Integer;
  P: PFrameReadEntry;
  Entry: TFrameReadEntry;
  Where: string;
begin
  Pending := PendingInlineReader;
  Line := PendingInlineLine;
  Col := PendingInlineCol;
  PendingInlineReader := nil;
  P := FrameReadChain;
  while P <> nil do
  begin
    if P^.Cls = Instance.ClassType then
    begin
      if Pending <> nil then
        Where := Pending.FSourceName + '(' + IntToStr(Line) + ',' + IntToStr(Col) + ')'
      else
        Where := Instance.ClassName + '.form(1,1)';
      raise EReadError.Create(Where + ': error F0111: ' + Format(SFormFrameRecursive, [Instance.ClassName]));
    end;
    P := P^.Next;
  end;
  Entry.Cls := Instance.ClassType;
  Entry.Next := FrameReadChain;
  FrameReadChain := @Entry;
  try
    Result := ReadInheritedResources(Instance, RootAncestor, ApplyRootName, Pending <> nil);
  finally
    FrameReadChain := Entry.Next;
  end;
end;

function InitComponentRes(const ResName: string; Instance: TComponent): Boolean;
var
  Data: Pointer;
  Size: NativeInt;
begin
  Result := FindResourceData(ResName, Data, Size);
  if Result then
    ReadFormData(Data, Size, Instance, ResName + '.form', True, False);
end;

{ ---------------------------------------------------------------- TThread (section 11) }

type
  { one Synchronize / Queue request; Done is nil for a queued one }
  TSyncEntry = class
  public
    Thread: TThread;
    Method: TThreadMethod;
    Done: SyncObjs.TEvent;
    Failure: TObject;
  end;

var
  SyncLock: SyncObjs.TCriticalSection;
  SyncEvent: SyncObjs.TEvent;
  SyncList: TList;

threadvar
  CurrentThreadVar: TThread;

procedure AddSyncEntry(Entry: TSyncEntry);
begin
  SyncLock.Enter;
  try
    SyncList.Add(Pointer(Entry));
  finally
    SyncLock.Leave;
  end;
  SyncEvent.SetEvent;
  if Assigned(WakeMainThread) then
    WakeMainThread(Entry.Thread);
end;

{ RTL-510 }
function ThreadProc(Parameter: Pointer): Integer;
var
  T: TThread;
  FreeIt: Boolean;
begin
  T := TThread(Parameter);
  CurrentThreadVar := T;
  try
    if not T.FTerminated then
      try
        T.Execute;
      except
        T.FFatalException := TObject(AcquireExceptionObject);
      end;
  finally
    Result := T.FReturnValue;
    FreeIt := T.FFreeOnTerminate;
    T.FFinished := True;
    if Assigned(T.FOnTerminate) then
    TThread.Synchronize(T, T.CallOnTerminate);
    if FreeIt then
      T.Free;
    CurrentThreadVar := nil;
  end;
end;

{ the thread is made suspended; AfterConstruction starts it unless CreateSuspended }
constructor TThread.Create(CreateSuspended: Boolean);
begin
  inherited Create;
  FCreateSuspended := CreateSuspended;
  FHandle := BeginThread(nil, 0, ThreadProc, Pointer(Self), CREATE_SUSPENDED, FThreadID);
  if FHandle = 0 then
    raise EThread.Create(SysErrorMessage(GetLastError));
end;

procedure TThread.AfterConstruction;
begin
  inherited AfterConstruction;
  if not FCreateSuspended then
    Start;
end;

{ a running thread is terminated and waited for; a never started one runs to its end without Execute }
destructor TThread.Destroy;
begin
  if (FHandle <> 0) and not FFinished and (FThreadID <> GetCurrentThreadId) then
  begin
    FTerminated := True;
    if not FStarted then
    begin
      FStarted := True;
      ResumeThread(FHandle);
    end;
    WaitFor;
  end;
  if FHandle <> 0 then
    CloseHandle(FHandle);
  FHandle := 0;
  FFatalException.Free;
  FFatalException := nil;
  inherited Destroy;
end;

procedure TThread.CallOnTerminate;
begin
  if Assigned(FOnTerminate) then
    FOnTerminate(Self);
end;

procedure TThread.Start;
begin
  if FStarted then
    raise EThread.Create(SThreadAlreadyStarted);
  FStarted := True;
  ResumeThread(FHandle);
end;

procedure TThread.Terminate;
begin
  FTerminated := True;
end;

{ on the main thread pending Synchronize / Queue entries run while waiting }
function TThread.WaitFor: Cardinal;
var
  Handles: array[0..1] of THandle;
  R: Cardinal;
begin
  if GetCurrentThreadId = MainThreadID then
  begin
    Handles[0] := FHandle;
    Handles[1] := SyncEvent.Handle;
    repeat
      R := WaitForMultipleObjects(2, @Handles[0], False, INFINITE);
      if R = WAIT_OBJECT_0 + 1 then
        CheckSynchronize(0);
    until R <> WAIT_OBJECT_0 + 1;
  end
  else
    WaitForSingleObject(FHandle, INFINITE);
  Result := Cardinal(FReturnValue);
end;

{ RTL-511 }
class procedure TThread.Synchronize(AThread: TThread; AMethod: TThreadMethod);
var
  Entry: TSyncEntry;
  Failure: TObject;
begin
  if GetCurrentThreadId = MainThreadID then
  begin
    AMethod;
    Exit;
  end;
  Entry := TSyncEntry.Create;
  Entry.Thread := AThread;
  Entry.Method := AMethod;
  Entry.Done := SyncObjs.TEvent.Create(nil, True, False, '');
  try
    AddSyncEntry(Entry);
    Entry.Done.WaitFor(INFINITE);
    Failure := Entry.Failure;
  finally
    Entry.Done.Free;
    Entry.Free;
  end;
  if Failure <> nil then
    raise Failure;
end;

procedure TThread.Synchronize(AMethod: TThreadMethod);
begin
  Synchronize(Self, AMethod);
end;

{ RTL-512 }
class procedure TThread.Queue(AThread: TThread; AMethod: TThreadMethod);
var
  Entry: TSyncEntry;
begin
  Entry := TSyncEntry.Create;
  Entry.Thread := AThread;
  Entry.Method := AMethod;
  AddSyncEntry(Entry);
end;

class function TThread.CurrentThread: TThread;
begin
  Result := CurrentThreadVar;
end;

class procedure TThread.Sleep(Milliseconds: Integer);
begin
  Windows.Sleep(Cardinal(Milliseconds));
end;

{ RTL-513: waits up to Timeout ms when nothing is pending, then runs every pending entry in FIFO order }
function CheckSynchronize(Timeout: Integer): Boolean;
var
  Pending: TList;
  Entry: TSyncEntry;
  I: Integer;
  Empty: Boolean;
begin
  if GetCurrentThreadId <> MainThreadID then
    raise EThread.CreateFmt(SCheckSynchronizeError, [GetCurrentThreadId]);
  SyncLock.Enter;
  try
    Empty := SyncList.Count = 0;
  finally
    SyncLock.Leave;
  end;
  if Empty and (Timeout > 0) then
    SyncEvent.WaitFor(Cardinal(Timeout));
  Result := False;
  Pending := TList.Create;
  try
    SyncLock.Enter;
    try
      for I := 0 to SyncList.Count - 1 do
        Pending.Add(SyncList[I]);
      SyncList.Clear;
    finally
      SyncLock.Leave;
    end;
    for I := 0 to Pending.Count - 1 do
    begin
      Entry := TSyncEntry(Pending[I]);
      Result := True;
      if Entry.Done <> nil then
      begin
        try
          Entry.Method;
        except
          Entry.Failure := TObject(AcquireExceptionObject);
        end;
        Entry.Done.SetEvent;
      end
      else
      begin
        try
          try
            Entry.Method;
          except
            if Assigned(ApplicationHandleException) then
              ApplicationHandleException(Entry.Thread)
            else
              ShowException(ExceptObject, ExceptAddr);
          end;
        finally
          Entry.Free;
        end;
      end;
    end;
  finally
    Pending.Free;
  end;
end;

initialization
  SyncLock := SyncObjs.TCriticalSection.Create;
  SyncEvent := SyncObjs.TEvent.Create(nil, False, False, '');
  SyncList := TList.Create;
  RegistryLock := SyncObjs.TCriticalSection.Create;

finalization
  SyncList.Free;
  SyncList := nil;
  SyncEvent.Free;
  SyncEvent := nil;
  SyncLock.Free;
  SyncLock := nil;
  RegistryLock.Free;
  RegistryLock := nil;

end.
