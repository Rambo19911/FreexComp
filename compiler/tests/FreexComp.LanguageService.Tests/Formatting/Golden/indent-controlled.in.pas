program IndentControlled;
var
I: Integer;
begin
if I > 0 then
Writeln('a')
else
Writeln('b');
if I > 1 then Writeln('same line');
if I = 1 then
Writeln('one')
else
if I = 2 then
Writeln('two')
else
Writeln('many');
for I := 1 to 3 do
Writeln(I);
while I > 0 do
Dec(I);
with TObject.Create do
Free;
if I = 0 then
try
Writeln('t');
finally
Writeln('f');
end;
end.
