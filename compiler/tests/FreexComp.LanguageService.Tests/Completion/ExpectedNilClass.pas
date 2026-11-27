{ context: expected-type }
program ExpectedNilClass;
type
  TShape = class end;
var
  Alpha: Integer;
  Shape: TShape;
begin
  Shape := {|}
end.
{#top Shape}
{#ranked nil, Shape}
{#unranked Alpha, True, False}
