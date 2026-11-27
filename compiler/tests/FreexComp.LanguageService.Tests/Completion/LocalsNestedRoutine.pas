{ context: locals }
program LocalsNestedRoutine;
procedure Outer(Depth: Integer);
var
  Outside: Integer;
  procedure Inner;
  var
    Inside: Integer;
  begin
    {|}
  end;
begin
  Inner;
end;
begin
end.
{#has Inside, Outside, Depth, Inner, Outer}
{#top Depth}
