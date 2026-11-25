// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// phase: 8
// covers: RTL-820, RTL-860, RTL-862
program Win_GetThreadTimes;
{ Windows unit, kernel32 part (winapi.md section 5.1): GetThreadTimes (task R-236), called for the current thread.
  The checks hold on any host: the creation time is set and not later than the present; the exit time of a running
  thread is undefined and not checked; the processor time (kernel + user, in 100 ns units) never decreases and grows
  when the thread computes. The amount of work is bounded by a count of steps, not by a clock. An invalid handle is
  refused with ERROR_INVALID_HANDLE. }
uses Windows, TestKit;

function Ticks(const T: TFileTime): UInt64;
begin
  Result := (UInt64(T.dwHighDateTime) shl 32) or UInt64(T.dwLowDateTime);
end;

function CpuTime: UInt64;
var
  Created, Exited, Kernel, User: TFileTime;
begin
  if GetThreadTimes(GetCurrentThread, Created, Exited, Kernel, User) then
    Result := Ticks(Kernel) + Ticks(User)
  else
    Result := 0;
end;

var
  Created, Exited, Kernel, User, Now: TFileTime;
  Before, After: UInt64;
  Step, I: Integer;
  Sum: Cardinal;

begin
  Section('current thread');
  Created.dwLowDateTime := 0;
  Created.dwHighDateTime := 0;
  Check(GetThreadTimes(GetCurrentThread, Created, Exited, Kernel, User), 'GetThreadTimes');
  Check(Ticks(Created) <> 0, 'creation time is set');
  GetSystemTimeAsFileTime(Now);
  Check(Ticks(Created) <= Ticks(Now), 'creation time is not later than the present');

  Section('processor time');
  Before := Ticks(Kernel) + Ticks(User);
  After := Before;
  Sum := 0;
  Step := 0;
  while (After = Before) and (Step < 20000) do
  begin
    for I := 1 to 1000000 do
      Sum := Sum * 31 + Cardinal(I);
    After := CpuTime;
    Inc(Step);
  end;
  Check(After >= Before, 'kernel + user time does not decrease');
  Check(After > Before, 'kernel + user time grows with work');
  Check(Sum <> 1, 'work was done');

  Section('invalid handle');
  SetLastError(0);
  Check(not GetThreadTimes(0, Created, Exited, Kernel, User), 'a null handle is refused');
  CheckEqI(ERROR_INVALID_HANDLE, GetLastError, 'ERROR_INVALID_HANDLE');
  Done;
end.
