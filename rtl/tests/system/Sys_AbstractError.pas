// covers: ABI-103, ABI-102
// opc-run: variants 1 2
program Sys_AbstractError;
{ _AbstractError, the code of abstract VMT slots (ABI §9.1). Variant 1: no AbstractErrorProc, so runtime error 211
  (message on stderr, exit code 211). Variant 2: the hook is called first; it returns, so runtime error 211 follows.
  Called directly: the compiler's abstract methods arrive with C-083. The variant is ParamStr(1). }

procedure Hook;
begin
  Writeln('hook called');
end;

begin
  Writeln('assigned before ', Assigned(AbstractErrorProc));
  if ParamStr(1) = '2' then
    AbstractErrorProc := Hook;
  _AbstractError;
  Writeln('not reached');
end.
