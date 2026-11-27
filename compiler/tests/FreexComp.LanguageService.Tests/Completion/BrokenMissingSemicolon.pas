{ context: broken }
program BrokenMissingSemicolon;
var
  Global: Integer;
procedure Work(Param: Integer);
var
  Local: Integer;
begin
  Local := Param
  {|}
end;
begin
end.
{#has Local, Param, Global}
