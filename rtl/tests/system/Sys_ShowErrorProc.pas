// covers: RTL-132
// opc: --app-type gui
// opc-run: variants 1 2
program Sys_ShowErrorProc;
{ A GUI-subsystem program (IsConsole = False) whose standard handles are redirected by the test runner.
  Variant 1: ShowErrorProc is set, so the runtime-error text goes to it without a line break (and not to
  ErrOutput). Variant 2: without the hook the text goes to ErrOutput as in a console program. The address digits
  are not printed (they depend on the build). The variant is ParamStr(1). }
procedure Show(const Text: string);
begin
  Writeln('show: [', Copy(Text, 1, Length(Text) - 16), '] length ', Length(Text));
end;

begin
  Writeln('IsConsole ', IsConsole);
  Writeln('assigned before ', Assigned(ShowErrorProc));
  if ParamStr(1) = '1' then
    ShowErrorProc := Show;
  RunError(204);
end.
