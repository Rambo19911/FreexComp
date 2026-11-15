{ Generics.Collections - generic lists, dictionaries, queues, stacks and array routines
  (docs: rtl/other-units.md section 8.2, RTL-672). Element moves are element-wise assignments, so managed element
  types are reference-counted correctly. Default comparers are created on first use, so a list of a type without a
  default comparer works until it is searched or sorted. }
unit Generics.Collections;

interface

uses
  SysUtils, Generics.Defaults;

type
  TCollectionNotification = (cnAdded, cnExtracted, cnRemoved);
  TCollectionNotifyEvent<T> = procedure(Sender: TObject; const Item: T; Action: TCollectionNotification) of object;

  TDictionaryOwnership = (doOwnsKeys, doOwnsValues);
  TDictionaryOwnerships = set of TDictionaryOwnership;

  TPair<K, V> = record
    Key: K;
    Value: V;
    class function Create(const AKey: K; const AValue: V): TPair<K, V>; static;
  end;

  TEnumerator<T> = class abstract
  protected
    function DoGetCurrent: T; virtual; abstract;
    function DoMoveNext: Boolean; virtual; abstract;
  public
    function MoveNext: Boolean;
    property Current: T read DoGetCurrent;
  end;

  TEnumerable<T> = class abstract
  protected
    function DoGetEnumerator: TEnumerator<T>; virtual; abstract;
  public
    function GetEnumerator: TEnumerator<T>;
    function ToArray: TArray<T>; virtual;
  end;

  { without Comparer (or with nil) TComparer<T>.Default is used; without Index/Count the whole array }
  TArray = class
  public
    class procedure Sort<T>(var Values: array of T); overload; static;
    class procedure Sort<T>(var Values: array of T; const Comparer: IComparer<T>); overload; static;
    class procedure Sort<T>(var Values: array of T; const Comparer: IComparer<T>; Index, Count: Integer); overload;
      static;
    { True when found; FoundIndex = the first index whose item is not less than Item (the insertion point) }
    class function BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer): Boolean;
      overload; static;
    class function BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer;
      const Comparer: IComparer<T>): Boolean; overload; static;
    class function BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer;
      const Comparer: IComparer<T>; Index, Count: Integer): Boolean; overload; static;
    class procedure Copy<T>(const Source: array of T; var Destination: array of T;
      SourceIndex, DestIndex, Count: Integer); static;
  end;

  TList<T> = class(TEnumerable<T>)
  private
    FItems: TArray<T>;
    FCount: Integer;
    FComparer: IComparer<T>;
    FOnNotify: TCollectionNotifyEvent<T>;
    function GetItem(Index: Integer): T;
    procedure SetItem(Index: Integer; const Value: T);
    procedure SetCount(Value: Integer);
    function GetCapacity: Integer;
    procedure SetCapacity(Value: Integer);
    procedure Grow(ACount: Integer);
    function Comparer: IComparer<T>;
  protected
    function DoGetEnumerator: TEnumerator<T>; override;
    procedure Notify(const Item: T; Action: TCollectionNotification); virtual;
  public
    constructor Create; overload;
    constructor Create(const AComparer: IComparer<T>); overload;
    constructor Create(const Collection: TEnumerable<T>); overload;
    destructor Destroy; override;
    function Add(const Value: T): Integer;
    procedure AddRange(const Values: array of T); overload;
    procedure AddRange(const Collection: TEnumerable<T>); overload;
    procedure Insert(Index: Integer; const Value: T);
    procedure InsertRange(Index: Integer; const Values: array of T); overload;
    procedure InsertRange(Index: Integer; const Collection: TEnumerable<T>); overload;
    function Remove(const Value: T): Integer;
    procedure Delete(Index: Integer);
    procedure DeleteRange(AIndex, ACount: Integer);
    function Extract(const Value: T): T;
    procedure Exchange(Index1, Index2: Integer);
    procedure Move(CurIndex, NewIndex: Integer);
    function First: T;
    function Last: T;
    procedure Clear;
    function Contains(const Value: T): Boolean;
    function IndexOf(const Value: T): Integer;
    function LastIndexOf(const Value: T): Integer;
    procedure Reverse;
    procedure Sort; overload;
    procedure Sort(const AComparer: IComparer<T>); overload;
    function BinarySearch(const Item: T; out Index: Integer): Boolean; overload;
    function BinarySearch(const Item: T; out Index: Integer; const AComparer: IComparer<T>): Boolean; overload;
    procedure TrimExcess;
    function ToArray: TArray<T>; override;
    property Count: Integer read FCount write SetCount;
    property Capacity: Integer read GetCapacity write SetCapacity;
    property Items[Index: Integer]: T read GetItem write SetItem; default;
    property OnNotify: TCollectionNotifyEvent<T> read FOnNotify write FOnNotify;
  end;

  TListEnumerator<T> = class(TEnumerator<T>)
  private
    FList: TList<T>;
    FIndex: Integer;
  protected
    function DoGetCurrent: T; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(AList: TList<T>);
  end;

  TObjectList<T: class> = class(TList<T>)
  private
    FOwnsObjects: Boolean;
  protected
    procedure Notify(const Item: T; Action: TCollectionNotification); override;
  public
    constructor Create(AOwnsObjects: Boolean = True); overload;
    constructor Create(const AComparer: IComparer<T>; AOwnsObjects: Boolean = True); overload;
    property OwnsObjects: Boolean read FOwnsObjects write FOwnsObjects;
  end;

  { Keys and Values of a dictionary: views that enumerate the dictionary's pairs }
  TKeyCollection<K, V> = class(TEnumerable<K>)
  private
    FPairs: TEnumerable<TPair<K, V>>;
  protected
    function DoGetEnumerator: TEnumerator<K>; override;
  public
    constructor Create(APairs: TEnumerable<TPair<K, V>>);
  end;

  TKeyEnumerator<K, V> = class(TEnumerator<K>)
  private
    FInner: TEnumerator<TPair<K, V>>;
  protected
    function DoGetCurrent: K; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(AInner: TEnumerator<TPair<K, V>>);
    destructor Destroy; override;
  end;

  TValueCollection<K, V> = class(TEnumerable<V>)
  private
    FPairs: TEnumerable<TPair<K, V>>;
  protected
    function DoGetEnumerator: TEnumerator<V>; override;
  public
    constructor Create(APairs: TEnumerable<TPair<K, V>>);
  end;

  TValueEnumerator<K, V> = class(TEnumerator<V>)
  private
    FInner: TEnumerator<TPair<K, V>>;
  protected
    function DoGetCurrent: V; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(AInner: TEnumerator<TPair<K, V>>);
    destructor Destroy; override;
  end;

  { RTL-672: entries live in dense arrays in insertion order (a removed entry leaves a hole, compacted when the
    arrays are full or the index is rebuilt); the index table (a power of two, linear probing, load <= 0.75)
    holds entry numbers, -1 = empty; removal shifts the probe run back (no tombstones) }
  TDictionary<K, V> = class(TEnumerable<TPair<K, V>>)
  private
    FKeys: TArray<K>;
    FValues: TArray<V>;
    FHashes: TArray<Integer>;
    FLive: TArray<Boolean>;
    FEntryCount: Integer;
    FCount: Integer;
    FIndex: TArray<Integer>;
    FComparer: IEqualityComparer<K>;
    FValueComparer: IEqualityComparer<V>;
    FKeyCollection: TKeyCollection<K, V>;
    FValueCollection: TValueCollection<K, V>;
    FOnKeyNotify: TCollectionNotifyEvent<K>;
    FOnValueNotify: TCollectionNotifyEvent<V>;
    procedure Init(ACapacity: Integer; const AComparer: IEqualityComparer<K>);
    function Hash(const Key: K): Integer;
    function Find(const Key: K; out Slot: Integer): Integer;
    procedure Rebuild(ACapacity: Integer);
    procedure Unlink(Slot: Integer);
    procedure RemoveEntry(Slot: Integer; Action: TCollectionNotification);
    procedure Append(const Key: K; const Value: V);
    function GetItem(const Key: K): V;
    procedure SetItem(const Key: K; const Value: V);
    function GetKeys: TKeyCollection<K, V>;
    function GetValues: TValueCollection<K, V>;
  protected
    function DoGetEnumerator: TEnumerator<TPair<K, V>>; override;
    procedure KeyNotify(const Key: K; Action: TCollectionNotification); virtual;
    procedure ValueNotify(const Value: V; Action: TCollectionNotification); virtual;
  public
    constructor Create(ACapacity: Integer = 0); overload;
    constructor Create(const AComparer: IEqualityComparer<K>); overload;
    destructor Destroy; override;
    procedure Add(const Key: K; const Value: V);
    procedure Remove(const Key: K);
    function ExtractPair(const Key: K): TPair<K, V>;
    procedure Clear;
    function TryGetValue(const Key: K; out Value: V): Boolean;
    procedure AddOrSetValue(const Key: K; const Value: V);
    function ContainsKey(const Key: K): Boolean;
    function ContainsValue(const Value: V): Boolean;
    procedure TrimExcess;
    function ToArray: TArray<TPair<K, V>>; override;
    property Items[const Key: K]: V read GetItem write SetItem; default;
    property Count: Integer read FCount;
    property Keys: TKeyCollection<K, V> read GetKeys;
    property Values: TValueCollection<K, V> read GetValues;
    property OnKeyNotify: TCollectionNotifyEvent<K> read FOnKeyNotify write FOnKeyNotify;
    property OnValueNotify: TCollectionNotifyEvent<V> read FOnValueNotify write FOnValueNotify;
  end;

  TDictionaryEnumerator<K, V> = class(TEnumerator<TPair<K, V>>)
  private
    FDictionary: TDictionary<K, V>;
    FIndex: Integer;
  protected
    function DoGetCurrent: TPair<K, V>; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(ADictionary: TDictionary<K, V>);
  end;

  TObjectDictionary<K, V> = class(TDictionary<K, V>)
  private
    FOwnerships: TDictionaryOwnerships;
  protected
    procedure KeyNotify(const Key: K; Action: TCollectionNotification); override;
    procedure ValueNotify(const Value: V; Action: TCollectionNotification); override;
  public
    constructor Create(Ownerships: TDictionaryOwnerships; ACapacity: Integer = 0); overload;
    constructor Create(Ownerships: TDictionaryOwnerships; const AComparer: IEqualityComparer<K>); overload;
  end;

  TQueue<T> = class(TEnumerable<T>)
  private
    FItems: TArray<T>;
    FHead: Integer;
    FCount: Integer;
    FOnNotify: TCollectionNotifyEvent<T>;
    function Take(Action: TCollectionNotification): T;
  protected
    function DoGetEnumerator: TEnumerator<T>; override;
    procedure Notify(const Item: T; Action: TCollectionNotification); virtual;
  public
    destructor Destroy; override;
    procedure Enqueue(const Value: T);
    function Dequeue: T;
    function Extract: T;
    function Peek: T;
    procedure Clear;
    procedure TrimExcess;
    function ToArray: TArray<T>; override;
    property Count: Integer read FCount;
    property OnNotify: TCollectionNotifyEvent<T> read FOnNotify write FOnNotify;
  end;

  TQueueEnumerator<T> = class(TEnumerator<T>)
  private
    FQueue: TQueue<T>;
    FIndex: Integer;
  protected
    function DoGetCurrent: T; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(AQueue: TQueue<T>);
  end;

  TStack<T> = class(TEnumerable<T>)
  private
    FItems: TArray<T>;
    FCount: Integer;
    FOnNotify: TCollectionNotifyEvent<T>;
    function Take(Action: TCollectionNotification): T;
  protected
    function DoGetEnumerator: TEnumerator<T>; override;
    procedure Notify(const Item: T; Action: TCollectionNotification); virtual;
  public
    destructor Destroy; override;
    procedure Push(const Value: T);
    function Pop: T;
    function Extract: T;
    function Peek: T;
    procedure Clear;
    procedure TrimExcess;
    function ToArray: TArray<T>; override;
    property Count: Integer read FCount;
    property OnNotify: TCollectionNotifyEvent<T> read FOnNotify write FOnNotify;
  end;

  { enumerates from the bottom of the stack to the top (push order), like ToArray }
  TStackEnumerator<T> = class(TEnumerator<T>)
  private
    FStack: TStack<T>;
    FIndex: Integer;
  protected
    function DoGetCurrent: T; override;
    function DoMoveNext: Boolean; override;
  public
    constructor Create(AStack: TStack<T>);
  end;

  TObjectQueue<T: class> = class(TQueue<T>)
  private
    FOwnsObjects: Boolean;
  protected
    procedure Notify(const Item: T; Action: TCollectionNotification); override;
  public
    constructor Create(AOwnsObjects: Boolean = True);
    procedure Dequeue; reintroduce;
    property OwnsObjects: Boolean read FOwnsObjects write FOwnsObjects;
  end;

  TObjectStack<T: class> = class(TStack<T>)
  private
    FOwnsObjects: Boolean;
  protected
    procedure Notify(const Item: T; Action: TCollectionNotification); override;
  public
    constructor Create(AOwnsObjects: Boolean = True);
    procedure Pop; reintroduce;
    property OwnsObjects: Boolean read FOwnsObjects write FOwnsObjects;
  end;

implementation

{ Classes: EListError (classes.md) }
uses
  RTLConsts, Classes;

procedure OutOfRange;
begin
  raise EArgumentOutOfRangeException.Create(SArgumentOutOfRange);
end;

procedure Unbalanced;
begin
  raise EListError.Create(SUnbalancedOperation);
end;

{ ------------------------------------------------------------------------------------------- TPair, bases }

class function TPair<K, V>.Create(const AKey: K; const AValue: V): TPair<K, V>;
begin
  Result.Key := AKey;
  Result.Value := AValue;
end;

function TEnumerator<T>.MoveNext: Boolean;
begin
  Result := DoMoveNext;
end;

function TEnumerable<T>.GetEnumerator: TEnumerator<T>;
begin
  Result := DoGetEnumerator;
end;

function TEnumerable<T>.ToArray: TArray<T>;
var
  E: TEnumerator<T>;
  N: Integer;
begin
  Result := nil;
  N := 0;
  E := GetEnumerator;
  try
    while E.MoveNext do
    begin
      if N = Length(Result) then
        SetLength(Result, N * 2 + 4);
      Result[N] := E.Current;
      Inc(N);
    end;
  finally
    E.Free;
  end;
  SetLength(Result, N);
end;

{ ------------------------------------------------------------------------------------------------- TArray }

class procedure TArray.Sort<T>(var Values: array of T);
begin
  Sort<T>(Values, nil, 0, Length(Values));
end;

class procedure TArray.Sort<T>(var Values: array of T; const Comparer: IComparer<T>);
begin
  Sort<T>(Values, Comparer, 0, Length(Values));
end;

{ stable bottom-up merge sort (RTL-418 rule): on equal items the left run wins }
class procedure TArray.Sort<T>(var Values: array of T; const Comparer: IComparer<T>; Index, Count: Integer);
var
  Tmp: TArray<T>;
  Width, Lo, Mid, Hi, I, J, N: Integer;
  C: IComparer<T>;
begin
  C := Comparer;
  if C = nil then
    C := TComparer<T>.Default;
  if (Index < 0) or (Count < 0) or (Index > Length(Values) - Count) then
    OutOfRange;
  if Count < 2 then
    Exit;
  SetLength(Tmp, Count);
  Width := 1;
  while Width < Count do
  begin
    Lo := 0;
    while Lo < Count do
    begin
      Mid := Lo + Width;
      if Mid > Count then
        Mid := Count;
      Hi := Mid + Width;
      if Hi > Count then
        Hi := Count;
      I := Lo;
      J := Mid;
      N := Lo;
      while (I < Mid) and (J < Hi) do
      begin
        if C.Compare(Values[Index + J], Values[Index + I]) < 0 then
        begin
          Tmp[N] := Values[Index + J];
          Inc(J);
        end
        else
        begin
          Tmp[N] := Values[Index + I];
          Inc(I);
        end;
        Inc(N);
      end;
      while I < Mid do
      begin
        Tmp[N] := Values[Index + I];
        Inc(I);
        Inc(N);
      end;
      while J < Hi do
      begin
        Tmp[N] := Values[Index + J];
        Inc(J);
        Inc(N);
      end;
      Lo := Hi;
    end;
    for I := 0 to Count - 1 do
      Values[Index + I] := Tmp[I];
    Width := Width * 2;
  end;
end;

class function TArray.BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer): Boolean;
begin
  Result := BinarySearch<T>(Values, Item, FoundIndex, nil, 0, Length(Values));
