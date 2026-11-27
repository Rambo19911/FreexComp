{ context: expected-type }
program ExpectedCaseLabelSecondLabel;
type
  TColor = (Red, Green, Blue);
var
  Alpha: Integer;
  Paint: TColor;
begin
  case Paint of
    Red, {|}: Alpha := 1;
  end;
end.
{#ranked Green, Blue}
{#unranked Alpha}
