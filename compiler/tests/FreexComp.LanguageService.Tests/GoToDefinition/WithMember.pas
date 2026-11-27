program WithMember;
type
  TPoint = record
    {#}X, Y: Integer;
  end;
var
  P: TPoint;
begin
  with P do
    {|}X := 5;
  Writeln(P.X);
end.
