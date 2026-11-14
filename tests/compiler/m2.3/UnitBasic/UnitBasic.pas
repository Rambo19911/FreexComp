// covers: LANG-500, LANG-506, LANG-511, LANG-515, LANG-522
program UnitBasic;
// a program and one unit: a type, a constant, a variable and routines of the unit's interface are used by the program
uses Shapes;

var
  R, S: TRect;
begin
  R := MakeRect(2, 3);
  S := MakeRect(4, 5);
  Describe(R);
  Writeln(Area(R) + Area(S));
  Writeln(Created);
  Created := 10;
  Writeln(Created * Sides);
end.
