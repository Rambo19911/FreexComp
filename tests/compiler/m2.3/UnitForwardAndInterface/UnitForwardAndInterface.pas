// covers: LANG-400, LANG-401, LANG-506
program UnitForwardAndInterface;
// interface routines implemented with the parameter list (and result type) omitted, a forward declaration in the
// implementation section, and a call from an earlier routine to a later implemented interface routine
uses Calc;
begin
  Writeln(Add(2, 3));
  Show('hello');
  Writeln(Twice(Add(1, 1)));
end.
