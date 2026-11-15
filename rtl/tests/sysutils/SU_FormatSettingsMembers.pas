// phase: 7
// covers: RTL-203
program SU_FormatSettingsMembers;
{ sysutils.md RTL-203 (task R-124): the static record functions TFormatSettings.Invariant, TFormatSettings.Create and
  TFormatSettings.Create(LocaleName) return the same records as InvariantFormatSettings / CreateFormatSettings.
  Machine-independent (RTL-903): the user locale and 'de-DE' are compared with the unit-level result on this machine. }
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

var
  F: TFormatSettings;
begin
  Section('static record functions (RTL-203)');
  F := TFormatSettings.Invariant;
  CheckEqS(Describe(InvariantFormatSettings), Describe(F), 'Invariant = InvariantFormatSettings');
  CheckEqS('yyyy-mm-dd', F.ShortDateFormat, 'Invariant ShortDateFormat');
  F := TFormatSettings.Create;
  CheckEqS(Describe(CreateFormatSettings), Describe(F), 'Create = CreateFormatSettings');
  F := TFormatSettings.Create('de-DE');
  CheckEqS(Describe(CreateFormatSettings('de-DE')), Describe(F), 'Create(LocaleName) = CreateFormatSettings(LocaleName)');
  CheckEqS('Januar', F.LongMonthNames[1], 'Create(''de-DE'') LongMonthNames[1]');
  CheckEqS(Describe(InvariantFormatSettings), Describe(TFormatSettings.Create('')), 'Create('''') = invariant');
  CheckEqS(Describe(InvariantFormatSettings), Describe(FormatSettings), 'FormatSettings unaffected');
  CheckEqS('-', TFormatSettings.Invariant.DateSeparator, 'field of a call result');
  Done;
end.
