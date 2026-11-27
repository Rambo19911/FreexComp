program P;
uses Shapes;
var
  S: TSquare;
begin
  S := TSquare.Create;
  Writeln(S.{|}Area);
end.
