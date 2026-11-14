// covers: LANG-247, CMP-342
program PointerArithmetic;
type
  PInteger = ^Integer;
  TWords = array[0..4] of Word;
var
  Chars: array[0..5] of Char;
  P, Q: PChar;
  Nums: array[0..4] of Integer;
  IP, JP: PInteger;
  I: Integer;
  W: TWords;
  PW: ^TWords;
  U: Pointer;
  Bytes: array[0..3] of Byte;
begin
  Chars[0] := 'H';
  Chars[1] := 'e';
  Chars[2] := 'l';
  Chars[3] := 'l';
  Chars[4] := 'o';
  Chars[5] := #0;
  P := @Chars[0];
  Q := P;
  while Q^ <> #0 do
    Inc(Q);
  Writeln(Q - P);
  Writeln(P[1], (P + 4)^, P[0]);
  Q := P + 3;
  Writeln(Q^, ' ', (Q - 2)^, ' ', Q > P, ' ', Q < P, ' ', P <= Q);
  Dec(Q, 3);
  Writeln(Q = P);
  P[2] := 'L';
  Writeln(Chars[2]);
  Q := 1 + P;
  Writeln(Q^);
  for I := 0 to 4 do
    Nums[I] := I * 10;
  // a pointer to an array indexes the array (no pointer arithmetic needed)
  PW := @W;
  PW[1] := 7;
  Writeln(W[1]);
{$POINTERMATH ON}
  IP := @Nums[0];
  JP := IP + 4;
  Writeln(JP^, ' ', JP - IP, ' ', IP[2], ' ', (JP - 1)^);
  Inc(IP, 2);
  Writeln(IP^);
  IP[1] := 99;
  Writeln(Nums[3]);
  Dec(IP);
  Writeln(IP^, ' ', IP < JP);
  Bytes[0] := 1;
  Bytes[1] := 2;
  Bytes[2] := 3;
  Bytes[3] := 4;
  U := @Bytes[0];
  U := U + 2;
  Writeln(U - @Bytes[0]);
{$POINTERMATH OFF}
end.
