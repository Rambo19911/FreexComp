// covers: LANG-225
program DivByZero;
var
  A, B: Integer;
begin
  A := 10;
  B := 2;
  Writeln(A div B);
  B := B - 2;
  Writeln(A div B);
  Writeln('never');
end.
