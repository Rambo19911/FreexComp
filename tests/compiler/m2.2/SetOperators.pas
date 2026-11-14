// covers: LANG-252, LANG-280
program SetOperators;
type
  TSmall = set of 0..7;
  TMid = set of 0..31;
  TBig = set of 0..63;
var
  A, B, C: TSmall;
  M: TMid;
  L: TBig;

procedure Show(S: TBig);
var
  I: Integer;
begin
  Write('{');
  for I := 0 to 63 do
    if I in S then
      Write(' ', I);
  Writeln(' }');
end;

begin
  A := [1, 2, 3, 4];
  B := [3, 4, 5, 6];
  C := A + B;
  Show(C);
  C := A * B;
  Show(C);
  C := A - B;
  Show(C);
  C := B - A;
  Show(C);
  Writeln(A = B, ' ', A <> B, ' ', A * B = [3, 4], ' ', [] = A - A);
  Writeln([3, 4] <= A, ' ', A <= B, ' ', A >= [1, 2], ' ', B >= A, ' ', [] <= B);
  M := [0, 16, 31];
  M := M + [5];
  Show(M);
  L := [0, 32, 63];
  L := L - [32] + [40];
  Show(L);
  L := M;
  Show(L);
  M := L * [1..20];
  Show(M);
  A := [7];
  A := A + [0] - [];
  Show(A);
  Writeln(A <> [], ' ', A - [0, 7] = []);
end.
