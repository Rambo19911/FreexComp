// covers: LANG-231, LANG-340, CMP-412
program RealFunctions;
var
  D: Double;
  S: Single;
  I: Integer;
  Q: Int64;
begin
  { Trunc: toward zero (constant arguments are folded, LANG-340) }
  Writeln(Trunc(2.7), ' ', Trunc(-2.7), ' ', Trunc(0.999), ' ', Trunc(-0.5));
  D := 2.7;
  Writeln(Trunc(D), ' ', Trunc(-D));
  { Round: to nearest, ties to even }
  D := 0.5;
  Writeln(Round(D), ' ', Round(D + 1), ' ', Round(D + 2), ' ', Round(-D), ' ', Round(-D - 1), ' ', Round(-D - 2));
  D := 2.51;
  Writeln(Round(D), ' ', Round(-D));
  D := 0.49999999999999994;
  Writeln(Round(D));
  Writeln(Round(2.5), ' ', Round(3.5), ' ', Round(-3.5));
  D := 4503599627370497.0;
  Writeln(Round(D), ' ', Trunc(D));
  D := 1e18;
  Q := Trunc(D);
  Writeln(Q);
  D := -9223372036854775808.0;
  Writeln(Trunc(D));
  I := Trunc(123.9);
  Writeln(I);
  { Int and Frac keep the sign }
  D := 2.75;
  Writeln(Int(D):0:2, ' ', Frac(D):0:2, ' ', Int(-D):0:2, ' ', Frac(-D):0:2);
  D := 1e300;
  Writeln(Int(D) = D, ' ', Frac(D) = 0);
  D := 0.25;
  Writeln(Int(D):0:1, ' ', Int(-D):0:1, Frac(D));
  { Sqrt: llvm.sqrt.f64; a negative argument gives NaN }
  Writeln(Sqrt(2));
  D := 16;
  Writeln(Sqrt(D):0:3);
  I := 81;
  Writeln(Sqrt(I):0:0);
  D := -1;
  D := Sqrt(D);
  Writeln(D <> D);
  S := 2.25;
  Writeln(Sqrt(S):0:2);
  { Abs and Sqr on reals }
  D := -2.5;
  Writeln(Abs(D):0:2, ' ', Sqr(D):0:3);
  S := -1.5;
  Writeln(Abs(S):0:1, ' ', Sqr(S):0:2);
end.
