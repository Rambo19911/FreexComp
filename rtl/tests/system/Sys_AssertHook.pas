// covers: ABI-103, RTL-137
// opc-run: variants 1 2 3
program Sys_AssertHook;
{ The AssertErrorProc hook of _Assert. Variant 1: the hook receives message, file name and line and returns, so
  the default text and runtime error 227 follow. Variant 2: an Assert without a message passes '' to the hook,
  which ends the program with Halt(9). Variant 3: a hook installed and removed again is not called. The variant
  is ParamStr(1). }
var
  V: string;
  Zero: Integer;

procedure Hook(const Msg, FileName: string; Line: Integer; Addr: Pointer);
begin
  Writeln('hook: [', Msg, '] [', FileName, '] line ', Line);
  if V = '2' then
    Halt(9);
end;

begin
  V := ParamStr(1);
  Zero := 0;
  Writeln('assigned before ', Assigned(AssertErrorProc));
  AssertErrorProc := Hook;
  Assert(Zero = 0, 'passes');
  case V[1] of
    '1':
      Assert(Zero = 1, 'hooked message');
    '2':
      Assert(Zero > 0);
    '3':
      begin
        AssertErrorProc := nil;
        Assert(False, 'unhooked');
      end;
  end;
  Writeln('not reached');
end.
