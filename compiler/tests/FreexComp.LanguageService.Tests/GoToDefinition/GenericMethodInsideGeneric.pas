program GenericMethodInsideGeneric;
type
  TStack<T> = class
    Count: Integer;
    procedure Push(const V: T);
    procedure PushTwice(const V: T);
  end;
procedure TStack<T>.{#}Push(const V: T);
begin
  Count := Count + 1;
end;
procedure TStack<T>.PushTwice(const V: T);
begin
  {|}Push(V);
  Push(V);
end;
var
  S: TStack<Integer>;
begin
  S := TStack<Integer>.Create;
  S.PushTwice(1);
  Writeln(S.Count);
end.
