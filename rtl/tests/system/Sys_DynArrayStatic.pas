// covers: ABI-045, ABI-046, RTL-087
// phase: 8
program Sys_DynArrayStatic;
{$POINTERMATH ON}
{ Static dynamic-array blocks (RefCount -1, read-only memory): until typed dynamic-array constants exist (C-389) the
  blocks are built by hand in a page that is then made read-only, so any write into them faults. AddRef, Clr,
  Finalize and the counting paths leave them unchanged; a variable holding one drops it at scope end; assignment
  (_DynArrayAsg, compiled code, _CopyArray, _CopyRecord) stores a heap copy (RefCount 1, Capacity 0) whose nested
  static rows are copied too and whose string elements share the literals; Copy, SetLength (also to the same length)
  and _DynArrayCat with a static source or destination give heap blocks; the static blocks never change; nothing
  leaks. }
uses Windows;

type
  TInts = array of Integer;
  TGrid = array of TInts;
  TStrs = array of string;
  TPair = record
    N: Integer;
    A: TInts;
  end;

var
  Page: PByte;
  SInts, SRow, SGrid, SStrs: Pointer;  { element 0 of the read-only blocks }
  Base: NativeInt;

{ header fields read through the block address (rtl-abi.md section 4): RefCount -16, Capacity -12 }
function RC(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 16)^;
end;

function Cap(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 12)^;
end;

function Len(P: Pointer): NativeInt;
begin
  Result := 0;
  if P <> nil then
    Result := PNativeInt(PByte(P) - 8)^;
end;

{ the elements of an array of Integer given by its block address }
procedure Show(const Title: string; P: Pointer);
var
  I: NativeInt;
begin
  Write(Title, ':');
  for I := 0 to Len(P) - 1 do
    Write(' ', PInteger(P)[I]);
  Writeln;
end;

{ heap block facts: unique, exact (Capacity 0), not the static block S }
procedure Heap(const Title: string; P, S: Pointer);
begin
  Writeln(Title, ': rc ', RC(P), ' cap ', Cap(P), ' len ', Len(P), ' copied ', (P <> nil) and (P <> S));
end;

function Block(Offset, Count: NativeInt): Pointer;
begin
  PInteger(Page + Offset)^ := -1;
  PInteger(Page + Offset + 4)^ := 0;
  PNativeInt(Page + Offset + 8)^ := Count;
  Result := Page + Offset + 16;
end;

procedure Build;
var
  Lit: string;
  Old: DWORD;
begin
  Page := VirtualAlloc(nil, 4096, MEM_COMMIT or MEM_RESERVE, PAGE_READWRITE);
  SInts := Block(0, 3);
  PInteger(SInts)[0] := 10;
  PInteger(SInts)[1] := 20;
  PInteger(SInts)[2] := 30;
  SRow := Block(64, 2);
  PInteger(SRow)[0] := 7;
  PInteger(SRow)[1] := 8;
  SGrid := Block(128, 2);
  PPointer(SGrid)[0] := SInts;
  PPointer(SGrid)[1] := SRow;
  SStrs := Block(192, 2);
  Lit := 'alpha';
  PPointer(SStrs)[0] := Pointer(Lit);
  Lit := 'beta';
  PPointer(SStrs)[1] := Pointer(Lit);
  Writeln('read-only ', VirtualProtect(Page, 4096, PAGE_READONLY, Old));
end;

procedure Statics(const Title: string);
begin
  Writeln(Title, ': rc ', RC(SInts), ' ', RC(SRow), ' ', RC(SGrid), ' ', RC(SStrs), ' cap ', Cap(SInts), ' len ',
    Len(SInts), ' ', Len(SGrid));
  Show('  ints', SInts);
  Show('  row', SRow);
  Writeln('  grid rows ', PPointer(SGrid)[0] = SInts, ' ', PPointer(SGrid)[1] = SRow, ' strs ',
    PString(SStrs)[0], ' ', PString(SStrs)[1]);
