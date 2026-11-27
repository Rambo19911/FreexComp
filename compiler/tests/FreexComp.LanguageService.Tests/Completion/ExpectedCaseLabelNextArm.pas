{ context: expected-type }
program ExpectedCaseLabelNextArm;
type
  TColor = (Red, Green, Blue);
var
  Alpha: Integer;
  Paint: TColor;
begin
  case Paint of
    Red: Alpha := 1;
    {|}
  end;
end.
{#ranked Green, Blue}
{#unranked Alpha, Writeln}
