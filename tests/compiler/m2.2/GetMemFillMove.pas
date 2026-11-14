// covers: LANG-268, CMP-412
program GetMemFillMove;
type
  PByte = ^Byte;
  TRec = record
    A: Integer;
    B: array[0..3] of Char;
  end;
var
  P: PByte;
  I, Sum: Integer;
  R, S: TRec;
  Buf: array[0..9] of Byte;
  U: Pointer;
begin
  GetMem(P, 16);
  FillChar(P^, 16, 7);
{$POINTERMATH ON}
  Sum := 0;
  for I := 0 to 15 do
    Sum := Sum + P[I];
  Writeln(Sum);
  FillChar(P^, 8, 0);
  Sum := 0;
  for I := 0 to 15 do
    Sum := Sum + P[I];
  Writeln(Sum);
  for I := 0 to 9 do
    Buf[I] := I;
  Move(Buf, P^, 10);
  Writeln(P[0], ' ', P[9], ' ', P[10]);
  // overlapping move to the right
  Move(Buf[0], Buf[2], 6);
  for I := 0 to 9 do
    Write(Buf[I]);
  Writeln;
  ReallocMem(P, 32);
  Writeln(P[9], ' ', P[15]);
  FreeMem(P);
{$POINTERMATH OFF}
  R.A := 12345;
  R.B[0] := 'a';
  R.B[1] := 'b';
  R.B[2] := 'c';
  R.B[3] := 'd';
  FillChar(S, SizeOf(S), 0);
  Move(R, S, SizeOf(TRec));
  Writeln(S.A, ' ', S.B[0], S.B[3]);
  FillChar(S.B, SizeOf(S.B), 0);
  Writeln(Ord(S.B[1]), ' ', S.A);
  FillChar(S, SizeOf(S), True);
  Writeln(S.A);
  U := @Buf;
  FillChar(U^, 3, 255);
  Writeln(Buf[0], ' ', Buf[2], ' ', Buf[3]);
end.
