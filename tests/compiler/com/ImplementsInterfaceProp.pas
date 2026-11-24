// covers: LANG-690, LANG-691
program ImplementsInterfaceProp;

{ A class delegates ILog to an interface-typed field property and IStore to a property with a getter method. Calls through
  the outer object's references reach the delegates; the getter runs once per delegated call. QueryInterface, _AddRef and
  _Release stay the outer object's: "as" and Supports give references into the outer object, and the delegates are released
  only when the outer object is destroyed. }

uses SysUtils;

type
  ILog = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8C01}']
    procedure Line(const S: string);
    function Count: Integer;
  end;

  IStore = interface
    ['{5E0C1A11-0B6F-4C1D-9A52-3D1F6E7A8C02}']
    procedure Put(Key: Integer; const Value: string);
    function Get(Key: Integer): string;
    procedure Swap(var A, B: Integer);
  end;

  TLog = class(TInterfacedObject, ILog)
    FCount: Integer;
    procedure Line(const S: string);
    function Count: Integer;
    destructor Destroy; override;
  end;

  TStore = class(TInterfacedObject, IStore)
    FValues: array[0..3] of string;
    procedure Put(Key: Integer; const Value: string);
    function Get(Key: Integer): string;
    procedure Swap(var A, B: Integer);
    destructor Destroy; override;
  end;

  TOuter = class(TObject, ILog, IStore)
  private
    FRef: Integer;
    FLog: ILog;
    FStore: IStore;
    FGetterCalls: Integer;
    function GetStore: IStore;
  public
    constructor Create;
    destructor Destroy; override;
    function QueryInterface(const IID: TGUID; out Obj): HResult; stdcall;
    function _AddRef: Integer; stdcall;
    function _Release: Integer; stdcall;
    property Log: ILog read FLog implements ILog;
    property Store: IStore read GetStore implements IStore;
    property GetterCalls: Integer read FGetterCalls;
  end;

procedure TLog.Line(const S: string);
begin
  Inc(FCount);
  WriteLn('log ', FCount, ': ', S);
end;

function TLog.Count: Integer;
begin
  Result := FCount;
end;

destructor TLog.Destroy;
begin
  WriteLn('log destroyed');
  inherited Destroy;
end;

procedure TStore.Put(Key: Integer; const Value: string);
begin
  FValues[Key] := Value;
end;

function TStore.Get(Key: Integer): string;
begin
  Result := FValues[Key];
end;

procedure TStore.Swap(var A, B: Integer);
var
  T: Integer;
begin
  T := A;
  A := B;
  B := T;
end;

destructor TStore.Destroy;
begin
  WriteLn('store destroyed');
  inherited Destroy;
end;

constructor TOuter.Create;
begin
  inherited Create;
  FLog := TLog.Create;
  FStore := TStore.Create;
end;

destructor TOuter.Destroy;
begin
  WriteLn('outer destroyed');
  inherited Destroy;
end;

function TOuter.GetStore: IStore;
begin
  Inc(FGetterCalls);
  Result := FStore;
end;

function TOuter.QueryInterface(const IID: TGUID; out Obj): HResult;
begin
  if GetInterface(IID, Obj) then
    Result := 0
  else
    Result := HResult($80004002);
end;

function TOuter._AddRef: Integer;
begin
  Inc(FRef);
  WriteLn('  outer addref -> ', FRef);
  Result := FRef;
end;

function TOuter._Release: Integer;
begin
  Dec(FRef);
  WriteLn('  outer release -> ', FRef);
  Result := FRef;
  if FRef = 0 then
    Destroy;
end;

procedure Run;
var
  Obj: TOuter;
  Log, Log2: ILog;
  Store: IStore;
  Unknown: IInterface;
  X, Y: Integer;
begin
  Obj := TOuter.Create;
  WriteLn('take ILog');
  Log := Obj;
  Log.Line('first');
  Log.Line('second');
  WriteLn('count ', Log.Count);

  WriteLn('as IStore');
  Store := Log as IStore;
  WriteLn('getter calls ', Obj.GetterCalls);
  Store.Put(1, 'one');
  Store.Put(2, 'two');
  WriteLn(Store.Get(1), ' ', Store.Get(2));
  X := 3;
  Y := 4;
  Store.Swap(X, Y);
  WriteLn(X, ' ', Y);
  WriteLn('getter calls ', Obj.GetterCalls);

  WriteLn('identity');
  Unknown := Store as IInterface;
  WriteLn(Pointer(Log) <> Pointer(Obj.Log));
  WriteLn(Pointer(Store) <> Pointer(Obj.FStore));
  WriteLn('supports');
  WriteLn(Supports(Unknown, ILog, Log2));
  WriteLn(Pointer(Log2) = Pointer(Log));
  Log2.Line('third');
  WriteLn('release all but one');
  Log2 := nil;
  Unknown := nil;
  Log := nil;
  WriteLn('last reference');
  Store := nil;
  WriteLn('leave');
end;

begin
  Run;
  WriteLn('done');
end.
