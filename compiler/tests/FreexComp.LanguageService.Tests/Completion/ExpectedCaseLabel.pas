{ context: expected-type }
program ExpectedCaseLabel;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
  Paint: TColor;
begin
  case Paint of
    {|}
  end;
end.
{#top Green}
{#ranked Red, Green}
{#unranked Alpha}
