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
  EClassNotFound = class(EFilerError);
  EMethodNotFound = class(EFilerError);
  EInvalidImage = class(EFilerError);
  EResNotFound = class(Exception);
  EComponentError = class(Exception);
  EInvalidOperation = class(Exception);
  EThread = class(Exception);
  EOutOfResources = class(EOutOfMemory);
  EBitsError = class(Exception);

  TFiler = class;

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

  { section 10.3 (completed by the reader tasks) }
  TFiler = class(TObject)
  end;

const
  fmCreate = $FF00;

function Point(X, Y: Integer): TPoint;
function Rect(Left, Top, Right, Bottom: Integer): TRect;
function Bounds(Left, Top, Width, Height: Integer): TRect;
function SmallPoint(X, Y: SmallInt): TSmallPoint;

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

end.
