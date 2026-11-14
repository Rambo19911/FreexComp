// covers: ABI-125, RTL-160
program Raise_PassesThroughRaiseRoutine;
{ System._Raise is usable as the debugger's raise hook (ABI-125): a software breakpoint (int3) on the first
  instruction of _Raise, armed before each raise and counted by a vectored exception handler the way a debugger would
  count it, is hit by a raise statement in the main program block and by one in a nested call; at that instruction
  RCX = the object and RDX = the address of "raise ... at"; a re-raise (raise;) does not enter _Raise (it goes
  through _ReRaise). The breakpoint byte is restored at each hit, so execution goes on normally. }
{$POINTERMATH ON}
uses Windows, TestKit;

const
  CtxRcx = 128;                        { CONTEXT offsets (x64) }
  CtxRdx = 136;
  CtxRip = 248;
  EXCEPTION_CONTINUE_EXECUTION = -1;
  EXCEPTION_CONTINUE_SEARCH = 0;

type
  EMark = class
    Id: Integer;
    constructor Create(AId: Integer);
  end;

var
  Target: PByte;
  Original: Byte;
  Armed: Boolean;
  Hits: Integer;
  LastRcx, LastRdx: Pointer;
  Marker: Integer;
  OldProtect: DWORD;
  Veh: Pointer;
  SeenRcx: Boolean;
  Id: Integer;

constructor EMark.Create(AId: Integer);
begin
  Id := AId;
end;

procedure SetByte(B: Byte);
begin
  Target^ := B;
  FlushInstructionCache(GetCurrentProcess, Target, 1);
end;

procedure Arm;
begin
  SetByte($CC);
  Armed := True;
end;

procedure Disarm;
begin
  SetByte(Original);
  Armed := False;
end;

{ the breakpoint at _Raise: restore the byte, record the registers, resume at the same instruction }
function OnException(Info: Pointer): LONG; stdcall;
var
  Rec, Ctx: PByte;
begin
  Result := EXCEPTION_CONTINUE_SEARCH;
  Rec := PPointer(Info)^;
  Ctx := PPointer(PByte(Info) + 8)^;
  if Armed and (PCardinal(Rec)^ = EXCEPTION_BREAKPOINT) and (PPointer(Rec + 16)^ = Pointer(Target)) then
  begin
    Disarm;
    Hits := Hits + 1;
    LastRcx := PPointer(Ctx + CtxRcx)^;
    LastRdx := PPointer(Ctx + CtxRdx)^;
    PPointer(Ctx + CtxRip)^ := Pointer(Target);
    Result := EXCEPTION_CONTINUE_EXECUTION;
  end;
end;

procedure Nested(Depth: Integer);
begin
  if Depth = 0 then
    raise EMark.Create(2) at @Marker;
  Nested(Depth - 1);
end;

begin
  Section('set-up');
  Target := PByte(@_Raise);
  Original := Target^;
  Check(VirtualProtect(Target, 1, PAGE_EXECUTE_READWRITE, OldProtect), 'code byte writable');
  Veh := AddVectoredExceptionHandler(1, @OnException);
  Check(Veh <> nil, 'vectored handler installed');

  Section('raise in the main program');
  Arm;
  SeenRcx := False;
  try
    raise EMark.Create(1) at @Marker;
  except
    on E: EMark do
      SeenRcx := LastRcx = Pointer(E);
  end;
  CheckEqI(1, Hits, 'breakpoint at _Raise hit once');
  CheckEqB(True, SeenRcx, 'RCX = the raised object');
  Check(LastRdx = @Marker, 'RDX = the raise address');

  Section('raise in a nested call');
  Arm;
  Id := 0;
  try
    Nested(3);
  except
    on E: EMark do
    begin
      Id := E.Id;
      SeenRcx := LastRcx = Pointer(E);
    end;
  end;
  CheckEqI(2, Hits, 'hit by the nested raise');
  CheckEqI(2, Id, 'the nested exception was caught');
  CheckEqB(True, SeenRcx, 'RCX = the raised object');

  Section('re-raise');
  Id := 0;
  try
    try
      raise EMark.Create(3);
    except
      Arm;
      raise;
    end;
  except
    on E: EMark do
      Id := E.Id;
  end;
  CheckEqI(3, Id, 're-raised exception caught');
  CheckEqI(2, Hits, 'raise; does not enter _Raise');
  CheckEqB(True, Armed, 'breakpoint still armed after raise;');
  Disarm;

  RemoveVectoredExceptionHandler(Veh);
  Done;
end.
