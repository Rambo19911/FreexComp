{ context: expression }
program ExpressionEnumValue;
type
  TLight = (Off, Dim, Bright);
var
  L: TLight;
begin
  L := {|}
end.
{#has Off, Dim, Bright, L}
{#kind Bright=EnumMember}
