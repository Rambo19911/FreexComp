program GenericMethodInstantiated;
type
  TUtil = class
    class function Max<T>(const A, B: T): T;
  end;
class function TUtil.{#}Max<T>(const A, B: T): T;
begin
  Result := A;
end;
begin
  Writeln(TUtil.{|}Max<Integer>(1, 2));
end.