end;

{ a local variable holding a static block: read through it, dropped (not released) at scope end }
procedure Holds;
var
  B: TInts;
begin
  PPointer(@B)^ := SInts;
  Writeln('local holds: ', Length(B), ' ', B[1], ' ', High(B));
end;

{ a value parameter bound to a static block: counted on entry and released on exit, both leave it }
procedure TakeValue(B: TInts);
begin
  Writeln('value param: ', Length(B), ' ', B[2]);
end;

procedure CountingPaths;
var
  P: Pointer;
  Pair: array[0..1] of Pointer;
  B: TInts;
begin
  _DynArrayAddRef(SInts);
  _AddRef(@SInts, TypeInfo(TInts));
  _AddRefArray(SGrid, TypeInfo(TInts), 2);
  Writeln('addref: ', RC(SInts), ' ', RC(SRow));
  P := SInts;
  _DynArrayClr(P, TypeInfo(TInts));
  Writeln('clr: nil ', P = nil, ' rc ', RC(SInts));
  P := SInts;
  _Finalize(@P, TypeInfo(TInts));
  Pair[0] := SInts;
  Pair[1] := SRow;
  _FinalizeArray(@Pair, TypeInfo(TInts), 2);
  Writeln('finalize: nil ', P = nil, ' ', Pair[0] = nil, ' ', Pair[1] = nil, ' rc ', RC(SInts), ' ', RC(SRow));
  Holds;
  PPointer(@B)^ := SInts;
  TakeValue(B);
  Writeln('after value param: ', PPointer(@B)^ = SInts, ' rc ', RC(SInts));
end;

procedure Assignment;
var
  A, B: TInts;
  T, U: TStrs;
  G, H: TGrid;
  P: Pointer;
  Dst: array[0..1] of Pointer;
begin
  { compiled assignment from a variable that holds a static block }
  PPointer(@B)^ := SInts;
  A := B;
  Heap('asg', PPointer(@A)^, SInts);
  A[0] := 99;
  Show('  copy written', PPointer(@A)^);
  Show('  static', PPointer(@B)^);
  { the helper itself }
  P := nil;
  _DynArrayAsg(P, SRow, TypeInfo(TInts));
  Heap('_DynArrayAsg', P, SRow);
  Show('  copy', P);
  _DynArrayClr(P, TypeInfo(TInts));

  { string elements share the literals; the copy is writable }
  PPointer(@T)^ := SStrs;
  U := T;
  Heap('strs', PPointer(@U)^, SStrs);
  Writeln('  ', U[0], ' ', U[1], ' literal shared ', PPointer(@U[0])^ = PPointer(SStrs)[0], ' ', StringRefCount(U[0]));
  U[1] := U[1] + '!';
  Writeln('  written ', U[1], ' static ', T[1]);

  { nested: every reachable block of the copy is a heap block }
  PPointer(@G)^ := SGrid;
  H := G;
  Heap('nested', PPointer(@H)^, SGrid);
  Heap('  row 0', PPointer(@H[0])^, SInts);
  Heap('  row 1', PPointer(@H[1])^, SRow);
  H[0][0] := 5;
  H[1][1] := 6;
  Show('  row 0 written', PPointer(@H[0])^);
  Show('  row 1 written', PPointer(@H[1])^);

  { _CopyArray of dynamic-array values (each element a static block) }
  Dst[0] := nil;
  Dst[1] := nil;
  _CopyArray(@Dst, SGrid, TypeInfo(TInts), 2);
  Heap('_CopyArray 0', Dst[0], SInts);
  Heap('_CopyArray 1', Dst[1], SRow);
  _FinalizeArray(@Dst, TypeInfo(TInts), 2);
end;

procedure Records;
var
  R1, R2, R3: TPair;
