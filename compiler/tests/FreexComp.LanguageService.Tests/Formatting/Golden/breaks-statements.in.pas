program BreaksStatements;
var
A, B: Integer;
begin
A := 1; B := 2; A := A + B;
A := 3; // trailing comment stays
B := 4; { block } A := 5;
Writeln(A); Writeln(B)
end.
