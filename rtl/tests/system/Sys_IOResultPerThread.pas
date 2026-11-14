// covers: ABI-120, ABI-132, ABI-173
program Sys_IOResultPerThread;
{ InOutRes is per thread. The main thread records error 7 with SetInOutRes and starts thread A. A sees 0, makes
  error 102 (Reset of an unassigned file), then starts thread B and waits for it while its own error is pending. B
  sees 0, makes error 103 (Close of a file that is not open) and reads it. After B has ended, A still reads its own
  102. Last the main thread reads its own 7. Every second IOResult shows the reset to 0. BeginThread does not exist
  yet, so the threads are created directly with CreateThread (the start routine passed as a Pointer until C-161). }
{$I-}

const
  Infinite = $FFFFFFFF;

function CreateThread(Attributes: Pointer; StackSize: NativeUInt; Start, Param: Pointer; Flags: Cardinal;
  ThreadId: Pointer): THandle; stdcall; external 'kernel32.dll' name 'CreateThread';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; stdcall;
  external 'kernel32.dll' name 'WaitForSingleObject';
function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';

var
  FA, FB: Text;
  AStart, AFirst, ASecond, BStart, BFirst, BSecond: Integer;

function WorkerB(Param: Pointer): Cardinal;
begin
  BStart := IOResult;
  AssignFile(FB, 'never-opened.txt');
  CloseFile(FB);
  BFirst := IOResult;
  BSecond := IOResult;
  Result := 0;
end;

function WorkerA(Param: Pointer): Cardinal;
var
  H: THandle;
begin
  AStart := IOResult;
  Reset(FA);
  H := CreateThread(nil, 0, @WorkerB, nil, 0, nil);
  WaitForSingleObject(H, Infinite);
  CloseHandle(H);
  AFirst := IOResult;
  ASecond := IOResult;
  Result := 0;
end;

var
  H: THandle;
  MFirst, MSecond: Integer;
begin
  SetInOutRes(7);
  H := CreateThread(nil, 0, @WorkerA, nil, 0, nil);
  WaitForSingleObject(H, Infinite);
  CloseHandle(H);
  MFirst := IOResult;
  MSecond := IOResult;
  Writeln('A: ', AStart, ' ', AFirst, ' ', ASecond);
  Writeln('B: ', BStart, ' ', BFirst, ' ', BSecond);
  Writeln('main: ', MFirst, ' ', MSecond);
end.