end;

class function TArray.BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer;
  const Comparer: IComparer<T>): Boolean;
begin
  Result := BinarySearch<T>(Values, Item, FoundIndex, Comparer, 0, Length(Values));
end;

class function TArray.BinarySearch<T>(const Values: array of T; const Item: T; out FoundIndex: Integer;
  const Comparer: IComparer<T>; Index, Count: Integer): Boolean;
var
  Lo, Hi, Mid: Integer;
  C: IComparer<T>;
begin
  C := Comparer;
  if C = nil then
    C := TComparer<T>.Default;
  if (Index < 0) or (Count < 0) or (Index > Length(Values) - Count) then
    OutOfRange;
  Lo := Index;
  Hi := Index + Count;
  while Lo < Hi do
  begin
    Mid := Lo + (Hi - Lo) div 2;
    if C.Compare(Values[Mid], Item) < 0 then
      Lo := Mid + 1
    else
      Hi := Mid;
  end;
  FoundIndex := Lo;
  Result := (Lo < Index + Count) and (C.Compare(Values[Lo], Item) = 0);
end;

class procedure TArray.Copy<T>(const Source: array of T; var Destination: array of T;
  SourceIndex, DestIndex, Count: Integer);
var
  I: Integer;
begin
  if (Count < 0) or (SourceIndex < 0) or (DestIndex < 0) or (SourceIndex > Length(Source) - Count) or
    (DestIndex > Length(Destination) - Count) then
    OutOfRange;
  for I := 0 to Count - 1 do
    Destination[DestIndex + I] := Source[SourceIndex + I];
