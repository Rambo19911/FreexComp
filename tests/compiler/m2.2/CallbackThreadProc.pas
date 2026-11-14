// covers: LANG-480, LANG-462, CMP-378, CMP-433
program CallbackThreadProc;
type
  PInteger = ^Integer;
  TThreadStartRoutine = function(Parameter: Pointer): Cardinal; stdcall;

const
  INFINITE = $FFFFFFFF;

function CreateThread(Attributes: Pointer; StackSize: NativeUInt; StartAddress: TThreadStartRoutine;
  Parameter: Pointer; CreationFlags: Cardinal; ThreadId: Pointer): Pointer; stdcall; external 'kernel32.dll';
function WaitForSingleObject(Handle: Pointer; Milliseconds: Cardinal): Cardinal; stdcall; external 'kernel32.dll';
function CloseHandle(Handle: Pointer): LongBool; stdcall; external 'kernel32.dll';

// an ordinary routine: no directive is needed to serve as an OS callback (LANG-480, CMP-433)
function Worker(Parameter: Pointer): Cardinal;
var
  P: PInteger;
begin
  P := Parameter;
  P^ := P^ * 10 + 7;
  Result := 5;
end;

var
  Value, Before: Integer;
  H: Pointer;
begin
  Value := 3;
  Before := Value;
  H := CreateThread(nil, 0, Worker, @Value, 0, nil);
  Writeln(Before, ' ', H <> nil, ' ', WaitForSingleObject(H, INFINITE), ' ', Value);
  CloseHandle(H);
  Before := Value;
  H := CreateThread(nil, 0, @Worker, @Value, 0, nil);
  Writeln(Before, ' ', H <> nil, ' ', WaitForSingleObject(H, INFINITE), ' ', Value);
  CloseHandle(H);
end.
