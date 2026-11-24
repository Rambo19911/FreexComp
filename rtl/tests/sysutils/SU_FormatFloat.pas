// phase: 7
// covers: RTL-239
program SU_FormatFloat;
{ sysutils.md RTL-239: FormatFloat picture strings. Digit placeholders '0' and '#', the decimal point, thousand
  separators, scientific notation (E+ / E-), up to three sections (positive;negative;zero), quoted and plain literals,
  rounding half away from zero on the 15-digit decimal, large and tiny values, the empty picture, NaN and infinities,
  and the separators of an explicit TFormatSettings. }
uses
  SysUtils, TestKit;

procedure P(const Fmt: string; V: Double; const E: string);
begin
  CheckEqS(E, FormatFloat(Fmt, V), QuoteStr(Fmt) + ' -> ' + QuoteStr(E));
end;

procedure TestDigits;
begin
  Section('0 and #');
  P('0', 7, '7');
  P('0', 1234, '1234');
  P('000', 5, '005');
  P('000', -5, '-005');
  P('#', 7, '7');
  P('#', 0, '');
  P('###', 12, '12');
  P('#0', 5, '5');
  P('#00', 5, '05');
  P('0#', 5, '05');
  P('#0#', 5, '05');
  P('00', 12345, '12345');
  Section('decimal point');
  P('0.00', 1.5, '1.50');
  P('0.00', 0, '0.00');
  P('0.00', -1.5, '-1.50');
  P('0.##', 1.5, '1.5');
  P('0.##', 1, '1');
  P('0.##', 1.255, '1.26');
  P('0.0#', 1, '1.0');
  P('0.0#', 1.25, '1.25');
  P('0.#0', 1, '1.00');
  P('#.##', 0.5, '.5');
  P('#.##', 1, '1');
  P('#.##', 0, '');
  P('0.#', 0, '0');
  P('.00', 12.5, '12.50');
  P('.00', 0.5, '.50');
  P('0.0.0', 1.25, '1.25');
  P('0.000000', 0.1, '0.100000');
  P('0.####', 0.0625, '0.0625');
end;

procedure TestThousands;
begin
  Section('thousand separator');
  P('#,##0.00', 1234.5678, '1,234.57');
  P('#,##0.00', 1234567.891, '1,234,567.89');
  P('#,##0.00', 0.5, '0.50');
  P('#,##0', 999, '999');
  P('#,##0', 1000, '1,000');
  P('#,##0', -1234567, '-1,234,567');
  P('0,000', 5, '0,005');
  P('0,', 1234567, '1,234,567');
  P(',0', 1234, '1,234');
  P('0.00,', 1234.5, '1,234.50');
  P('#,###', 0, '');
  P('#,##0', 1e15, '1,000,000,000,000,000');
end;

procedure TestExponent;
begin
  Section('exponent');
  P('0.00E+00', 1234.5, '1.23E+03');
  P('0.00E+00', 12350, '1.24E+04');
  P('0.00E+00', 0.00012, '1.20E-04');
  P('0.00E+00', -1234.5, '-1.23E+03');
  P('0.00E+00', 0, '0.00E+00');
  P('0.0E-0', 1200, '1.2E3');
  P('0.0E-0', 0.00012, '1.2E-4');
  P('0.0e+0', 1200, '1.2e+3');
  P('0.0e-00', 1200, '1.2e03');
  P('00.0E+0', 1234, '12.3E+2');
  P('#.#E+0', 9.96, '1E+1');
  P('0.###E+0', 1500, '1.5E+3');
  P('0.00E+0000', 1e100, '1.00E+0100');
  P('0.00E+000000', 1e-100, '1.00E-0100');
  P('0E+', 123456, '1E+5');
  P('0.000E+0', 1e308, '1.000E+308');
  P('0.000E+0', 5e-324, '4.941E-324');
  P('#,##0.0E+0', 123456, '1234.6E+2');
  P('0 EUR', 5, '5 EUR');
  P('0e', 5, '5e');
end;

procedure TestSections;
begin
  Section('sections');
  P('0.0;(0.0)', 2.5, '2.5');
  P('0.0;(0.0)', -2.5, '(2.5)');
  P('0.0;(0.0)', 0, '0.0');
  P('0.0;(0.0);zero', 2.5, '2.5');
  P('0.0;(0.0);zero', -2.5, '(2.5)');
  P('0.0;(0.0);zero', 0, 'zero');
  P('0.0;;zero', -2.5, '-2.5');
  P('0.0;;zero', 0, 'zero');
  P('0.0;(0.0);', 0, '0.0');
  P('0.0;-0.0', -2.5, '-2.5');
  P('+0;-0;0', 3, '+3');
  P('+0;-0;0', -3, '-3');
  P('+0;-0;0', 0, '0');
  P('0;0;0;9', 0, '0');
  P('0;neg', -3, 'neg');
  P('0.0;(0.0);-', -0.01, '-');
  P('0.0;(0.0)', -0.01, '0.0');
  P('0.0;(0.0);-', 0.01, '-');
  P('0.00', -0.001, '0.00');
  P(';(0)', 3, '3');
  P(';(0)', -3, '(3)');
  P(';(0.0)', -0.01, '0');
  P('0 "a;b";0 "c"', 1, '1 a;b');
  P('0 "a;b";0 "c"', -1, '1 c');
