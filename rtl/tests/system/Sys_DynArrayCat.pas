// covers: ABI-046, ABI-047, RTL-088
// phase: 8
// opc-run: variants 1 2 3
program Sys_DynArrayCat;
{$POINTERMATH ON}
{ Variant 1: _DynArrayCat (the helper behind A + B + ... and Concat; called directly until the compiler lowers them,
  C-390): elements in source order, the sources unchanged, a unique exact result; managed elements gain one reference
  per copy and lose it on release; Dest aliasing a source (A := A + A, A := B + A); an empty total gives nil and
  releases the old value; a single source gives a copy; the in-place append of a unique first source grows the block
  by max(NewLength, Room + Room div 2, 4), records the room in the Capacity field and keeps Pointer(A) while the room
  lasts; a shared first source is not extended; SetLength resets Capacity to 0; 100 000 single-element appends take
  27 growths. Variant 2: a total length beyond High(NativeInt) is runtime error 203 before anything changes.
  Variant 3: a total that fits but whose payload does not is runtime error 203.
  The variant is the last character of the command line. }
type
  TInts = array of Integer;
  TStrs = array of string;
  TGrid = array of TInts;

  { a header without elements (RefCount -1 = static, so no helper writes it): only its Length is read }
  TFakeBlock = record
    RefCount: Integer;
    Capacity: Integer;
    Length: NativeInt;
    First: Integer;
  end;

var
  Base: NativeInt;
  Cmd: PChar;
  Fake: TFakeBlock;

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
  Writeln(' (rc ', RC(PPointer(@A)^), ' cap ', Cap(PPointer(@A)^), ')');
end;

{ A := a new array First, First + 1, ... of N elements (a procedure, so that no function-result temporary keeps a
  reference and the block is unique) }
procedure Fill(var A: TInts; N, First: Integer);
var
  I: Integer;
begin
  A := nil;
  SetLength(A, N);
  for I := 0 to N - 1 do
    A[I] := First + I;
end;

{ Dest := S0 + S1 (+ S2): the element-0 pointers are read without taking references, as compiled code passes them }
procedure Cat2(var Dest: TInts; const S0, S1: TInts);
var
  Srcs: array[0..1] of Pointer;
begin
  Srcs[0] := PPointer(@S0)^;
  Srcs[1] := PPointer(@S1)^;
  _DynArrayCat(PPointer(@Dest)^, TypeInfo(TInts), 2, @Srcs[0]);
end;

procedure Order;
var
  A, B, C, D: TInts;
  Srcs: array[0..3] of Pointer;
begin
  Fill(A, 2, 1);
  Fill(B, 1, 3);
  Fill(C, 3, 4);
  Srcs[0] := PPointer(@A)^;
  Srcs[1] := PPointer(@B)^;
  Srcs[2] := nil;
  Srcs[3] := PPointer(@C)^;
  _DynArrayCat(PPointer(@D)^, TypeInfo(TInts), 4, @Srcs[0]);
  Show('order', D);
  Show('  a', A);
  Show('  b', B);
  Show('  c', C);
  D[0] := 100;
  Writeln('  result not shared: ', A[0]);
  { Concat(A): a single source gives a copy }
  Srcs[0] := PPointer(@A)^;
  _DynArrayCat(PPointer(@D)^, TypeInfo(TInts), 1, @Srcs[0]);
  Show('one source', D);
  Writeln('  copy ', PPointer(@D)^ <> PPointer(@A)^);
  { empty total: nil, the old value released }
  Srcs[0] := nil;
  Srcs[1] := nil;
  _DynArrayCat(PPointer(@D)^, TypeInfo(TInts), 2, @Srcs[0]);
  Writeln('empty: nil ', PPointer(@D)^ = nil);
  B := nil;
  Cat2(D, B, B);
  Writeln('nil + nil: nil ', PPointer(@D)^ = nil);
  Cat2(D, B, A);
  Show('nil + a', D);
  Writeln('  fresh ', PPointer(@D)^ <> PPointer(@A)^, ' a rc ', RC(PPointer(@A)^));
end;

procedure Aliasing;
var
  A, B, Keep: TInts;
begin
  Fill(A, 2, 1);
  Keep := A;
  Cat2(A, A, A);
  Show('a + a', A);
  Show('  old a', Keep);
  Fill(A, 2, 1);
  Fill(B, 2, 8);
  Cat2(A, B, A);
  Show('b + a', A);
  Show('  b', B);
  { a unique A as first and later source is not extended in place }
  Fill(A, 4, 1);
  Cat2(A, A, A);
  Show('a + a unique', A);
end;

procedure Managed;
var
  X: string;
  S1, S2, D: TStrs;
  G, H: TGrid;
  Row: TInts;
  Srcs: array[0..1] of Pointer;
