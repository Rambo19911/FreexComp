// covers: ABI-120, ABI-124, ABI-180, ABI-181, RTL-064, RTL-071, RTL-135
// opc-run: variants 1 2
program Sys_Threads;
{ BeginThread / EndThread (system.md section 5.1, R-076). The variant is ParamStr(1).
  1: IsMultiThread is set by the first BeginThread; four threads each keep their own threadvar values (and the main
     thread its own); a thread raises and catches an exception while the main thread is inside a handler of its own,
     and neither sees the other's exception; the result of the thread function and the code given to EndThread are
     the thread's exit code, and nothing after EndThread runs; four threads share one string through 100 000
     assignments each and its reference count is exact afterwards; a thread that has used its per-thread block and its
     threadvar block holds exactly those two counted blocks (RTL-064) until it ends, and after threads ending both ways
     (return and EndThread) the live count is back where it was.
  2: an exception that escapes the thread function reaches the top-level frame of the BeginThread wrapper: the
     unhandled-exception path (ABI-124) with exit code 217; exit procedures still run, the waiting main thread does
     not go on.
  The threads only write into their job records; the main thread checks after waiting with WaitForSingleObject, and
  the hold-and-release case uses two events, so no step depends on timing. }
uses TestKit;

const
  Infinite = $FFFFFFFF;
  ThreadCount = 4;
  Assignments = 100000;

type
  EThread = class
    Id: Integer;
    destructor Destroy; override;
  end;
  EMain = class(EThread);

  PJob = ^TJob;
  TJob = record
    Index: Integer;
    BeforeCounter: Integer;
    BeforeTag: Int64;
    AfterCounter: Integer;
    AfterTag: Int64;
    CaughtId: Integer;
    CaughtWasCurrent: Boolean;
    CurrentAfter: Boolean;
    UseEndThread: Boolean;
  end;

function CloseHandle(hObject: THandle): LongBool; stdcall; external 'kernel32.dll' name 'CloseHandle';
function WaitForSingleObject(Handle: THandle; Milliseconds: Cardinal): Cardinal; stdcall;
  external 'kernel32.dll' name 'WaitForSingleObject';
function GetExitCodeThread(Handle: THandle; var ExitCode: Cardinal): LongBool; stdcall;
  external 'kernel32.dll' name 'GetExitCodeThread';
function CreateEventW(Attributes: Pointer; ManualReset, InitialState: LongBool; Name: PWideChar): THandle; stdcall;
  external 'kernel32.dll' name 'CreateEventW';
function SetEvent(Handle: THandle): LongBool; stdcall; external 'kernel32.dll' name 'SetEvent';

threadvar
  Counter: Integer;
  Tag: Int64;

var
  Destroyed: Integer;
  Jobs: array[0..ThreadCount - 1] of TJob;
  Handles: array[0..ThreadCount - 1] of THandle;
  Shared: string;
  Reached: Integer;
  Ready, Go: THandle;
  SavedExitProc: Pointer;

destructor EThread.Destroy;
begin
  AtomicIncrement(Destroyed);
  inherited Destroy;
end;

{ ---------------------------------------------------------------- thread functions }

function VarWorker(Parameter: Pointer): Integer;
var
  Job: PJob;
  K: Integer;
begin
  Job := Parameter;
  Job^.BeforeCounter := Counter;
  Job^.BeforeTag := Tag;
  for K := 1 to 1000 + Job^.Index do
    Counter := Counter + 1;
  Tag := Int64(Job^.Index) * 1000000000000;
  Job^.AfterCounter := Counter;
  Job^.AfterTag := Tag;
  Result := 10 + Job^.Index;
end;

function RaiseWorker(Parameter: Pointer): Integer;
var
  Job: PJob;
  E: EThread;
begin
  Job := Parameter;
  E := EThread.Create;
  E.Id := 100 + Job^.Index;
  try
    raise E;
  except
    on X: EThread do
    begin
      Job^.CaughtId := X.Id;
      Job^.CaughtWasCurrent := ExceptObject = X;
    end;
  end;
  Job^.CurrentAfter := ExceptObject = nil;
  Result := 0;
end;

function EndWorker(Parameter: Pointer): Integer;
begin
  Counter := 3;
  Reached := 1;
  EndThread(42);
  Reached := 2;
  Result := 0;
