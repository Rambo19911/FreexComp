// covers: RTL-959, RTL-965
library threadlib;
// every thread that calls Touch gets a per-thread block and a threadvar block of this module

threadvar
  Slot: Integer;

// adds Index + 1 to the calling thread's variable; the I/O result of the calling thread is set and read back
function Touch(Index: Integer): Integer; stdcall;
begin
  Slot := Slot + Index + 1;
  Result := Slot;
  SetInOutRes(100 + Index);
  if IOResult <> 100 + Index then
    Result := -1
  else if IOResult <> 0 then
    Result := -2;
end;

// the live blocks of this module's memory manager (per-thread blocks are among them)
function LiveBlocks: NativeInt; stdcall;
begin
  Result := _LiveAllocCount;
end;

exports
  Touch,
  LiveBlocks;

end.
