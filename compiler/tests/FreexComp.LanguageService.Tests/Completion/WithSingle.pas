{ context: with }
program WithSingle;
type
  TRect = record
    Left, Top, Width, Height: Integer;
  end;
var
  R: TRect;
begin
  with R do
    {|}
end.
{#has Left, Top, Width, Height, R, Writeln}
{#kind Left=Field}
{#top Height}
