{ context: inline-variable }
program InlineVariableOutOfScope;
procedure Work;
begin
  begin
    var Temp := 1;
    Writeln(Temp);
  end;
  {|}
end;
begin
end.
{#has Work, Writeln}
{#not Temp}
