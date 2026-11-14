// covers: LANG-911
program ErrWriteBadType;
type
  TColor = (Red, Green, Blue);
  TPoint = record X, Y: Integer; end;
  TDigits = set of 0..9;
  TInts = array[1..3] of Integer;
var
  C: TColor;
  P: TPoint;
  S: TDigits;
  A: TInts;
  Ptr: Pointer;
  D: array of Integer;
begin
  Writeln(C);
  Writeln(P);
  Write('set: ', S);
  Writeln(A, 1);
  Writeln(Ptr);
  Writeln(D);
  Writeln(Red, Ord(Red));
end.
