// phase: 8
// covers: RTL-1264, RTL-1273
program SU_FloatHelpers;
{ sysutils.md section 12, task R-269 (F10-030): TSingleHelper and TDoubleHelper (block E of standard-helpers.md
  section 5.2). ToString is FloatToStr / FloatToStrF (a Single widened exactly first), with the global settings or the
  given ones; Parse is StrToFloat (Single: then converted, a finite value that overflows Single is an error), TryParse
  leaves 0; NaN and the infinities are class functions, the Is... tests read the bit pattern; the 15-digit round trip.
  Receivers are variables and class functions through the type name (literal receivers: R-275). }
uses
  SysUtils, TestKit;

var
  { out arguments of TryParse preset to a non-zero value (globals: a preset local would be reported as unused) }
  OutDouble: Double;
  OutSingle: Single;

function FloatMessage(const Text: string): string;
begin
  Result := '''' + Text + ''' is not a valid floating point value';
end;

procedure DoubleFails(const Text: string);
var
  V: Double;
begin
  Expect('EConvertError', FloatMessage(Text));
  try
    V := Double.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Double.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure SingleFails(const Text: string);
var
  V: Single;
begin
  Expect('EConvertError', FloatMessage(Text));
  try
    V := Single.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Single.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

function Comma: TFormatSettings;
begin
  Result := TFormatSettings.Invariant;
  Result.DecimalSeparator := ',';
end;

procedure TestText;
var
  D, Sum: Double;
  FS: TFormatSettings;
begin
  Section('ToString');
  FS := Comma;
  D := 1.5;
  CheckEqS('1.5', D.ToString, 'D.ToString');
  Sum := 0.1;
  Sum := Sum + 0.2;
  CheckEqS('0.3', Sum.ToString, '(0.1 + 0.2).ToString');
  CheckEqS('1,5', D.ToString(FS), 'D.ToString(FS)');
  CheckEqS('1.50', D.ToString(ffFixed, 15, 2), 'D.ToString(ffFixed, 15, 2)');
  CheckEqS('1,50', D.ToString(ffFixed, 15, 2, FS), 'D.ToString(ffFixed, 15, 2, FS)');
  CheckEqS('1.5', Double.ToString(D), 'Double.ToString(D)');
  CheckEqS('1,5', Double.ToString(D, FS), 'Double.ToString(D, FS)');
  CheckEqS('1.500E+00', Double.ToString(D, ffExponent, 4, 2), 'Double.ToString(D, ffExponent, 4, 2)');
  CheckEqS('1,50', Double.ToString(D, ffFixed, 15, 2, FS), 'Double.ToString(D, ffFixed, 15, 2, FS)');
  CheckEqS(FloatToStr(-2.25), (-D - 0.75).ToString, 'a parenthesised expression as receiver');
  FormatSettings.DecimalSeparator := ',';
  CheckEqS('1,5', D.ToString, 'D.ToString uses the global settings');
  CheckEqS('1,5', Double.ToString(D), 'Double.ToString uses the global settings');
  CheckEqI(15, Trunc(Double.Parse('15,25')), 'Double.Parse uses the global settings');
  FormatSettings := TFormatSettings.Invariant;
  CheckEqS('1.5', D.ToString, 'D.ToString after the settings are restored');
end;

procedure TestParse;
var
  D: Double;
  FS: TFormatSettings;
begin
  Section('Parse and TryParse');
  FS := Comma;
  Check(Double.Parse('1.5') = 1.5, 'Double.Parse(''1.5'')');
  Check(Double.Parse(' 2.5 ') = 2.5, 'Double.Parse trims');
  Check(Double.Parse('1e3') = 1000, 'Double.Parse(''1e3'')');
  Check(Double.Parse('1,5', FS) = 1.5, 'Double.Parse(''1,5'', FS)');
  DoubleFails('1,5');
  DoubleFails('abc');
  DoubleFails('');
  DoubleFails('1e400');
  Expect('EConvertError', FloatMessage('1.5'));
  try
    D := Double.Parse('1.5', FS);
    Writeln('# no exception, ', D);
  except
    on E: Exception do CheckRaises('Double.Parse(''1.5'', FS)', E.ClassName, E.Message);
  end;
  OutDouble := 7;
  Check(not Double.TryParse('x', OutDouble), 'Double.TryParse(''x'')');
  Check(OutDouble = 0, 'Double.TryParse leaves 0');
  Check(Double.TryParse('1,5', D, FS) and (D = 1.5), 'Double.TryParse(''1,5'', D, FS)');
  OutDouble := 7;
  Check(not Double.TryParse('1.5', OutDouble, FS), 'Double.TryParse(''1.5'', D, FS)');
  Check(OutDouble = 0, 'Double.TryParse with FS leaves 0');
  Check(Double.TryParse('-0.25', D) and (D = -0.25), 'Double.TryParse(''-0.25'')');
  CheckEqI(8, Double.Size, 'Double.Size');
end;

procedure TestSpecial;
var
  Nn, P, M, D: Double;
