program EnumValue;
type
  TColor = (Red, {#}Green, Blue);
var
  C: TColor;
begin
  C := {|}Green;
  Writeln(Ord(C));
end.
