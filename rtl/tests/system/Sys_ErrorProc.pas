// covers: ABI-101, RTL-131, RTL-132
// opc-run: variants 1 2 3
program Sys_ErrorProc;
{ The ErrorProc hook of _RunError. Variant 1: the hook returns, so the fatal path follows (message, exit code of
  the error). Variant 2: the hook ends the program itself with Halt(3); no runtime error message is written.
  Variant 3: a hook installed and removed again is not called. Inline-check errors (range check, here) reach the
  hook too. The variant is ParamStr(1). }
{$R+}
var
  V: string;
  A: array[1..3] of Integer;
  I: Integer;

procedure Hook(Code: Integer; Addr: Pointer);
begin
  Writeln('hook: code ', Code, ', address = ErrorAddr ', Addr = ErrorAddr);
  if V = '2' then
    Halt(3);
end;

begin
  V := ParamStr(1);
  Writeln('assigned before ', Assigned(ErrorProc));
  ErrorProc := Hook;
  Writeln('assigned after ', Assigned(ErrorProc));
  if V = '3' then
  begin
    ErrorProc := nil;
    RunError(204);
  end;
  I := 4;
  A[I] := 1;
  Writeln('not reached');
end.
