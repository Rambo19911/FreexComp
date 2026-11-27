{ context: expression }
program ExpressionCondition;
var
  Done: Boolean;
begin
  while not {|} do
    Done := True;
end.
{#has Done, True, False}
{#not until, begin}
