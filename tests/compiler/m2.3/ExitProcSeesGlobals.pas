// covers: ABI-104, LANG-523, CMP-355, RTL-050
program ExitProcSeesGlobals;
// the program's managed globals are released after the ExitProc chain (C-183): the exit procedure still sees S
var
  S: string;
  Saved: Pointer;

procedure X;
begin
  ExitProc := Saved;
  Writeln('[', S, ']');
end;

begin
  S := 'abc';
  Saved := ExitProc;
  ExitProc := @X;
end.
