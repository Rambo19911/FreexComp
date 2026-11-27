{ context: enum-type }
program EnumScoped;
{$SCOPEDENUMS ON}
type
  TMode = (Read, Write, Append);
{$SCOPEDENUMS OFF}
var
  M: TMode;
begin
  M := TMode.{|}
end.
{#has Read, Write, Append}
{#kind Append=EnumMember}
