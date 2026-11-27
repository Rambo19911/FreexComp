program P;
uses Base;
type
  TDerived = class(TBase)
  end;
var
  D: TDerived;
begin
  D := TDerived.Create;
  D.{|}Tag := 1;
  Writeln(D.Tag);
end.