end;

function ShareWorker(Parameter: Pointer): Integer;
var
  L: string;
  K: Integer;
begin
  for K := 1 to Assignments do
    L := Shared;
  Result := Length(L);
end;

{ uses the per-thread block (IOResult) and the threadvar block, then waits until the main thread has counted }
function HoldWorker(Parameter: Pointer): Integer;
begin
  Counter := 1;
  Result := IOResult;
  SetEvent(Ready);
  WaitForSingleObject(Go, Infinite);
end;

{ everything the RTL keeps per thread: threadvars, a caught exception, strings }
procedure BusyWork(Job: PJob);
var
  S: string;
begin
  Counter := Job^.Index;
  Tag := Counter;
  S := Shared + IntStr(Job^.Index);
  try
    raise EThread.Create;
  except
    on E: EThread do
      Job^.CaughtId := Length(S);
  end;
end;

{ half of them end with EndThread, which does not finalise the frames it leaves: the managed work is in BusyWork,
  whose locals and temporaries are released when it returns }
function BusyWorker(Parameter: Pointer): Integer;
begin
  BusyWork(Parameter);
  Result := IOResult;
  if PJob(Parameter)^.UseEndThread then
    EndThread(7);
end;

function EscapeWorker(Parameter: Pointer): Integer;
begin
  Writeln('thread raising');
  raise EThread.Create at Pointer($ABCD);
end;

{ ---------------------------------------------------------------- helpers }

function Start(Func: TThreadFunc; Job: PJob): THandle;
var
  Id: Cardinal;
begin
  Id := 0;
  Result := BeginThread(nil, 0, Func, Job, 0, Id);
  Check(Result <> 0, 'BeginThread returns a handle');
  Check(Id <> 0, 'BeginThread returns a thread id');
end;

function Finish(H: THandle): Cardinal;
begin
  Result := $FFFFFFFF;
  WaitForSingleObject(H, Infinite);
  GetExitCodeThread(H, Result);
  CloseHandle(H);
end;

procedure ResetJobs;
var
  I: Integer;
begin
  for I := 0 to ThreadCount - 1 do
  begin
    FillChar(Jobs[I], SizeOf(TJob), 0);
    Jobs[I].Index := I;
  end;
end;

{ ---------------------------------------------------------------- sections }

procedure TestThreadVars;
var
  I: Integer;
  Code: Cardinal;
begin
  Section('threadvars');
  ResetJobs;
  Counter := 77;
  Tag := -5;
  CheckEqB(False, IsMultiThread, 'IsMultiThread before BeginThread');
  for I := 0 to ThreadCount - 1 do
    Handles[I] := Start(VarWorker, @Jobs[I]);
  CheckEqB(True, IsMultiThread, 'IsMultiThread after BeginThread');
  for I := 0 to ThreadCount - 1 do
  begin
    Code := Finish(Handles[I]);
    CheckEqI(10 + I, Code, 'exit code = result of thread ' + IntStr(I));
    CheckEqI(0, Jobs[I].BeforeCounter, 'Counter starts at 0 in thread ' + IntStr(I));
    CheckEqI(0, Jobs[I].BeforeTag, 'Tag starts at 0 in thread ' + IntStr(I));
    CheckEqI(1000 + I, Jobs[I].AfterCounter, 'own Counter in thread ' + IntStr(I));
    CheckEqI(Int64(I) * 1000000000000, Jobs[I].AfterTag, 'own Tag in thread ' + IntStr(I));
  end;
  CheckEqI(77, Counter, 'main Counter unchanged');
  CheckEqI(-5, Tag, 'main Tag unchanged');
end;

procedure TestExceptions;
var
  I: Integer;
  M: EMain;
