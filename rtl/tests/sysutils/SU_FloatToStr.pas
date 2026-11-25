// covers: RTL-230, RTL-231, RTL-232, RTL-233, RTL-234, RTL-235, RTL-236, RTL-237, RTL-246
program SU_FloatToStr;
{ sysutils.md section 5: FloatToStr and every FloatToStrF format (normative rows of RTL-232 ... 234 and more). }
uses
  SysUtils, TestKit;

procedure G(V: Double; const E: string);
begin
  CheckEqS(E, FloatToStr(V), 'FloatToStr ' + E);
end;

procedure F(V: Double; Fmt: TFloatFormat; P, D: Integer; const E: string);
begin
  CheckEqS(E, FloatToStrF(V, Fmt, P, D), 'FloatToStrF ' + E);
end;

{ the shortest text of V is E, and E reads back to exactly V }
procedure S(V: Double; const E: string);
var
  T: string;
  Back: Double;
begin
  Check(TryFloatToStrShortest(V, T), 'shortest is a number ' + E);
  CheckEqS(E, T, 'shortest ' + E);
  Check(TryStrToFloat(T, Back, TFormatSettings.Invariant), 'shortest reads back ' + E);
  CheckEqF(V, Back, 0, 'shortest round trip ' + E);
end;

{ no text for a value that is not a number }
procedure SNone(V: Double; const Name: string);
var
  T: string;
begin
  T := 'x';
  CheckEqB(False, TryFloatToStrShortest(V, T), 'shortest: False for ' + Name);
  CheckEqS('', T, 'shortest: empty text for ' + Name);
end;

var
  Zero, One, NegZero: Double;
  Text: string;
