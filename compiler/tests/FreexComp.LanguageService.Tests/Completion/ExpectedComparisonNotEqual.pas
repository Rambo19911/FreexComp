{ context: expected-type }
program ExpectedComparisonNotEqual;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
  Paint: TColor;
begin
  while Paint <> {|} do
    Alpha := 1;
end.
{#ranked Red, Green}
{#unranked Alpha}
