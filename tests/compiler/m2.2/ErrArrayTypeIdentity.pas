// covers: LANG-206, LANG-207, LANG-256
program ErrArrayTypeIdentity;
type
  TVec = array[1..3] of Integer;
var
  A, B: array[1..3] of Integer;
  C: array[1..3] of Integer;
  V: TVec;
  W: TVec;
begin
  A := B;
  V := W;
  A := C;
  V := A;
  C := V;
end.
