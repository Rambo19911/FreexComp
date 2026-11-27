{ context: type-position }
program TypeParameter;
type
  TColor = (Red, Green);
procedure Paint(Color: {|});
begin
end;
begin
end.
{#has TColor, Integer, Boolean}
{#kind TColor=Enum}
{#not Paint, Red}
