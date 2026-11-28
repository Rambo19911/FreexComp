program P;

uses UShape, USquare, UCircle;

var
  S: TSquare;
  C: TCircle;
  Total: Integer;
begin
  S := TSquare.Create;
  S.Side := 4;
  C := TCircle.Create;
  C.Radius := 2;
  Report(S);
  Report(C);
  Total := S.Area + C.Area;
  Writeln('total ', Total);
  S.Free;
  C.Free;
end.
