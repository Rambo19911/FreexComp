// covers: ABI-180, ABI-181, RTL-064, RTL-071
program Sys_ThreadRelease;
{ BeginThread / EndThread (system.md section 5.1, R-076), split from Sys_Threads: a thread that has used its
  per-thread block and its threadvar block holds exactly those two counted blocks (RTL-064) until it ends, and after
  threads ending both ways (return and EndThread) the live count is back where it was. A first thread runs before
  counting (as in Sys_Threads, where earlier sections start threads), so nothing allocated once per process by the
  first BeginThread is counted. The threads only write into their job records; the main thread checks after waiting
  with WaitForSingleObject, and the hold-and-release case uses two events, so no step depends on timing. }
uses TestKit;

const
  Infinite = $FFFFFFFF;
  ThreadCount = 4;

type
  EThread = class
  end;

  PJob = ^TJob;
  TJob = record
    Index: Integer;
    CaughtId: Integer;
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
  Jobs: array[0..ThreadCount - 1] of TJob;
  Handles: array[0..ThreadCount - 1] of THandle;
  Shared: string;
  Reached: Integer;
  Ready, Go: THandle;

{ ---------------------------------------------------------------- thread functions }

function EndWorker(Parameter: Pointer): Integer;
begin
  Counter := 3;
  Reached := 1;
  EndThread(42);
  Reached := 2;
  Result := 0;
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
  Id: Cardinal;
begin
  Section('release of the per-thread blocks');
  Shared := 'shared ' + IntStr(12345);
  Finish(BeginThread(nil, 0, EndWorker, nil, 0, Id));   { the first thread of the process, not counted }
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

begin
  TestRelease;
  Done;
end.
