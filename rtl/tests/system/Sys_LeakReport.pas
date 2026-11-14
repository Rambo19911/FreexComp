// covers: RTL-064, ABI-104
// opc-run: variants 1 2
program Sys_LeakReport;
{ RTL-064 leak report at ABI-104 step 3 in a console program: the line goes to ErrOutput after the finalizations
  and the exit code is not changed. Three blocks are allocated and one of them is freed by an exit procedure, so the
  report (2 blocks) shows that the count is taken after step 1. Variant 1: exit code 0. Variant 2: exit code 3 kept.
  The exact text and the no-leak cases are in Sys_LeakReportGui. The variant is ParamStr(1). }
var
  P, Q, R: Pointer;
  OldExit: Pointer;

procedure FreeR;
begin
  ExitProc := OldExit;
  FreeMem(R);
end;

begin
  ReportMemoryLeaksOnShutdown := True;
  GetMem(P, 16);
  GetMem(Q, 100);
  GetMem(R, 32);
  OldExit := ExitProc;
  ExitProc := @FreeR;
  Writeln('main done');
  if ParamStr(1) = '2' then
    ExitCode := 3;
end.
