// covers: RTL-610
program Math_Basics;
{ other-units.md section 2 (task R-053): Min/Max (all overloads, NaN operands), the array extremes, EnsureRange,
  InRange, Sign, Floor/Ceil (+64), RoundTo (half-even), SimpleRoundTo (half away from zero), the float tests, the
  constants, DivMod, IfThen, RandomRange, Sum, SumInt, Mean and the errors (EInvalidOp, EInvalidArgument). }
uses
  SysUtils, Types, Math, TestKit;

var
  Msg: string;
  Empty: array of Double;
  EmptyInt: array of Integer;

procedure Caught(E: Exception);
begin
  Msg := E.ClassName + ': ' + E.Message;
end;

procedure Expect(const Expected, Name: string);
begin
  CheckEqS(Expected, Msg, Name);
  Msg := 'no exception';
end;

procedure TestMinMax;
var
  I64a, I64b: Int64;
  Ua, Ub: UInt64;
  N: Double;
begin
  Section('Min, Max');
  CheckEqI(-3, Min(-3, 5), 'Min Integer');
  CheckEqI(5, Max(-3, 5), 'Max Integer');
  I64a := -5000000000;
  I64b := 7000000000;
  CheckEqI(I64a, Min(I64a, I64b), 'Min Int64');
  CheckEqI(I64b, Max(I64a, I64b), 'Max Int64');
  Ua := UInt64($FFFFFFFFFFFFFFF0);
  Ub := 1;
  Check(Min(Ua, Ub) = 1, 'Min UInt64 (unsigned order)');
  Check(Max(Ua, Ub) = UInt64($FFFFFFFFFFFFFFF0), 'Max UInt64 (unsigned order)');
  CheckEqF(-1.5, Min(-1.5, 2.25), 0, 'Min Double');
  CheckEqF(2.25, Max(-1.5, 2.25), 0, 'Max Double');
  N := NaN;
  CheckEqF(4.0, Min(N, 4.0), 0, 'Min(NaN, 4)');
  CheckEqF(4.0, Min(4.0, N), 0, 'Min(4, NaN)');
  CheckEqF(4.0, Max(N, 4.0), 0, 'Max(NaN, 4)');
  CheckEqF(4.0, Max(4.0, N), 0, 'Max(4, NaN)');
  CheckEqI(-7, MinIntValue([3, -7, 9]), 'MinIntValue');
  CheckEqI(9, MaxIntValue([3, -7, 9]), 'MaxIntValue');
  CheckEqF(-0.5, MinValue([1.5, -0.5, 0.25]), 0, 'MinValue');
  CheckEqF(1.5, MaxValue([1.5, -0.5, 0.25]), 0, 'MaxValue');
  Msg := 'no exception';
  try MinIntValue(EmptyInt); except on E: Exception do Caught(E); end;
  Expect('EInvalidArgument: Invalid argument', 'MinIntValue of empty');
  try MaxIntValue(EmptyInt); except on E: Exception do Caught(E); end;
  Expect('EInvalidArgument: Invalid argument', 'MaxIntValue of empty');
  try MinValue(Empty); except on E: Exception do Caught(E); end;
  Expect('EInvalidArgument: Invalid argument', 'MinValue of empty');
  try MaxValue(Empty); except on E: Exception do Caught(E); end;
  Expect('EInvalidArgument: Invalid argument', 'MaxValue of empty');
end;

procedure TestRange;
var
  A: Int64;
begin
  Section('EnsureRange, InRange');
  CheckEqI(3, EnsureRange(1, 3, 8), 'EnsureRange below');
  CheckEqI(8, EnsureRange(10, 3, 8), 'EnsureRange above');
  CheckEqI(5, EnsureRange(5, 3, 8), 'EnsureRange inside');
  CheckEqI(10, EnsureRange(5, 10, 0), 'EnsureRange AMin > AMax: AMin wins');
  A := 9000000000;
  CheckEqI(8000000000, EnsureRange(A, 1, 8000000000), 'EnsureRange Int64');
  CheckEqF(0.5, EnsureRange(0.25, 0.5, 1.0), 0, 'EnsureRange Double');
  CheckEqB(True, InRange(3, 3, 8), 'InRange at AMin');
  CheckEqB(True, InRange(8, 3, 8), 'InRange at AMax');
  CheckEqB(False, InRange(9, 3, 8), 'InRange above');
  CheckEqB(False, InRange(A, 1, 8000000000), 'InRange Int64');
  CheckEqB(True, InRange(0.75, 0.5, 1.0), 'InRange Double');
end;

procedure TestSign;
var
  A: Int64;
