// covers: LANG-700, LANG-710, LANG-711
program GenericInterface;

type
  IComparer<T> = interface
    function Compare(const Left, Right: T): Integer;
  end;

  IEnumerator<T> = interface
    function GetCurrent: T;
    function MoveNext: Boolean;
    property Current: T read GetCurrent;
  end;

  IEnumerable<T> = interface
    function GetEnumerator: IEnumerator<T>;
  end;

  IList<T> = interface(IEnumerable<T>)
    ['{6E1C2A40-6D1B-4C51-9A57-0B2F1C3D4E5F}']
    function Count: Integer;
    function Get(Index: Integer): T;
  end;

  TIntComparer = class(TInterfacedObject, IComparer<Integer>)
    function Compare(const Left, Right: Integer): Integer;
  end;

  TStrComparer = class(TInterfacedObject, IComparer<string>)
    function Compare(const Left, Right: string): Integer;
  end;

  TReverse<T> = class(TInterfacedObject, IComparer<T>)
  private
    FInner: IComparer<T>;
  public
    constructor Create(const AInner: IComparer<T>);
    function Compare(const Left, Right: T): Integer;
  end;

  TArrayEnum<T> = class(TInterfacedObject, IEnumerator<T>)
  private
    FItems: array of T;
    FIndex: Integer;
  public
    constructor Create(const AItems: array of T);
    function GetCurrent: T;
    function MoveNext: Boolean;
  end;

  TArrayList<T> = class(TInterfacedObject, IEnumerable<T>, IList<T>)
  private
    FItems: array of T;
  public
    constructor Create(const AItems: array of T);
    function GetEnumerator: IEnumerator<T>;
    function Count: Integer;
    function Get(Index: Integer): T;
  end;

  TUtil = class
    class function Max<T>(const A, B: T; const C: IComparer<T>): T;
  end;

function TIntComparer.Compare(const Left, Right: Integer): Integer;
begin
  Result := Left - Right;
end;

function TStrComparer.Compare(const Left, Right: string): Integer;
begin
  if Left < Right then
    Result := -1
  else if Left > Right then
    Result := 1
  else
    Result := 0;
end;

constructor TReverse<T>.Create(const AInner: IComparer<T>);
begin
  inherited Create;
  FInner := AInner;
end;

function TReverse<T>.Compare(const Left, Right: T): Integer;
begin
  Result := FInner.Compare(Right, Left);
end;

constructor TArrayEnum<T>.Create(const AItems: array of T);
var
  I: Integer;
begin
  inherited Create;
  SetLength(FItems, Length(AItems));
  for I := 0 to High(AItems) do
    FItems[I] := AItems[I];
  FIndex := -1;
end;

function TArrayEnum<T>.GetCurrent: T;
begin
  Result := FItems[FIndex];
end;

function TArrayEnum<T>.MoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < Length(FItems);
end;

constructor TArrayList<T>.Create(const AItems: array of T);
var
  I: Integer;
begin
  inherited Create;
  SetLength(FItems, Length(AItems));
  for I := 0 to High(AItems) do
    FItems[I] := AItems[I];
end;

function TArrayList<T>.GetEnumerator: IEnumerator<T>;
begin
  Result := TArrayEnum<T>.Create(FItems);
end;

function TArrayList<T>.Count: Integer;
begin
  Result := Length(FItems);
end;

function TArrayList<T>.Get(Index: Integer): T;
begin
  Result := FItems[Index];
end;

class function TUtil.Max<T>(const A, B: T; const C: IComparer<T>): T;
begin
  if C.Compare(A, B) >= 0 then
    Result := A
  else
    Result := B;
end;

procedure Run;
var
  IC: IComparer<Integer>;
  SC: IComparer<string>;
  E: IEnumerator<string>;
  L: IList<Integer>;
  N: IEnumerable<Integer>;
  I: Integer;
begin
  IC := TIntComparer.Create;
  SC := TStrComparer.Create;
  Writeln(IC.Compare(5, 3), ' ', SC.Compare('a', 'b'));
  Writeln(TUtil.Max<Integer>(4, 9, IC), ' ', TUtil.Max<string>('pear', 'apple', SC));
  IC := TReverse<Integer>.Create(IC);
  Writeln(IC.Compare(5, 3), ' ', TUtil.Max<Integer>(4, 9, IC));
  E := TArrayEnum<string>.Create(['x', 'y', 'z']);
  while E.MoveNext do
    Write(E.Current);
  Writeln;
  L := TArrayList<Integer>.Create([10, 20, 30]);
  Writeln(L.Count, ' ', L.Get(1));
  N := L;
  for I in N do
    Write(I, ';');
  Writeln;
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Run;
  Writeln('leaked: ', _LiveAllocCount - Before);
end.
