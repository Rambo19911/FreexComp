// covers: RTL-131, RTL-132
// opc-run: variants 1 2
program Sys_RunError;
{ Variant 1: RunError(201). Variant 2: RunError without a code is error 0. Output written before the error is
  flushed first; the address is 16 upper-case hex digits (zero until the compiler provides it, C-173). }
var
  P: PChar;
begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Writeln('before ', ErrorAddr = nil);
  Write('pending without line end');
  if P^ = '1' then
    RunError(201)
  else
    RunError;
  Writeln('not reached');
end.
