{ context: with }
program WithMemberHidesLocal;
type
  TConfig = record
    Port: Integer;
    Host: string;
  end;
procedure Apply(const Config: TConfig);
var
  Port: string;
begin
  with Config do
    Port := {|}
end;
begin
end.
{#has Port, Host, Config}
{#kind Port=Field}
