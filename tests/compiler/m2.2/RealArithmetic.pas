// covers: LANG-226, LANG-230, LANG-231, LANG-233, CMP-313, CMP-316
program RealArithmetic;
var
  A, B, D: Double;
  S, T: Single;
  I, J: Integer;
  C: Cardinal;
  Q: Int64;
  U: UInt64;
  R: Real;
  E: Extended;

function Area(W, H: Double): Double;
begin
  Result := W * H;
end;

function Halve(V: Single): Single;
begin
  Result := V / 2;
end;

begin
  A := 1.5;
  B := 0.25;
  Writeln(A + B = 1.75, ' ', A - B = 1.25, ' ', A * B = 0.375, ' ', A / B = 6);
  D := -A;
  Writeln(D = -1.5, ' ', -D = A, ' ', D + A = 0, ' ', Abs(D) = A, ' ', Sqr(A) = 2.25);

  { integers are promoted; / is always a real division }
  I := 7;
  J := 2;
  D := I / J;
  Writeln(D = 3.5, ' ', I / J > I div J, ' ', I * B = 1.75, ' ', 1 / 4 = B);
  C := 4000000000;
  Q := -9000000000;
  U := 18446744073709551615;
  Writeln(C / 2 = 2000000000, ' ', Q * 0.5 = -4500000000, ' ', U / 2 > 9.2e18);

  { Single operations stay in Single; mixing with Double widens }
  S := 0.1;
  D := 0.1;
  Writeln(S = D, ' ', S = Single(D), ' ', S + S = 0.2, ' ', S + S = Single(0.2));
  S := 16777216;
  T := S + 1;
  Writeln(T = S, ' ', S + 1.0 = 16777217);

  { real parameters and results; Real and Extended are Double }
  Writeln(Area(A, 4) = 6, ' ', Halve(3) = 1.5, ' ', SizeOf(R), ' ', SizeOf(E), ' ', SizeOf(S));

  { constants are folded in Double }
  R := 1 / 3;
  E := R * 3;
  Writeln(E = 1, ' ', 0.1 + 0.2 = 0.3, ' ', 0.1 + 0.2 > 0.3);
  D := 0;
  for I := 1 to 10 do
    D := D + 0.1;
  Writeln(D = 1, ' ', D < 1, ' ', Abs(D - 1) < 1e-15);
end.