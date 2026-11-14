// covers: LANG-225
program DivModByMinusOne;
var
  A, B: Integer;
  X, Y: Int64;
begin
  A := Low(Integer);
  B := -1;
  Writeln(A div B);
  Writeln(A mod B);
  Writeln(A div -1, ' ', A mod -1);
  X := Low(Int64);
  Y := -1;
  Writeln(X div Y);
  Writeln(X mod Y);
  Writeln(X div -1, ' ', X mod -1);
  A := 7;
  Writeln(A div B, ' ', A mod B);
  B := 1;
  Writeln(Low(Integer) div B, ' ', Low(Integer) mod B);
end.
