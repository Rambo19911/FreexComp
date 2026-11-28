{ context: broken }
program BrokenAnonymousMissingEnd;
type
  TProc = reference to procedure;
procedure Work(Limit: Integer);
var
  Run: TProc;
  Total: Integer;
begin
  Run := procedure
    var Mine: Integer;
    begin
      Mine := Total + ;
      {|}
end;
begin
end.
{#has Mine, Total, Limit, Run}
{#kind Mine=Variable, Limit=Parameter}
