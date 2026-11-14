// covers: LANG-258, CMP-331
program ArrayRangeCheck;
{$R+}
var
  A: array[1..5] of Integer;
  E: array['a'..'c'] of Byte;
  I: Integer;
  C: Char;
begin
  for I := 1 to 5 do
    A[I] := I * 2;
  I := 5;
  Writeln(A[I]);
  C := 'c';
  E[C] := 9;
  Writeln(E[C]);
  I := 0;
  Writeln(A[I]);
  Writeln('never');
end.
