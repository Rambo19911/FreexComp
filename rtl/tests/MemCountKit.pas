unit MemCountKit;
{ Shared by Sys_MemoryManagerCounting: a counting manager that forwards every request to the manager it replaced.
  The initialization installs it - this unit uses no unit and is listed first in the uses clause of the program, so
  it runs before any other unit's - and the finalization, which runs last, restores the previous manager and prints
  the counts. }

interface

implementation

var
  Prev, Mine: TMemoryManager;
  Gets, Frees: Integer;

function CountGetMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  Result := Prev.GetMem(Size);
end;

function CountAllocMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  Result := Prev.AllocMem(Size);
end;

function CountFreeMem(P: Pointer): Integer;
begin
  Frees := Frees + 1;
  Result := Prev.FreeMem(P);
end;

initialization
  GetMemoryManager(Prev);
  Mine := Prev;
  Mine.GetMem := CountGetMem;
  Mine.AllocMem := CountAllocMem;
  Mine.FreeMem := CountFreeMem;
  SetMemoryManager(Mine);

finalization
  SetMemoryManager(Prev);
  Writeln('gets >= 200: ', Gets >= 200);
  Writeln('gets = frees: ', Gets = Frees);
end.
