// covers: LANG-223, LANG-220, LANG-280
program IntegerWraparound;
{$Q-}
{$R-}
var
  I: Integer;
  B: Byte;
  C: Cardinal;
  S: ShortInt;
  W: Word;
  X: Int64;
  U: UInt64;
begin
  I := MaxInt;
  I := I + 1;
  Writeln(I);
  I := Low(Integer);
  I := I - 1;
  Writeln(I);
  B := 255;
  B := B + 1;
  Writeln(B);
  C := 0;
  C := C - 1;
  Writeln(C);
  S := 127;
  Inc(S);
  Writeln(S);
  W := 0;
  Dec(W);
  Writeln(W);
  X := High(Int64);
  X := X + 1;
  Writeln(X);
  U := 0;
  U := U - 1;
  Writeln(U);
  I := 65536;
  I := I * I;
  Writeln(I);
  I := 100000;
  I := I * 100000;
  Writeln(I);
  I := -2147483647;
  I := I - 2;
  Writeln(I);
  I := 300;
  B := I;
  Writeln(B);
  I := -1;
  W := I;
  Writeln(W);
end.