begin
  X := 'x';
  X := X + 'y';
  SetLength(S1, 2);
  S1[0] := X;
  S1[1] := X;
  SetLength(S2, 1);
  S2[0] := X;
  Writeln('strings before ', StringRefCount(X));
  Srcs[0] := PPointer(@S1)^;
  Srcs[1] := PPointer(@S2)^;
  _DynArrayCat(PPointer(@D)^, TypeInfo(TStrs), 2, @Srcs[0]);
  Writeln('  joined ', Length(D), ' ', D[0], D[2], ' refs ', StringRefCount(X));
  D := nil;
  Writeln('  released ', StringRefCount(X));
  { in place (S1 unique): the appended string gains a reference, the moved ones keep theirs }
  _DynArrayCat(PPointer(@S1)^, TypeInfo(TStrs), 2, @Srcs[0]);
  Writeln('  in place ', Length(S1), ' refs ', StringRefCount(X), ' cap ', Cap(PPointer(@S1)^));
  S1 := nil;
  S2 := nil;
  Writeln('  all released ', StringRefCount(X));

  Fill(Row, 2, 5);
  SetLength(G, 1);
  G[0] := Row;
  Srcs[0] := PPointer(@G)^;
  Srcs[1] := PPointer(@G)^;
  _DynArrayCat(PPointer(@H)^, TypeInfo(TGrid), 2, @Srcs[0]);
  Writeln('rows: ', Length(H), ' shared row ', PPointer(@H[1])^ = PPointer(@Row)^, ' rc ', RC(PPointer(@Row)^));
  H := nil;
  G := nil;
  Writeln('  released rc ', RC(PPointer(@Row)^));
end;

procedure InPlace;
var
  A, B, C, Other: TInts;
  P: Pointer;
  Srcs: array[0..2] of Pointer;
begin
  Fill(A, 2, 1);
  Fill(B, 1, 3);
  Show('exact', A);
  Cat2(A, A, B);
  Show('grown', A);
  P := PPointer(@A)^;
  B[0] := 4;
  Cat2(A, A, B);
  Show('in room', A);
  Writeln('  same block ', PPointer(@A)^ = P);
  B[0] := 5;
  Cat2(A, A, B);
  Show('grown again', A);
  P := PPointer(@A)^;
  B[0] := 6;
  Cat2(A, A, B);
  Writeln('  same block ', PPointer(@A)^ = P, ' cap ', Cap(P));
  { empty sources leave it as it is }
  C := nil;
  Cat2(A, A, C);
  Writeln('  + nil: same block ', PPointer(@A)^ = P, ' length ', Length(A));
  { three sources, the first unique: in place }
  Fill(C, 4, 7);
  Srcs[0] := PPointer(@A)^;
  Srcs[1] := PPointer(@B)^;
  Srcs[2] := PPointer(@C)^;
  _DynArrayCat(PPointer(@A)^, TypeInfo(TInts), 3, @Srcs[0]);
  Show('three', A);
  { shared: a new exact block, the other owner unchanged }
  Other := A;
  Cat2(A, A, B);
  Show('shared', A);
  Writeln('  other ', Length(Other), ' new block ', PPointer(@A)^ <> PPointer(@Other)^, ' other rc ',
    RC(PPointer(@Other)^));
  Other := nil;
  { SetLength resizes to the exact length: Capacity 0 }
  Cat2(A, A, B);
  Writeln('before setlength cap > 0: ', Cap(PPointer(@A)^) > 0);
  SetLength(A, Length(A) + 1);
  Show('setlength grow', A);
  Cat2(A, A, B);
  Writeln('  cap > 0: ', Cap(PPointer(@A)^) > 0);
  SetLength(A, 3);
  Show('setlength shrink', A);
end;

procedure Appends;
var
  A, B: TInts;
  I, Growths, LastCap: Integer;
  Sum: Int64;
begin
  SetLength(B, 1);
  Growths := 0;
  LastCap := 0;
  for I := 0 to 99999 do
  begin
    B[0] := I;
    Cat2(A, A, B);
    if Cap(PPointer(@A)^) <> LastCap then
    begin
      Inc(Growths);
      LastCap := Cap(PPointer(@A)^);
    end;
  end;
  Sum := 0;
  for I := 0 to High(A) do
    Sum := Sum + A[I];
  Writeln('appends ', Length(A), ' sum ', Sum, ' growths ', Growths, ' cap ', LastCap, ' rc ', RC(PPointer(@A)^));
end;

procedure Overflow(Each: NativeInt);
var
  D: TInts;
  Srcs: array[0..1] of Pointer;
begin
  Fake.RefCount := -1;
  Fake.Capacity := 0;
  Fake.Length := Each;
  Srcs[0] := @Fake.First;
  Srcs[1] := @Fake.First;
  Writeln('overflow ', Each > 0);
  _DynArrayCat(PPointer(@D)^, TypeInfo(TInts), 2, @Srcs[0]);
  Writeln('not reached');
end;

begin
  Cmd := CmdLine;
  while Cmd^ <> #0 do
    Inc(Cmd);
  Dec(Cmd);
  case Cmd^ of
    '2':
      Overflow(High(NativeInt) div 2 + 1);
    '3':
      Overflow(High(NativeInt) div 8);
  else
    Base := _LiveAllocCount;
    Order;
    Aliasing;
    Managed;
    InPlace;
    Appends;
    Writeln('leaks ', _LiveAllocCount - Base);
  end;
end.
