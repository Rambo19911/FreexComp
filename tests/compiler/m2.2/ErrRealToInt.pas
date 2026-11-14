// covers: LANG-231, LANG-280, LANG-290
program ErrRealToInt;
var
  I: Integer;
  B: Byte;
  Q: Int64;
  X: Double;
  S: Single;

procedure P(N: Integer);
begin
end;

begin
  I := X;
  B := S;
  Q := 2.5;
  I := X * 2;
  P(X);
  I := I / 2;
  I := Integer(X);
end.