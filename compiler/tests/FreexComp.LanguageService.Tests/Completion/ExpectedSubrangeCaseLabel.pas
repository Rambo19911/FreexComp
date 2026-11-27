{ context: expected-type }
program ExpectedSubrangeCaseLabel;
type
  TDay = (Mon, Tue, Wed, Thu, Fri, Sat, Sun);
  TWeekday = Mon..Fri;
const
  Midweek = Wed;
  Rest = Sun;
var
  Alpha: Integer;
  Work: TWeekday;
begin
  case Work of
    {|}
  end;
end.
{#top Fri}
{#ranked Mon, Tue, Wed, Thu, Fri, Midweek}
{#unranked Sat, Sun, Rest, Alpha}
