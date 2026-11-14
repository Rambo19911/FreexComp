// covers: LANG-345, ABI-180, CMP-414
// interface threadvars of a unit reached through its descriptor; the unit's $init registers before its own statements
program ThreadVarUnits;

uses TvLib;

threadvar
  Own: Integer;

begin
  Own := 4;
  Hit;
  Writeln(Hits, ' ', Own, ' ', HiddenValue);
  Hits := 7;
  Name[2] := 'x';
  Writeln(Hits, ' ', Name[2], ' ', Own);
end.
