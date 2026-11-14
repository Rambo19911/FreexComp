// covers: RTL-610
program Math_Trig;
{ other-units.md section 2 (task R-054): Power (every rule of its row), IntPower, Hypot, the logarithms, the
  trigonometric, inverse and hyperbolic functions, DegToRad / RadToDeg, and the EInvalidOp domain errors. Reference
  values are correctly rounded (or within one unit) results; non-exact results are checked to a few units in the last
  place (CheckEqF), exact cases with 0. }
uses
  SysUtils, Math, TestKit;

var
  Msg: string;

procedure Caught(E: Exception);
begin
  Msg := E.ClassName + ': ' + E.Message;
end;

procedure ExpectInvalidOp(const Name: string);
begin
  CheckEqS('EInvalidOp: Invalid floating point operation', Msg, Name);
  Msg := 'no exception';
end;

procedure TestPower;
begin
  Section('Power, IntPower, Hypot');
  CheckEqF(1024, Power(2, 10), 0, 'Power(2, 10)');
  CheckEqF(0.25, Power(2, -2), 0, 'Power(2, -2)');
  CheckEqF(-8, Power(-2, 3), 0, 'Power(-2, 3): negative base, odd exponent');
  CheckEqF(16, Power(-2, 4), 0, 'Power(-2, 4): negative base, even exponent');
  CheckEqF(0, Power(0, 5), 0, 'Power(0, 5)');
  CheckEqF(0, Power(0, 0.5), 0, 'Power(0, 0.5)');
  CheckEqF(1, Power(5, 0), 0, 'Power(5, 0)');
  CheckEqF(1, Power(NaN, 0), 0, 'Power(NaN, 0)');
  CheckEqF(1, Power(0, 0), 0, 'Power(0, 0)');
  CheckEqF(Infinity, Power(0, -1), 0, 'Power(0, -1): reciprocal of 0');
  CheckEqF(4294967296.0, Power(2, 32), 0, 'Power(2, 32): integral exponent beyond Integer');
  CheckEqF(1.4142135623730951, Power(2, 0.5), 2, 'Power(2, 0.5)');
  CheckEqF(31.622776601683793, Power(10, 1.5), 4, 'Power(10, 1.5)');
  CheckEqF(2.7556759606310752, Power(1.5, 2.5), 4, 'Power(1.5, 2.5)');
  CheckEqF(9.513656920021768, Power(0.5, -3.25), 4, 'Power(0.5, -3.25)');
  Msg := 'no exception';
  try Power(-8, 1 / 3); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('Power(-8, 1/3): negative base, non-integral exponent');
  CheckEqF(243, IntPower(3, 5), 0, 'IntPower(3, 5)');
  CheckEqF(0.125, IntPower(2, -3), 0, 'IntPower(2, -3)');
  CheckEqF(-27, IntPower(-3, 3), 0, 'IntPower(-3, 3)');
  CheckEqF(1, IntPower(7, 0), 0, 'IntPower(7, 0)');
  CheckEqF(2.5937424601000023, IntPower(1.1, 10), 4, 'IntPower(1.1, 10)');
  CheckEqF(5, Hypot(3, 4), 0, 'Hypot(3, 4)');
  CheckEqF(13, Hypot(-5, 12), 0, 'Hypot(-5, 12)');
  CheckEqF(0, Hypot(0, 0), 0, 'Hypot(0, 0)');
  CheckEqF(1.4142135623730952E+300, Hypot(1E300, 1E300), 2, 'Hypot(1E300, 1E300): no overflow');
  CheckEqF(1E-300, Hypot(1E-300, 1E-320), 1, 'Hypot: no underflow');
  CheckEqF(Infinity, Hypot(NegInfinity, 1), 0, 'Hypot(-Inf, 1)');
end;