end;

{ -------------------------------------------------------------------------------------------------- TList }

constructor TList<T>.Create;
begin
  inherited Create;
end;

constructor TList<T>.Create(const AComparer: IComparer<T>);
begin
  inherited Create;
  FComparer := AComparer;
end;

constructor TList<T>.Create(const Collection: TEnumerable<T>);
begin
  inherited Create;
  AddRange(Collection);
end;

destructor TList<T>.Destroy;
begin
  Clear;
  inherited Destroy;
end;

function TList<T>.Comparer: IComparer<T>;
begin
  if FComparer = nil then
    FComparer := TComparer<T>.Default;
  Result := FComparer;
end;

function TList<T>.DoGetEnumerator: TEnumerator<T>;
begin
  Result := TListEnumerator<T>.Create(Self);
end;

procedure TList<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  if Assigned(FOnNotify) then
    FOnNotify(Self, Item, Action);
end;

function TList<T>.GetItem(Index: Integer): T;
begin
  if (Index < 0) or (Index >= FCount) then
    OutOfRange;
  Result := FItems[Index];
end;

procedure TList<T>.SetItem(Index: Integer; const Value: T);
var
  Old: T;
begin
  if (Index < 0) or (Index >= FCount) then
    OutOfRange;
  Old := FItems[Index];
  FItems[Index] := Value;
  Notify(Old, cnRemoved);
  Notify(Value, cnAdded);
