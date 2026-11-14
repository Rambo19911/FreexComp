// phase: 4
// covers: RTL-660, RTL-661, RTL-662
program Sync_Objects;
{ SyncObjs (other-units.md section 7): TCriticalSection (re-entrant, TryEnter from another thread fails while held,
  mutual exclusion of four threads), TEvent (manual and auto reset, named), TMutex (ownership, abandoned), TSemaphore
  (counts), the WaitFor mapping of RTL-660 incl. wrError with LastError, EOSError on creation failure (RTL-661), the
  TSynchroObject defaults, and TInterlocked for Integer, Int64 and Pointer. Threads report through records the main
  thread reads after WaitForSingleObject on the thread handle, so no step depends on timing. }
uses
  Windows, SysUtils, SyncObjs, TestKit;

const
  Workers = 4;
  Rounds = 20000;

type
  PWork = ^TWork;
  TWork = record
    Lock: TCriticalSection;
    Counter: ^Integer;
  end;

var
  Shared: Integer;
  TryResult: Boolean;
  Lock: TCriticalSection;
  Mutex: TMutex;

{ waits for the thread and closes its handle }
procedure Join(H: THandle);
begin
  WaitForSingleObject(H, INFINITE);
  CloseHandle(H);
end;

function Spawn(F: TThreadFunc; P: Pointer): THandle;
var
  Id: Cardinal;
begin
  Result := BeginThread(nil, 0, F, P, 0, Id);
end;

function TryFromOtherThread(P: Pointer): Integer;
begin
  TryResult := Lock.TryEnter;
  if TryResult then
    Lock.Leave;
  Result := 0;
end;

function AddMany(P: Pointer): Integer;
var
  I, V: Integer;
begin
  for I := 1 to Rounds do
  begin
    PWork(P)^.Lock.Enter;
    V := PWork(P)^.Counter^;
    V := V + 1;
    PWork(P)^.Counter^ := V;
    PWork(P)^.Lock.Leave;
  end;
  Result := 0;
end;

function TakeMutexAndQuit(P: Pointer): Integer;
begin
  Mutex.Acquire;
  Result := 0;
end;

procedure TestCriticalSection;
var
  Work: TWork;
  H: array[0..Workers - 1] of THandle;
  I: Integer;
begin
  Section('TCriticalSection');
  Lock := TCriticalSection.Create;
  Lock.Enter;
  Lock.Enter;
  CheckEqB(True, Lock.TryEnter, 're-entrant TryEnter by the owner');
  Lock.Leave;
  Lock.Leave;
  Join(Spawn(TryFromOtherThread, nil));
  CheckEqB(False, TryResult, 'TryEnter from another thread fails while held');
  Lock.Leave;
  Join(Spawn(TryFromOtherThread, nil));
  CheckEqB(True, TryResult, 'TryEnter from another thread succeeds when free');
  Lock.Acquire;
  Lock.Release;
  Shared := 0;
  Work.Lock := Lock;
  Work.Counter := @Shared;
  for I := 0 to Workers - 1 do
    H[I] := Spawn(AddMany, @Work);
  for I := 0 to Workers - 1 do
    Join(H[I]);
  CheckEqI(Workers * Rounds, Shared, 'mutual exclusion of four threads');
  Lock.Free;
end;

procedure TestEvent;
var
  E, E2: TEvent;
  S: TSimpleEvent;
  Name: string;
begin
  Section('TEvent');
  E := TEvent.Create;
  Check(E.Handle <> 0, 'handle');
  Check(E.WaitFor(0) = wrTimeout, 'default: not signaled');
  E.SetEvent;
  Check(E.WaitFor(0) = wrSignaled, 'signaled');
  Check(E.WaitFor(0) = wrSignaled, 'default: manual reset stays signaled');
  E.ResetEvent;
  Check(E.WaitFor(0) = wrTimeout, 'ResetEvent');
  E.Free;
  E := TEvent.Create(nil, False, True, '');
  Check(E.WaitFor(0) = wrSignaled, 'auto reset, initially signaled');
  Check(E.WaitFor(0) = wrTimeout, 'auto reset consumed the signal');
  E.SetEvent;
  E.Acquire;
  Check(E.WaitFor(0) = wrTimeout, 'Acquire waits for the event');
  E.Free;
  Name := 'FreexCompSyncTestEvent' + IntToStr(GetCurrentProcessId);
  E := TEvent.Create(nil, True, False, Name);
  E2 := TEvent.Create(nil, True, False, Name);
  E.SetEvent;
  Check(E2.WaitFor(0) = wrSignaled, 'a named event is shared');
  E2.Free;
  E.Free;
  S := TSimpleEvent.Create;
  Check(S.WaitFor(0) = wrTimeout, 'TSimpleEvent is TEvent');
  S.Free;
