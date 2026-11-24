// covers: LANG-544
program ErrExportsInUnit;
// an exports clause in a unit (interface or implementation section) is E2211 at the keyword;
// the clause is still parsed and its items give no further diagnostics
uses ExpUnit;
begin
  WriteLn(Twice(21));
end.
