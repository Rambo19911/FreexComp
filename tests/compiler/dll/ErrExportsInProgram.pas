// covers: LANG-544
program ErrExportsInProgram;
// an exports clause in a program file is E2211 - at the outermost level and inside a routine;
// the clause is still parsed and its items give no further diagnostics

function Twice(A: Integer): Integer;
  exports Twice;
begin
  Result := A * 2;
end;

exports
  Twice name 'twice',
  NoSuchRoutine index 70000;

begin
  WriteLn(Twice(21));
end.
