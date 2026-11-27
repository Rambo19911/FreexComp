{ context: expression }
program ExpressionArgument;
var
  Name: string;
function Greeting: string; begin Result := 'hi' end;
begin
  Writeln({|});
end.
{#has Name, Greeting, nil}
{#kind Greeting=Routine, Name=Variable}
{#not repeat}
