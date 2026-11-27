program GenericFieldOfInstance;
type
  TBox<T> = class
    {#}Value: T;
  end;
var
  B: TBox<Integer>;
begin
  B := TBox<Integer>.Create;
  Writeln(B.{|}Value);
end.
