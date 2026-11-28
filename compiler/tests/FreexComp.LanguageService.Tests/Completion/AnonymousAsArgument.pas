{ context: locals }
program AnonymousAsArgument;
type
  TProc = reference to procedure;
procedure Call(Action: TProc);
begin
  Action();
end;
procedure Work(Limit: Integer);
var
  Total: Integer;
begin
  Total := Limit;
  Call(procedure
    var Mine: Integer;
    begin
      Mine := Total;
      {|}
    end);
end;
begin
end.
{#has Mine, Total, Limit, Call, Work}
{#kind Mine=Variable, Total=Variable}
{#not Action}
{#top Limit}
