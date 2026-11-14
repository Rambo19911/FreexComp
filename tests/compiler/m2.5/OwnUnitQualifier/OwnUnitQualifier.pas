// covers: LANG-538
program OwnUnitQualifier;

{ C-214: inside a unit (and a program) its own name qualifies its own identifiers: interface and implementation
  declarations, types, constants, variables, routines. }

uses UOwn;

type
  TLocal = Integer;

var
  X: OwnUnitQualifier.TLocal;

begin
  OwnUnitQualifier.X := 20;
  Writeln(UOwn.Twice(OwnUnitQualifier.X), ' ', Twice(1));
end.