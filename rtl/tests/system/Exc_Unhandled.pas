// covers: RTL-134, RTL-135, RTL-136, ABI-121, ABI-124, ABI-104
// opc-run: variants 1 2 3 4 5 6
program Exc_Unhandled;
{ The unhandled-exception path and the fatal path of OS exceptions (R-063). The variant is ParamStr(1).
  1: _UnhandledException, called the way the top-level handler of _start calls it (inside a handler, after
     _BeginCatch), without ExceptProc: 'Unhandled exception <ClassName> at $<16 hex digits>' on stderr, exit
     procedures still run, exit code 217.
  2: the same with ExceptProc assigned: the hook gets object and address and writes the message itself; exit 217.
  3: an access violation inside try..except without ExceptObjProc: 'Runtime error 216 at $...', exit code 216, the
     handler does not run (ABI-121 step 2).
  4: the same with ExceptObjProc returning an object: it is caught like a Pascal exception, ExceptAddr = the fault
     address, the hook saw error 216 and the OS record.
  5: ExceptObjProc returning nil: the fatal path of variant 3.
  6: an exit procedure that raises (ABI-104): reported like an unhandled exception, the next exit procedure still runs,
     exit code 217.
  TODO(C-095): a raise that reaches the real top-level handler of _start joins this test when _start has it. }
{$POINTERMATH ON}
type
  EBoom = class
    Code: Integer;
  end;

var
  V: string;
  Target: PInteger;
  SavedExitProc: Pointer;
  HookCode: Integer;
  HookOsCode: Cardinal;
  HookAddr: Pointer;

procedure AtExit;
begin
  ExitProc := SavedExitProc;
  Writeln('exit procedure ran');
end;

{ variant 6: the first exit procedure called; it raises after putting the next one back }
procedure RaisingExit;
begin
  ExitProc := @AtExit;
  Writeln('raising exit procedure');
  raise EBoom.Create at Pointer($5678);
end;

procedure Hook(Obj: TObject; Addr: Pointer);
begin
  Writeln('ExceptProc: ', Obj.ClassName, ', address ', NativeUInt(Addr) = $1234);
end;

function OsHook(ErrorCode: Integer; ExceptionRecord: Pointer): Pointer;
var
  E: EBoom;
begin
  HookCode := ErrorCode;
  HookOsCode := PCardinal(ExceptionRecord)^;
  HookAddr := PPointer(PByte(ExceptionRecord) + 16)^;
  Writeln('ExceptObjProc: error ', ErrorCode);
  if V = '5' then
    Result := nil
  else
  begin
    E := EBoom.Create;
    E.Code := ErrorCode;
    Result := Pointer(E);
  end;
end;

procedure Unhandled;
begin
  try
    raise EBoom.Create at Pointer($1234);
  except
    Writeln('top-level handler');
    _UnhandledException;
  end;
  Writeln('not reached');
end;

{ the faulting store in a called routine (LANG-830: faults are caught when they happen in a callee) }
procedure Poke(P: PInteger);
begin
  P^ := 1;
end;

procedure Fault;
begin
  try
    Poke(Target);
    Writeln('not reached');
  except
    on E: EBoom do
    begin
      Writeln('caught EBoom ', E.Code, ', ExceptAddr = fault address ', ExceptAddr = HookAddr,
        ', OS code $C0000005 ', HookOsCode = $C0000005);
    end;
  end;
end;

begin
  V := ParamStr(1);
  SavedExitProc := ExitProc;
  ExitProc := @AtExit;
  Target := nil;
  if V = '2' then
    ExceptProc := Hook;
  if (V = '4') or (V = '5') then
    ExceptObjProc := OsHook;
  Writeln('start ', V);
  if V = '6' then
    ExitProc := @RaisingExit
  else if (V = '1') or (V = '2') then
    Unhandled
  else
    Fault;
  Writeln('end');
end.
