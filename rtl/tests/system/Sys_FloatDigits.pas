// covers: RTL-100
program Sys_FloatDigits;
{ Exact decimal digits of binary floating-point values (system.md RTL-100): "p N" rows round the exact value to N
  significant digits with round-half-even; "s" rows are the shortest digits that read back to the same Double, "f"
  rows the same for Single. Values are built from their bit patterns, so no decimal-to-binary conversion is involved.
  A row prints <sign><first digit>.<other digits>E<decimal exponent>. }

type
  PDouble = ^Double;
  PSingle = ^Single;

// TODO(C-072): call System._FloatDigitsPrecision/_FloatDigitsShortest directly; a program's call to a System routine
// is emitted without its "declare" today, so the link names are declared here and resolved by the linker.
procedure FloatDigitsPrecision(V: Double; Precision: Integer; var R: TFloatDigits);
  external name 'System._FloatDigitsPrecision';
procedure FloatDigitsShortest(V: Double; IsSingle: Boolean; var R: TFloatDigits);
  external name 'System._FloatDigitsShortest';

var
  R: TFloatDigits;

procedure Show;
var
  I: Integer;
begin
  if R.Negative then
    Write('-');
  if R.IsNaN then
    Write('NaN')
  else if R.IsInfinite then
    Write('Inf')
  else if R.Count = 0 then
    Write('0')
  else
  begin
    Write(R.Digits[0], '.');
    for I := 1 to R.Count - 1 do
      Write(R.Digits[I]);
    Write('E', R.Exponent - 1);
  end;
  Writeln;
end;

function Bits(U: UInt64): Double;
begin
  Result := PDouble(@U)^;
end;

function Bits32(U: Cardinal): Single;
begin
  Result := PSingle(@U)^;
end;

procedure P(U: UInt64; N: Integer);
begin
  Write(' p', N, ' ');
  FloatDigitsPrecision(Bits(U), N, R);
  Show;
end;

{ precision 17 and shortest }
procedure D(U: UInt64);
begin
  P(U, 17);
  Write(' s ');
  FloatDigitsShortest(Bits(U), False, R);
  Show;
end;

procedure F(U: Cardinal);
begin
  Write(' f ');
  FloatDigitsShortest(Bits32(U), True, R);
  Show;
end;

begin
  Writeln('# Double: 17 digits and shortest');
  Writeln('0.1'); D($3FB999999999999A);
  Writeln('0.3'); D($3FD3333333333333);
  Writeln('1/3'); D($3FD5555555555555);
  Writeln('2/3'); D($3FE5555555555555);
  Writeln('0.95'); D($3FEE666666666666);
  Writeln('4.35'); D($4011666666666666);
  Writeln('9.999'); D($4023FF7CED916873);
  Writeln('1.0'); D($3FF0000000000000);
  Writeln('-1234.5'); D($C0934A0000000000);
  Writeln('1e22'); D($4480F0CF064DD592);
  Writeln('1e23'); D($44B52D02C7E14AF6);
  Writeln('123456789012345678'); D($437B69B4BA630F35);
  Writeln('tie 1000000000000000.25'); D($430C6BF526340002);
  Writeln('tie 1000000000000000.75'); D($430C6BF526340006);
  Writeln('2^-1'); D($3FE0000000000000);
  Writeln('2^-44'); D($3D30000000000000);
  Writeln('2^53-1'); D($433FFFFFFFFFFFFF);
  Writeln('2^53'); D($4340000000000000);
  Writeln('2^63'); D($43E0000000000000);
  Writeln('2^1023'); D($7FE0000000000000);
  Writeln('2^-1022 smallest normal'); D($0010000000000000);
  Writeln('largest denormal'); D($000FFFFFFFFFFFFF);
  Writeln('5e-324 smallest denormal'); D($0000000000000001);
  Writeln('1.5e-323'); D($0000000000000003);
  Writeln('largest finite'); D($7FEFFFFFFFFFFFFF);
  Writeln('-largest finite'); D($FFEFFFFFFFFFFFFF);
  Writeln('0'); D($0000000000000000);
  Writeln('-0'); D($8000000000000000);
  Writeln('+Inf'); D($7FF0000000000000);
  Writeln('-Inf'); D($FFF0000000000000);
  Writeln('NaN'); D($7FF8000000000000);

  Writeln('# Double: other precisions');
  Writeln('0.125 tie to even'); P($3FC0000000000000, 2);
  Writeln('0.375 tie to even'); P($3FD8000000000000, 2);
  Writeln('2.5 tie to even'); P($4004000000000000, 1);
  Writeln('3.5 tie to even'); P($400C000000000000, 1);
  Writeln('9.5 tie, carry'); P($4023000000000000, 1);
  Writeln('9.999 carry'); P($4023FF7CED916873, 3);
  Writeln('0.95 below the tie'); P($3FEE666666666666, 1);
  Writeln('0.1 exact digits'); P($3FB999999999999A, 60);
  Writeln('1.0 trailing zeros'); P($3FF0000000000000, 25);
  Writeln('-1234.5'); P($C0934A0000000000, 4);
  Writeln('5e-324 exact (751 digits)'); P($0000000000000001, 751);
  Writeln('largest denormal exact (767 digits), precision clipped to 768'); P($000FFFFFFFFFFFFF, 1000);
  Writeln('precision below 1 is 1'); P($4011666666666666, 0);

  Writeln('# Single: shortest');
  Writeln('0.1f'); F($3DCCCCCD);
  Writeln('0.3f'); F($3E99999A);
  Writeln('1.0f'); F($3F800000);
  Writeln('2^24'); F($4B800000);
  Writeln('1e10f'); F($501502F9);
  Writeln('7.038531e-26f'); F($15AE43FE);
  Writeln('largest finite'); F($7F7FFFFF);
  Writeln('smallest normal'); F($00800000);
  Writeln('smallest denormal'); F($00000001);
  Writeln('-0'); F($80000000);
  Writeln('-Inf'); F($FF800000);
end.
