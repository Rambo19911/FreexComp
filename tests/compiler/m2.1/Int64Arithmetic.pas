// covers: LANG-221, LANG-222, LANG-223, LANG-225, LANG-290
program Int64Arithmetic;
var
  A, B: Int64;
  I: Integer;
begin
  A := 3000000000;
  Writeln(A);
  A := A * 3;
  Writeln(A);
  I := 2000000000;
  A := I + I;
  Writeln(A);
  A := Int64(I) + I;
  Writeln(A);
  A := 10;
  Writeln(A + I);
  Writeln(I * 3, ' ', Int64(I) * 3);
  Writeln(High(Int64) div 2, ' ', Low(Int64) div 2);
  A := -9000000000;
  Writeln(A div 7, ' ', A mod 7);
  B := 4294967296;
  Writeln(A div B, ' ', A mod B, ' ', B * B div 2);
  I := -3;
  Writeln(B * I, ' ', B - I, ' ', I - B);
  A := 1;
  B := 0;
  while B < 62 do
  begin
    A := A * 2;
    B := B + 1;
  end;
  Writeln(A, ' ', A + (A - 1) * 2 + 1);
end.
