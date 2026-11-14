// covers: RTL-238, RTL-202
program SU_StrToFloat;
{ sysutils.md RTL-238: TryStrToFloat / StrToFloatDef (the raising StrToFloat: SU_ConvertErrors). }
uses
  SysUtils, TestKit;

procedure Ok(const S: string; E: Double);
var
  V: Double;
begin
  if TryStrToFloat(S, V) then
    CheckEqF(E, V, 0, 'TryStrToFloat ' + QuoteStr(S))
  else
    Check(False, 'TryStrToFloat ' + QuoteStr(S));
end;

procedure Bad(const S: string);
var
  V: Double;
begin
  Check(not TryStrToFloat(S, V), 'rejects ' + QuoteStr(S));
end;

var
  V, Zero: Double;
begin
  Zero := 0;
  Section('accepted');
  Ok('1.5', 1.5);
  Ok('  -2.25 '#9, -2.25);
  Ok('1e3', 1000);
  Ok('1E-2', 0.01);
  Ok('.5', 0.5);
  Ok('+7', 7);
  Ok('0.1', 0.1);
  Ok('123456789012345678901234567890', 123456789012345678901234567890.0);
  Ok('2.2250738585072011e-308', 2.2250738585072011e-308);
  Ok('1e-400', 0);
  Ok('4.9e-324', 4.9406564584124654e-324);
  Ok('1.7976931348623157e308', 1.7976931348623157e308);
  Ok('INF', 1 / Zero);
  Ok('-inf', -1 / Zero);

  Section('rejected');
  Bad('');
  Bad('   ');
  Bad('1,5');
  Bad('1,000.5');
  Bad('1.');
  Bad('abc');
  Bad('1.5x');
  Bad('1e400');
  Bad('-1e309');
  Bad('1 2');

  Section('decimal separator from the settings');
  FormatSettings.DecimalSeparator := ',';
  Ok('1,5', 1.5);
  Bad('1.5');
  FormatSettings.DecimalSeparator := '.';
  V := StrToFloatDef('2,5', 9);
  CheckEqF(9, V, 0, 'StrToFloatDef default');
  CheckEqF(2.5, StrToFloatDef('2.5', 9), 0, 'StrToFloatDef value');
  FormatSettings.DecimalSeparator := ';';
  Check(TryStrToFloat('3;25', V, FormatSettings) and (V = 3.25), 'explicit settings overload');
  FormatSettings.DecimalSeparator := '.';
  Check(not TryStrToFloat('3;25', V, FormatSettings), 'explicit settings reject ;');
  Done;
end.