begin
  R1.N := 4;
  PPointer(@R1.A)^ := SInts;
  R2 := R1;
  Heap('record', PPointer(@R2.A)^, SInts);
  R2.A[2] := 33;
  Show('  written', PPointer(@R2.A)^);
  _CopyRecord(@R3, @R1, TypeInfo(TPair));
  Heap('_CopyRecord', PPointer(@R3.A)^, SInts);
  Writeln('  n ', R3.N, ' source keeps static ', PPointer(@R1.A)^ = SInts);
end;

procedure CopyAndSetLength;
var
  B: TInts;
  G, H: TGrid;
begin
  PPointer(@G)^ := SGrid;
  H := Copy(G);
  Heap('copy', PPointer(@H)^, SGrid);
  Heap('  row 0', PPointer(@H[0])^, SInts);
  H[0][1] := 21;
  Show('  row 0 written', PPointer(@H[0])^);
  B := Copy(G[0], 1, 5);
  Heap('copy part', PPointer(@B)^, SInts);
  Show('  part', PPointer(@B)^);

  B := nil;
  PPointer(@B)^ := SInts;
  SetLength(B, 3);
  Heap('setlength same', PPointer(@B)^, SInts);
  B[0] := -1;
  Show('  written', PPointer(@B)^);
  B := nil;
  PPointer(@B)^ := SInts;
  SetLength(B, 5);
  Heap('setlength grow', PPointer(@B)^, SInts);
  Show('  grown', PPointer(@B)^);
  B := nil;
  PPointer(@B)^ := SInts;
  SetLength(B, 1);
  Heap('setlength shrink', PPointer(@B)^, SInts);
  Show('  shrunk', PPointer(@B)^);
  B := nil;
  PPointer(@B)^ := SInts;
  SetLength(B, 0);
  Writeln('setlength 0: nil ', PPointer(@B)^ = nil);

  G := nil;
  PPointer(@G)^ := SGrid;
  SetLength(G, 3);
  Heap('setlength nested', PPointer(@G)^, SGrid);
  Heap('  row 0', PPointer(@G[0])^, SInts);
  G[0][0] := 1;
  Writeln('  rows ', Length(G[0]), ' ', Length(G[1]), ' nil ', PPointer(@G[2])^ = nil, ' ', G[0][0]);
  G := nil;
  PPointer(@G)^ := SGrid;
  SetLength(G, 2, 4);
  Heap('setlength 2 dims', PPointer(@G)^, SGrid);
  Heap('  row 1', PPointer(@G[1])^, SRow);
  Show('  row 1', PPointer(@G[1])^);
end;

procedure Concatenation;
var
  A, B: TInts;
  G: TGrid;
  Srcs: array[0..1] of Pointer;
begin
  Srcs[0] := SInts;
  Srcs[1] := SRow;
  _DynArrayCat(PPointer(@A)^, TypeInfo(TInts), 2, @Srcs[0]);
  Heap('cat', PPointer(@A)^, SInts);
  Show('  joined', PPointer(@A)^);
  { a static destination that is also the first source is copied, never extended }
  PPointer(@B)^ := SInts;
  Srcs[0] := SInts;
  Srcs[1] := SRow;
  _DynArrayCat(PPointer(@B)^, TypeInfo(TInts), 2, @Srcs[0]);
  Heap('cat into static', PPointer(@B)^, SInts);
  Show('  joined', PPointer(@B)^);
  Srcs[0] := SGrid;
  Srcs[1] := SGrid;
  _DynArrayCat(PPointer(@G)^, TypeInfo(TGrid), 2, @Srcs[0]);
  Heap('cat nested', PPointer(@G)^, SGrid);
  Heap('  row 0', PPointer(@G[0])^, SInts);
  Heap('  row 3', PPointer(@G[3])^, SRow);
  G[3][0] := 70;
  Show('  row 3 written', PPointer(@G[3])^);
end;

begin
  Build;
  Base := _LiveAllocCount;
  Statics('static');
  CountingPaths;
  Assignment;
  Records;
  CopyAndSetLength;
  Concatenation;
  Statics('static after');
  Writeln('leaks ', _LiveAllocCount - Base);
end.
