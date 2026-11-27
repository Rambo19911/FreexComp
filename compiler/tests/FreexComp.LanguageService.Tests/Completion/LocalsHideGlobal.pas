{ context: locals }
program LocalsHideGlobal;
var
  Value: string;
procedure Work;
var
  Value: Integer;
  Zeta: Integer;
begin
  {|}
end;
begin
end.
{#has Value, Zeta}
{#kind Value=Variable}
{#top Value}
