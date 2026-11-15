// covers: RTL-042, ABI-105
program Sys_MXCSR;
{ The floating-point environment of RTL-042: MXCSR is $1F80 (all exceptions masked, round to nearest even) when the
  program body starts, and at the start of a thread created through BeginThread even when the creating thread has
  changed its own MXCSR first (here: round toward zero, invalid operation unmasked). The thread only stores the
  value it sees; the main thread prints it after waiting. }
const
  Infinite = $FFFFFFFF;

function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; stdcall;
  external 'kernel32.dll' name 'WaitForSingleObject';

var
  Seen: Cardinal;

function ThreadFunc(Param: Pointer): Integer;
begin
  Seen := _GetMXCSR;
  Result := 0;
end;

var
  Id: Cardinal;
  H: THandle;
  Saved: Cardinal;
begin
  Writeln('start-up MXCSR ', _GetMXCSR, ' = $1F80 ', _GetMXCSR = $1F80);
  Saved := _GetMXCSR;
  _SetMXCSR($7F00);
  Writeln('changed MXCSR ', _GetMXCSR, ' = $7F00 ', _GetMXCSR = $7F00);
  Seen := 0;
  H := BeginThread(nil, 0, ThreadFunc, nil, 0, Id);
  WaitForSingleObject(H, Infinite);
  CloseHandle(H);
  _SetMXCSR(Saved);
  Writeln('thread MXCSR ', Seen, ' = $1F80 ', Seen = $1F80);
  Writeln('main MXCSR again ', _GetMXCSR, ' = $1F80 ', _GetMXCSR = $1F80);
end.