procedure TestLogs;
begin
  Section('logarithms');
  CheckEqF(3, Log10(1000), 0, 'Log10(1000)');
  CheckEqF(-3, Log10(0.001), 0, 'Log10(0.001)');
  CheckEqF(0, Log10(1), 0, 'Log10(1)');
  CheckEqF(0.3010299956639812, Log10(2), 2, 'Log10(2)');
  CheckEqF(0.8450980400142568, Log10(7), 2, 'Log10(7)');
  CheckEqF(3, Log2(8), 0, 'Log2(8)');
  CheckEqF(-1, Log2(0.5), 0, 'Log2(0.5)');
  CheckEqF(-1074, Log2(4.9406564584124654E-324), 0, 'Log2 of the smallest subnormal');
  CheckEqF(3.321928094887362, Log2(10), 2, 'Log2(10)');
  CheckEqF(1.584962500721156, Log2(3), 2, 'Log2(3)');
  CheckEqF(4, LogN(3, 81), 2, 'LogN(3, 81)');
  CheckEqF(9.9999999995E-11, LnXP1(1E-10), 2, 'LnXP1(1E-10): accurate near 0');
  CheckEqF(0.6931471805599453, LnXP1(1), 1, 'LnXP1(1)');
  CheckEqF(-0.6931471805599453, LnXP1(-0.5), 1, 'LnXP1(-0.5)');
  CheckEqF(0, LnXP1(0), 0, 'LnXP1(0)');
  Msg := 'no exception';
  try Log10(0); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('Log10(0)');
  try Log10(-1); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('Log10(-1)');
  try Log2(-0.0); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('Log2(-0)');
  try LogN(-2, 4); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('LogN(-2, 4)');
  try LogN(2, 0); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('LogN(2, 0)');
  try LnXP1(-1); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('LnXP1(-1)');
  Check(IsNan(Log10(NaN)), 'Log10(NaN) is NaN');
end;

procedure TestTrig;
var
  S, C: Double;
begin
  Section('trigonometric');
  CheckEqF(0, Tan(0), 0, 'Tan(0)');
  CheckEqF(1E-9, Tan(1E-9), 0, 'Tan(1E-9)');
  CheckEqF(0.5463024898437905, Tan(0.5), 2, 'Tan(0.5)');
  CheckEqF(1.5574077246549023, Tan(1), 2, 'Tan(1)');
  CheckEqF(2.185039863261519, Tan(-2), 2, 'Tan(-2)');
  CheckEqF(-0.5583496378112418, Tan(1E10), 2, 'Tan(1E10)');
  CheckEqF(0.9999999999999999, Tan(Pi / 4), 1, 'Tan(Pi/4)');
  Check(IsNan(Tan(Infinity)), 'Tan(Infinity) is NaN');
  CheckEqF(0.6420926159343306, Cotan(1), 3, 'Cotan(1)');
  CheckEqF(1.830487721712452, Cotan(0.5), 3, 'Cotan(0.5)');
  CheckEqF(0.5235987755982989, ArcSin(0.5), 2, 'ArcSin(0.5)');
  CheckEqF(1.1197695149986342, ArcSin(0.9), 2, 'ArcSin(0.9)');
  CheckEqF(-1.5707963267948966, ArcSin(-1), 0, 'ArcSin(-1)');
  CheckEqF(0, ArcSin(0), 0, 'ArcSin(0)');
  CheckEqF(1.0471975511965979, ArcCos(0.5), 2, 'ArcCos(0.5)');
  CheckEqF(1.4706289056333368, ArcCos(0.1), 2, 'ArcCos(0.1)');
  CheckEqF(3.141592653589793, ArcCos(-1), 0, 'ArcCos(-1)');
  CheckEqF(0, ArcCos(1), 0, 'ArcCos(1)');
  Msg := 'no exception';
  try ArcSin(1.5); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('ArcSin(1.5)');
  try ArcCos(-1.01); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('ArcCos(-1.01)');
  CheckEqF(0, ArcTan2(0, 0), 0, 'ArcTan2(0, 0)');
  CheckEqF(0.7853981633974483, ArcTan2(1, 1), 1, 'ArcTan2(1, 1)');
  CheckEqF(2.356194490192345, ArcTan2(1, -1), 1, 'ArcTan2(1, -1)');
  CheckEqF(-2.356194490192345, ArcTan2(-1, -1), 1, 'ArcTan2(-1, -1)');
  CheckEqF(0.5880026035475675, ArcTan2(2, 3), 2, 'ArcTan2(2, 3)');
  CheckEqF(-2.5535900500422257, ArcTan2(-2, -3), 2, 'ArcTan2(-2, -3)');
  CheckEqF(1.5707963267948966, ArcTan2(1, 0), 0, 'ArcTan2(1, 0)');
  CheckEqF(-1.5707963267948966, ArcTan2(-1, 0), 0, 'ArcTan2(-1, 0)');
  CheckEqF(3.141592653589793, ArcTan2(0, -1), 0, 'ArcTan2(0, -1)');
  CheckEqF(-3.141592653589793, ArcTan2(-0.0, -1), 0, 'ArcTan2(-0, -1)');
  CheckEqF(3.141592653589793, ArcTan2(1E-300, -1), 0, 'ArcTan2(1E-300, -1)');
  CheckEqF(0.7853981633974483, ArcTan2(Infinity, Infinity), 1, 'ArcTan2(Inf, Inf)');
  SinCos(1, S, C);
  CheckEqF(Sin(1), S, 0, 'SinCos sine');
  CheckEqF(Cos(1), C, 0, 'SinCos cosine');
  CheckEqF(3.141592653589793, DegToRad(180), 0, 'DegToRad(180)');
  CheckEqF(0.7853981633974483, DegToRad(45), 0, 'DegToRad(45)');
  CheckEqF(180, RadToDeg(Pi), 0, 'RadToDeg(Pi)');
  CheckEqF(57.29577951308232, RadToDeg(1), 0, 'RadToDeg(1)');
