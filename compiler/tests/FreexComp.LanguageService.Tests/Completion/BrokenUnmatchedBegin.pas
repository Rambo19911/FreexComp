{ context: broken }
program BrokenUnmatchedBegin;
var
  Global: Integer;
procedure Work(Param: Integer);
var
  Local: Integer;
begin
  if Param > 0 then
  begin
    Local := 1;
  {|}
end;
begin
end.
{#has Local, Param, Global, Work}
