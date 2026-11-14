// covers: LANG-300, LANG-301
program IntegerPrecedence;
var
  A, B, C: Integer;
begin
  Writeln(2 + 3 * 4, ' ', (2 + 3) * 4, ' ', 10 - 4 - 3, ' ', 10 - (4 - 3));
  Writeln(100 div 10 div 5, ' ', 100 div (10 div 5), ' ', 2 * 3 mod 4, ' ', 2 * (3 mod 4));
  Writeln(-2 * 3, ' ', -(2 + 3) * 4, ' ', 1 + 2 shl 3, ' ', (1 + 2) shl 3);
  Writeln(5 and 3 + 1, ' ', 5 and (3 + 1), ' ', 5 or 2 * 3, ' ', (5 or 2) * 3);
  A := 2;
  B := 3;
  C := 4;
  Writeln(A + B * C, ' ', (A + B) * C, ' ', A * B + C, ' ', A * (B + C));
  Writeln(C - B - A, ' ', C - (B - A), ' ', C * C div B, ' ', C * (C div B));
  Writeln(A + B * C - C div A mod B, ' ', ((A + B) * (C - A)) div (B - A));
  Writeln(-A * B, ' ', -(A * B) + C, ' ', A shl B + C, ' ', A shl (B + C));
end.
