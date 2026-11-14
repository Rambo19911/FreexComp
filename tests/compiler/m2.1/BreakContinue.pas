// covers: LANG-363
program BreakContinue;
var
  I, J, N: Integer;
begin
  for I := 1 to 10 do
  begin
    if I = 6 then
      Break;
    if Odd(I) then
      Continue;
    Write(I, ' ');
  end;
  Writeln('| for I=', I);
  I := 0;
  while True do
  begin
    I := I + 1;
    if I mod 3 = 0 then
      Continue;
    if I > 10 then
      Break;
    Write(I, ' ');
  end;
  Writeln('| while I=', I);
  I := 0;
  N := 0;
  repeat
    I := I + 1;
    if I = 2 then
      Continue;
    N := N + I;
    if I = 5 then
      Break;
  until I >= 100;
  Writeln('repeat N=', N, ' I=', I);
  I := 0;
  repeat
    I := I + 1;
    Continue;
    Writeln('never');
  until I = 3;
  Writeln('continue reaches until, I=', I);
  for I := 1 to 3 do
  begin
    for J := 1 to 3 do
    begin
      if J = 2 then
        Continue;
      if (I = 2) and (J = 3) then
        Break;
      Write(I, J, ' ');
    end;
    if I = 3 then
      Break;
  end;
  Writeln('|');
end.
