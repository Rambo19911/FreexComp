{ context: broken }
program BrokenEnclosingTypeMembers;
type
  TJob = class
    FName: string;
    procedure Run;
    procedure Report;
  end;
procedure TJob.Report; begin end;
procedure TJob.Run;
var
  Attempt: Integer;
begin
  Attempt := := 2;
  if Attempt > then
    Writeln(;
  {|}
end;
begin
end.
{#has Attempt, Self, FName, Report, Run}
{#top Attempt}
