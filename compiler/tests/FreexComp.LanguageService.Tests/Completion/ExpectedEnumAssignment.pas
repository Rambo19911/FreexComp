{ context: expected-type }
program ExpectedEnumAssignment;
type
  TColor = (Red, Green);
const
  Favourite = Green;
  Limit = 10;
var
  Alpha: Integer;
  Paint: TColor;
function Pick: TColor;
begin
  Result := Red;
end;
procedure Reset;
begin
end;
begin
  Paint := {|}
end.
{#top Favourite}
{#ranked Red, Green, Paint, Favourite, Pick}
{#unranked Alpha, Limit, Reset, TColor, nil, True, Writeln}
