// covers: LANG-210, LANG-355, LANG-361
program EnumCaseFor;
type
  TDay = (Mon, Tue, Wed, Thu, Fri, Sat, Sun);
  TWork = Mon..Fri;
var
  D: TDay;
  W: TWork;
  Count: Integer;

procedure Describe(D: TDay);
begin
  case D of
    Mon: Writeln('start');
    Tue..Thu: Writeln('middle');
    Fri: Writeln('almost');
  else
    Writeln('weekend');
  end;
end;

begin
  for D := Mon to Sun do
    Describe(D);
  Count := 0;
  for D := Sun downto Fri do
    Inc(Count);
  Writeln(Count);
  for D := Low(TDay) to High(TDay) do
    Write(Ord(D));
  Writeln;
  for W := Low(TWork) to High(TWork) do
    Write(Ord(W));
  Writeln;
  for D := Wed to Tue do
    Writeln('never');
  W := Fri;
  case W of
    Mon..Thu: Writeln('work');
    Fri: Writeln('friday');
  end;
end.