begin
  Section('NaN and infinities');
  Nn := Double.NaN;
  Check(Nn.IsNan, 'Nn.IsNan');
  Check(Double.IsNan(Nn), 'Double.IsNan(Nn)');
  CheckEqS('Nan', Nn.ToString, 'Nn.ToString');
  Check(not Nn.IsInfinity, 'not Nn.IsInfinity');
  P := Double.PositiveInfinity;
  Check(P.IsInfinity, 'P.IsInfinity');
  Check(P.IsPositiveInfinity, 'P.IsPositiveInfinity');
  Check(not P.IsNegativeInfinity, 'not P.IsNegativeInfinity');
  Check(not P.IsNan, 'not P.IsNan');
  CheckEqS('+Inf', P.ToString, 'P.ToString');
  Check(Double.IsInfinity(P) and Double.IsPositiveInfinity(P), 'class forms on +Inf');
  M := Double.NegativeInfinity;
  CheckEqS('-Inf', M.ToString, 'M.ToString');
  Check(M.IsNegativeInfinity and Double.IsNegativeInfinity(M) and M.IsInfinity, 'M is the negative infinity');
  Check(Double.Parse('Nan').IsNan, 'Double.Parse(''Nan'').IsNan');
  Check(Double.Parse('-Inf').IsNegativeInfinity, 'Double.Parse(''-Inf'').IsNegativeInfinity');
  Check(Double.Parse('inf').IsPositiveInfinity, 'Double.Parse(''inf'').IsPositiveInfinity');
  D := 1.5;
  Check(not D.IsNan and not D.IsInfinity, 'D is finite');
  Check(not Double.IsNan(D) and not Double.IsInfinity(D), 'class forms on a finite value');
  D := Double.MaxValue;
  CheckEqS('1.79769313486232E308', D.ToString, 'Double.MaxValue');
  Check(not D.IsInfinity, 'Double.MaxValue is finite');
  D := Double.MinValue;
  Check(D = -Double.MaxValue, 'Double.MinValue = -MaxValue');
  D := Double.Epsilon;
  CheckEqS('4.94065645841247E-324', D.ToString, 'Double.Epsilon');
  Check(D > 0, 'Double.Epsilon is positive');
end;

procedure RoundTrip(V: Double);
begin
  Check(Double.Parse(V.ToString) = V, 'round trip ' + V.ToString);
end;

procedure TestRoundTrip;
begin
  Section('round trip of 15 digits');
  RoundTrip(0.1);
  RoundTrip(1.5);
  RoundTrip(-2.25);
  RoundTrip(1e100);
  RoundTrip(123456.789);
  RoundTrip(1e-7);
  RoundTrip(123456789012345);
end;

procedure TestSingle;
var
  S, T: Single;
  FS: TFormatSettings;
begin
  Section('Single');
  FS := Comma;
  S := 0.5;
  CheckEqS('0.5', S.ToString, 'Single(0.5).ToString');
  S := 0.1;
  CheckEqS('0.100000001490116', S.ToString, 'Single(0.1).ToString');
  CheckEqS('0.100000001490116', Single.ToString(S), 'Single.ToString(S)');
  CheckEqS('0,100000001490116', S.ToString(FS), 'S.ToString(FS)');
  CheckEqS('0.10', S.ToString(ffFixed, 15, 2), 'S.ToString(ffFixed, 15, 2)');
  CheckEqS('0,10', Single.ToString(S, ffFixed, 15, 2, FS), 'Single.ToString(S, ffFixed, 15, 2, FS)');
  Check(Single.Parse('0.5') = 0.5, 'Single.Parse(''0.5'')');
  Check(Single.Parse('0,25', FS) = 0.25, 'Single.Parse(''0,25'', FS)');
  T := Single.Parse('3.4028235e38');
  Check(not T.IsInfinity, 'Single.Parse(''3.4028235e38'') is finite');
  CheckEqS('3.40282346638529E38', T.ToString, 'the largest Single');
  SingleFails('1e39');
  SingleFails('3.5e38');
  SingleFails('-1e39');
  SingleFails('1e400');
  SingleFails('x');
  OutSingle := 3;
  Check(not Single.TryParse('1e39', OutSingle), 'Single.TryParse(''1e39'')');
  Check(OutSingle = 0, 'Single.TryParse leaves 0');
  OutSingle := 3;
  Check(not Single.TryParse('0.5', OutSingle, FS), 'Single.TryParse(''0.5'', V, FS)');
  Check(OutSingle = 0, 'Single.TryParse with FS leaves 0');
  Check(Single.TryParse('2.5', T) and (T = 2.5), 'Single.TryParse(''2.5'')');
  Check(Single.Parse('Inf').IsInfinity, 'Single.Parse(''Inf'').IsInfinity');
  Check(Single.Parse('-Inf').IsNegativeInfinity, 'Single.Parse(''-Inf'').IsNegativeInfinity');
  Check(Single.Parse('nan').IsNan, 'Single.Parse(''nan'').IsNan');
  T := Single.NaN;
  Check(T.IsNan and Single.IsNan(T), 'Single.NaN in a variable: IsNan');
  CheckEqS('Nan', T.ToString, 'Single.NaN.ToString');
  T := Single.PositiveInfinity;
  Check(T.IsPositiveInfinity and Single.IsInfinity(T) and not T.IsNegativeInfinity, 'Single.PositiveInfinity');
  CheckEqS('+Inf', T.ToString, 'Single.PositiveInfinity.ToString');
  T := Single.NegativeInfinity;
  Check(T.IsNegativeInfinity and Single.IsNegativeInfinity(T), 'Single.NegativeInfinity');
  Check(not S.IsNan and not S.IsInfinity and not Single.IsPositiveInfinity(S), 'Single(0.1) is finite');
  CheckEqI(4, Single.Size, 'Single.Size');
  T := Single.MaxValue;
  CheckEqS('3.40282346638529E38', T.ToString, 'Single.MaxValue');
  T := Single.Epsilon;
  CheckEqS('1.40129846432482E-45', T.ToString, 'Single.Epsilon');
  Check(T > 0, 'Single.Epsilon is positive');
  T := Single.MinValue;
  Check(T = -Single.MaxValue, 'Single.MinValue = -MaxValue');
end;

begin
  TestText;
  TestParse;
  TestSpecial;
  TestRoundTrip;
  TestSingle;
  Done;
end.
