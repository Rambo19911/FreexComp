// covers: LANG-258
program ErrConstIndex;
type
  TColor = (Red, Green, Blue);
var
  A: array[1..3] of Integer;
  L: array['a'..'c'] of Integer;
  B: array[Green..Blue] of Integer;
  M: array[0..1, 0..1] of Byte;
begin
  A[0] := 1;
  A[4] := 1;
  L['d'] := 1;
  B[Red] := 1;
  M[1, 2] := 0;
  A[2] := A[-1];
end.
