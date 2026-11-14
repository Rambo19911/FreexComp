// covers: LANG-505, LANG-538
program UnitDottedName;
// a dotted unit name is one name: file My.Utils.pas, qualifier My.Utils
uses My.Utils;
begin
  Writeln(Square(4));
  Writeln(My.Utils.Square(5));
  Writeln(My.Utils.Version + Version);
end.
