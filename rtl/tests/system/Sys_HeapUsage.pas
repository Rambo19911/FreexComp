// covers: RTL-1062
// phase: 8
program Sys_HeapUsage;
{ GetHeapUsage with the default manager: the block count rises by one with GetMem and falls by one with FreeMem, it
  is the count of live blocks, and bytes are not tracked. }
var
  P: Pointer;
  U0, U1, U2: THeapUsage;
begin
  U0 := GetHeapUsage;
  GetMem(P, 1000);
  U1 := GetHeapUsage;
  Writeln('block count is the live count ', NativeInt(U1.BlockCount) = _LiveAllocCount);
  FreeMem(P);
  U2 := GetHeapUsage;
  Writeln('after GetMem ', NativeInt(U1.BlockCount) - NativeInt(U0.BlockCount));
  Writeln('after FreeMem ', NativeInt(U2.BlockCount) - NativeInt(U1.BlockCount));
  Writeln('bytes ', U0.AllocatedBytes, ' ', U1.AllocatedBytes, ' ', U2.AllocatedBytes);
end.
