program InsideGeneric;
type
  TStack<T> = class
    {#}Count: Integer;
    procedure Push(const V: T);
  end;
procedure TStack<T>.Push(const V: T);
begin
  {|}Count := Count + 1;
end;
var
  S: TStack<Integer>;
begin
  S := TStack<Integer>.Create;
  S.Push(1);
  Writeln(S.Count);
end.
