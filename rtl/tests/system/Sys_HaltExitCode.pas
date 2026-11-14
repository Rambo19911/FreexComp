// covers: RTL-030, RTL-031
// opc-run: variants 1 2 3
program Sys_HaltExitCode;
{ The variant (1, 2 or 3) is the last character of the command line (ParamStr arrives with R-011). }
var
  P: PChar;
  V: Char;
begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  V := P^;
  Writeln('ExitCode at start ', ExitCode);
  Writeln('IsConsole ', IsConsole);
  Writeln('HInstance set ', HInstance <> 0);
  Writeln('MainThreadID set ', MainThreadID <> 0);
  case V of
    '1':
      begin
        Writeln('halt 7');
        Halt(7);
        Writeln('not reached');
      end;
    '2':
      Writeln('fall through');
    '3':
      begin
        ExitCode := 3;
        Writeln('exit code 3');
      end;
  end;
end.
