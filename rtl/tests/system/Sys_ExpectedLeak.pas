// covers: RTL-1062, RTL-1063
// phase: 8
// opc: --app-type gui
// opc-run: variants 1 2 3 4
program Sys_ExpectedLeak;
{ Expected leaks and the leak report (default manager). Two blocks are leaked. Variant 1: one is registered - the
  report names 1 block. Variant 2: both are registered - no report. Variant 3: one is registered and unregistered
  again - the report names 2 blocks. Variant 4: nothing is leaked and nothing registered - unregistering answers
  False, and there is no report. The variant is ParamStr(1). A GUI program with ShowErrorProc set, as in
  Sys_LeakReportGui: the hook writes the report to Output, so its exact text and its absence are checked. }
var
  P, Q: Pointer;
  V: string;

procedure Show(const Text: string);
begin
  Writeln('show: [', Text, ']');
end;

begin
  ShowErrorProc := Show;
  ReportMemoryLeaksOnShutdown := True;
  V := ParamStr(1);
  if V = '4' then
  begin
    GetMem(P, 16);
    Writeln('unregister on an empty counter ', UnregisterExpectedMemoryLeak(P));
    FreeMem(P);
  end
  else
  begin
    GetMem(P, 16);
    GetMem(Q, 100);
    Writeln('register ', RegisterExpectedMemoryLeak(P));
    if V = '2' then
      Writeln('register ', RegisterExpectedMemoryLeak(Q));
    if V = '3' then
    begin
      Writeln('unregister ', UnregisterExpectedMemoryLeak(P));
      Writeln('unregister again ', UnregisterExpectedMemoryLeak(P));
    end;
  end;
  V := '';
  Writeln('main done');
end.