end;

{ growth: x2 from 4 until ACount fits }
procedure TList<T>.Grow(ACount: Integer);
var
  NewCap: Integer;
begin
  NewCap := Length(FItems);
  if ACount <= NewCap then
    Exit;
  if NewCap = 0 then
    NewCap := 4;
  while NewCap < ACount do
    NewCap := NewCap * 2;
  SetLength(FItems, NewCap);
end;

function TList<T>.GetCapacity: Integer;
begin
  Result := Length(FItems);
end;

procedure TList<T>.SetCapacity(Value: Integer);
begin
  if Value < FCount then
    OutOfRange;
  SetLength(FItems, Value);
end;

procedure TList<T>.SetCount(Value: Integer);
begin
  if Value < 0 then
    OutOfRange;
  if Value < FCount then
    DeleteRange(Value, FCount - Value)
  else
  begin
    Grow(Value);
    FCount := Value;
  end;
end;

function TList<T>.Add(const Value: T): Integer;
begin
  Grow(FCount + 1);
  FItems[FCount] := Value;
  Result := FCount;
  Inc(FCount);
  Notify(Value, cnAdded);
end;

procedure TList<T>.AddRange(const Values: array of T);
begin
  InsertRange(FCount, Values);
end;

procedure TList<T>.AddRange(const Collection: TEnumerable<T>);
begin
  InsertRange(FCount, Collection);
end;

procedure TList<T>.Insert(Index: Integer; const Value: T);
var
  I: Integer;
begin
  if (Index < 0) or (Index > FCount) then
    OutOfRange;
  Grow(FCount + 1);
  for I := FCount downto Index + 1 do
    FItems[I] := FItems[I - 1];
  FItems[Index] := Value;
  Inc(FCount);
  Notify(Value, cnAdded);
end;

procedure TList<T>.InsertRange(Index: Integer; const Values: array of T);
var
  I, N: Integer;
begin
  if (Index < 0) or (Index > FCount) then
    OutOfRange;
  N := Length(Values);
  Grow(FCount + N);
  for I := FCount - 1 downto Index do
    FItems[I + N] := FItems[I];
  for I := 0 to N - 1 do
    FItems[Index + I] := Values[I];
  Inc(FCount, N);
  for I := 0 to N - 1 do
    Notify(Values[I], cnAdded);
end;

procedure TList<T>.InsertRange(Index: Integer; const Collection: TEnumerable<T>);
begin
  InsertRange(Index, Collection.ToArray);
end;

function TList<T>.Remove(const Value: T): Integer;
begin
  Result := IndexOf(Value);
  if Result >= 0 then
    Delete(Result);
end;

procedure TList<T>.Delete(Index: Integer);
begin
  if (Index < 0) or (Index >= FCount) then
    OutOfRange;
  DeleteRange(Index, 1);
end;

procedure TList<T>.DeleteRange(AIndex, ACount: Integer);
var
  Gone: TArray<T>;
  I: Integer;
begin
  if (AIndex < 0) or (ACount < 0) or (AIndex > FCount - ACount) then
    OutOfRange;
  if ACount = 0 then
    Exit;
  SetLength(Gone, ACount);
  for I := 0 to ACount - 1 do
    Gone[I] := FItems[AIndex + I];
  for I := AIndex to FCount - ACount - 1 do
    FItems[I] := FItems[I + ACount];
  for I := FCount - ACount to FCount - 1 do
    FItems[I] := Default(T);
  Dec(FCount, ACount);
  for I := 0 to ACount - 1 do
    Notify(Gone[I], cnRemoved);
end;

function TList<T>.Extract(const Value: T): T;
var
  Index, I: Integer;
begin
  Index := IndexOf(Value);
  if Index < 0 then
  begin
    Result := Default(T);
    Exit;
  end;
  Result := FItems[Index];
  for I := Index to FCount - 2 do
    FItems[I] := FItems[I + 1];
  FItems[FCount - 1] := Default(T);
  Dec(FCount);
  Notify(Result, cnExtracted);
end;

