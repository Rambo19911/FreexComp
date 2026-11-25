program BlockInline;

var
  G: Integer;

begin
  G := 2;
  var Top: Integer := G * 5;
  if G > 1 then
  begin
    var Inner: string := 'in';
    WriteLn(Inner);
  end;
  if G > 0 then
  begin
    var Inner: Integer := Top + 1;
    WriteLn(Inner);
  end;
  for var I := 1 to 2 do
    WriteLn(I * Top);
  WriteLn(Top);
end.
