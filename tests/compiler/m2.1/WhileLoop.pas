// covers: LANG-360
program WhileLoop;
var
  I, Sum, N: Integer;
begin
  I := 1;
  Sum := 0;
  while I <= 10 do
  begin
    Sum := Sum + I;
    I := I + 1;
  end;
  Writeln(Sum, ' ', I);
  I := 5;
  while I < 5 do
  begin
    Writeln('never');
    I := I + 1;
  end;
  Writeln('zero iterations, I=', I);
  while False do
    Writeln('never either');
  N := 27;
  I := 0;
  while N <> 1 do
  begin
    if Odd(N) then
      N := 3 * N + 1
    else
      N := N div 2;
    I := I + 1;
  end;
  Writeln('collatz steps ', I);
  I := 0;
  while I < 3 do
  begin
    N := 0;
    while N < I do
    begin
      Write(I, N, ' ');
      N := N + 1;
    end;
    I := I + 1;
  end;
  Writeln('|');
end.
