{ context: expected-type }
program ExpectedAnonymousResult;
type
  TColor = (Red, Green);
  TPick = reference to function: TColor;
var
  Alpha: Integer;
function Count(Limit: Integer): Integer;
var
  Pick: TPick;
  Shade: TColor;
begin
  Shade := Green;
  Pick := function: TColor
    begin
      Result := {|}
    end;
  Result := Limit;
end;
begin
end.
{#ranked Red, Green, Result, Shade}
{#unranked Alpha, Limit}
{#kind Result=Variable}