procedure TList<T>.Exchange(Index1, Index2: Integer);
var
  Tmp: T;
begin
  if (Index1 < 0) or (Index1 >= FCount) or (Index2 < 0) or (Index2 >= FCount) then
    OutOfRange;
  Tmp := FItems[Index1];
  FItems[Index1] := FItems[Index2];
  FItems[Index2] := Tmp;
end;

procedure TList<T>.Move(CurIndex, NewIndex: Integer);
var
  Tmp: T;
  I: Integer;
begin
  if (CurIndex < 0) or (CurIndex >= FCount) or (NewIndex < 0) or (NewIndex >= FCount) then
    OutOfRange;
  Tmp := FItems[CurIndex];
  if CurIndex < NewIndex then
    for I := CurIndex to NewIndex - 1 do
      FItems[I] := FItems[I + 1]
  else
    for I := CurIndex downto NewIndex + 1 do
      FItems[I] := FItems[I - 1];
  FItems[NewIndex] := Tmp;
end;

function TList<T>.First: T;
begin
  if FCount = 0 then
    OutOfRange;
  Result := FItems[0];
end;

function TList<T>.Last: T;
begin
  if FCount = 0 then
    OutOfRange;
  Result := FItems[FCount - 1];
end;

procedure TList<T>.Clear;
begin
  DeleteRange(0, FCount);
  FItems := nil;
end;

function TList<T>.Contains(const Value: T): Boolean;
begin
  Result := IndexOf(Value) >= 0;
end;

function TList<T>.IndexOf(const Value: T): Integer;
var
  I: Integer;
  C: IComparer<T>;
begin
  C := Comparer;
  for I := 0 to FCount - 1 do
    if C.Compare(FItems[I], Value) = 0 then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

function TList<T>.LastIndexOf(const Value: T): Integer;
var
  I: Integer;
  C: IComparer<T>;
begin
  C := Comparer;
  for I := FCount - 1 downto 0 do
    if C.Compare(FItems[I], Value) = 0 then
    begin
      Result := I;
      Exit;
    end;
  Result := -1;
end;

procedure TList<T>.Reverse;
var
  I, J: Integer;
  Tmp: T;
begin
  I := 0;
  J := FCount - 1;
  while I < J do
  begin
    Tmp := FItems[I];
    FItems[I] := FItems[J];
    FItems[J] := Tmp;
    Inc(I);
    Dec(J);
  end;
end;

procedure TList<T>.Sort;
begin
  TArray.Sort<T>(FItems, Comparer, 0, FCount);
end;

procedure TList<T>.Sort(const AComparer: IComparer<T>);
begin
  TArray.Sort<T>(FItems, AComparer, 0, FCount);
end;

function TList<T>.BinarySearch(const Item: T; out Index: Integer): Boolean;
begin
  Result := TArray.BinarySearch<T>(FItems, Item, Index, Comparer, 0, FCount);
end;

function TList<T>.BinarySearch(const Item: T; out Index: Integer; const AComparer: IComparer<T>): Boolean;
begin
  Result := TArray.BinarySearch<T>(FItems, Item, Index, AComparer, 0, FCount);
end;

procedure TList<T>.TrimExcess;
begin
  SetLength(FItems, FCount);
end;

function TList<T>.ToArray: TArray<T>;
var
  I: Integer;
begin
  SetLength(Result, FCount);
  for I := 0 to FCount - 1 do
    Result[I] := FItems[I];
end;

constructor TListEnumerator<T>.Create(AList: TList<T>);
begin
  inherited Create;
  FList := AList;
  FIndex := -1;
end;

function TListEnumerator<T>.DoGetCurrent: T;
begin
  Result := FList.FItems[FIndex];
end;

function TListEnumerator<T>.DoMoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < FList.FCount;
end;

constructor TObjectList<T>.Create(AOwnsObjects: Boolean);
begin
  inherited Create;
  FOwnsObjects := AOwnsObjects;
end;

constructor TObjectList<T>.Create(const AComparer: IComparer<T>; AOwnsObjects: Boolean);
begin
  inherited Create(AComparer);
  FOwnsObjects := AOwnsObjects;
end;

procedure TObjectList<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  inherited Notify(Item, Action);
  if FOwnsObjects and (Action = cnRemoved) then
    Item.Free;
end;

{ ---------------------------------------------------------------------------------- key / value collections }

constructor TKeyCollection<K, V>.Create(APairs: TEnumerable<TPair<K, V>>);
begin
  inherited Create;
  FPairs := APairs;
end;

function TKeyCollection<K, V>.DoGetEnumerator: TEnumerator<K>;
begin
  Result := TKeyEnumerator<K, V>.Create(FPairs.GetEnumerator);
end;

constructor TKeyEnumerator<K, V>.Create(AInner: TEnumerator<TPair<K, V>>);
begin
  inherited Create;
  FInner := AInner;
end;

destructor TKeyEnumerator<K, V>.Destroy;
begin
  FInner.Free;
  inherited Destroy;
end;

function TKeyEnumerator<K, V>.DoGetCurrent: K;
begin
  Result := FInner.Current.Key;
end;

function TKeyEnumerator<K, V>.DoMoveNext: Boolean;
begin
  Result := FInner.MoveNext;
end;

constructor TValueCollection<K, V>.Create(APairs: TEnumerable<TPair<K, V>>);
begin
  inherited Create;
  FPairs := APairs;
end;

function TValueCollection<K, V>.DoGetEnumerator: TEnumerator<V>;
begin
  Result := TValueEnumerator<K, V>.Create(FPairs.GetEnumerator);
end;

constructor TValueEnumerator<K, V>.Create(AInner: TEnumerator<TPair<K, V>>);
begin
  inherited Create;
  FInner := AInner;
end;

destructor TValueEnumerator<K, V>.Destroy;
begin
  FInner.Free;
  inherited Destroy;
end;

function TValueEnumerator<K, V>.DoGetCurrent: V;
begin
  Result := FInner.Current.Value;
end;

