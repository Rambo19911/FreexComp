// covers: LANG-910, LANG-912
program WriteNoNewline;
var
  I: Integer;
begin
  Write('abc');
  Write(1, 2);
  Writeln('!');
  I := 0;
  while I < 3 do
  begin
    Write(I);
    Write(' ');
    I := I + 1;
  end;
  Writeln('|');
  Write('end');
  Write(Output);
  Write(Output, '.');
  Writeln(Output);
end.
