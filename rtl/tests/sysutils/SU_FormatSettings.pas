// covers: RTL-203, RTL-205, RTL-206
program SU_FormatSettings;
{ InvariantFormatSettings has the values of RTL-206; the global FormatSettings starts as a copy of it (RTL-205) and is
  an independent copy (changing it does not change later InvariantFormatSettings results). }
uses
  SysUtils, TestKit;

const
  { typed constant arrays of strings are E2900 today: the names are compared as one joined string }
  ShortMonths = 'Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec ';
  LongMonths = 'January February March April May June July August September October November December ';
  ShortDays = 'Sun Mon Tue Wed Thu Fri Sat ';
  LongDays = 'Sunday Monday Tuesday Wednesday Thursday Friday Saturday ';

procedure CheckInvariant(const F: TFormatSettings; const P: string);
var
  I: Integer;
  S1, S2, L1, L2: string;
begin
  CheckEqS('$', F.CurrencyString, P + 'CurrencyString');
  CheckEqI(0, F.CurrencyFormat, P + 'CurrencyFormat');
  CheckEqI(2, F.CurrencyDecimals, P + 'CurrencyDecimals');
  CheckEqI(1, F.NegCurrFormat, P + 'NegCurrFormat');
  CheckEqS(',', F.ThousandSeparator, P + 'ThousandSeparator');
  CheckEqS('.', F.DecimalSeparator, P + 'DecimalSeparator');
  CheckEqS('-', F.DateSeparator, P + 'DateSeparator');
  CheckEqS(':', F.TimeSeparator, P + 'TimeSeparator');
  CheckEqS(',', F.ListSeparator, P + 'ListSeparator');
  CheckEqS('yyyy-mm-dd', F.ShortDateFormat, P + 'ShortDateFormat');
  CheckEqS('dddd, d mmmm yyyy', F.LongDateFormat, P + 'LongDateFormat');
  CheckEqS('hh:nn', F.ShortTimeFormat, P + 'ShortTimeFormat');
  CheckEqS('hh:nn:ss', F.LongTimeFormat, P + 'LongTimeFormat');
  CheckEqS('AM', F.TimeAMString, P + 'TimeAMString');
  CheckEqS('PM', F.TimePMString, P + 'TimePMString');
  CheckEqI(50, F.TwoDigitYearCenturyWindow, P + 'TwoDigitYearCenturyWindow');
  S1 := '';
  L1 := '';
  S2 := '';
  L2 := '';
  for I := 1 to 12 do
  begin
    S1 := S1 + F.ShortMonthNames[I] + ' ';
    L1 := L1 + F.LongMonthNames[I] + ' ';
  end;
  for I := 1 to 7 do
  begin
    S2 := S2 + F.ShortDayNames[I] + ' ';
    L2 := L2 + F.LongDayNames[I] + ' ';
  end;
  CheckEqS(ShortMonths, S1, P + 'ShortMonthNames');
  CheckEqS(LongMonths, L1, P + 'LongMonthNames');
  CheckEqS(ShortDays, S2, P + 'ShortDayNames');
  CheckEqS(LongDays, L2, P + 'LongDayNames');
end;

var
  F: TFormatSettings;
begin
  Section('InvariantFormatSettings');
  CheckInvariant(InvariantFormatSettings, 'invariant ');
  Section('FormatSettings at start-up');
  CheckInvariant(FormatSettings, 'global ');
  Section('independent copies');
  FormatSettings.DecimalSeparator := ',';
  FormatSettings.LongMonthNames[1] := 'Januar';
  F := InvariantFormatSettings;
  CheckEqS('.', F.DecimalSeparator, 'invariant unchanged');
  CheckEqS('January', F.LongMonthNames[1], 'invariant names unchanged');
  CheckEqS('Januar', FormatSettings.LongMonthNames[1], 'global changed');
  F.ShortDayNames[1] := 'X';
  CheckEqS('Sun', InvariantFormatSettings.ShortDayNames[1], 'copy does not alias');
  Done;
end.
