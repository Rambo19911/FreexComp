program RecordField;
type
  TPoint = record
    X, {#}Y: Integer;
  end;
var
  P: TPoint;
begin
  P.{|}Y := 2;
  Writeln(P.Y);
end.
