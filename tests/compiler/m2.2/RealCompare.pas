// covers: LANG-232, LANG-301, CMP-315
program RealCompare;
var
  A, B: Double;
  S: Single;
  I: Integer;
  Q: Int64;
  C: Cardinal;

procedure Show(X, Y: Double);
begin
  Writeln(X = Y, ' ', X <> Y, ' ', X < Y, ' ', X <= Y, ' ', X > Y, ' ', X >= Y);
end;

begin
  Show(1, 2);
  Show(2, 2);
  Show(-0.5, -1.5);
  Show(0, -0.0);

  { mixed integer and real operands compare in Double }
  A := 2.5;
  I := 2;
  Writeln(A > I, ' ', I < A, ' ', A = I + 0.5, ' ', I >= A, ' ', 3 > A);
  Q := 9007199254740993;
  A := 9007199254740992;
  Writeln(Q = A, ' ', Q > A);
  C := 4294967295;
  Writeln(C > -1.0, ' ', C = 4294967295.0);

  { Single against constants and Double }
  S := 0.5;
  Writeln(S = 0.5, ' ', S < 1, ' ', S = A / A / 2);
  S := 0.1;
  Writeln(S = 0.1, ' ', S > 0.1, ' ', S = Single(0.1));

  { real conditions }
  A := 0;
  B := 1;
  I := 0;
  while A < B do
  begin
    A := A + 0.25;
    I := I + 1;
  end;
  Writeln(I);
end.