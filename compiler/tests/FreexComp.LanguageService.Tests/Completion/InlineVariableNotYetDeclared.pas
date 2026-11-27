{ context: inline-variable }
program InlineVariableNotYetDeclared;
procedure Work;
begin
  {|}
  var Later := 1;
  Writeln(Later);
end;
begin
end.
{#has Work}
{#not Later}
