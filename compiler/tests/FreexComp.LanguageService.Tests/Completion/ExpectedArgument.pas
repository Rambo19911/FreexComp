{ context: expected-type }
program ExpectedArgument;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
  Current: TColor;
procedure Fill(Color: TColor; Width: Integer);
begin
end;
begin
  Fill({|});
end.
{#top Current}
{#ranked Red, Green, Current}
{#unranked Alpha, Fill, nil}
