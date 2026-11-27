{ context: inline-variable }
program InlineVariableForLoop;
procedure Work;
begin
  for var Index := 1 to 3 do
    Writeln({|});
end;
begin
end.
{#has Index}
{#kind Index=Variable}
{#top Index}
