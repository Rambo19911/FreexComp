{ context: enum-type }
program EnumTypeName;
type
  TColor = (Red, Green, Blue);
var
  C: TColor;
begin
  C := TColor.{|}
end.
{#has Red, Green, Blue}
{#kind Red=EnumMember, Blue=EnumMember}
{#not TColor, C, Ord}
