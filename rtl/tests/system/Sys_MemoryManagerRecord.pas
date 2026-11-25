// covers: RTL-1060, RTL-1061, RTL-1064
// phase: 8
program Sys_MemoryManagerRecord;
{ The memory-manager record with its three optional fields: sizes; the default manager assigns all seven;
  IsMemoryManagerSet follows a change of any field, also of an optional one alone; a manager without the optional
  fields makes RegisterExpectedMemoryLeak answer False and GetHeapUsage answer zero; a nil block is never
  registered. }
var
  Old, Mine, Back: TMemoryManager;
  P: Pointer;
  U: THeapUsage;
  A, B, C: Boolean;
begin
  Writeln('SizeOf(TMemoryManager) ', SizeOf(TMemoryManager));
  Writeln('SizeOf(THeapUsage) ', SizeOf(THeapUsage));
  Writeln('set at start ', IsMemoryManagerSet);
  GetMemoryManager(Old);
  Writeln('allocation fields assigned ', Assigned(Old.GetMem) and Assigned(Old.FreeMem) and Assigned(Old.ReallocMem)
    and Assigned(Old.AllocMem));
  Writeln('optional fields assigned ', Assigned(Old.RegisterExpectedMemoryLeak)
    and Assigned(Old.UnregisterExpectedMemoryLeak) and Assigned(Old.GetHeapUsage));

  GetMem(P, 24);
  Writeln('default: register nil ', RegisterExpectedMemoryLeak(nil), ', unregister nil ',
    UnregisterExpectedMemoryLeak(nil));
  A := RegisterExpectedMemoryLeak(P);
  B := UnregisterExpectedMemoryLeak(P);
  Writeln('default: register ', A, ', unregister ', B);
  U := GetHeapUsage;
  Writeln('default: usage counts blocks ', U.BlockCount > 0, ', bytes ', U.AllocatedBytes);

  { one optional field differs: the manager counts as set }
  Mine := Old;
  Mine.GetHeapUsage := nil;
  SetMemoryManager(Mine);
  A := IsMemoryManagerSet;
  U := GetHeapUsage;
  SetMemoryManager(Old);
  Writeln('set with one optional field changed ', A, ', usage ', U.BlockCount, ' ', U.AllocatedBytes);
  Writeln('set after restoring ', IsMemoryManagerSet);

  { no optional field at all: the allocation fields still serve }
  Mine := Old;
  Mine.RegisterExpectedMemoryLeak := nil;
  Mine.UnregisterExpectedMemoryLeak := nil;
  Mine.GetHeapUsage := nil;
  SetMemoryManager(Mine);
  A := IsMemoryManagerSet;
  B := RegisterExpectedMemoryLeak(P);
  C := UnregisterExpectedMemoryLeak(P);
  U := GetHeapUsage;
  GetMemoryManager(Back);
  SetMemoryManager(Old);
  Writeln('without optional fields: set ', A, ', register ', B, ', unregister ', C, ', usage ', U.BlockCount, ' ',
    U.AllocatedBytes);
  Writeln('Get returns the seven fields ', (Back.GetMem = Old.GetMem) and not Assigned(Back.RegisterExpectedMemoryLeak)
    and not Assigned(Back.UnregisterExpectedMemoryLeak) and not Assigned(Back.GetHeapUsage));
  Writeln('set after restoring ', IsMemoryManagerSet);
  FreeMem(P);
end.
