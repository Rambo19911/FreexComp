// covers: LANG-775, LANG-776, LANG-780, CMP-1390, CMP-1391
program GenericNestedEnumerator;

type
  TList<T> = class
  public
    type
      TNode = record
        Value: T;
        Index: Integer;
      end;

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
    function Count: Integer;
    function Node(I: Integer): TNode;
    function GetEnumerator: TEnumerator;
  end;

procedure TList<T>.Add(const V: T);
begin
  SetLength(FItems, Length(FItems) + 1);
  FItems[High(FItems)] := V;
end;

function TList<T>.Count: Integer;
begin
  Result := Length(FItems);
end;

function TList<T>.Node(I: Integer): TNode;
begin
  Result.Value := FItems[I];
  Result.Index := I;
end;

function TList<T>.GetEnumerator: TEnumerator;
begin
  Result := TEnumerator.Create(Self);
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
  Result := FIndex < FList.Count;
end;

function TList<T>.TEnumerator.GetCurrent: T;
begin
  Result := FList.FItems[FIndex];
end;

procedure Run;
var
  S: TList<string>;
  L: TList<Integer>;
  Again: TList<Integer>;
  E: TList<Integer>.TEnumerator;
  N: TList<Integer>.TNode;
  Text: string;
  I: Integer;
  C1, C2, C3: TClass;
begin
  S := TList<string>.Create;
  S.Add('alpha');
  S.Add('beta');
  for Text in S do
    WriteLn(Text);
  L := TList<Integer>.Create;
  L.Add(10);
  L.Add(20);
  L.Add(30);
  for I in L do
    Write(I, ' ');
  WriteLn;
  E := L.GetEnumerator;
  while E.MoveNext do
    Write(E.Current * 2, ' ');
  WriteLn;
  WriteLn(E.ClassName);
  E.Free;
  N := L.Node(1);
  WriteLn(N.Index, ' ', N.Value);
  Again := TList<Integer>.Create;
  C1 := TList<Integer>.TEnumerator;
  C2 := TList<string>.TEnumerator;
  E := Again.GetEnumerator;
  C3 := E.ClassType;
  E.Free;
  WriteLn(C1 <> C2);
  WriteLn(C1 = C3);
  WriteLn(SizeOf(TList<Integer>.TNode) = SizeOf(N));
  Again.Free;
  L.Free;
  S.Free;
end;

var
  Live: NativeInt;
begin
  Live := _LiveAllocCount;
  Run;
  WriteLn('leaks ', _LiveAllocCount - Live);
end.