function TValueEnumerator<K, V>.DoMoveNext: Boolean;
begin
  Result := FInner.MoveNext;
end;

{ -------------------------------------------------------------------------------------------- TDictionary }

constructor TDictionary<K, V>.Create(ACapacity: Integer);
begin
  inherited Create;
  Init(ACapacity, nil);
end;

constructor TDictionary<K, V>.Create(const AComparer: IEqualityComparer<K>);
begin
  inherited Create;
  Init(0, AComparer);
end;

procedure TDictionary<K, V>.Init(ACapacity: Integer; const AComparer: IEqualityComparer<K>);
begin
  if ACapacity < 0 then
    OutOfRange;
  FComparer := AComparer;
  if FComparer = nil then
    FComparer := TEqualityComparer<K>.Default;
  if ACapacity > 0 then
    Rebuild(ACapacity);
end;

destructor TDictionary<K, V>.Destroy;
begin
  Clear;
  FKeyCollection.Free;
  FValueCollection.Free;
  inherited Destroy;
end;

function TDictionary<K, V>.Hash(const Key: K): Integer;
begin
  Result := FComparer.GetHashCode(Key) and $7FFFFFFF;
end;

{ the entry number of Key, or -1 with Slot = the empty index slot where it would go }
function TDictionary<K, V>.Find(const Key: K; out Slot: Integer): Integer;
var
  H, Mask, E: Integer;
begin
  Slot := -1;
  Result := -1;
  if Length(FIndex) = 0 then
    Exit;
  H := Hash(Key);
  Mask := Length(FIndex) - 1;
  Slot := H and Mask;
  while FIndex[Slot] >= 0 do
  begin
    E := FIndex[Slot];
    if (FHashes[E] = H) and FComparer.Equals(FKeys[E], Key) then
    begin
      Result := E;
      Exit;
    end;
    Slot := (Slot + 1) and Mask;
  end;
end;

{ compacts the entries (insertion order kept) and builds an index for at least ACapacity entries at load <= 0.75 }
procedure TDictionary<K, V>.Rebuild(ACapacity: Integer);
var
  Cap, I, N, Slot, Mask: Integer;
begin
  if ACapacity < FCount then
    ACapacity := FCount;
  N := 0;
  for I := 0 to FEntryCount - 1 do
    if FLive[I] then
    begin
      if N <> I then
      begin
        FKeys[N] := FKeys[I];
        FValues[N] := FValues[I];
        FHashes[N] := FHashes[I];
        FLive[N] := True;
        FKeys[I] := Default(K);
        FValues[I] := Default(V);
        FLive[I] := False;
      end;
      Inc(N);
    end;
  FEntryCount := N;
  if ACapacity = 0 then
  begin
    FIndex := nil;
    FKeys := nil;
    FValues := nil;
    FHashes := nil;
    FLive := nil;
    Exit;
  end;
  Cap := 4;
  while Cap * 3 < ACapacity * 4 do
    Cap := Cap * 2;
  SetLength(FKeys, ACapacity);
  SetLength(FValues, ACapacity);
  SetLength(FHashes, ACapacity);
  SetLength(FLive, ACapacity);
  SetLength(FIndex, Cap);
  for I := 0 to Cap - 1 do
    FIndex[I] := -1;
  Mask := Cap - 1;
  for I := 0 to FEntryCount - 1 do
  begin
    Slot := FHashes[I] and Mask;
    while FIndex[Slot] >= 0 do
      Slot := (Slot + 1) and Mask;
    FIndex[Slot] := I;
  end;
end;

{ clears an index slot and shifts the rest of its probe run back (linear probing without tombstones) }
procedure TDictionary<K, V>.Unlink(Slot: Integer);
var
  Mask, J, Home: Integer;
  Stays: Boolean;
begin
  Mask := Length(FIndex) - 1;
  J := Slot;
  while True do
  begin
    J := (J + 1) and Mask;
    if FIndex[J] < 0 then
      Break;
    Home := FHashes[FIndex[J]] and Mask;
    if Slot <= J then
      Stays := (Slot < Home) and (Home <= J)
    else
      Stays := (Slot < Home) or (Home <= J);
    if not Stays then
    begin
      FIndex[Slot] := FIndex[J];
      Slot := J;
    end;
  end;
  FIndex[Slot] := -1;
end;

procedure TDictionary<K, V>.RemoveEntry(Slot: Integer; Action: TCollectionNotification);
var
  E: Integer;
  Key: K;
  Value: V;
begin
  E := FIndex[Slot];
  Key := FKeys[E];
  Value := FValues[E];
  Unlink(Slot);
  FLive[E] := False;
  FKeys[E] := Default(K);
  FValues[E] := Default(V);
  Dec(FCount);
  KeyNotify(Key, Action);
  ValueNotify(Value, Action);
end;

procedure TDictionary<K, V>.Append(const Key: K; const Value: V);
var
  Slot, Mask: Integer;
begin
  if (Length(FIndex) = 0) or ((FCount + 1) * 4 > Length(FIndex) * 3) or (FEntryCount = Length(FKeys)) then
    Rebuild((FCount + 1) * 2);
  FKeys[FEntryCount] := Key;
  FValues[FEntryCount] := Value;
  FHashes[FEntryCount] := Hash(Key);
  FLive[FEntryCount] := True;
  Mask := Length(FIndex) - 1;
  Slot := FHashes[FEntryCount] and Mask;
  while FIndex[Slot] >= 0 do
    Slot := (Slot + 1) and Mask;
  FIndex[Slot] := FEntryCount;
  Inc(FEntryCount);
  Inc(FCount);
  KeyNotify(Key, cnAdded);
  ValueNotify(Value, cnAdded);
end;

function TDictionary<K, V>.DoGetEnumerator: TEnumerator<TPair<K, V>>;
begin
  Result := TDictionaryEnumerator<K, V>.Create(Self);
end;

procedure TDictionary<K, V>.KeyNotify(const Key: K; Action: TCollectionNotification);
begin
  if Assigned(FOnKeyNotify) then
    FOnKeyNotify(Self, Key, Action);
