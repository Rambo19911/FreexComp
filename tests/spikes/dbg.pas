program dbg;
var g: Integer;
function Add(a, b: Integer): Integer;
begin
  Add := a + b;
end;
begin
  g := Add(2, 3);
  g := g * 2;
end.
