// covers: RTL-960, RTL-968
library heaplib;

uses SysUtils;

var
  Kept: string;
  Old: TMemoryManager;
  Calls: NativeInt;

// a string that lives and dies inside the call
function TextLength(Count: Integer): Integer; stdcall;
var
  S: string;
begin
  S := StringOfChar('x', Count) + IntToStr(Count);
  Result := Length(S);
end;

// a string the library keeps until Drop
function Keep(Count: Integer): Integer; stdcall;
begin
  SetLength(Kept, Count);
  Result := Length(Kept);
end;

procedure Drop; stdcall;
begin
  Kept := '';
end;

function LiveBlocks: NativeInt; stdcall;
begin
  Result := _LiveAllocCount;
end;

function CountingGetMem(Size: NativeInt): Pointer;
begin
  Calls := Calls + 1;
  Result := Old.GetMem(Size);
end;

function CountingAllocMem(Size: NativeInt): Pointer;
begin
  Calls := Calls + 1;
  Result := Old.AllocMem(Size);
end;

function CountingReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  Calls := Calls + 1;
  Result := Old.ReallocMem(P, Size);
end;

// a memory manager of the library that counts the requests it gets
procedure InstallManager; stdcall;
var
  M: TMemoryManager;
begin
  GetMemoryManager(Old);
  M := Old;
  M.GetMem := CountingGetMem;
  M.AllocMem := CountingAllocMem;
  M.ReallocMem := CountingReallocMem;
  SetMemoryManager(M);
end;

procedure RestoreManager; stdcall;
begin
  SetMemoryManager(Old);
end;

function ManagerCalls: NativeInt; stdcall;
begin
  Result := Calls;
end;

function ManagerIsSet: Integer; stdcall;
begin
  Result := Ord(IsMemoryManagerSet);
end;

exports
  TextLength,
  Keep,
  Drop,
  LiveBlocks,
  InstallManager,
  RestoreManager,
  ManagerCalls,
  ManagerIsSet;

end.
