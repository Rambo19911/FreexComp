// covers: RTL-812, RTL-813
program Win_CriticalSectionImport;
{ kernel32.def lists InitializeCriticalSection (task R-222): a program that declares the export itself, without the
  Windows unit, links against the generated import library and the lock works (re-entrant for its owner). }
uses TestKit;

type
  TLock = record
    DebugInfo: Pointer;
    LockCount: Integer;
    RecursionCount: Integer;
    OwningThread: NativeUInt;
    LockSemaphore: NativeUInt;
    SpinCount: NativeUInt;
  end;

procedure InitializeCriticalSection(var Lock: TLock); stdcall; external 'kernel32.dll' name 'InitializeCriticalSection';
procedure EnterCriticalSection(var Lock: TLock); stdcall; external 'kernel32.dll' name 'EnterCriticalSection';
procedure LeaveCriticalSection(var Lock: TLock); stdcall; external 'kernel32.dll' name 'LeaveCriticalSection';
procedure DeleteCriticalSection(var Lock: TLock); stdcall; external 'kernel32.dll' name 'DeleteCriticalSection';

var
  Lock: TLock;

begin
  Section('InitializeCriticalSection');
  InitializeCriticalSection(Lock);
  CheckEqI(0, Lock.RecursionCount, 'not owned after initialisation');
  EnterCriticalSection(Lock);
  EnterCriticalSection(Lock);
  CheckEqI(2, Lock.RecursionCount, 'entered twice by the owner');
  LeaveCriticalSection(Lock);
  LeaveCriticalSection(Lock);
  CheckEqI(0, Lock.RecursionCount, 'released');
  DeleteCriticalSection(Lock);
  Done;
end.
