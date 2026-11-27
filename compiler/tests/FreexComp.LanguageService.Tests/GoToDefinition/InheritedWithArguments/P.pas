program P;
uses Base;
type
  TDerived = class(TBase)
    procedure Put(const S: string); override;
  end;
procedure TDerived.Put(const S: string);
begin
  inherited {|}Put(S + '!');
end;
begin
  TDerived.Create.Put('x');
end.
