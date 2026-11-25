// covers: RTL-1067
// phase: 8
program Sys_MemoryManagerSwitchRelease;
{ Without DEBUG the runtime has neither the counter nor the check: the switch of Sys_MemoryManagerSwitchCheck
  variant 1 - a manager installed while a block is live - passes. }
var
  Old, First, Back: TMemoryManager;
  P: Pointer;

function FirstGetMem(Size: NativeInt): Pointer;
begin
  Result := Old.GetMem(Size);
end;

begin
  GetMemoryManager(Old);
  First := Old;
  First.GetMem := FirstGetMem;
  Writeln('check is on by default ', CheckMemoryManagerSwitch);
  GetMem(P, 16);
  Writeln('switching with a live block');
  SetMemoryManager(First);
  GetMemoryManager(Back);
  Writeln('switched ', Back.GetMem = First.GetMem);
  FreeMem(P);
  SetMemoryManager(Old);
  Writeln('main done');
end.
