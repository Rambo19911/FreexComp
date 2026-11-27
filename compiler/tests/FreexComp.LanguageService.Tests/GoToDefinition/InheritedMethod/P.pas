program P;
uses Base;
type
  TDerived = class(TBase)
  end;
var
  D: TDerived;
begin
  D := TDerived.Create;
  D.{|}Greet;
end.
