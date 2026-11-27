{ context: expression }
program ExpressionInsideNotEqual;
type
  TItem = record
    Id: Integer;
  end;
var
  Alpha, Beta: Integer;
begin
  if Alpha <{|}> Beta then
    Writeln('differ');
end.
{#has Alpha, Beta, TItem, nil}
{#not Integer, array}
