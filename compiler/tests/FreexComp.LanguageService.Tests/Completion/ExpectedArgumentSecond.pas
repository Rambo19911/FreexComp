{ context: expected-type }
program ExpectedArgumentSecond;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
  Current: TColor;
procedure Fill(Width: Integer; Color: TColor);
begin
end;
begin
  Fill(1, {|});
end.
{#ranked Red, Green, Current}
{#unranked Alpha}