begin
  Section('exceptions in threads');
  ResetJobs;
  Destroyed := 0;
  M := EMain.Create;
  M.Id := 1;
  try
    raise M;
  except
    for I := 0 to ThreadCount - 1 do
      Handles[I] := Start(RaiseWorker, @Jobs[I]);
    for I := 0 to ThreadCount - 1 do
    begin
      Finish(Handles[I]);
      CheckEqI(100 + I, Jobs[I].CaughtId, 'thread ' + IntStr(I) + ' caught its own exception');
      Check(Jobs[I].CaughtWasCurrent, 'ExceptObject in the thread''s handler ' + IntStr(I));
      Check(Jobs[I].CurrentAfter, 'ExceptObject nil after the thread''s handler ' + IntStr(I));
    end;
    Check(ExceptObject = M, 'main handler still sees its own exception');
    CheckEqI(ThreadCount, Destroyed, 'each thread''s exception destroyed by its handler');
  end;
  CheckEqI(ThreadCount + 1, Destroyed, 'main exception destroyed by its handler');
end;

procedure TestEndThread;
var
  Code: Cardinal;
begin
  Section('EndThread');
  Reached := 0;
  Code := Finish(Start(EndWorker, nil));
  CheckEqI(42, Code, 'exit code given to EndThread');
  CheckEqI(1, Reached, 'nothing after EndThread runs');
end;

procedure TestSharedString;
var
  I: Integer;
begin
  Section('shared string');
  Shared := 'shared ' + IntStr(12345);
  CheckEqI(1, StringRefCount(Shared), 'one owner before');
  for I := 0 to ThreadCount - 1 do
    Handles[I] := Start(ShareWorker, nil);
  for I := 0 to ThreadCount - 1 do
    CheckEqI(Length(Shared), Finish(Handles[I]), 'thread ' + IntStr(I) + ' saw the string');
  CheckEqI(1, StringRefCount(Shared), 'reference count exact after ' + IntStr(ThreadCount) + ' x '
    + IntStr(Assignments) + ' assignments');
end;

procedure RunBusy;
var
  I: Integer;
  Code: Cardinal;
begin
  ResetJobs;
  Jobs[1].UseEndThread := True;
  Jobs[3].UseEndThread := True;
  for I := 0 to ThreadCount - 1 do
    Handles[I] := Start(BusyWorker, @Jobs[I]);
  for I := 0 to ThreadCount - 1 do
  begin
    Code := Finish(Handles[I]);
    if Jobs[I].UseEndThread then
      CheckEqI(7, Code, 'busy thread ' + IntStr(I) + ' ended by EndThread')
    else
      CheckEqI(0, Code, 'busy thread ' + IntStr(I) + ' returned');
    CheckEqI(Length(Shared) + 1, Jobs[I].CaughtId, 'busy thread ' + IntStr(I) + ' caught');
  end;
end;

procedure TestRelease;
var
  Before: NativeInt;
  H: THandle;
  Code: Cardinal;
begin
  Section('release of the per-thread blocks');
  Counter := Counter + 1;                { the main thread's blocks exist before counting }
  IOResult;
  Ready := CreateEventW(nil, True, False, nil);
  Go := CreateEventW(nil, True, False, nil);
  Before := _LiveAllocCount;
  H := Start(HoldWorker, nil);
  WaitForSingleObject(Ready, Infinite);
  CheckEqI(2, _LiveAllocCount - Before, 'a running thread holds its per-thread block and its threadvar block');
  SetEvent(Go);
  CheckEqI(0, Finish(H), 'hold thread result');
  CheckEqI(0, _LiveAllocCount - Before, 'both freed when the thread function returns');
  Code := Finish(Start(EndWorker, nil));
  CheckEqI(42, Code, 'EndThread exit code');
  CheckEqI(0, _LiveAllocCount - Before, 'both freed by EndThread');
  RunBusy;                               { its message strings are freed when it returns }
  CheckEqI(0, _LiveAllocCount - Before, 'no leaks after threads ending both ways');
  CloseHandle(Ready);
  CloseHandle(Go);
end;

procedure AtExit;
begin
  ExitProc := SavedExitProc;
  Writeln('exit procedure ran');
end;

procedure Escape;
var
  H: THandle;
  Id: Cardinal;
begin
  SavedExitProc := ExitProc;
  ExitProc := @AtExit;
  Writeln('start 2');
  H := BeginThread(nil, 0, EscapeWorker, nil, 0, Id);
  WaitForSingleObject(H, Infinite);
  Writeln('main thread went on');
end;

begin
  if ParamStr(1) = '2' then
    Escape
  else
  begin
    TestThreadVars;
    TestExceptions;
    TestEndThread;
    TestSharedString;
    TestRelease;
    Done;
  end;
end.
