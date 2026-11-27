{ context: expected-type }
program ExpectedInSet;
type
  TColor = (Red, Green);
  TColors = set of TColor;
var
  Alpha: Integer;
  Paint: TColor;
  Warm: TColors;
begin
  if Paint in {|} then
    Alpha := 1;
end.
{#top Warm}
{#ranked Warm}
{#unranked Alpha, Red, Paint}
