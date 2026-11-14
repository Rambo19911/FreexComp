// covers: LANG-364
program HaltExitCode;

procedure Stop(Code: Integer);
begin
  Writeln('stopping with ', Code);
  Halt(Code);
  Writeln('never');
end;

begin
  Writeln('before');
  Stop(3);
  Writeln('never in main');
end.
