// covers: LANG-990, LANG-993
program ClassHelperUnits;

{ two class helpers for one class in two units: the unit named later in the uses clause supplies the active helper, in every
  unit by that unit's own uses clauses; a helper that inherits from another one makes the members of both reachable; the
  active helper of a class applies to instances of its descendants. }

uses
  UShape, UHelperA, UHelperB, UViewAB, UViewC;

var
  S: TShape;
  C: TCircle;
begin
  S := TShape.Create;
  C := TCircle.Create;
  Writeln(S.Tag, ' ', S.OnlyB);
  Writeln(ViewAB(S));
  Writeln(ViewC(S));
  Writeln(C.Tag);
  C.Free;
  S.Free;
end.