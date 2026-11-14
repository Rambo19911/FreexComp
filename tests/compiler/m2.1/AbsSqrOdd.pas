// covers: LANG-213
program AbsSqrOdd;
var
  I: Integer;
  X: Int64;
  B: Byte;
  S: ShortInt;
  W: Word;
begin
  I := -7;
  Writeln(Abs(I), ' ', Abs(-I), ' ', Abs(0), ' ', Abs(-2147483647));
  Writeln(Sqr(I), ' ', Sqr(12), ' ', Sqr(-3));
  Writeln(Odd(I), ' ', Odd(I + 1), ' ', Odd(0), ' ', Odd(-1));
  X := -5000000000;
  Writeln(Abs(X), ' ', Sqr(X div 1000), ' ', Odd(X), ' ', Odd(X + 1));
  B := 200;
  S := -100;
  Writeln(Sqr(B), ' ', Abs(S), ' ', Sqr(S), ' ', Odd(B));
  I := Low(Integer);
  Writeln(Abs(I));
  I := 65536;
  Writeln(Sqr(I));
  W := $1234;
  Writeln(Lo(W), ' ', Hi(W), ' ', Swap(W));
  I := $ABCD;
  Writeln(Lo(I), ' ', Hi(I), ' ', Swap(I));
  Writeln(Lo($1FF), ' ', Hi($FF00), ' ', Swap($00FF));
end.