end;

procedure TestMutexSemaphore;
var
  Sem: TSemaphore;
begin
  Section('TMutex, TSemaphore');
  Mutex := TMutex.Create;
  Check(Mutex.WaitFor(0) = wrSignaled, 'free mutex');
  Check(Mutex.WaitFor(0) = wrSignaled, 'owner waits again (recursive)');
  Mutex.Release;
  Mutex.Release;
  Join(Spawn(TakeMutexAndQuit, nil));
  Check(Mutex.WaitFor(0) = wrAbandoned, 'owner thread ended without Release');
  Mutex.Release;
  Mutex.Free;
  Mutex := TMutex.Create(True);
  Check(Mutex.WaitFor(0) = wrSignaled, 'initial owner');
  Mutex.Release;
  Mutex.Release;
  Mutex.Free;
  Sem := TSemaphore.Create(2, 2);
  Check(Sem.WaitFor(0) = wrSignaled, 'semaphore count 2 -> 1');
  Check(Sem.WaitFor(0) = wrSignaled, 'semaphore count 1 -> 0');
  Check(Sem.WaitFor(0) = wrTimeout, 'semaphore count 0');
  Sem.Release;
  Check(Sem.WaitFor(0) = wrSignaled, 'Release adds one');
  Sem.Free;
end;

procedure TestErrors;
var
  E: TEvent;
  M: TMutex;
  Code: Integer;
  Name: string;
begin
  Section('errors');
  E := TEvent.Create;
  CloseHandle(E.Handle);
  Check(E.WaitFor(0) = wrError, 'WaitFor on a closed handle');
  CheckEqI(ERROR_INVALID_HANDLE, E.LastError, 'LastError = GetLastError');
  E.Free;
  Name := 'FreexCompSyncTestMutex' + IntToStr(GetCurrentProcessId);
  M := TMutex.Create(False, Name);
  Code := 0;
  E := nil;
  try
    E := TEvent.Create(nil, True, False, Name);
  except
    on X: EOSError do
      Code := X.ErrorCode;
  end;
  CheckEqI(ERROR_INVALID_HANDLE, Code, 'a name of another object kind raises EOSError');
  Check(E = nil, 'no object');
  M.Free;
end;

procedure TestBase;
var
  S: TSynchroObject;
begin
  Section('TSynchroObject');
  S := TSynchroObject.Create;
  S.Acquire;
  S.Release;
  Check(S.WaitFor(0) = wrError, 'WaitFor of the base class');
  S.Free;
end;

procedure TestInterlocked;
var
  I: Integer;
  L: Int64;
  P, Q: Pointer;
begin
  Section('TInterlocked');
  I := 5;
  CheckEqI(6, TInterlocked.Increment(I), 'Increment Integer returns the new value');
  CheckEqI(5, TInterlocked.Decrement(I), 'Decrement Integer');
  CheckEqI(15, TInterlocked.Add(I, 10), 'Add Integer');
  CheckEqI(15, TInterlocked.Exchange(I, 3), 'Exchange Integer returns the old value');
  CheckEqI(3, I, 'exchanged');
  CheckEqI(3, TInterlocked.CompareExchange(I, 9, 4), 'CompareExchange mismatch returns the old value');
  CheckEqI(3, I, 'unchanged on mismatch');
  CheckEqI(3, TInterlocked.CompareExchange(I, 9, 3), 'CompareExchange match');
  CheckEqI(9, I, 'changed on match');
  L := Int64($100000000);
  Check(TInterlocked.Increment(L) = Int64($100000001), 'Increment Int64');
  Check(TInterlocked.Decrement(L) = Int64($100000000), 'Decrement Int64');
  Check(TInterlocked.Add(L, Int64($100000000)) = Int64($200000000), 'Add Int64');
  Check(TInterlocked.Exchange(L, 7) = Int64($200000000), 'Exchange Int64');
  Check(TInterlocked.CompareExchange(L, 8, 7) = 7, 'CompareExchange Int64');
  Check(L = 8, 'Int64 changed');
  P := nil;
  Q := @I;
  Check(TInterlocked.Exchange(P, Q) = nil, 'Exchange Pointer');
  Check(P = Q, 'Pointer exchanged');
  Check(TInterlocked.CompareExchange(P, nil, Q) = Q, 'CompareExchange Pointer');
  Check(P = nil, 'Pointer changed');
end;

begin
  TestCriticalSection;
  TestEvent;
  TestMutexSemaphore;
  TestErrors;
  TestBase;
  TestInterlocked;
  Done;
end.
