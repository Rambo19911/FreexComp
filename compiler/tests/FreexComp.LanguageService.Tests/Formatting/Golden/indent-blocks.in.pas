program IndentBlocks;
var
I, J: Integer;
begin
I := 0;
begin
J := 1;
  if I = 0 then
begin
      J := 2;
    end;
end;
try
I := 1;
finally
J := 2;
end;
try
    I := 1;
except
on E: Exception do
Writeln('x');
end;
repeat
I := I + 1;
until I > 3;
while I > 0 do
begin
Dec(I);
end;
end.
