{ context: generic-instantiated }
program GenericInstantiated;
type
  TBox<T> = class
    Value: T;
    procedure Put(const AValue: T);
    function Get: T;
  end;
procedure TBox<T>.Put(const AValue: T); begin Value := AValue end;
function TBox<T>.Get: T; begin Result := Value end;
var
  B: TBox<Integer>;
begin
  B := TBox<Integer>.Create;
  B.{|}
end.
{#has Value, Put, Get, Free}
{#kind Value=Field, Put=Method}
{#not T}