end;

procedure TestLiterals;
begin
  Section('literals');
  P('0.0 "kg"', 1.5, '1.5 kg');
  P('0.0 ''kg''', 1.5, '1.5 kg');
  P('"#"0', 7, '#7');
  P('"0.0" 0', 7, '0.0 7');
  P('''E+0'' 0', 7, 'E+0 7');
  P('"it''s" 0', 7, 'it''s 7');
  P('''say "x"'' 0', 7, 'say "x" 7');
  P('0 "abc', 5, '5 abc');
  P('""0', 5, '5');
  P('$0.00', 1.5, '$1.50');
  P('$0', 123, '$123');
  P('0%', 50, '50%');
  P('(0)', 5, '(5)');
  P('00-00', 1234, '12-34');
  P('00-00', 123456, '1234-56');
  P('0 pcs', -5, '-5 pcs');
  P('abc', 12, 'abc');
  P('"none"', 0, 'none');
end;

procedure TestRounding;
begin
  Section('rounding');
  P('0', 0.5, '1');
  P('0', 1.5, '2');
  P('0', 2.5, '3');
  P('0', -2.5, '-3');
  P('0', 0.4, '0');
  P('0', -0.4, '0');
  P('0', 0.49999, '0');
  P('0.0', 0.25, '0.3');
  P('0.0', 0.35, '0.4');
  P('0.0', 0.05, '0.1');
  P('0.0', 0.04, '0.0');
  P('0.00', 1.005, '1.01');
  P('0.00', 2.675, '2.68');
  P('0.00', -2.675, '-2.68');
  P('0.00', 999.995, '1000.00');
  P('0', 999.5, '1000');
  P('#,##0', 999.5, '1,000');
  P('0.000', 0.0004, '0.000');
  P('0.000', 0.0005, '0.001');
  P('0.00', 0.1 + 0.2, '0.30');
end;

procedure TestRange;
var
  Zero, One: Double;
begin
  Section('large and tiny values');
  Zero := 0;
  One := 1;
  P('0', 1e20, '100000000000000000000');
  P('0.0', 1e20, '100000000000000000000.0');
  P('0', 123456789012345678.0, '123456789012346000');
  P('0.00', 123456789012345.0, '123456789012345.00');
  P('0.00', 1234567890123.456, '1234567890123.46');
  P('0.00', 1e-10, '0.00');
  P('0.00', -1e-10, '0.00');
  P('0.000000000000', 1e-10, '0.000000000100');
  P('0.##', -Zero, '0');
  Section('empty picture');
  P('', 1234.5, '1234.5');
  P('', -0.1, '-0.1');
  P('', 0, '0');
  P('', 1e20, '1E20');
  P('', One / 3, '0.333333333333333');
  P(';;', 2.5, '2.5');
  Section('NaN and infinities');
  P('0.00', Zero / Zero, 'Nan');
  P('0.00', One / Zero, '+Inf');
  P('0.00;(0.00)', -One / Zero, '-Inf');
  P('', One / Zero, '+Inf');
end;

procedure TestSettings;
var
  FS: TFormatSettings;
begin
  Section('format settings');
  FS := InvariantFormatSettings;
  FS.DecimalSeparator := ',';
  FS.ThousandSeparator := ' ';
  CheckEqS('1 234 567,89', FormatFloat('#,##0.00', 1234567.891, FS), 'separators of the settings');
  CheckEqS('1,5', FormatFloat('', 1.5, FS), 'empty picture with settings');
  CheckEqS('1.5', FormatFloat('0"."0', 15, FS), 'a quoted point is a literal');
  FS.ThousandSeparator := #0;
  CheckEqS('1234567,9', FormatFloat('#,##0.0', 1234567.891, FS), 'no grouping when the separator is #0');
  FormatSettings.DecimalSeparator := ',';
  FormatSettings.ThousandSeparator := '.';
  CheckEqS('1.234,50', FormatFloat('#,##0.00', 1234.5), 'global settings');
  FormatSettings := InvariantFormatSettings;
  CheckEqS('1,234.50', FormatFloat('#,##0.00', 1234.5), 'global settings restored');
end;

begin
  TestDigits;
  TestThousands;
  TestExponent;
  TestSections;
  TestLiterals;
  TestRounding;
  TestRange;
  TestSettings;
  Done;
end.
