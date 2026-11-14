// covers: LANG-501, LANG-507
program UnitProgramExitCode;
uses Log, Fin;
var
  Global: Integer;
begin
  Global := 5;
  Note('main');
  ExitCode := Global - 1;
  if ExitCode = 4 then
    Exit;
  Note('never');
end.