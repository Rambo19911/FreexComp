// covers: RTL-081, RTL-085, ABI-043
// opc-run: variants 1 2 3
program Sys_DynArray2;
{$POINTERMATH ON}
{ Variant 1: multi-dimensional SetLength - every inner array gets the inner lengths (rectangular), zero elements, kept
  elements on resize; resizing a shared outer array copies it and makes every inner array unique, leaving the other
  owner's rows intact; an inner length 0 gives nil rows; three dimensions; managed innermost elements; nothing leaks.
  Variant 2: a negative inner length is runtime error 201 before anything changes. Variant 3: a negative outer length
  is runtime error 201.
  The variant is the last character of the command line (ParamStr arrives with R-011). }
type
  TRow = array of Integer;
  TGrid = array of TRow;
  TCube = array of array of array of Integer;
  TNames = array of array of string;

var
  Base: NativeInt;
  Cmd: PChar;

{ the RefCount of a block (rtl-abi.md section 4), 0 for nil; read as PPointer(@A)^, so no temporary holds a reference }
function RC(P: Pointer): Integer;
begin
  Result := 0;
  if P <> nil then
    Result := PInteger(PByte(P) - 16)^;
end;

procedure Show(const Title: string; const G: TGrid);
var
  I, J: Integer;
begin
  Write(Title, ':');
  for I := 0 to High(G) do
  begin
    Write(' [');
    for J := 0 to High(G[I]) do
    begin
      if J > 0 then
        Write(' ');
      Write(G[I][J]);
    end;
    Write(']');
  end;
  Writeln;
end;

procedure Run;
var
  G, H: TGrid;
  C: TCube;
  N: TNames;
  X: string;
  I, J, K, Sum: Integer;
begin
  SetLength(G, 2, 3);
  Writeln(Length(G), ' ', Length(G[0]), ' ', Length(G[1]), ' ', RC(PPointer(@G[0])^), ' ', PPointer(@G[0])^ <> PPointer(@G[1])^);
  Show('new', G);
  for I := 0 to 1 do
    for J := 0 to 2 do
      G[I][J] := I * 10 + J + 1;
  Show('filled', G);

  { a unique grid resized in place: rows keep their elements }
  SetLength(G, 3, 4);
  Show('grown', G);
  SetLength(G, 2, 2);
  Show('shrunk', G);

  { a shared grid: the outer array is copied and every row made unique; the other owner keeps its rows }
  H := G;
  SetLength(H, 2, 2);
  H[0][0] := 99;
  Show('G', G);
  Show('H', H);
  Writeln(RC(PPointer(@G)^), ' ', RC(PPointer(@H)^), ' ', RC(PPointer(@G[0])^), ' ', RC(PPointer(@H[0])^));

  { rows shared after a one-dimensional copy of the outer array, then made unique row by row }
  H := Copy(G);
  Writeln(RC(PPointer(@G[1])^), ' ', PPointer(@H[1])^ = PPointer(@G[1])^);
  SetLength(H, 3, 1);
  H[1][0] := -5;
  Show('G', G);
  Show('H', H);
  Writeln(RC(PPointer(@G[1])^), ' ', RC(PPointer(@H[1])^));

  { inner length 0 }
  SetLength(H, 2, 0);
  Writeln(Length(H), ' ', PPointer(@H[0])^ = nil, ' ', PPointer(@H[1])^ = nil);
  SetLength(H, 0, 5);
  Writeln(Length(H), ' ', PPointer(@H)^ = nil);

  { three dimensions }
  SetLength(C, 2, 3, 4);
  Sum := 0;
  for I := 0 to High(C) do
    for J := 0 to High(C[I]) do
    begin
      Sum := Sum + Length(C[I][J]) * 100;
      for K := 0 to High(C[I][J]) do
        Sum := Sum + C[I][J][K];
    end;
  Writeln(Length(C), ' ', Length(C[1]), ' ', Length(C[1][2]), ' ', Sum);
  C[1][2][3] := 7;
  SetLength(C, 2, 3, 5);
  Writeln(C[1][2][3], ' ', C[1][2][4], ' ', Length(C[0][0]));

  { managed innermost elements }
  X := 'n';
  X := X + 'm';
  SetLength(N, 2, 2);
  N[0][1] := X;
  N[1][0] := X;
  Writeln(StringRefCount(X), ' [', N[0][0], '] ', N[0][1]);
  SetLength(N, 2, 1);
  Writeln(StringRefCount(X), ' ', N[1][0]);
  N := nil;
  Writeln(StringRefCount(X));
end;

procedure NegativeInner;
var
  G: TGrid;
  L: Integer;
begin
  SetLength(G, 1, 1);
  G[0][0] := 3;
  L := -1;
  Writeln('inner ', L);
  SetLength(G, 4, L);
  Writeln('not reached');
end;

procedure NegativeOuter;
var
  G: TGrid;
  L: Integer;
begin
  L := -2;
  Writeln('outer ', L);
  SetLength(G, L, 3);
  Writeln('not reached');
end;

begin
  Cmd := CmdLine;
  while Cmd^ <> #0 do
    Inc(Cmd);
  Dec(Cmd);
  case Cmd^ of
    '2':
      NegativeInner;
    '3':
      NegativeOuter;
  else
    Base := _LiveAllocCount;
    Run;
    Writeln('leaks ', _LiveAllocCount - Base);
  end;
end.
