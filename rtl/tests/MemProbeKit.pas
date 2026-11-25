unit MemProbeKit;
{ Shared by the memory-manager tests: a manager that forwards every request to the manager it replaced and remembers
  the blocks it handed out. Installed by the initialization of this unit, which uses no unit and is listed first in
  the uses clause of the program, so it runs before any other unit's (RTL-1065). ForeignReleases counts the blocks
  it was asked to release or to resize although it never handed them out. The manager is not removed. }

interface

var
  ForeignReleases: Integer;

implementation

const
  MaxBlocks = 4096;

var
  Prev, Mine: TMemoryManager;
  Handed: array[0..MaxBlocks - 1] of Pointer;
  Count: Integer;

procedure Note(P: Pointer);
begin
  if (P <> nil) and (Count < MaxBlocks) then
  begin
    Handed[Count] := P;
    Count := Count + 1;
  end;
end;

{ forgets P; False when it was never handed out }
function Known(P: Pointer): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 0 to Count - 1 do
    if Handed[I] = P then
    begin
      Handed[I] := nil;
      Exit(True);
    end;
end;

function ProbeGetMem(Size: NativeInt): Pointer;
begin
  Result := Prev.GetMem(Size);
  Note(Result);
end;

function ProbeAllocMem(Size: NativeInt): Pointer;
begin
  Result := Prev.AllocMem(Size);
  Note(Result);
end;

function ProbeReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  if not Known(P) then
    ForeignReleases := ForeignReleases + 1;
  Result := Prev.ReallocMem(P, Size);
  Note(Result);
end;

function ProbeFreeMem(P: Pointer): Integer;
begin
  if not Known(P) then
    ForeignReleases := ForeignReleases + 1;
  Result := Prev.FreeMem(P);
end;

initialization
  GetMemoryManager(Prev);
  Mine := Prev;
  Mine.GetMem := ProbeGetMem;
  Mine.AllocMem := ProbeAllocMem;
  Mine.ReallocMem := ProbeReallocMem;
  Mine.FreeMem := ProbeFreeMem;
  SetMemoryManager(Mine);
end.
