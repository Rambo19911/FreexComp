{ context: statement-start }
program StatementStartAfterSemicolon;
procedure Step;
begin
end;
var
  Total: Integer;
begin
  Total := 0;
  {|}
end.
{#has Step, Total, for, while}