begin
  Zero := 0;
  One := 1;
  NegZero := -Zero;
  Section('RTL-232 FloatToStr');
  G(1.0, '1');
  G(0.1, '0.1');
  G(1.5, '1.5');
  G(0.1 + 0.2, '0.3');
  G(-2.25, '-2.25');
  G(One / 3, '0.333333333333333');
  G(100.0, '100');
  G(1e15, '1E15');
  G(123456789012345.0, '123456789012345');
  G(1.5e-7, '1.5E-7');
  G(0.00001, '0.00001');
  G(0.000001, '1E-6');
  G(1e100, '1E100');
  G(0.0, '0');
  G(NegZero, '0');
  G(-1e-300, '-1E-300');
  G(2.0 / 3, '0.666666666666667');
  G(999999999999999.5, '1E15');
  G(1.7976931348623157e308, '1.79769313486232E308');
  G(4.9406564584124654e-324, '4.94065645841247E-324');
  G(12345.678, '12345.678');
  G(-0.000012345, '-0.000012345');
  G(Zero / Zero, 'Nan');
  G(One / Zero, '+Inf');
  G(-One / Zero, '-Inf');

  Section('RTL-231 ffGeneral');
  F(1234.5, ffGeneral, 4, 0, '1234');            { an exact tie rounds to the even digit (RTL-230) }
  F(12345, ffGeneral, 4, 0, '1.234E4');
  F(12355, ffGeneral, 4, 2, '1.236E04');
  F(1e-20, ffGeneral, 15, 9, '1E-0020');
  F(0.5, ffGeneral, 1, 0, '0.5');
  F(0.1, ffGeneral, 17, 0, '0.10000000000000001');
  F(0.1, ffGeneral, 30, 0, '0.10000000000000001');

  Section('RTL-233 ffExponent');
  F(1234.5, ffExponent, 5, 2, '1.2345E+03');
  F(1234.5, ffExponent, 15, 3, '1.23450000000000E+003');
  F(-0.00125, ffExponent, 3, 0, '-1.25E-3');
  F(0, ffExponent, 4, 2, '0.000E+00');
  F(9.99, ffExponent, 2, 9, '1.0E+0001');
  F(One / Zero, ffExponent, 4, 2, '+Inf');

  Section('RTL-234 ffFixed');
  F(2.5, ffFixed, 17, 0, '2');
  F(3.5, ffFixed, 17, 0, '4');
  F(1e20, ffFixed, 15, 1, '100000000000000000000.0');
  F(3.14159, ffFixed, 17, 2, '3.14');
  F(2.5, ffFixed, 17, 3, '2.500');
  F(0.125, ffFixed, 17, 2, '0.12');
  F(0.375, ffFixed, 17, 2, '0.38');
  F(-0.004, ffFixed, 17, 2, '0.00');
  F(-1.5, ffFixed, 17, 1, '-1.5');
  F(0.4, ffFixed, 17, 0, '0');
  F(0.6, ffFixed, 17, 0, '1');
  F(99.96, ffFixed, 17, 1, '100.0');
  F(1.23456, ffFixed, 3, 4, '1.2300');
  F(0.001, ffFixed, 17, 25, '0.001000000000000000');
  F(1e-10, ffFixed, 15, 2, '0.00');
  F(Zero / Zero, ffFixed, 15, 2, 'Nan');

  Section('RTL-235 ffNumber');
  F(1234567.891, ffNumber, 17, 2, '1,234,567.89');
  F(-1234.5, ffNumber, 17, 0, '-1,234');
  F(999.999, ffNumber, 17, 2, '1,000.00');
  F(12, ffNumber, 17, 1, '12.0');
  F(123456, ffNumber, 17, 0, '123,456');

  Section('RTL-236 ffCurrency');
  F(1234.5, ffCurrency, 17, 2, '$1,234.50');
  F(-1234.5, ffCurrency, 17, 2, '-$1,234.50');
  F(0, ffCurrency, 17, 2, '$0.00');
  FormatSettings.CurrencyFormat := 3;
  FormatSettings.NegCurrFormat := 15;
  FormatSettings.CurrencyString := 'EUR';
  F(7, ffCurrency, 17, 0, '7 EUR');
  F(-7, ffCurrency, 17, 0, '(7 EUR)');
  FormatSettings.NegCurrFormat := 0;
  F(-7, ffCurrency, 17, 0, '(EUR7)');
  FormatSettings.NegCurrFormat := 8;
  F(-7, ffCurrency, 17, 0, '-7 EUR');

  Section('RTL-237 separators');
  FormatSettings.DecimalSeparator := ',';
  FormatSettings.ThousandSeparator := '.';
  G(1.5, '1,5');
  F(1234.5, ffNumber, 17, 1, '1.234,5');
  F(1234.5, ffExponent, 5, 2, '1,2345E+03');
  FormatSettings.ThousandSeparator := #0;
  F(1234.5, ffNumber, 17, 1, '1234,5');

  Section('TryFloatToStrShortest (independent of FormatSettings)');
  S(0.0, '0');
  S(1.0, '1');
  S(-2.25, '-2.25');
  S(0.1, '0.1');
  S(0.1 + 0.2, '0.30000000000000004');
  S(One / 3, '0.3333333333333333');
  S(2.0 / 3, '0.6666666666666666');
  S(100.0, '100');
  S(12345.678, '12345.678');
  S(9007199254740993.0, '9007199254740992');
  S(123456789012345680000.0, '123456789012345680000');
  S(1e20, '100000000000000000000');
  S(1e21, '1e+21');
  S(1.5e21, '1.5e+21');
  S(1e100, '1e+100');
  S(0.000001, '0.000001');
  S(0.0000015, '0.0000015');
  S(-0.000012345, '-0.000012345');
  S(1e-7, '1e-7');
  S(1.5e-7, '1.5e-7');
  S(-1e-300, '-1e-300');
  S(1.7976931348623157e308, '1.7976931348623157e+308');
  S(2.2250738585072014e-308, '2.2250738585072014e-308');
  S(4.9406564584124654e-324, '5e-324');
  CheckEqB(True, TryFloatToStrShortest(NegZero, Text), 'shortest is a number -0');
  CheckEqS('-0', Text, 'shortest -0 keeps the sign');
  SNone(Zero / Zero, 'a NaN');
  SNone(One / Zero, 'plus infinity');
  SNone(-One / Zero, 'minus infinity');
  Done;
end.
