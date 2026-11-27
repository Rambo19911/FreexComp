program SpacingOperatorsOff;
var
A, B: Integer;
begin
A := 1 + 2 * 3 - (4 div 2);
B := - A;
if (A <> B) and (A >= 0) then
B := A mod 2;
end.
