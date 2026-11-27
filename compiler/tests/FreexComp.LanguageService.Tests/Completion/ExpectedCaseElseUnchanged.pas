{ context: expected-type }
program ExpectedCaseElseUnchanged;
type
  TColor = (Red, Green, Blue);
var
  Alpha: Integer;
  Paint: TColor;
begin
  case Paint of
    Red: Alpha := 1;
  else
    Alpha := 2;
    {|}
  end;
end.
{#unranked Green, Blue, Alpha}
