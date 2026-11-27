{ context: visibility }
program P;
uses Vault;
var
  S: TSafe;
begin
  S.{|}
end.
{#has Open, Code}
{#not FCode, Lock, Check}
