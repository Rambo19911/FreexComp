// covers: RTL-064
// opc: --app-type gui
// opc-run: variants 1 2 3
program Sys_LeakReportGui;
{ RTL-064 in a GUI program (IsConsole = False) with ShowErrorProc set: the report text arrives at the hook without a
  line break (RTL-132) and the hook writes it to the redirected Output, which ABI-104 step 4 flushes afterwards, so
  the exact text and its absence are checked. Variant 1: one block leaked, report on. Variant 2: report on, nothing
  leaked (blocks and strings freed): no report. Variant 3: one block leaked, report off: no report. }
var
  P: Pointer;
  S: string;

procedure Show(const Text: string);
begin
  Writeln('show: [', Text, ']');
end;

begin
  ShowErrorProc := Show;
  ReportMemoryLeaksOnShutdown := ParamStr(1) <> '3';
  S := ParamStr(0);
  GetMem(P, 24);
  if ParamStr(1) = '2' then
  begin
    FreeMem(P);
    S := '';
  end;
  Writeln('main done');
end.
