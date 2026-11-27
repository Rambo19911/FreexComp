{ context: expected-type }
program ExpectedExit;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
function Pick(Flag: Boolean): TColor;
begin
  if Flag then
    Exit({|});
  Result := Red;
end;
begin
end.
{#top Result}
{#ranked Red, Green, Result}
{#unranked Alpha, Flag}
