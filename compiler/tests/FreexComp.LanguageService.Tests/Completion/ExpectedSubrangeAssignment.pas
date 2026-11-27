{ context: expected-type }
program ExpectedSubrangeAssignment;
type
  TDay = (Mon, Tue, Wed, Thu, Fri, Sat, Sun);
  TWeekday = Mon..Fri;
const
  Midweek = Wed;
  Rest = Sun;
  Pinned: TWeekday = Tue;
  Limit = 10;
var
  Alpha: Integer;
  Today: TDay;
  Work: TWeekday;
function Pick: TDay;
begin
  Result := Mon;
end;
function Shift: TWeekday;
begin
  Result := Mon;
end;
procedure Reset;
begin
end;
begin
  Work := {|}
end.
{#top Fri}
{#ranked Mon, Tue, Wed, Thu, Fri, Work, Today, Midweek, Pinned, Pick, Shift}
{#unranked Sat, Sun, Rest, Alpha, Limit, Reset, TDay, TWeekday, nil, True, Writeln}
