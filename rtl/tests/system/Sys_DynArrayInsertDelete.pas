// covers: ABI-048, ABI-049, RTL-084, RTL-089
// phase: 8
// opc-run: variants 1 2 3
program Sys_DynArrayInsertDelete;
{$POINTERMATH ON}
{ Variant 1: _DynArrayInsert / _DynArrayDelete, called directly until the compiler lowers Insert and Delete (C-391):
  start, middle, end, Index and Count clipped, Count <= 0 and out-of-range no-ops; growth by the append rule and the
  room kept by Delete and reused by Insert; delete to empty gives nil; the source inside the array (Insert(A, A, 1),
  across Index, in the part that moves up); interface and string elements counted; a shared array is copied first
  (also for a no-op); a static (read-only) target or source gives heap blocks with nested static rows copied; no
  leaks. Variants 2, 3: Length + Count beyond High(NativeInt) / a payload too large is runtime error 203 before
  anything changes. The variant is the last command-line character. }
uses Windows;

type
  TInts = array of Integer;
  TStrs = array of string;
  TGrid = array of TInts;
  TIntfs = array of IInterface;

  TCounted = class(TInterfacedObject)
    destructor Destroy; override;
  end;

var
  Base: NativeInt;
  Cmd: PChar;
  Freed: Integer;
  Page: PByte;
  SInts, SRow, SGrid: Pointer;  { element 0 of read-only (static) blocks }
  Fake: array[0..2] of NativeInt;  { a static header (RefCount -1) without elements: only Length is read }

destructor TCounted.Destroy;
begin
  Inc(Freed);
  inherited Destroy;
end;

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

procedure Show(const Title: string; const A: TInts);
var
  I: Integer;
begin
  Write(Title, ':');
  for I := 0 to High(A) do
    Write(' ', A[I]);
  Writeln(' (rc ', RC(PPointer(@A)^), ' cap ', Cap(PPointer(@A)^), ' nil ', PPointer(@A)^ = nil, ')');
end;

procedure Fill(var A: TInts; N, First: Integer);
var
  I: Integer;
begin
  A := nil;
  SetLength(A, N);
  for I := 0 to N - 1 do
    A[I] := First + I;
end;

procedure Ins(var A: TInts; Index: NativeInt; Src: Pointer; Count: NativeInt);
begin
  _DynArrayInsert(PPointer(@A)^, TypeInfo(TInts), Index, Src, Count);
end;

procedure Del(var A: TInts; Index, Count: NativeInt);
begin
  _DynArrayDelete(PPointer(@A)^, TypeInfo(TInts), Index, Count);
end;

procedure Positions;
var
  A, B: TInts;
  X: array[0..1] of Integer;
  P: Pointer;
begin
  X[0] := 100;
  X[1] := 101;
  Fill(A, 3, 1);
  Ins(A, 0, @X[0], 1);
  Show('insert start', A);
  Ins(A, 2, @X[0], 2);
  Show('insert middle', A);
  Ins(A, Length(A), @X[1], 1);
  Show('insert end', A);
  P := PPointer(@A)^;
  Ins(A, 99, @X[0], 1);
  Show('insert past end', A);
  Ins(A, -5, @X[1], 1);
  Show('insert before start', A);
  Ins(A, 1, @X[0], 0);
  Ins(A, 1, @X[0], -3);
  Writeln('  in room, count <= 0: same block ', PPointer(@A)^ = P, ' length ', Length(A));
  B := nil;
  Ins(B, 0, nil, 0);
  Writeln('nil, nothing: nil ', PPointer(@B)^ = nil);
  Ins(B, 5, @X[0], 2);
  Show('into nil', B);
  Fill(A, 6, 1);
  Del(A, 0, 1);
  Show('delete start', A);
  Del(A, 1, 2);
  Show('delete middle', A);
  Del(A, 2, 99);
  Show('delete end clipped', A);
  P := PPointer(@A)^;
  Del(A, 2, 1);
  Del(A, -1, 1);
  Del(A, 0, 0);
  Del(A, 0, -1);
  Writeln('  no-ops: same block ', PPointer(@A)^ = P);
  Show('  unchanged', A);
  Del(A, 0, 2);
  Show('delete all', A);
  Del(A, 0, 1);
  Show('delete from nil', A);
  { the room left by Delete is used by Insert }
  Fill(A, 4, 1);
  Del(A, 1, 2);
  P := PPointer(@A)^;
  Ins(A, 1, @X[0], 2);
  Show('room reused', A);
  Writeln('  same block ', PPointer(@A)^ = P);
end;

procedure Overlap;
var
  A: TInts;
  S: TStrs;
  X: string;
begin
  Fill(A, 3, 1);
  Ins(A, 1, PPointer(@A)^, Length(A));
  Show('insert(a, a, 1)', A);
  Fill(A, 4, 1);
  Ins(A, 2, @A[1], 2);
  Show('source across index', A);
  Fill(A, 4, 1);
  Del(A, 3, 1);
  Ins(A, 0, @A[1], 1);
  Show('source in the moved part', A);
  X := 'p';
  X := X + 'q';
  SetLength(S, 2);
  S[0] := X;
  S[1] := X;
  _DynArrayInsert(PPointer(@S)^, TypeInfo(TStrs), 1, PPointer(@S)^, 2);
  Writeln('strings into themselves: ', Length(S), ' ', S[0], S[3], ' refs ', StringRefCount(X));
  _DynArrayDelete(PPointer(@S)^, TypeInfo(TStrs), 0, 3);
  Writeln('  deleted 3: ', Length(S), ' refs ', StringRefCount(X));
  S := nil;
  Writeln('  released ', StringRefCount(X));
end;

procedure Interfaces;
var
  O1, O2: TCounted;
  I1, I2: IInterface;
  A: TIntfs;
