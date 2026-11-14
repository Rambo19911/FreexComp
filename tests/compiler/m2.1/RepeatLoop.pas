// covers: LANG-360
program RepeatLoop;
var
  I, Sum: Integer;
begin
  I := 0;
  repeat
    Writeln('body runs once, I=', I);
  until True;
  I := 10;
  repeat
    I := I + 1;
  until I > 5;
  Writeln(I);
  I := 0;
  Sum := 0;
  repeat
    I := I + 1;
    Sum := Sum + I * I;
  until I = 4;
  Writeln(Sum);
  I := 3;
  repeat
    Write(I, ' ');
    I := I - 1
  until I < 0;
  Writeln('|');
  I := 1;
  repeat
    I := I * 3;
    if I > 50 then
      Writeln('big ', I)
  until I > 100;
end.
