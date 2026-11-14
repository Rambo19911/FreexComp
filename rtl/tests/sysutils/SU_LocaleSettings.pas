// covers: RTL-207
program SU_LocaleSettings;
{ sysutils.md RTL-207: CreateFormatSettings (user locale) and CreateFormatSettings(LocaleName). Machine-independent
  (RTL-903): for the user locale only non-empty fields and translated pictures are asserted; for 'en-US' only the
  names, which users cannot override; '' and an invalid name give the invariant record; the global FormatSettings
  stays invariant (RTL-205). }
uses
  SysUtils, TestKit;

function Describe(const F: TFormatSettings): string;
var
  I: Integer;
begin
  Result := F.CurrencyString + '|' + IntToStr(F.CurrencyFormat) + '|' + IntToStr(F.CurrencyDecimals) + '|' +
    IntToStr(F.NegCurrFormat) + '|' + F.ThousandSeparator + F.DecimalSeparator + F.DateSeparator +
    F.TimeSeparator + F.ListSeparator + '|' + F.ShortDateFormat + '|' + F.LongDateFormat + '|' +
    F.ShortTimeFormat + '|' + F.LongTimeFormat + '|' + F.TimeAMString + '|' + F.TimePMString + '|' +
    IntToStr(F.TwoDigitYearCenturyWindow);
  for I := 1 to 12 do
    Result := Result + '|' + F.ShortMonthNames[I] + '/' + F.LongMonthNames[I];
  for I := 1 to 7 do
    Result := Result + '|' + F.ShortDayNames[I] + '/' + F.LongDayNames[I];
end;

{ no OS picture letter is left after the translation (M, H, t) }
function Translated(const Pic: string): Boolean;
begin
  Result := (Pic <> '') and (Pos('M', Pic) = 0) and (Pos('H', Pic) = 0) and (Pos('t', Pic) = 0);
end;

procedure TestUser;
var
  F: TFormatSettings;
  I: Integer;
  AllNames: Boolean;
begin
  Section('user locale');
  F := CreateFormatSettings;
  Check(F.CurrencyString <> '', 'CurrencyString');
  Check(F.DecimalSeparator <> #0, 'DecimalSeparator');
  Check(F.DateSeparator <> #0, 'DateSeparator');
  Check(F.TimeSeparator <> #0, 'TimeSeparator');
  Check(F.ListSeparator <> #0, 'ListSeparator');
  Check(F.CurrencyDecimals <= 9, 'CurrencyDecimals');
  Check(Translated(F.ShortDateFormat), 'ShortDateFormat translated');
  Check(Translated(F.LongDateFormat), 'LongDateFormat translated');
  Check(Translated(F.ShortTimeFormat), 'ShortTimeFormat translated');
  Check(Translated(F.LongTimeFormat), 'LongTimeFormat translated');
  Check(Pos('n', F.LongTimeFormat) > 0, 'LongTimeFormat minutes are n');
  Check(Pos('y', F.ShortDateFormat) > 0, 'ShortDateFormat has a year');
  AllNames := True;
  for I := 1 to 12 do
    AllNames := AllNames and (F.ShortMonthNames[I] <> '') and (F.LongMonthNames[I] <> '');
  for I := 1 to 7 do
    AllNames := AllNames and (F.ShortDayNames[I] <> '') and (F.LongDayNames[I] <> '');
  Check(AllNames, 'month and day names');
  CheckEqI(50, F.TwoDigitYearCenturyWindow, 'TwoDigitYearCenturyWindow');
  Check(FormatDateTime('c', EncodeDate(2024, 3, 9) + EncodeTime(13, 5, 7, 0), F) <> '', 'usable by FormatDateTime');
end;

procedure TestNamed;
var
  F: TFormatSettings;
begin
  Section('named locale');
  F := CreateFormatSettings('en-US');
  CheckEqS('January', F.LongMonthNames[1], 'en-US LongMonthNames[1]');
  CheckEqS('December', F.LongMonthNames[12], 'en-US LongMonthNames[12]');
  CheckEqS('Dec', F.ShortMonthNames[12], 'en-US ShortMonthNames[12]');
  CheckEqS('Sunday', F.LongDayNames[1], 'en-US LongDayNames[1] is Sunday');
  CheckEqS('Monday', F.LongDayNames[2], 'en-US LongDayNames[2]');
  CheckEqS('Saturday', F.LongDayNames[7], 'en-US LongDayNames[7]');
  CheckEqS('Sun', F.ShortDayNames[1], 'en-US ShortDayNames[1]');
  CheckEqS('Sat', F.ShortDayNames[7], 'en-US ShortDayNames[7]');
  F := CreateFormatSettings('de-DE');
  CheckEqS('Januar', F.LongMonthNames[1], 'de-DE LongMonthNames[1]');
  CheckEqS('Sonntag', F.LongDayNames[1], 'de-DE LongDayNames[1]');
end;

procedure TestInvariant;
var
  Inv: string;
begin
  Section('invariant');
  Inv := Describe(InvariantFormatSettings);
  CheckEqS(Inv, Describe(CreateFormatSettings('')), 'CreateFormatSettings('''') = invariant');
  CheckEqS(Inv, Describe(CreateFormatSettings('not a locale!')), 'an invalid name keeps every invariant value');
  CreateFormatSettings;
  CreateFormatSettings('de-DE');
  CheckEqS(Inv, Describe(FormatSettings), 'FormatSettings unaffected');
  CheckEqS(Inv, Describe(InvariantFormatSettings), 'InvariantFormatSettings unaffected');
end;

begin
  TestUser;
  TestNamed;
  TestInvariant;
  Done;
end.
