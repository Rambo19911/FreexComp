{ context: directive }
program DirectiveInsideBody;
var
  Total: Integer;
begin
  {${|}}
  Total := 1;
end.
{#has IFDEF, WARNINGS, HINTS, X}
{#kind X=Directive}
{#not Total}
