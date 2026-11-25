unit MemArenaKit;
{ Shared by Sys_MemoryManagerArena: a manager that serves every request from a static array and never calls the
  default manager. Blocks are 16-byte aligned, carry their size in a header and are never reused. The
  initialization installs it - this unit uses no unit and is listed first in the uses clause of the program, so it
  runs before any other unit's (RTL-1065). A block that does not lie in the array is refused: releasing it fails
  (runtime error 204), resizing it returns nil (runtime error 203). The manager is not removed. }
{$POINTERMATH ON}

interface

var
  LiveAtInit: NativeInt;               { _LiveAllocCount of the default manager when the arena was installed }

function InArena(P: Pointer): Boolean;
function ArenaBlocks: Integer;

implementation

const
  ArenaSize = 4 * 1024 * 1024;
  HeaderSize = 16;

var
  Arena: array[0..ArenaSize - 1] of Byte;
  Used: NativeInt;
  Blocks: Integer;
  Mine: TMemoryManager;                { zeroed: the optional fields stay nil }

function InArena(P: Pointer): Boolean;
begin
  Result := (NativeUInt(P) >= NativeUInt(@Arena[0])) and (NativeUInt(P) < NativeUInt(@Arena[0]) + ArenaSize);
end;

function ArenaBlocks: Integer;
begin
  Result := Blocks;
end;

function ArenaGetMem(Size: NativeInt): Pointer;
var
  Need: NativeInt;
begin
  Result := nil;
  Need := HeaderSize + (Size + 15) div 16 * 16;
  if Used + Need > ArenaSize then
    Exit;
  PNativeInt(@Arena[Used])^ := Size;
  Result := @Arena[Used + HeaderSize];
  Used := Used + Need;
  Blocks := Blocks + 1;
end;

function ArenaAllocMem(Size: NativeInt): Pointer;
begin
  Result := ArenaGetMem(Size);
  if Result <> nil then
    FillChar(Result^, Size, 0);
end;

function ArenaFreeMem(P: Pointer): Integer;
begin
  if InArena(P) then
    Result := 0
  else
    Result := 1;
end;

function ArenaReallocMem(P: Pointer; Size: NativeInt): Pointer;
var
  Old: NativeInt;
begin
  Result := nil;
  if not InArena(P) then
    Exit;
  Old := PNativeInt(PByte(P) - HeaderSize)^;
  Result := ArenaGetMem(Size);
  if Result = nil then
    Exit;
  if Old > Size then
    Old := Size;
  Move(P^, Result^, Old);
end;

initialization
  Used := (16 - NativeInt(NativeUInt(@Arena[0]) mod 16)) mod 16;
  Mine.GetMem := ArenaGetMem;
  Mine.FreeMem := ArenaFreeMem;
  Mine.ReallocMem := ArenaReallocMem;
  Mine.AllocMem := ArenaAllocMem;
  LiveAtInit := _LiveAllocCount;
  SetMemoryManager(Mine);
end.
