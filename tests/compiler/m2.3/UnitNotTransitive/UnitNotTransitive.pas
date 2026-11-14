// covers: LANG-517
program UnitNotTransitive;
uses Middle;
var P: TPoint;
begin
  Writeln(Home.X, Shifted(1).Y);
  Writeln(Origin);
  Writeln(Base.Origin);
end.
