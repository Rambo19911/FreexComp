// covers: RTL-131, RTL-132
// opc-run: variants 1 2 3
program Sys_RunError;
{ Variant 1: RunError(201). Variant 2: RunError without a code is error 0. Output written before the error is
  flushed first; the address is 16 upper-case hex digits and not zero (the return address of _RunError, C-173).
  Variant 3: a range error raised through the helper _RangeError reports the user's call site (R-199): the address
  ErrorProc receives lies inside Fail, the routine with the failing index, not inside the helper. }
{$R+}
var
  P: PChar;
  A: array[0..3] of Integer;
  FailStart: NativeUInt;

procedure Fail(I: Integer);
begin
  A[I] := 1;
  Writeln('not reached');
end;

procedure Hook(Code: Integer; Addr: Pointer);
begin
  Writeln('error ', Code, ' inside Fail ',
    (NativeUInt(Addr) > FailStart) and (NativeUInt(Addr) < FailStart + 256), ', = ErrorAddr ', Addr = ErrorAddr);
end;

begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  Writeln('before ', ErrorAddr = nil);
  Write('pending without line end');
  if P^ = '3' then
  begin
    Writeln;
    FailStart := NativeUInt(@Fail);
    ErrorProc := Hook;
    Fail(10);
  end;
  if P^ = '1' then
    RunError(201)
  else
    RunError;
  Writeln('not reached');
end.
