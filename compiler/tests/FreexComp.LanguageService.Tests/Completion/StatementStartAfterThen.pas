{ context: statement-start }
program StatementStartAfterThen;
var
  Ready: Boolean;
  Count: Integer;
begin
  if Ready then
    {|}
end.
{#has Count, Ready, Inc, begin, case}
{#kind Count=Variable}
