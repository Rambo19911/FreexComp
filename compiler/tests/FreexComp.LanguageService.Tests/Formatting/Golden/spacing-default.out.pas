program SpacingDefault;
type
  TPtr = ^Integer;
  TArr = array[0..9] of Integer;
  TGen<T> = class
  end;
var
  A, B: Integer;
  P: TPtr;
  S: set of Byte;
  L: TGen<Integer>;
begin
  A := 1 + 2 * 3 - (4 div 2);
  B := -A;
  P := @A;
  P^ := 5;
  S := [1, 2..3];
  if (A <> B) and not (A = 0) then A := A mod 2;
  Writeln(A, B:5, 'x');
  A := Length('abc');
  L := TGen<Integer>.Create;
  if A in [1, 2] then B := A shl 2;
end.