begin
  Section('Sign');
  CheckEqI(-1, Sign(-7), 'Sign(-7)');
  CheckEqI(0, Sign(0), 'Sign(0)');
  CheckEqI(1, Sign(7), 'Sign(7)');
  A := -9000000000;
  CheckEqI(-1, Sign(A), 'Sign Int64');
  CheckEqI(-1, Sign(-0.5), 'Sign(-0.5)');
  CheckEqI(0, Sign(-0.0), 'Sign(-0.0)');
  CheckEqI(1, Sign(Infinity), 'Sign(Infinity)');
  Msg := 'no exception';
  try Sign(NaN); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Sign(NaN)');
end;

procedure TestFloorCeil;
begin
  Section('Floor, Ceil');
  CheckEqI(2, Floor(2.7), 'Floor(2.7)');
  CheckEqI(-3, Floor(-2.2), 'Floor(-2.2)');
  CheckEqI(-2, Floor(-2.0), 'Floor(-2.0)');
  CheckEqI(3, Ceil(2.2), 'Ceil(2.2)');
  CheckEqI(-2, Ceil(-2.7), 'Ceil(-2.7)');
  CheckEqI(0, Ceil(-0.5), 'Ceil(-0.5)');
  CheckEqI(-2147483648, Floor(-2147483648.0), 'Floor at Low(Integer)');
  CheckEqI(2147483647, Ceil(2147483646.5), 'Ceil to High(Integer)');
  CheckEqI(10000000001, Floor64(10000000001.5), 'Floor64');
  CheckEqI(-10000000001, Ceil64(-10000000001.5), 'Ceil64');
  CheckEqI(-10000000002, Floor64(-10000000001.5), 'Floor64 negative');
  Msg := 'no exception';
  try Floor(2147483648.0); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Floor out of range');
  try Ceil(2147483647.5); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Ceil out of range');
  try Floor(NaN); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Floor(NaN)');
  try Floor64(1E19); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Floor64 out of range');
  try Ceil64(Infinity); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'Ceil64(Infinity)');
end;

procedure TestRounding;
begin
  Section('RoundTo, SimpleRoundTo');
  CheckEqF(1235000, RoundTo(1234567, 3), 0, 'RoundTo(1234567, 3)');
  CheckEqF(2000, RoundTo(2500, 3), 0, 'RoundTo(2500, 3): half to even');
  CheckEqF(4000, RoundTo(3500, 3), 0, 'RoundTo(3500, 3): half to even');
  CheckEqF(2, RoundTo(2.5, 0), 0, 'RoundTo(2.5, 0)');
  CheckEqF(1.23, RoundTo(1.2345, -2), 0, 'RoundTo(1.2345, -2)');
  CheckEqF(0.12, RoundTo(0.125, -2), 0, 'RoundTo(0.125, -2): half to even');
  CheckEqF(-0.38, RoundTo(-0.375, -2), 0, 'RoundTo(-0.375, -2): half to even');
  CheckEqF(1E300, RoundTo(1E300, -2), 0, 'RoundTo of a large value');
  CheckEqF(0.13, SimpleRoundTo(0.125), 0, 'SimpleRoundTo(0.125)');
  CheckEqF(-0.13, SimpleRoundTo(-0.125), 0, 'SimpleRoundTo(-0.125)');
  CheckEqF(3, SimpleRoundTo(2.5, 0), 0, 'SimpleRoundTo(2.5, 0)');
  CheckEqF(-3, SimpleRoundTo(-2.5, 0), 0, 'SimpleRoundTo(-2.5, 0)');
  CheckEqF(1300, SimpleRoundTo(1250, 2), 0, 'SimpleRoundTo(1250, 2)');
  CheckEqF(0, SimpleRoundTo(0, 1), 0, 'SimpleRoundTo(0, 1)');
  Msg := 'no exception';
  try RoundTo(1.0, 38); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'RoundTo Digit 38');
  try SimpleRoundTo(1.0, -38); except on E: Exception do Caught(E); end;
  Expect('EInvalidOp: Invalid floating point operation', 'SimpleRoundTo Digit -38');
end;

