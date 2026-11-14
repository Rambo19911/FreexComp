// covers: RTL-042
// opc-run: variants 1 2 3 4
program Sys_FloatBasics;
{ Trunc, Round, Int, Frac through the System helpers _Trunc/_Round/_Int/_Frac (system.md section 5.2), Sqrt, and
  float division by zero (masked exceptions: no error). Arguments are variables so the compiler cannot fold them.
  Variant 1: values. Variant 2: Trunc out of Int64 is error 207. Variant 3: Round(NaN) is error 207.
  Variant 4: Trunc(-Inf) is error 207. The variant is the last character of the command line (ParamStr: R-011). }
var
  D, Zero, One: Double;
  S: Single;
  P: PChar;

procedure RoundRow(X: Double);
begin
  D := X;
  Write(' ', Round(D));
end;

procedure TruncRow(X: Double);
begin
  D := X;
  Write(' ', Trunc(D));
end;

{ Int and Frac, with the sign of a zero shown through 1/x }
procedure IntFracRow(X: Double);
var
  I, F: Double;
begin
  D := X;
  I := Int(D);
  F := Frac(D);
  Write(I:0:3, ' ', F:0:3);
  if I = 0 then
    Write(' int', 1 / I:5);
  if F = 0 then
    Write(' frac', 1 / F:5);
  Writeln;
end;

procedure Values;
begin
  Write('Round:');
  RoundRow(0.5);
  RoundRow(1.5);
  RoundRow(2.5);
  RoundRow(3.5);
  RoundRow(-0.5);
  RoundRow(-1.5);
  RoundRow(-2.5);
  RoundRow(2.51);
  RoundRow(-2.49);
  RoundRow(0.49999999999999994);
  RoundRow(4503599627370497.0);
  RoundRow(2251799813685248.5);
  RoundRow(2251799813685249.5);
  RoundRow(9223372036854774784.0);
  RoundRow(-9223372036854775808.0);
  RoundRow(5e-324);
  RoundRow(0);
  Writeln;

  Write('Trunc:');
  TruncRow(2.7);
  TruncRow(-2.7);
  TruncRow(0.999);
  TruncRow(-0.5);
  TruncRow(1e18);
  TruncRow(4503599627370495.5);
  TruncRow(9223372036854774784.0);
  TruncRow(-9223372036854775808.0);
  TruncRow(-5e-324);
  Writeln;

  Writeln('Int Frac:');
  IntFracRow(2.75);
  IntFracRow(-2.75);
  IntFracRow(0.25);
  IntFracRow(-0.25);
  IntFracRow(3.0);
  IntFracRow(-3.0);
  IntFracRow(1e300);
  IntFracRow(4503599627370495.5);
  IntFracRow(-4503599627370495.5);
  IntFracRow(123456.789);
  D := 0.5;
  Zero := 0;
  One := 1;
  Writeln(Int(One / Zero), ' ', Int(-One / Zero), ' ', Int(Zero / Zero), ' ', Frac(One / Zero));

  Writeln('Sqrt:');
  D := 2;
  Writeln(Sqrt(D));
  D := 1e-300;
  Writeln(Sqrt(D));
  D := -1;
  Writeln(Sqrt(D));
  S := 2.25;
  Writeln(Sqrt(S):0:2);

  Writeln('division by zero:');
  Writeln(One / Zero, ' ', -One / Zero, ' ', Zero / Zero, ' ', One / -Zero);
end;

begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  case P^ of
    '1':
      Values;
    '2':
      begin
        D := 9223372036854775808.0;
        Writeln(Trunc(D));
      end;
    '3':
      begin
        Zero := 0;
        D := Zero / Zero;
        Writeln(Round(D));
      end;
    '4':
      begin
        Zero := 0;
        D := -1 / Zero;
        Writeln(Trunc(D));
      end;
  end;
end.
