{ context: strict-protected }
program P;
uses Base;
type
  TChild = class(TBase)
    procedure Work;
  end;
procedure TChild.Work;
begin
  {|}
end;
begin
end.
{#has FState, Notify, Work}
{#kind Notify=Method}
{#not FHidden}
