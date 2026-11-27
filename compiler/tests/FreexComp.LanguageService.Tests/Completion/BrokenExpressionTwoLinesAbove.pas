{ context: broken }
program BrokenExpressionTwoLinesAbove;
var
  Global: Integer;
procedure Work(Param: Integer);
var
  Local: Integer;
begin
  Local := (Param + ;
  Writeln(Local);
  {|}
end;
begin
end.
{#has Local, Param, Global, Work, Writeln}
{#top Local}