end;

procedure TDictionary<K, V>.ValueNotify(const Value: V; Action: TCollectionNotification);
begin
  if Assigned(FOnValueNotify) then
    FOnValueNotify(Self, Value, Action);
end;

procedure TDictionary<K, V>.Add(const Key: K; const Value: V);
var
  Slot: Integer;
begin
  if Find(Key, Slot) >= 0 then
    raise EListError.Create(SGenericDuplicateItem);
  Append(Key, Value);
end;

procedure TDictionary<K, V>.Remove(const Key: K);
var
  Slot: Integer;
begin
  if Find(Key, Slot) >= 0 then
    RemoveEntry(Slot, cnRemoved);
end;

function TDictionary<K, V>.ExtractPair(const Key: K): TPair<K, V>;
var
  Slot, E: Integer;
begin
  E := Find(Key, Slot);
  if E < 0 then
  begin
    Result.Key := Key;
    Result.Value := Default(V);
    Exit;
  end;
  Result.Key := FKeys[E];
  Result.Value := FValues[E];
  RemoveEntry(Slot, cnExtracted);
end;

procedure TDictionary<K, V>.Clear;
var
  I: Integer;
  OldKeys: TArray<K>;
  OldValues: TArray<V>;
  OldLive: TArray<Boolean>;
  N: Integer;
begin
  OldKeys := FKeys;
  OldValues := FValues;
  OldLive := FLive;
  N := FEntryCount;
  FKeys := nil;
  FValues := nil;
  FHashes := nil;
  FLive := nil;
  FIndex := nil;
  FEntryCount := 0;
  FCount := 0;
  for I := 0 to N - 1 do
    if OldLive[I] then
    begin
      KeyNotify(OldKeys[I], cnRemoved);
      ValueNotify(OldValues[I], cnRemoved);
    end;
end;

function TDictionary<K, V>.TryGetValue(const Key: K; out Value: V): Boolean;
var
  Slot, E: Integer;
begin
  E := Find(Key, Slot);
  Result := E >= 0;
  if Result then
    Value := FValues[E]
  else
    Value := Default(V);
end;

procedure TDictionary<K, V>.AddOrSetValue(const Key: K; const Value: V);
var
  Slot, E: Integer;
  Old: V;
begin
  E := Find(Key, Slot);
  if E < 0 then
  begin
    Append(Key, Value);
    Exit;
  end;
  Old := FValues[E];
  FValues[E] := Value;
  ValueNotify(Old, cnRemoved);
  ValueNotify(Value, cnAdded);
end;

function TDictionary<K, V>.ContainsKey(const Key: K): Boolean;
var
  Slot: Integer;
begin
  Result := Find(Key, Slot) >= 0;
end;

function TDictionary<K, V>.ContainsValue(const Value: V): Boolean;
var
  I: Integer;
begin
  if FValueComparer = nil then
    FValueComparer := TEqualityComparer<V>.Default;
  for I := 0 to FEntryCount - 1 do
    if FLive[I] and FValueComparer.Equals(FValues[I], Value) then
    begin
      Result := True;
      Exit;
    end;
  Result := False;
end;

procedure TDictionary<K, V>.TrimExcess;
begin
  Rebuild(FCount);
end;

function TDictionary<K, V>.ToArray: TArray<TPair<K, V>>;
var
  I, N: Integer;
begin
  SetLength(Result, FCount);
  N := 0;
  for I := 0 to FEntryCount - 1 do
    if FLive[I] then
    begin
      Result[N].Key := FKeys[I];
      Result[N].Value := FValues[I];
      Inc(N);
    end;
end;

function TDictionary<K, V>.GetItem(const Key: K): V;
var
  Slot, E: Integer;
begin
  E := Find(Key, Slot);
  if E < 0 then
    raise EListError.Create(SGenericItemNotFound);
  Result := FValues[E];
end;

procedure TDictionary<K, V>.SetItem(const Key: K; const Value: V);
var
  Slot, E: Integer;
  Old: V;
begin
  E := Find(Key, Slot);
  if E < 0 then
    raise EListError.Create(SGenericItemNotFound);
  Old := FValues[E];
  FValues[E] := Value;
  ValueNotify(Old, cnRemoved);
  ValueNotify(Value, cnAdded);
end;

function TDictionary<K, V>.GetKeys: TKeyCollection<K, V>;
begin
  if FKeyCollection = nil then
    FKeyCollection := TKeyCollection<K, V>.Create(Self);
  Result := FKeyCollection;
end;

function TDictionary<K, V>.GetValues: TValueCollection<K, V>;
begin
  if FValueCollection = nil then
    FValueCollection := TValueCollection<K, V>.Create(Self);
  Result := FValueCollection;
end;

constructor TDictionaryEnumerator<K, V>.Create(ADictionary: TDictionary<K, V>);
begin
  inherited Create;
  FDictionary := ADictionary;
  FIndex := -1;
end;

function TDictionaryEnumerator<K, V>.DoGetCurrent: TPair<K, V>;
begin
  Result.Key := FDictionary.FKeys[FIndex];
  Result.Value := FDictionary.FValues[FIndex];
end;

function TDictionaryEnumerator<K, V>.DoMoveNext: Boolean;
begin
  repeat
    Inc(FIndex);
  until (FIndex >= FDictionary.FEntryCount) or FDictionary.FLive[FIndex];
  Result := FIndex < FDictionary.FEntryCount;
end;

constructor TObjectDictionary<K, V>.Create(Ownerships: TDictionaryOwnerships; ACapacity: Integer);
begin
  inherited Create(ACapacity);
  FOwnerships := Ownerships;
end;

constructor TObjectDictionary<K, V>.Create(Ownerships: TDictionaryOwnerships; const AComparer: IEqualityComparer<K>);
begin
  inherited Create(AComparer);
  FOwnerships := Ownerships;
end;

