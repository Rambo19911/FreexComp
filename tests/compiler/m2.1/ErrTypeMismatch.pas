// covers: LANG-280, LANG-420
program ErrTypeMismatch;
var
  I: Integer;
  B: Boolean;
  C: Char;

procedure P(N: Integer);
begin
end;

begin
  I := B;
  B := I;
  C := I;
  I := C;
  P(B);
end.
