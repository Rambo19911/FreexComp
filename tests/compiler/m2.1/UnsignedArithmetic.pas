// covers: LANG-220, LANG-221, LANG-222, LANG-227, LANG-920
program UnsignedArithmetic;
var
  C, D: Cardinal;
  U: UInt64;
  I: Integer;
  W: Word;
  B: Byte;
begin
  C := 4000000000;
  Writeln(C);
  C := C + 294967295;
  Writeln(C);
  U := 18446744073709551615;
  Writeln(U);
  U := U div 2;
  Writeln(U);
  U := U + 1;
  Writeln(U);
  C := 3000000000;
  I := -1;
  Writeln(C + I);
  Writeln(C * 2);
  Writeln(C div 7, ' ', C mod 7);
  D := 7;
  Writeln(C div D, ' ', C mod D, ' ', D - C);
  U := 10;
  Writeln(U - 3, ' ', U * U, ' ', U div 3, ' ', U mod 3);
  Writeln(High(Cardinal) + 1);
  C := 5;
  Writeln(-C);
  W := 65535;
  B := 255;
  Writeln(W + B, ' ', W * W, ' ', B * B);
  U := 9223372036854775808;
  Writeln(U, ' ', U div 3, ' ', U mod 1000);
  C := C - 6;
  Writeln(C);
end.
