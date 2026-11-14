// covers: RTL-213, ABI-121
program SU_OSExceptions;
{ sysutils.md RTL-213: OS exceptions become SysUtils exceptions through System.ExceptObjProc - nil-area reads and
  writes as EAccessViolation with the exact message, and every other mapped OS code (raised with RaiseException)
  as its class with ExceptionCode / ExceptionAddress filled in. }
uses
  Windows, SysUtils, SUFaults, TestKit;

procedure Access(Write: Boolean);
var
  M, Expected: string;
  Code: Cardinal;
  Addr: Pointer;
begin
  M := '(no exception)';
  Code := 0;
  Addr := nil;
  try
    if Write then
      PokeAt(PInteger(NativeInt(16)))
    else
      Writeln(PeekAt(PInteger(NativeInt(16))));
  except
    on E: EAccessViolation do
    begin
      M := E.Message;
      Code := E.ExceptionCode;
      Addr := E.ExceptionAddress;
    end;
  end;
  if Write then
    Expected := 'write'
  else
    Expected := 'read';
  CheckEqS('Access violation at address $' + IntToHex(Int64(NativeInt(Addr)), 16) + ': ' + Expected +
    ' of address $0000000000000010', M, Expected + ' message');
  CheckEqI($C0000005, Code, Expected + ' ExceptionCode');
  Check(Addr <> nil, Expected + ' ExceptionAddress');
end;

procedure Mapped(Code: Cardinal; const ClassName, Msg: string);
var
  Got: string;
  GotCode: Cardinal;
begin
  Got := '(no exception)';
  GotCode := 0;
  try
    RaiseException(Code, 0, 0, nil);
  except
    on E: EExternal do
    begin
      Got := E.ClassName + ': ' + E.Message;
      GotCode := E.ExceptionCode;
    end;
  end;
  CheckEqS(ClassName + ': ' + Msg, Got, IntToHex(Code, 8));
  CheckEqI(Code, GotCode, IntToHex(Code, 8) + ' ExceptionCode');
end;

var
  Before: NativeInt;
begin
  Before := _LiveAllocCount;
  Section('access violations');
  Access(False);
  Access(True);
  Section('other OS codes');
  Mapped($C0000094, 'EDivByZero', 'Division by zero');
  Mapped($C0000095, 'EIntOverflow', 'Integer overflow');
  Mapped($C000008E, 'EZeroDivide', 'Floating point division by zero');
  Mapped($C0000090, 'EInvalidOp', 'Invalid floating point operation');
  Mapped($C0000091, 'EOverflow', 'Floating point overflow');
  Mapped($C0000093, 'EUnderflow', 'Floating point underflow');
  Mapped($C000001D, 'EPrivilege', 'Privileged instruction');
  Mapped($C0000096, 'EPrivilege', 'Privileged instruction');
  Mapped($C000008C, 'ERangeError', 'Range check error');
  CheckEqI(0, _LiveAllocCount - Before, 'no leaks');
  Done;
end.
