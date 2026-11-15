// covers: LANG-700, LANG-710, LANG-712, LANG-717
program GenericClass;

type
  TStack<T> = class
  private
    FItems: array of T;
    FCount: Integer;
  public
    procedure Push(const Item: T);
    function Pop: T;
    function Peek: T;
    function IsEmpty: Boolean;
    property Count: Integer read FCount;
  end;

procedure TStack<T>.Push(const Item: T);
begin
  if FCount = Length(FItems) then
    SetLength(FItems, FCount * 2 + 1);
  FItems[FCount] := Item;
  Inc(FCount);
end;

function TStack<T>.Pop: T;
begin
  Dec(FCount);
  Result := FItems[FCount];
  FItems[FCount] := Default(T);
end;

function TStack<T>.Peek: T;
begin
  Result := FItems[FCount - 1];
end;

function TStack<T>.IsEmpty: Boolean;
begin
  Result := FCount = 0;
end;

var
  Ints: TStack<Integer>;
  Strs: TStack<string>;
  I: Integer;
begin
  Ints := TStack<Integer>.Create;
  for I := 1 to 5 do
    Ints.Push(I * I);
  Writeln(Ints.Count, ' ', Ints.Peek);
  while not Ints.IsEmpty do
    Write(' ', Ints.Pop);
  Writeln;
  Ints.Free;
  Strs := TStack<string>.Create;
  Strs.Push('a');
  Strs.Push('bc');
  Strs.Push(Strs.Peek + 'd');
  Writeln(Strs.Count);
  while not Strs.IsEmpty do
    Writeln(Strs.Pop);
  Strs.Free;
end.