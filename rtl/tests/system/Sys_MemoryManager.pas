// covers: RTL-062, RTL-064
// opc-run: variants 1 2 3
program Sys_MemoryManager;
{ The pluggable memory manager. Variant 1: a counting manager that chains to the default one sees the allocations
  of GetMem, AllocMem, ReallocMem, New, strings and dynamic arrays; IsMemoryManagerSet follows Set/Get; the default
  manager's counters (_LiveAllocCount) still see the chained blocks. Variant 2: a FreeMem that reports failure is
  runtime error 204. Variant 3: a GetMem that returns nil is runtime error 203. The variant is ParamStr(1). }
type
  TPair = record
    A, B: Int64;
  end;
  PPair = ^TPair;

var
  Old, Mine, Back: TMemoryManager;
  Gets, Frees, Reallocs, Allocs: Integer;
  P: PByte;
  R: PPair;
  V: string;
  Baseline: NativeInt;
  SetWhile: Boolean;

function MyGetMem(Size: NativeInt): Pointer;
begin
  Gets := Gets + 1;
  if V = '3' then
    Result := nil
  else
    Result := Old.GetMem(Size);
end;

function MyFreeMem(P: Pointer): Integer;
begin
  Frees := Frees + 1;
  if V = '2' then
    Result := 1
  else
    Result := Old.FreeMem(P);
end;

function MyReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  Reallocs := Reallocs + 1;
  Result := Old.ReallocMem(P, Size);
end;

function MyAllocMem(Size: NativeInt): Pointer;
begin
  Allocs := Allocs + 1;
  Result := Old.AllocMem(Size);
end;

{ in a routine of its own, so the temporaries of the calls are released before it returns }
procedure ManagedValues;
var
  S: string;
  D: array of Integer;
begin
  S := StringOfChar('x', 30);
  S := '';
  SetLength(D, 5);
  D := nil;
end;

begin
  V := ParamStr(1);
  Writeln('set at start ', IsMemoryManagerSet);
  GetMemoryManager(Old);
  Writeln('default fields assigned ', Assigned(Old.GetMem) and Assigned(Old.FreeMem) and Assigned(Old.ReallocMem)
    and Assigned(Old.AllocMem));
  Mine.GetMem := MyGetMem;
  Mine.FreeMem := MyFreeMem;
  Mine.ReallocMem := MyReallocMem;
  Mine.AllocMem := MyAllocMem;
  Baseline := _LiveAllocCount;
  SetMemoryManager(Mine);
  SetWhile := IsMemoryManagerSet;
  if V = '2' then
  begin
    GetMem(P, 10);
    Writeln('freeing');
    FreeMem(P);
    Writeln('not reached');
  end;
  if V = '3' then
  begin
    Writeln('allocating');
    GetMem(P, 10);
    Writeln('not reached');
  end;
  GetMem(P, 10);
  ReallocMem(P, 1000);
  FreeMem(P);
  P := AllocMem(20);
  FreeMem(P);
  New(R);
  Dispose(R);
  ManagedValues;
  GetMem(P, 0);
  FreeMem(P);
  GetMemoryManager(Back);
  SetMemoryManager(Old);
  Writeln('set while mine ', SetWhile);
  Writeln('live blocks after ', _LiveAllocCount - Baseline);
  Writeln('Get returned mine ', Assigned(Back.GetMem) and (Back.GetMem = Mine.GetMem) and (Back.FreeMem = Mine.FreeMem));
  Writeln('set after restore ', IsMemoryManagerSet);
  Writeln('gets ', Gets, ', frees ', Frees, ', reallocs ', Reallocs, ', allocs ', Allocs);
end.
