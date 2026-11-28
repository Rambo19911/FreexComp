// covers: LANG-982, LANG-983, LANG-702
program GenericConstraintForms;

type
  IOrdered = interface
    function Key: Integer;
    function CompareTo(OtherKey: Integer): Integer;
  end;

  IGreeter = interface
    function Greet: string;
  end;

  ILoud = interface(IGreeter)
    function Shout: string;
  end;

  TNumber = class(TInterfacedObject, IOrdered)
  private
    FKey: Integer;
  public
    constructor Create(AKey: Integer);
    function Key: Integer;
    function CompareTo(OtherKey: Integer): Integer;
  end;

  TBase = class(TInterfacedObject)
    function Name: string; virtual;
  end;

  TChild = class(TBase)
    function Name: string; override;
  end;

  // a descendant of TBase that implements IGreeter and has a public parameterless constructor
  TPolite = class(TBase, IGreeter)
  private
    FWord: string;
  public
    constructor Create;
    function Name: string; override;
    function Greet: string;
  end;

  // implements IGreeter through its ancestor only
  TPoliteKid = class(TPolite)
    function Name: string; override;
  end;

  TLoud = class(TInterfacedObject, ILoud)
    function Greet: string;
    function Shout: string;
  end;

  // an interface type: its methods are called on objects and on interface references alike
  TSorter<T: IOrdered> = class
    class procedure Sort(var Items: array of T);
    class function Keys(const Items: array of T): string;
  end;

  // grouped lists: a value key, an object value (nil when the key is absent)
  TMap<K: record; V: class> = class
  private
    FKeys: array of K;
    FValues: array of V;
  public
    procedure Add(const Key: K; Value: V);
    function Find(const Key: K): V;
  end;

  // a shared list: both parameters are classes
  TPairOf<A, B: class> = class
    First: A;
    Second: B;
    function Complete: Boolean;
    procedure FreeBoth;
  end;

  // a class type, an interface type and constructor together
  TMake<T: TBase, IGreeter, constructor> = class
    class function Make: T;
    class function Describe(const Item: T): string;
  end;

  // class: nil, Assigned, is, as and Free
  TSlot<T: class> = class
    Item: T;
    function IsEmpty: Boolean;
    function Holds: Boolean;
    function HoldsChild: Boolean;
    function AsBase: TBase;
    procedure Clear;
  end;

  TGreeting<T: IGreeter> = class
    class function Run(const G: T): string;
  end;

  TNamer<T: TBase> = class
    class function NameOf(const Item: T): string;
  end;

  TUtil = class
    class function NameTwice<T: TBase>(const Item: T): string; static;
    class function Larger<T: record>(const A, B: T): T; static;
    class function Fresh<T: class, constructor>: T; static;
  end;

constructor TNumber.Create(AKey: Integer);
begin
  FKey := AKey;
end;

function TNumber.Key: Integer;
begin
  Result := FKey;
end;

function TNumber.CompareTo(OtherKey: Integer): Integer;
begin
  Result := FKey - OtherKey;
end;

function TBase.Name: string;
begin
  Result := 'base';
end;

function TChild.Name: string;
begin
  Result := 'child';
end;

constructor TPolite.Create;
begin
  FWord := 'good day';
end;

function TPolite.Name: string;
begin
  Result := 'polite';
end;

function TPolite.Greet: string;
begin
  Result := FWord;
end;

function TPoliteKid.Name: string;
begin
  Result := 'polite kid';
end;

function TLoud.Greet: string;
begin
  Result := 'hey';
end;

function TLoud.Shout: string;
begin
  Result := 'HEY';
end;

class procedure TSorter<T>.Sort(var Items: array of T);
var
  I, J: Integer;
  Held: T;
begin
  for I := High(Items) downto 1 do
    for J := 0 to I - 1 do
      if Items[J].CompareTo(Items[J + 1].Key) > 0 then
      begin
        Held := Items[J];
        Items[J] := Items[J + 1];
        Items[J + 1] := Held;
      end;
end;

class function TSorter<T>.Keys(const Items: array of T): string;
var
  I: Integer;
  Digit: string;
begin
  Result := '';
  for I := 0 to High(Items) do
  begin
    Str(Items[I].Key, Digit);
    if I > 0 then
      Result := Result + ' ';
    Result := Result + Digit;
  end;
end;

procedure TMap<K, V>.Add(const Key: K; Value: V);
begin
  SetLength(FKeys, Length(FKeys) + 1);
  SetLength(FValues, Length(FValues) + 1);
  FKeys[High(FKeys)] := Key;
  FValues[High(FValues)] := Value;
end;

function TMap<K, V>.Find(const Key: K): V;
var
  I: Integer;
begin
  Result := nil;
  for I := 0 to High(FKeys) do
    if FKeys[I] = Key then
      Result := FValues[I];
end;

function TPairOf<A, B>.Complete: Boolean;
begin
  Result := Assigned(First) and Assigned(Second);
end;

procedure TPairOf<A, B>.FreeBoth;
begin
  First.Free;
  Second.Free;
  First := nil;
  Second := nil;
end;

class function TMake<T>.Make: T;
begin
  Result := T.Create;
end;

class function TMake<T>.Describe(const Item: T): string;
begin
  Result := Item.Name + ' says ' + Item.Greet;
end;

function TSlot<T>.IsEmpty: Boolean;
begin
  Result := Item = nil;
end;

function TSlot<T>.Holds: Boolean;
begin
  Result := Assigned(Item);
end;

function TSlot<T>.HoldsChild: Boolean;
begin
  Result := Item is TChild;
end;

