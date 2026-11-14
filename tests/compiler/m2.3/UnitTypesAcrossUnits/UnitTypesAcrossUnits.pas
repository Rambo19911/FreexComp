// covers: LANG-515, LANG-517, LANG-311
program UnitTypesAcrossUnits;
// an enumeration, a set, a record and a static array declared in Palette are used by Canvas and by the program
uses Palette, Canvas;

var
  Row: TRow;
  S: TColors;
  C: TColor;
  P: TPixel;
begin
  Row := MakeRow;
  for C := Low(TColor) to High(TColor) do
  begin
    Write(ColorName(C));
    if C in Colors(Row) then
      Write(' used');
    if C in Warm then
      Write(' warm');
    Writeln;
  end;
  P := Row[3];
  Writeln(P.X, ' ', P.Y, ' ', Ord(P.Color));
  S := Colors(Row) * Warm;
  Writeln(Yellow in S, ' ', Red in S);
  Writeln(Ord(Palette.Blue), ' ', Ord(High(Palette.TColor)), ' ', SizeOf(Palette.TPixel));
end.
