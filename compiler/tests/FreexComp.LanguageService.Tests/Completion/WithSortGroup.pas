{ context: with }
program WithSortGroup;
type
  TPoint = record
    X, Y: Integer;
  end;
procedure Move(var Pt: TPoint; Delta: Integer);
begin
  with Pt do
    {|}
end;
begin
end.
{#has X, Y, Delta, Pt, Move}
{#top Delta}
