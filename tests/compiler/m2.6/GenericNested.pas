// covers: LANG-706, LANG-710
program GenericNested;

type
  TPair<K, V> = record
    Key: K;
    Value: V;
  end;

  TList<T> = class
  private
    FItems: array of T;
  public
    procedure Add(const Item: T);
    function Get(Index: Integer): T;
    function Count: Integer;
  end;

procedure TList<T>.Add(const Item: T);
begin
  SetLength(FItems, Length(FItems) + 1);
  FItems[High(FItems)] := Item;
end;

function TList<T>.Get(Index: Integer): T;
begin
  Result := FItems[Index];
end;

function TList<T>.Count: Integer;
begin
  Result := Length(FItems);
end;

function MakePair(const K: string; V: Integer): TPair<string, Integer>;
begin
  Result.Key := K;
  Result.Value := V;
end;

var
  L: TList<TPair<string, Integer>>;
  LL: TList<TList<Integer>>;
  P: TPair<string, Integer>;
  I: Integer;
begin
  L := TList<TPair<string, Integer>>.Create;
  L.Add(MakePair('a', 1));
  L.Add(MakePair('b', 2));
  for I := 0 to L.Count - 1 do
  begin
    P := L.Get(I);
    Writeln(P.Key, P.Value);
  end;
  LL := TList<TList<Integer>>.Create;
  LL.Add(TList<Integer>.Create);
  LL.Get(0).Add(7);
  Writeln(LL.Count, ' ', LL.Get(0).Get(0));
  LL.Get(0).Free;
  LL.Free;
  L.Free;
end.