end;

procedure TestHyperbolic;
begin
  Section('hyperbolic');
  CheckEqF(1.1752011936438014, Sinh(1), 2, 'Sinh(1)');
  CheckEqF(1.0000000000166668E-05, Sinh(1E-5), 2, 'Sinh(1E-5)');
  CheckEqF(0.3045202934471426, Sinh(0.3), 2, 'Sinh(0.3)');
  CheckEqF(-10.017874927409903, Sinh(-3), 2, 'Sinh(-3)');
  CheckEqF(1.1169973830808555E+308, Sinh(710), 3, 'Sinh(710): finite near overflow');
  CheckEqF(1E-20, Sinh(1E-20), 0, 'Sinh(1E-20)');
  CheckEqF(1, Cosh(0), 0, 'Cosh(0)');
  CheckEqF(1.5430806348152437, Cosh(1), 2, 'Cosh(1)');
  CheckEqF(1.0453385141288605, Cosh(0.3), 2, 'Cosh(0.3)');
  CheckEqF(3.7621956910836314, Cosh(-2), 2, 'Cosh(-2)');
  CheckEqF(1.1169973830808555E+308, Cosh(710), 3, 'Cosh(710)');
  CheckEqF(Infinity, Cosh(800), 0, 'Cosh(800) overflows');
  CheckEqF(0.46211715726000974, Tanh(0.5), 2, 'Tanh(0.5)');
  CheckEqF(-9.999999999666668E-06, Tanh(-1E-5), 2, 'Tanh(-1E-5)');
  CheckEqF(0.9640275800758169, Tanh(2), 2, 'Tanh(2)');
  CheckEqF(1, Tanh(30), 0, 'Tanh(30)');
  CheckEqF(-1, Tanh(-30), 0, 'Tanh(-30)');
  CheckEqF(0.881373587019543, ArcSinh(1), 2, 'ArcSinh(1)');
  CheckEqF(-9.999999999833334E-06, ArcSinh(-1E-5), 2, 'ArcSinh(-1E-5)');
  CheckEqF(0.2956730475634224, ArcSinh(0.3), 2, 'ArcSinh(0.3)');
  CheckEqF(23.7189981105004, ArcSinh(1E10), 2, 'ArcSinh(1E10)');
  CheckEqF(0, ArcCosh(1), 0, 'ArcCosh(1)');
  CheckEqF(1.3169578969248166, ArcCosh(2), 2, 'ArcCosh(2)');
  CheckEqF(23.7189981105004, ArcCosh(1E10), 2, 'ArcCosh(1E10)');
  CheckEqF(0.00044721359190373475, ArcCosh(1.0000001), 4, 'ArcCosh(1.0000001)');
  CheckEqF(0.5493061443340549, ArcTanh(0.5), 2, 'ArcTanh(0.5)');
  CheckEqF(-1.0000000000333334E-05, ArcTanh(-1E-5), 2, 'ArcTanh(-1E-5)');
  CheckEqF(1.4722194895832204, ArcTanh(0.9), 2, 'ArcTanh(0.9)');
  CheckEqF(Infinity, ArcTanh(1), 0, 'ArcTanh(1)');
  CheckEqF(NegInfinity, ArcTanh(-1), 0, 'ArcTanh(-1)');
  Msg := 'no exception';
  try ArcCosh(0.5); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('ArcCosh(0.5)');
  try ArcTanh(1.5); except on E: Exception do Caught(E); end;
  ExpectInvalidOp('ArcTanh(1.5)');
end;

begin
  TestPower;
  TestLogs;
  TestTrig;
  TestHyperbolic;
  Done;
end.
