{ context: locals }
program AnonymousLocals;
type
  TProc = reference to procedure;
var
  Global: Integer;
procedure Work(Limit: Integer);
var
  Total: Integer;
  Run: TProc;
begin
  Total := 0;
  Run := procedure
    var Mine: Integer;
    begin
      Mine := Total + Limit;
      {|}
    end;
  Run();
end;
begin
end.
{#has Mine, Total, Limit, Run, Global, Work}
{#kind Mine=Variable, Total=Variable, Limit=Parameter}
{#top Limit}
