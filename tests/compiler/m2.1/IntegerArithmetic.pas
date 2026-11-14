// covers: LANG-221, LANG-225, LANG-300
program IntegerArithmetic;
var
  A, B: Integer;
begin
  A := 17;
  B := 5;
  Writeln(A + B, ' ', A - B, ' ', A * B, ' ', A div B, ' ', A mod B);
  A := -17;
  Writeln(A div B, ' ', A mod B);
  B := -5;
  Writeln(A div B, ' ', A mod B);
  A := 17;
  Writeln(A div B, ' ', A mod B);
  Writeln(B - A, ' ', B * B, ' ', -A, ' ', -(-A), ' ', +A);
  Writeln(-17 div 5, ' ', -17 mod 5, ' ', 17 mod -5, ' ', 17 div -5);
  Writeln(7 - 10, ' ', 0 * B, ' ', 0 div B, ' ', 4 mod 7);
  A := 1;
  B := 3;
  Writeln(A div B, ' ', A mod B, ' ', (-A) div B, ' ', (-A) mod B);
end.
