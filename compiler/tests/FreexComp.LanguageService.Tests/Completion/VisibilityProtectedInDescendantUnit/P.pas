{ context: visibility }
program P;
uses Vault;
type
  TBigSafe = class(TSafe)
    procedure Reinforce;
  end;
procedure TBigSafe.Reinforce;
begin
  Self.{|}
end;
begin
end.
{#has Check, Open, Reinforce}
{#not FCode, Lock}
