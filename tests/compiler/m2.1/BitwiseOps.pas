// covers: LANG-221, LANG-224, LANG-227, LANG-301
program BitwiseOps;
var
  A, B, N: Integer;
  W: Byte;
  S: SmallInt;
  C: Cardinal;
  X: Int64;
begin
  A := 12;
  B := 10;
  Writeln(A and B, ' ', A or B, ' ', A xor B, ' ', not A);
  W := 5;
  Writeln(not W);
  S := 0;
  Writeln(not S);
  Writeln(1 shl 4, ' ', 256 shr 4, ' ', 1 shl 33, ' ', $F0 and $3C, ' ', not 0);
  A := -1;
  Writeln(A shr 28, ' ', A shl 31);
  N := 33;
  Writeln(1 shl N);
  N := 32;
  B := 5;
  Writeln(B shl N, ' ', B shr N);
  X := 1;
  Writeln(X shl 40);
  N := 65;
  Writeln(X shl N);
  X := -1;
  Writeln(X shr 60);
  C := $80000000;
  Writeln(C shr 31, ' ', C shl 1, ' ', not C);
  A := $0F0F;
  B := $00FF;
  Writeln(A and not B, ' ', (A or B) xor $FFFF);
end.
