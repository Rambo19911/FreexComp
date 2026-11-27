{ context: inline-variable }
program InlineVariableInScope;
begin
  var Count := 10;
  var Name: string := 'x';
  {|}
end.
{#has Count, Name}
{#kind Count=Variable}
