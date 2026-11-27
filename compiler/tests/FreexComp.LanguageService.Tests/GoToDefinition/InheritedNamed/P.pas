program P;
uses Base;
type
  TDerived = class(TBase)
    procedure Step; override;
  end;
procedure TDerived.Step;
begin
  inherited {|}Step;
  Writeln('derived');
end;
begin
  TDerived.Create.Step;
end.
