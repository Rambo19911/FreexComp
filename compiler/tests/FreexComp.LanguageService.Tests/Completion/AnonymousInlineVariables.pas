{ context: inline-variable }
program AnonymousInlineVariables;
type
  TProc = reference to procedure;
procedure Work;
var
  Run: TProc;
begin
  var Before := 1;
  Run := procedure
    begin
      var Own := Before;
      {|}
      var NotYet := Own;
      Writeln(NotYet);
    end;
  var After := 2;
  Run();
  Writeln(After);
end;
begin
end.
{#has Before, Own, Run}
{#kind Before=Variable, Own=Variable}
{#not After, NotYet}
