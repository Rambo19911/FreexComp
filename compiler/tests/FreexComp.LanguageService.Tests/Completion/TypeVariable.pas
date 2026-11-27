{ context: type-position }
program TypeVariable;
type
  TWidget = class end;
var
  W: {|}
begin
end.
{#has TWidget, Integer, string, array, record, set of, TObject}
{#kind TWidget=Class}
{#not Writeln, begin}
