program ScopedEnumValue;
type
  TColor = (Red, Green, {#}Blue);
var
  C: TColor;
begin
  C := TColor.{|}Blue;
  Writeln(Ord(C));
end.
