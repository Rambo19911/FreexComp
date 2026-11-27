{ context: expected-type }
program ExpectedResult;
type
  TColor = (Red, Green);
var
  Alpha: Integer;
function Pick: TColor;
begin
  Result := {|}
end;
begin
end.
{#ranked Red, Green, Result, Pick}
{#unranked Alpha}
