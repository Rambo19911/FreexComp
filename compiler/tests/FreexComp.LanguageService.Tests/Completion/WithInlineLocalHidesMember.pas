{ context: with }
program WithInlineLocalHidesMember;
type
  TCounter = class
    Step: Integer;
    Total: Integer;
  end;
procedure Run(C: TCounter);
begin
  with C do
  begin
    var Step: string := 'x';
    {|}
  end;
end;
begin
end.
{#has Step, Total, C}
{#kind Step=Variable}
