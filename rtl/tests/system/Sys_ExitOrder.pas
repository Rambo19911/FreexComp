// covers: RTL-030, RTL-031, RTL-050, RTL-132, ABI-100, ABI-104
// opc-run: variants 1 2 3 4 5 6
program Sys_ExitOrder;
{ Shutdown order (ABI-104): the ExitProc chain (last installed first), then the finalization of every unit whose
  initialization completed, in reverse order, then ExitProcess(ExitCode). The variant is ParamStr(1).
  1: the main block ends. 2: Halt(5) in the main block. 3: Halt(6) inside an exit procedure - the remaining chain
  and the finalizations still run, the first exit procedure does not run again. 4: Halt(4) inside the
  initialization of ExitOrderB - only ExitOrderA is finalised. 5: Halt(8) inside the finalization of ExitOrderC -
  ExitOrderB and ExitOrderA are still finalised. 6: a runtime error runs exit procedures and finalizations too. }
uses ExitOrderA, ExitOrderB, ExitOrderC;

var
  SavedExit1, SavedExit2: Pointer;
  V: Char;                             { not a managed global: those are released at the end of the main block }
  Runs2: Integer;

procedure Exit1;
begin
  ExitProc := SavedExit1;
  Writeln('exit main 1, ExitCode ', ExitCode);
end;

procedure Exit2;
begin
  ExitProc := SavedExit2;
  Runs2 := Runs2 + 1;
  Writeln('exit main 2, run ', Runs2);
  if V = '3' then
  begin
    Writeln('halt 6 in exit main 2');
    Halt(6);
  end;
end;

begin
  V := ParamStr(1)[1];
  Writeln('main');
  SavedExit1 := ExitProc;
  ExitProc := @Exit1;
  SavedExit2 := ExitProc;
  ExitProc := @Exit2;
  if V = '2' then
  begin
    Writeln('halt 5 in main');
    Halt(5);
    Writeln('not reached');
  end;
  if V = '6' then
  begin
    Writeln('run error 201 in main');
    RunError(201);
    Writeln('not reached');
  end;
  Writeln('main done');
end.
