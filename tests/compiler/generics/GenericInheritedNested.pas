// covers: LANG-772, LANG-774, LANG-775, LANG-780
program GenericInheritedNested;

type
  TBase = class
  public
    type
      TInner = class
      public
        function Tag: string;
      end;
      TShadowed = class
      public
        function Tag: string;
      end;
  protected
    type
      TGuarded = class
      public
        function Tag: string;
      end;
  end;

  TDerived = class(TBase)
  public
    type
      TShadowed = class
      public
        function Tag: string;
      end;
    function MakeInner: TInner;
    function GuardedTag: string;
  end;

  TMore = class(TDerived)
  end;

  TList<T> = class
  public
    type
      TEnumerator = class
      private
        FList: TList<T>;
        FIndex: Integer;
      public
        constructor Create(AList: TList<T>);
        function MoveNext: Boolean;
        function GetCurrent: T;
        property Current: T read GetCurrent;
      end;
  private
    FItems: array of T;
  public
    procedure Add(const V: T);
    function GetEnumerator: TEnumerator;
  end;

  TObjectList<T: class> = class(TList<T>)
  public
    function First: T;
  end;

  TFoo = class
  public
    Name: string;
    constructor Create(const AName: string);
  end;

function TBase.TInner.Tag: string;
begin
  Result := 'base inner';
end;

function TBase.TShadowed.Tag: string;
begin
  Result := 'base shadowed';
end;

function TBase.TGuarded.Tag: string;
begin
  Result := 'guarded';
end;

function TDerived.TShadowed.Tag: string;
begin
  Result := 'derived shadowed';
end;

function TDerived.MakeInner: TInner;
begin
  Result := TInner.Create;
end;

function TDerived.GuardedTag: string;
var
  G: TGuarded;
begin
  G := TGuarded.Create;
  Result := G.Tag;
  G.Free;
end;

constructor TList<T>.TEnumerator.Create(AList: TList<T>);
begin
  inherited Create;
  FList := AList;
  FIndex := -1;
end;

function TList<T>.TEnumerator.MoveNext: Boolean;
begin
  Inc(FIndex);
  Result := FIndex < Length(FList.FItems);
end;

function TList<T>.TEnumerator.GetCurrent: T;
begin
  Result := FList.FItems[FIndex];
end;

procedure TList<T>.Add(const V: T);
begin
  SetLength(FItems, Length(FItems) + 1);
  FItems[High(FItems)] := V;
end;

function TList<T>.GetEnumerator: TEnumerator;
begin
  Result := TEnumerator.Create(Self);
end;

function TObjectList<T>.First: T;
var
  E: TEnumerator;
begin
  Result := nil;
  E := GetEnumerator;
  if E.MoveNext then
    Result := E.Current;
  E.Free;
end;

constructor TFoo.Create(const AName: string);
begin
  inherited Create;
  Name := AName;
end;

var
  X: TDerived.TInner;
  Y: GenericInheritedNested.TMore.TInner;
  S: TMore.TShadowed;
  B: TBase.TShadowed;
  D: TDerived;
  L: TObjectList<TFoo>;
  E: TObjectList<TFoo>.TEnumerator;
  F: TList<TFoo>.TEnumerator;
begin
  X := TDerived.TInner.Create;
  WriteLn(X.ClassName, ' ', X.Tag);
  Y := X;
  WriteLn(Y = X, ' ', TDerived.TInner = TBase.TInner, ' ', TMore.TInner = TBase.TInner);
  X.Free;

  S := TMore.TShadowed.Create;
  B := TBase.TShadowed.Create;
  WriteLn(S.Tag, ' / ', B.Tag, ' ', TypeInfo(TDerived.TShadowed) = TypeInfo(TBase.TShadowed));
  S.Free;
  B.Free;

  D := TDerived.Create;
  X := D.MakeInner;
  WriteLn(X.Tag, ' ', D.GuardedTag);
  X.Free;
  D.Free;

  L := TObjectList<TFoo>.Create;
  L.Add(TFoo.Create('one'));
  L.Add(TFoo.Create('two'));
  E := L.GetEnumerator;
  F := E;
  while F.MoveNext do
    WriteLn(F.Current.Name);
  WriteLn(E.ClassName, ' ', L.First.Name, ' ', TObjectList<TFoo>.TEnumerator = TList<TFoo>.TEnumerator);
  E.Free;
  E := TObjectList<TFoo>.TEnumerator.Create(L);
  WriteLn(E.MoveNext, ' ', E.Current.Name);
  E.Free;
  for var Item in L do
    Item.Free;
  L.Free;
end.