begin
  O1 := TCounted.Create;
  I1 := O1;
  O2 := TCounted.Create;
  I2 := O2;
  SetLength(A, 2);
  A[0] := I1;
  A[1] := I2;
  Writeln('interfaces: freed ', Freed, ' rc ', O1.RefCount, ' ', O2.RefCount);
  _DynArrayInsert(PPointer(@A)^, TypeInfo(TIntfs), 1, @I1, 1);
  Writeln('  element inserted: ', Length(A), ' rc ', O1.RefCount, ' ', O2.RefCount, ' ', A[1] = I1, A[2] = I2);
  _DynArrayInsert(PPointer(@A)^, TypeInfo(TIntfs), 0, PPointer(@A)^, Length(A));
  Writeln('  insert(a, a, 0): ', Length(A), ' rc ', O1.RefCount, ' ', O2.RefCount);
  _DynArrayDelete(PPointer(@A)^, TypeInfo(TIntfs), 0, 3);
  Writeln('  deleted 3: ', Length(A), ' rc ', O1.RefCount, ' ', O2.RefCount, ' first ', A[0] = I1);
  _DynArrayDelete(PPointer(@A)^, TypeInfo(TIntfs), 1, 99);
  Writeln('  deleted to 1: ', Length(A), ' rc ', O1.RefCount, ' ', O2.RefCount);
  _DynArrayDelete(PPointer(@A)^, TypeInfo(TIntfs), 0, 1);
  Writeln('  deleted all: nil ', PPointer(@A)^ = nil, ' rc ', O1.RefCount, ' ', O2.RefCount);
  I1 := nil;
  I2 := nil;
  Writeln('  released: freed ', Freed);
end;

procedure Shared;
var
  A, B: TInts;
  X: Integer;
begin
  X := 9;
  Fill(A, 3, 1);
  B := A;
  Ins(A, 0, @X, 1);
  Show('shared insert', A);
  Show('  other', B);
  B := A;
  Del(A, 0, 1);
  Show('shared delete', A);
  Show('  other', B);
  B := A;
  Del(A, 99, 1);
  Writeln('shared no-op delete copies: ', PPointer(@A)^ <> PPointer(@B)^, ' rc ', RC(PPointer(@A)^), RC(PPointer(@B)^));
  A[0] := 7;
  Writeln('  other unchanged ', B[0]);
  B := A;
  Ins(A, 0, nil, 0);
  Writeln('shared no-op insert copies: ', PPointer(@A)^ <> PPointer(@B)^);
  B := A;
  Ins(A, 1, PPointer(@B)^, Length(B));
  Show('shared, source the old block', A);
  Show('  other', B);
end;

function Block(Offset, Count: NativeInt): Pointer;
begin
  PInteger(Page + Offset)^ := -1;
  PInteger(Page + Offset + 4)^ := 0;
  PNativeInt(Page + Offset + 8)^ := Count;
  Result := Page + Offset + 16;
end;

procedure Statics;
var
  A: TInts;
  G: TGrid;
  Old: DWORD;
  X: Integer;
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
  Writeln('read-only ', VirtualProtect(Page, 4096, PAGE_READONLY, Old));
  X := 5;
  PPointer(@A)^ := SInts;
  Ins(A, 1, @X, 1);
  Show('static insert', A);
  A := nil;
  PPointer(@A)^ := SInts;
  Del(A, 0, 1);
  Show('static delete', A);
  A := nil;
  PPointer(@A)^ := SInts;
  Del(A, 5, 1);
  Show('static no-op delete', A);
  Writeln('  copied ', PPointer(@A)^ <> SInts);
  A := nil;
  PPointer(@A)^ := SInts;
  Del(A, 0, 3);
  Show('static delete all', A);
  Ins(A, 0, SInts, 3);
  Show('static source', A);
  A := nil;
  _DynArrayInsert(PPointer(@G)^, TypeInfo(TGrid), 0, SGrid, 2);
  Writeln('rows: ', Length(G), ' copied ', PPointer(@G[0])^ <> SInts, PPointer(@G[1])^ <> SRow, RC(PPointer(@G[0])^));
  G[0][0] := 11;
  Show('  row written', G[0]);
  G := nil;
  PPointer(@G)^ := SGrid;
  _DynArrayDelete(PPointer(@G)^, TypeInfo(TGrid), 0, 1);
  Writeln('static grid delete: ', Length(G), ' rc ', RC(PPointer(@G)^), ' row copied ', PPointer(@G[0])^ <> SRow);
  G[0][1] := 12;
  Show('  row written', G[0]);
  G := nil;
  Writeln('statics unchanged: rc ', RC(SInts), ' ', RC(SRow), ' ', RC(SGrid), ' ', PInteger(SInts)[0], ' ',
    PInteger(SRow)[1], ' ', PPointer(SGrid)[0] = SInts);
end;

procedure Overflow(Len, Count: NativeInt);
var
  A: TInts;
  X: Integer;
begin
  PInteger(@Fake[0])^ := -1;
  Fake[1] := Len;
  PPointer(@A)^ := @Fake[2];
  X := 1;
  Writeln('overflow ', Len > 0);
  Ins(A, 0, @X, Count);
  Writeln('not reached');
end;

begin
  Cmd := CmdLine;
  while Cmd^ <> #0 do
    Inc(Cmd);
  Dec(Cmd);
  case Cmd^ of
    '2':
      Overflow(High(NativeInt) - 1, 5);
    '3':
      Overflow(High(NativeInt) div 8, 1);
  else
    Base := _LiveAllocCount;
    Positions;
    Overlap;
    Interfaces;
    Shared;
    Statics;
    Writeln('leaks ', _LiveAllocCount - Base);
  end;
end.
