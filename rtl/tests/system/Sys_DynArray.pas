// covers: RTL-080, RTL-081, RTL-082, RTL-083, RTL-085, ABI-040, ABI-041, ABI-042, ABI-044
// opc-run: variants 1 2 3
program Sys_DynArray;
{$POINTERMATH ON}
{ Variant 1: one-dimensional dynamic arrays - the block header, SetLength growing (kept elements, zero new ones),
  shrinking and copying a shared block (also at the same length), sharing without copy-on-write, Copy with clamping,
  managed elements (strings, byte strings, inner arrays) counted on copy and released on shrink and on the last
  release; nothing leaks. Variant 2: a negative length is runtime error 201. Variant 3: an index outside 0..High
  with range checking on is runtime error 201.
  The variant is the last character of the command line (ParamStr arrives with R-011). }
type
  TInts = array of Integer;
  TStrs = array of string;
  TAnsis = array of AnsiString;
  TGrid = array of TStrs;

var
  Base: NativeInt;
  Cmd: PChar;

{ the RefCount of a block (rtl-abi.md section 4), 0 for nil; passed as PPointer(@A)^, so no temporary holds a reference }
function RC(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 16)^;
end;

procedure Show(const Title: string; const X: TInts);
var
  K: Integer;
begin
  Write(Title, ' [');
  for K := 0 to High(X) do
  begin
    if K > 0 then
      Write(' ');
    Write(X[K]);
  end;
  Writeln('] ', Length(X), ' ', PPointer(@X)^ = nil);
end;

procedure Run;
var
  A, B: TInts;
  S, T: TStrs;
  G, H: TGrid;
  AA: TAnsis;
  X: string;
  N: AnsiString;
  I, Bad: Integer;
begin
  { header and growth }
  SetLength(A, 3);
  A[0] := 7;
  A[1] := 8;
  A[2] := 9;
  Writeln(Length(A), ' ', RC(PPointer(@A)^), ' ', PNativeInt(PByte(PPointer(@A)^) - 8)^, ' ', PInteger(PByte(PPointer(@A)^) - 12)^,
    ' ', NativeUInt(PPointer(@A)^) mod 16);
  SetLength(A, 1000);
  Bad := 0;
  for I := 3 to 999 do
    if A[I] <> 0 then
      Bad := Bad + 1;
  Writeln(A[0], A[1], A[2], ' ', Length(A), ' nonzero ', Bad);
  SetLength(A, 2);
  Writeln(Length(A), ' ', A[1], ' ', RC(PPointer(@A)^));

  { a shared block: SetLength copies (also at the same length), element writes are shared }
  B := A;
  Writeln(RC(PPointer(@A)^), ' ', PPointer(@A)^ = PPointer(@B)^);
  SetLength(B, 2);
  B[0] := 1;
  Writeln(A[0], ' ', B[0], ' ', RC(PPointer(@A)^), ' ', RC(PPointer(@B)^), ' ', PPointer(@A)^ <> PPointer(@B)^);
  B := A;
  B[1] := 5;
  Writeln(A[1], ' ', RC(PPointer(@A)^));
  SetLength(B, 3);
  Writeln(A[1], ' ', B[1], ' ', B[2], ' ', RC(PPointer(@A)^), ' ', RC(PPointer(@B)^));
  SetLength(B, 0);
  Writeln(PPointer(@B)^ = nil, ' ', RC(PPointer(@A)^));
  A := nil;
  Writeln(PPointer(@A)^ = nil, ' ', Length(A));

  { Copy: a new unique block; Index < 0 counts from 0 with Count unchanged; nothing past the end }
  SetLength(A, 5);
  for I := 0 to 4 do
    A[I] := I * 10;
  B := Copy(A);
  Writeln(PPointer(@B)^ <> PPointer(@A)^, ' ', RC(PPointer(@A)^), ' ', RC(PPointer(@B)^));
  Show('all', B);
  B := Copy(A, 1, 3);
  Show('1,3', B);
  B := Copy(A, 3, 100);
  Show('3,100', B);
  B := Copy(A, -2, 2);
  Show('-2,2', B);
  B := Copy(A, 5, 1);
  Show('5,1', B);
  B := Copy(A, 2, 0);
  Show('2,0', B);
  B := Copy(A, 2, -1);
  Show('2,-1', B);
  B := Copy(A, 4, MaxInt);
  Show('4,max', B);
  A := Copy(A, 1, 2);
  Show('self', A);
  B := nil;
  A := Copy(B);
  Show('nil', A);

  { string elements: new ones are '', copies add a reference, shrinking and the last release drop them }
  X := 'ab';
  X := X + 'c';
  SetLength(S, 3);
  S[0] := X;
  S[2] := X;
  Writeln(StringRefCount(X), ' ', Length(S[1]), ' ', Pointer(S[1]) = nil);
  T := S;
  SetLength(T, 3);
  Writeln(StringRefCount(X), ' ', RC(PPointer(@S)^), ' ', RC(PPointer(@T)^));
  SetLength(T, 1);
  Writeln(StringRefCount(X), ' ', T[0]);
  SetLength(T, 4);
  Writeln(StringRefCount(X), ' ', Length(T[3]), ' ', T[0]);
  T := nil;
  Writeln(StringRefCount(X));
  T := Copy(S, 2, 1);
  Writeln(StringRefCount(X), ' ', T[0]);
  T := Copy(S);
  Writeln(StringRefCount(X));
  T := nil;
  SetLength(S, 1);
  Writeln(StringRefCount(X));
  S := nil;
  Writeln(StringRefCount(X));

  { AnsiString elements }
  N := AnsiString(X);
  SetLength(AA, 2);
  AA[1] := N;
  Writeln(RC(Pointer(N)), ' ', AA[1], ' ', Length(AA[0]));
  AA := nil;
  Writeln(RC(Pointer(N)));

  { arrays of arrays: an outer copy shares the inner blocks }
  SetLength(G, 2);
  SetLength(G[0], 2);
  G[0][1] := X;
  H := G;
  SetLength(H, 3);
  Writeln(RC(PPointer(@G[0])^), ' ', StringRefCount(X), ' ', RC(PPointer(@G)^), ' ', PPointer(@H[2])^ = nil);
  H := nil;
  Writeln(RC(PPointer(@G[0])^), ' ', StringRefCount(X));
  G := nil;
  Writeln(StringRefCount(X));
end;

procedure Negative;
var
  A: TInts;
  L: Integer;
begin
  SetLength(A, 2);
  L := -1;
  Writeln('length ', L);
  SetLength(A, L);
  Writeln('not reached');
end;

procedure OutOfRange;
var
  A: TInts;
  I: Integer;
begin
  SetLength(A, 3);
  {$R+}
  I := 2;
  A[I] := 1;
  Writeln('index ', I, ' ok');
  I := -1;
  A[I] := 1;
  Writeln('not reached');
  {$R-}
end;

begin
  Cmd := CmdLine;
  while Cmd^ <> #0 do
    Inc(Cmd);
  Dec(Cmd);
  case Cmd^ of
    '2':
      Negative;
    '3':
      OutOfRange;
  else
    Base := _LiveAllocCount;
    Run;
    Writeln('leaks ', _LiveAllocCount - Base);
  end;
end.
