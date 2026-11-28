{ context: expected-type }
program ExpectedAnonymousExit;
type
  TColor = (Red, Green);
  TPick = reference to function(Flag: Boolean): TColor;
var
  Alpha: Integer;
function Count(Limit: Integer): Integer;
var
  Pick: TPick;
begin
  Pick := function(Flag: Boolean): TColor
    begin
      if Flag then
        Exit({|});
      Result := Red;
    end;
  Result := Limit;
end;
begin
end.
{#top Result}
{#ranked Red, Green, Result}
{#unranked Alpha, Flag, Limit}
{#kind Result=Variable}