procedure TestFloatTests;
begin
  Section('float tests, constants');
  CheckEqB(True, IsNan(NaN), 'IsNan(NaN)');
  CheckEqB(False, IsNan(Infinity), 'IsNan(Infinity)');
  CheckEqB(True, IsInfinite(Infinity), 'IsInfinite(Infinity)');
  CheckEqB(True, IsInfinite(NegInfinity), 'IsInfinite(NegInfinity)');
  CheckEqB(False, IsInfinite(NaN), 'IsInfinite(NaN)');
  CheckEqB(False, IsInfinite(MaxDouble), 'IsInfinite(MaxDouble)');
  Check(Infinity > MaxDouble, 'Infinity > MaxDouble');
  Check(NegInfinity < -MaxDouble, 'NegInfinity < -MaxDouble');
  CheckEqF(2.2250738585072014E-308, MinDouble, 0, 'MinDouble');
  CheckEqF(1.7976931348623157E+308, MaxDouble, 0, 'MaxDouble');
  CheckEqF(1.1754943508222875E-38, MinSingle, 0, 'MinSingle');
  CheckEqF(3.4028234663852886E+38, MaxSingle, 0, 'MaxSingle');
  CheckEqB(True, SameValue(1.0, 1.0 + 1E-13), 'SameValue default epsilon');
  CheckEqB(False, SameValue(1.0, 1.0 + 1E-11), 'SameValue default epsilon, apart');
  CheckEqB(True, SameValue(1E20, 1E20 + 1E7), 'SameValue relative epsilon');
  CheckEqB(True, SameValue(1.0, 1.05, 0.1), 'SameValue explicit epsilon');
  CheckEqB(False, SameValue(1.0, 1.2, 0.1), 'SameValue explicit epsilon, apart');
  CheckEqB(True, SameValue(Infinity, Infinity), 'SameValue(Infinity, Infinity)');
  CheckEqB(False, SameValue(NaN, NaN), 'SameValue(NaN, NaN)');
  CheckEqB(True, IsZero(1E-13), 'IsZero(1E-13)');
  CheckEqB(False, IsZero(1E-11), 'IsZero(1E-11)');
  CheckEqB(True, IsZero(0.05, 0.1), 'IsZero explicit epsilon');
  CheckEqI(-1, CompareValue(1.0, 2.0), 'CompareValue less');
  CheckEqI(1, CompareValue(2.0, 1.0), 'CompareValue greater');
  CheckEqI(0, CompareValue(1.0, 1.0 + 1E-13), 'CompareValue same');
  CheckEqI(0, CompareValue(1.0, 1.05, 0.1), 'CompareValue explicit epsilon');
  CheckEqI(LessThanValue, CompareValue(1.0, 1.2, 0.1), 'CompareValue explicit epsilon, apart');
end;

procedure TestInteger;
var
  Q, R: Word;
  I: Integer;
begin
  Section('DivMod, IfThen, RandomRange');
  DivMod(1000003, 1000, Q, R);
  CheckEqI(1000, Q, 'DivMod result');
  CheckEqI(3, R, 'DivMod remainder');
  Msg := 'no exception';
  try DivMod(5, 0, Q, R); except on E: Exception do Caught(E); end;
  Expect('EDivByZero: Division by zero', 'DivMod by zero');
  CheckEqI(1, IfThen(True, 1, 2), 'IfThen Integer True');
  CheckEqI(2, IfThen(False, 1, 2), 'IfThen Integer False');
  CheckEqI(0, IfThen(False, 1), 'IfThen default 0');
  CheckEqI(9000000000, IfThen(True, Int64(9000000000), 1), 'IfThen Int64');
  CheckEqF(2.5, IfThen(False, 1.5, 2.5), 0, 'IfThen Double');
  RandSeed := 42;
  I := Random(11) - 5;
  RandSeed := 42;
  CheckEqI(I, RandomRange(5, -5), 'RandomRange = Random(Abs(AFrom - ATo)) + Min(AFrom, ATo)');
  RandSeed := 42;
  I := Random(11) - 5;
  RandSeed := 42;
  CheckEqI(I, RandomRange(-5, 5), 'RandomRange in either order');
  RandSeed := 7;
  CheckEqI(3, RandomRange(3, 3), 'RandomRange of an empty range');
  CheckEqI(7, RandSeed, 'RandomRange(3, 3) leaves RandSeed');
end;

procedure TestSums;
begin
  Section('Sum, SumInt, Mean');
  CheckEqF(6.5, Sum([1.5, 2.0, 3.0]), 0, 'Sum');
  CheckEqF(0, Sum(Empty), 0, 'Sum of empty');
  CheckEqI(6000000000, SumInt([2000000000, 2000000000, 2000000000]), 'SumInt in Int64');
  CheckEqI(0, SumInt(EmptyInt), 'SumInt of empty');
  CheckEqF(2.0, Mean([1.0, 2.0, 3.0]), 0, 'Mean');
  Msg := 'no exception';
  try Mean(Empty); except on E: Exception do Caught(E); end;
  Expect('EInvalidArgument: Invalid argument', 'Mean of empty');
  Check(EInvalidArgument.InheritsFrom(EMathError), 'EInvalidArgument is an EMathError');
end;

begin
  TestMinMax;
  TestRange;
  TestSign;
  TestFloorCeil;
  TestRounding;
  TestFloatTests;
  TestInteger;
  TestSums;
  Done;
end.
