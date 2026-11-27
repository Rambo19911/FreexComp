{ context: broken }
program P;
uses Shapes;
procedure Work;
var
  Count: Integer;
begin
  Count := Area(;
  {|}
end;
begin
end.
{#has Count, Area, TShape, Work}