procedure TObjectDictionary<K, V>.KeyNotify(const Key: K; Action: TCollectionNotification);
begin
  inherited KeyNotify(Key, Action);
  if (doOwnsKeys in FOwnerships) and (Action = cnRemoved) then
    TObject(PPointer(@Key)^).Free;
end;

procedure TObjectDictionary<K, V>.ValueNotify(const Value: V; Action: TCollectionNotification);
begin
  inherited ValueNotify(Value, Action);
  if (doOwnsValues in FOwnerships) and (Action = cnRemoved) then
    TObject(PPointer(@Value)^).Free;
end;

{ ------------------------------------------------------------------------------------------ TQueue, TStack }

destructor TQueue<T>.Destroy;
begin
  Clear;
  inherited Destroy;
end;

function TQueue<T>.DoGetEnumerator: TEnumerator<T>;
begin
  Result := TQueueEnumerator<T>.Create(Self);
end;

procedure TQueue<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  if Assigned(FOnNotify) then
    FOnNotify(Self, Item, Action);
end;

procedure TQueue<T>.Enqueue(const Value: T);
var
  I: Integer;
begin
  if FHead + FCount = Length(FItems) then
  begin
    if FHead > 0 then
    begin
      for I := 0 to FCount - 1 do
        FItems[I] := FItems[FHead + I];
      for I := FCount to FHead + FCount - 1 do
        FItems[I] := Default(T);
      FHead := 0;
    end;
    if FCount = Length(FItems) then
      SetLength(FItems, FCount * 2 + 4);
  end;
  FItems[FHead + FCount] := Value;
  Inc(FCount);
  Notify(Value, cnAdded);
end;

function TQueue<T>.Take(Action: TCollectionNotification): T;
begin
  if FCount = 0 then
    Unbalanced;
  Result := FItems[FHead];
  FItems[FHead] := Default(T);
  Inc(FHead);
  Dec(FCount);
  if FCount = 0 then
    FHead := 0;
  Notify(Result, Action);
end;

function TQueue<T>.Dequeue: T;
begin
  Result := Take(cnRemoved);
end;

function TQueue<T>.Extract: T;
begin
  Result := Take(cnExtracted);
end;

function TQueue<T>.Peek: T;
begin
  if FCount = 0 then
    Unbalanced;
  Result := FItems[FHead];
end;

procedure TQueue<T>.Clear;
begin
  while FCount > 0 do
    Take(cnRemoved);
  FItems := nil;
end;

procedure TQueue<T>.TrimExcess;
begin
  FItems := ToArray;
  FHead := 0;
end;

function TQueue<T>.ToArray: TArray<T>;
var
  I: Integer;
begin
  SetLength(Result, FCount);
  for I := 0 to FCount - 1 do
    Result[I] := FItems[FHead + I];
end;

constructor TQueueEnumerator<T>.Create(AQueue: TQueue<T>);
begin
  inherited Create;
  FQueue := AQueue;
  FIndex := -1;
end;

function TQueueEnumerator<T>.DoGetCurrent: T;
begin
  Result := FQueue.FItems[FQueue.FHead + FIndex];
end;

function TQueueEnumerator<T>.DoMoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < FQueue.FCount;
end;

destructor TStack<T>.Destroy;
begin
  Clear;
  inherited Destroy;
end;

function TStack<T>.DoGetEnumerator: TEnumerator<T>;
begin
  Result := TStackEnumerator<T>.Create(Self);
end;

procedure TStack<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  if Assigned(FOnNotify) then
    FOnNotify(Self, Item, Action);
end;

procedure TStack<T>.Push(const Value: T);
begin
  if FCount = Length(FItems) then
    SetLength(FItems, FCount * 2 + 4);
  FItems[FCount] := Value;
  Inc(FCount);
  Notify(Value, cnAdded);
end;

function TStack<T>.Take(Action: TCollectionNotification): T;
begin
  if FCount = 0 then
    Unbalanced;
  Dec(FCount);
  Result := FItems[FCount];
  FItems[FCount] := Default(T);
  Notify(Result, Action);
end;

function TStack<T>.Pop: T;
begin
  Result := Take(cnRemoved);
end;

function TStack<T>.Extract: T;
begin
  Result := Take(cnExtracted);
end;

function TStack<T>.Peek: T;
begin
  if FCount = 0 then
    Unbalanced;
  Result := FItems[FCount - 1];
end;

procedure TStack<T>.Clear;
begin
  while FCount > 0 do
    Take(cnRemoved);
  FItems := nil;
end;

procedure TStack<T>.TrimExcess;
begin
  SetLength(FItems, FCount);
end;

function TStack<T>.ToArray: TArray<T>;
var
  I: Integer;
begin
  SetLength(Result, FCount);
  for I := 0 to FCount - 1 do
    Result[I] := FItems[I];
end;

constructor TStackEnumerator<T>.Create(AStack: TStack<T>);
begin
  inherited Create;
  FStack := AStack;
  FIndex := -1;
end;

function TStackEnumerator<T>.DoGetCurrent: T;
begin
  Result := FStack.FItems[FIndex];
end;

function TStackEnumerator<T>.DoMoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < FStack.FCount;
end;

constructor TObjectQueue<T>.Create(AOwnsObjects: Boolean);
begin
  inherited Create;
  FOwnsObjects := AOwnsObjects;
end;

procedure TObjectQueue<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  inherited Notify(Item, Action);
  if FOwnsObjects and (Action = cnRemoved) then
    Item.Free;
end;

procedure TObjectQueue<T>.Dequeue;
begin
  inherited Dequeue;
end;

constructor TObjectStack<T>.Create(AOwnsObjects: Boolean);
begin
  inherited Create;
  FOwnsObjects := AOwnsObjects;
end;

procedure TObjectStack<T>.Notify(const Item: T; Action: TCollectionNotification);
begin
  inherited Notify(Item, Action);
  if FOwnsObjects and (Action = cnRemoved) then
    Item.Free;
end;

procedure TObjectStack<T>.Pop;
begin
  inherited Pop;
end;

end.
