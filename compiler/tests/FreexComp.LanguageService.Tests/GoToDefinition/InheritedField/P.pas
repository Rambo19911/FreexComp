program P;
uses Base;
type
  TDerived = class(TBase)
    procedure Show;
  end;
procedure TDerived.Show;
begin
  Writeln({|}FName);
end;
begin
  TDerived.Create.Show;
end.