function TSlot<T>.AsBase: TBase;
begin
  Result := Item as TBase;
end;

procedure TSlot<T>.Clear;
begin
  Item.Free;
  Item := nil;
end;

class function TGreeting<T>.Run(const G: T): string;
begin
  Result := G.Greet + '!';
end;

class function TNamer<T>.NameOf(const Item: T): string;
begin
  Result := Item.Name;
end;

class function TUtil.NameTwice<T>(const Item: T): string;
begin
  Result := Item.Name + ' ' + Item.Name;
end;

class function TUtil.Larger<T>(const A, B: T): T;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

class function TUtil.Fresh<T>: T;
begin
  Result := T.Create;
end;

procedure SortObjects;
var
  Items: array[0..2] of TNumber;
  I: Integer;
begin
  Items[0] := TNumber.Create(3);
  Items[1] := TNumber.Create(1);
  Items[2] := TNumber.Create(2);
  TSorter<TNumber>.Sort(Items);
  Writeln('objects: ', TSorter<TNumber>.Keys(Items));
  for I := 0 to 2 do
    Items[I].Free;
end;

procedure SortReferences;
var
  Items: array[0..2] of IOrdered;
begin
  Items[0] := TNumber.Create(30);
  Items[1] := TNumber.Create(10);
  Items[2] := TNumber.Create(20);
  TSorter<IOrdered>.Sort(Items);
  Writeln('references: ', TSorter<IOrdered>.Keys(Items));
end;

procedure Maps;
var
  ByNumber: TMap<Integer, TBase>;
  ByLetter: TMap<Char, TChild>;
  B: TBase;
  C: TChild;
begin
  B := TBase.Create;
  C := TChild.Create;
  ByNumber := TMap<Integer, TBase>.Create;
  ByNumber.Add(1, B);
  ByNumber.Add(2, C);
  Writeln('map: ', ByNumber.Find(1).Name, ' ', ByNumber.Find(2).Name, ' ', ByNumber.Find(3) = nil);
  ByLetter := TMap<Char, TChild>.Create;
  ByLetter.Add('c', C);
  Writeln('map: ', ByLetter.Find('c').Name, ' ', Assigned(ByLetter.Find('x')));
  ByLetter.Free;
  ByNumber.Free;
  C.Free;
  B.Free;
end;

procedure Pairs;
var
  Pair: TPairOf<TBase, TNumber>;
begin
  Pair := TPairOf<TBase, TNumber>.Create;
  Writeln('pair: ', Pair.Complete);
  Pair.First := TChild.Create;
  Pair.Second := TNumber.Create(7);
  Writeln('pair: ', Pair.Complete, ' ', Pair.First.Name, ' ', Pair.Second.Key);
  Pair.FreeBoth;
  Writeln('pair: ', Pair.Complete);
  Pair.Free;
end;

procedure Makes;
var
  P: TPolite;
  K: TPoliteKid;
begin
  P := TMake<TPolite>.Make;
  Writeln('make: ', TMake<TPolite>.Describe(P));
  P.Free;
  K := TMake<TPoliteKid>.Make;
  Writeln('make: ', TMake<TPoliteKid>.Describe(K));
  K.Free;
end;

procedure Slots;
var
  Bases: TSlot<TBase>;
  Children: TSlot<TChild>;
begin
  Bases := TSlot<TBase>.Create;
  Writeln('slot: ', Bases.IsEmpty, ' ', Bases.Holds, ' ', Bases.HoldsChild);
  Bases.Item := TBase.Create;
  Writeln('slot: ', Bases.IsEmpty, ' ', Bases.Holds, ' ', Bases.HoldsChild, ' ', Bases.AsBase.Name);
  Bases.Clear;
  Bases.Item := TChild.Create;
  Writeln('slot: ', Bases.HoldsChild, ' ', Bases.AsBase.Name);
  Bases.Clear;
  Writeln('slot: ', Bases.IsEmpty);
  Bases.Free;
  Children := TSlot<TChild>.Create;
  Children.Item := TChild.Create;
  Writeln('slot: ', Children.HoldsChild, ' ', Children.AsBase.Name);
  Children.Clear;
  Children.Free;
end;

procedure Greetings;
var
  Loud: ILoud;
  Kid: TPoliteKid;
begin
  Loud := TLoud.Create;
  // a descendant interface; a class that implements the interface through its ancestor
  Writeln('greeting: ', TGreeting<ILoud>.Run(Loud), ' ', Loud.Shout);
  Kid := TPoliteKid.Create;
  Writeln('greeting: ', TGreeting<TPoliteKid>.Run(Kid));
  Kid.Free;
end;

procedure Names;
var
  B: TBase;
  C: TChild;
begin
  B := TBase.Create;
  C := TChild.Create;
  // the class itself; a descendant
  Writeln('namer: ', TNamer<TBase>.NameOf(B), ' ', TNamer<TBase>.NameOf(C), ' ', TNamer<TChild>.NameOf(C));
  // a constrained generic method: written and inferred type arguments
  Writeln('method: ', TUtil.NameTwice<TBase>(C), ', ', TUtil.NameTwice(C));
  Writeln('method: ', TUtil.Larger<Integer>(3, 8), ' ', TUtil.Larger('a', 'b'), ' ', TUtil.Larger(2.5, 1.5):0:1);
  C.Free;
  B.Free;
  C := TUtil.Fresh<TChild>;
  Writeln('method: ', C.Name);
  C.Free;
end;

begin
  SortObjects;
  SortReferences;
  Maps;
  Pairs;
  Makes;
  Slots;
  Greetings;
  Names;
end.
