// covers: RTL-957, RTL-967
program usehalt;
// Halt inside the library ends this process with the library's exit code; the library shuts its own run-time
// library down first (exit procedures, finalization, flush), the host's is not asked

procedure Quit(Code: Integer); stdcall; external 'haltlib.dll';

begin
  Writeln('host: calling Quit(7), own ExitCode is ', ExitCode);
  Flush(Output);
  Quit(7);
  Writeln('host: not reached');
end.
