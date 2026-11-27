{ context: expected-type }
program ExpectedComparison;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
  Paint: TColor;
begin
  if Paint = {|} then
    Alpha := 1;
end.
{#top Green}
{#ranked Red, Green, Paint}
{#unranked Alpha}
