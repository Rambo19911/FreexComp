{ context: locals }
program LocalsInRoutine;
var
  Global: Integer;
procedure Work;
var
  Local: Integer;
  Name: string;
begin
  {|}
end;
begin
end.
{#has Local, Name, Global, Work}
{#kind Local=Variable}
{#top Local}
