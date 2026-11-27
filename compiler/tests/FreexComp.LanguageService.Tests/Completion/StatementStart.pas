{ context: statement-start }
program StatementStart;
var
  Counter: Integer;
begin
  {|}
end.
{#has Counter, Writeln, while, repeat, try, raise, tryfinally, forin, ifelse}
{#kind Counter=Variable, tryfinally=Snippet, ifelse=Snippet, try=Keyword, raise=Keyword}
{#not nil, private}
