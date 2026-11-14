// covers: RTL-202, RTL-245
program SU_StrToDate;
{ sysutils.md RTL-245: TryStrToDate / TryStrToTime / TryStrToDateTime and their Def forms, with and without a
  TFormatSettings argument. Part order from ShortDateFormat, the extra separators '/', '-', '.', two-part dates in the
  current year, two-digit years in the century window, time with optional seconds, milliseconds and AM/PM, date-time
  with an optional time, date 0 for a time alone, and rejected input. }
uses
  SysUtils, TestKit;

function DateText(DT: TDateTime): string;
begin
  Result := FormatDateTime('yyyy-mm-dd hh:nn:ss.zzz', DT);
end;

procedure Date(const S, Expected: string; const FS: TFormatSettings);
var
  DT: TDateTime;
begin
  if TryStrToDate(S, DT, FS) then
    CheckEqS(Expected, DateText(DT), 'date ' + QuoteStr(S))
  else
    CheckEqS(Expected, 'invalid', 'date ' + QuoteStr(S));
end;

procedure Time(const S, Expected: string);
var
  DT: TDateTime;
begin
  if TryStrToTime(S, DT) then
    CheckEqS(Expected, DateText(DT), 'time ' + QuoteStr(S))
  else
    CheckEqS(Expected, 'invalid', 'time ' + QuoteStr(S));
end;

procedure DateTime(const S, Expected: string);
var
  DT: TDateTime;
begin
  if TryStrToDateTime(S, DT) then
    CheckEqS(Expected, DateText(DT), 'date-time ' + QuoteStr(S))
  else
    CheckEqS(Expected, 'invalid', 'date-time ' + QuoteStr(S));
end;

{ the year a two-digit year Y maps to (RTL-245 window) }
function Windowed(Y: Integer): string;
var
  Start, Full: Integer;
begin
  Start := CurrentYear - InvariantFormatSettings.TwoDigitYearCenturyWindow;
  Full := (Start div 100) * 100 + Y;
  if Full < Start then
    Full := Full + 100;
  Result := IntToStr(Full);
end;

procedure TestDates;
var
  Inv, Dmy, Mdy: TFormatSettings;
  CY: string;
begin
  Section('dates');
  Inv := InvariantFormatSettings;
  Date('2024-03-05', '2024-03-05 00:00:00.000', Inv);
  Date('  2024-3-5  ', '2024-03-05 00:00:00.000', Inv);
  Date('2024/03/05', '2024-03-05 00:00:00.000', Inv);
  Date('2024.03.05', '2024-03-05 00:00:00.000', Inv);
  Date('0001-01-01', '0001-01-01 00:00:00.000', Inv);
  Date('9999-12-31', '9999-12-31 00:00:00.000', Inv);
  Date('1899-12-29', '1899-12-29 00:00:00.000', Inv);
  Date('2024-02-30', 'invalid', Inv);
  Date('2023-02-29', 'invalid', Inv);
  Date('2024-13-01', 'invalid', Inv);
  Date('2024-00-10', 'invalid', Inv);
  Date('', 'invalid', Inv);
  Date('2024', 'invalid', Inv);
  Date('2024-03-05-01', 'invalid', Inv);
  Date('2024--05', 'invalid', Inv);
  Date('2024-Mar-05', 'invalid', Inv);
  Date('2024-03-05x', 'invalid', Inv);
  Date('+2024-03-05', 'invalid', Inv);
  Dmy := Inv;
  Dmy.ShortDateFormat := 'dd/mm/yyyy';
  Dmy.DateSeparator := '/';
  Date('05/03/2024', '2024-03-05 00:00:00.000', Dmy);
  Date('5.3.2024', '2024-03-05 00:00:00.000', Dmy);
  Mdy := Inv;
  Mdy.ShortDateFormat := 'M/d/yyyy';
  Mdy.DateSeparator := ' ';
  Date('3 5 2024', '2024-03-05 00:00:00.000', Mdy);
  Date('3/5/2024', '2024-03-05 00:00:00.000', Mdy);
  CY := IntToStr(CurrentYear);
  Date('03-05', CY + '-03-05 00:00:00.000', Inv);
  Date('05/03', CY + '-03-05 00:00:00.000', Dmy);
  Date('3/5', CY + '-03-05 00:00:00.000', Mdy);
  Date('24-03-05', Windowed(24) + '-03-05 00:00:00.000', Inv);
  Date('5/3/99', Windowed(99) + '-03-05 00:00:00.000', Dmy);
  Date('5/3/0', Windowed(0) + '-03-05 00:00:00.000', Dmy);
  Date('5/3/024', '0024-03-05 00:00:00.000', Dmy);
end;

procedure TestTimes;
begin
  Section('times');
  Time('14', '1899-12-30 14:00:00.000');
  Time('14:07', '1899-12-30 14:07:00.000');
  Time('14:07:09', '1899-12-30 14:07:09.000');
  Time('14:07:09.045', '1899-12-30 14:07:09.045');
  Time(' 9:5:3 ', '1899-12-30 09:05:03.000');
  Time('0:00', '1899-12-30 00:00:00.000');
  Time('23:59:59.999', '1899-12-30 23:59:59.999');
  Time('2:07 PM', '1899-12-30 14:07:00.000');
  Time('2:07pm', '1899-12-30 14:07:00.000');
  Time('12:30 am', '1899-12-30 00:30:00.000');
  Time('12:30 PM', '1899-12-30 12:30:00.000');
  Time('11:59:59 AM', '1899-12-30 11:59:59.000');
  Time('13:00 PM', 'invalid');
  Time('24:00', 'invalid');
  Time('12:60', 'invalid');
  Time('12:00:60', 'invalid');
  Time('12:00:00.1000', 'invalid');
  Time('', 'invalid');
  Time('12:', 'invalid');
  Time(':30', 'invalid');
  Time('12:30:', 'invalid');
  Time('12.30', 'invalid');
  Time('12:30 XM', 'invalid');
  Time('1:2:3:4', 'invalid');
end;

procedure TestDateTimes;
var
  FS: TFormatSettings;
  DT: TDateTime;
begin
  Section('date and time');
  DateTime('2024-03-05 14:07:09', '2024-03-05 14:07:09.000');
  DateTime('2024-03-05', '2024-03-05 00:00:00.000');
  DateTime('2024-03-05   2:07 pm', '2024-03-05 14:07:00.000');
  DateTime('14:07', '1899-12-30 14:07:00.000');
  DateTime('1899-12-29 06:00', '1899-12-29 06:00:00.000');
  DateTime('2024-03-05 25:00', 'invalid');
  DateTime('2024-03-32 10:00', 'invalid');
  DateTime('', 'invalid');
  DateTime('soon', 'invalid');
  FS := InvariantFormatSettings;
  FS.TimeSeparator := '.';
  FS.TimeAMString := 'vorm';
  FS.TimePMString := 'nachm';
  CheckEqB(True, TryStrToDateTime('2024-03-05 2.07 nachm', DT, FS), 'settings: separator and PM string');
  CheckEqS('2024-03-05 14:07:00.000', DateText(DT), 'settings: value');
  CheckEqB(True, TryStrToTime('2:07:09 vorm', DT, FS), 'settings: AM string');
  CheckEqS('1899-12-30 02:07:09.000', DateText(DT), 'settings: AM value');
  CheckEqB(True, TryStrToTime('2:07 PM', DT, FS), 'PM is accepted with any settings');
  CheckEqS('1899-12-30 14:07:00.000', DateText(DT), 'PM value');
end;

procedure TestDefaults;
var
  FS: TFormatSettings;
begin
  Section('Def forms');
  FS := InvariantFormatSettings;
  CheckEqS('2024-03-05 00:00:00.000', DateText(StrToDateDef('2024-03-05', 7)), 'StrToDateDef valid');
  CheckEqS('1900-01-06 00:00:00.000', DateText(StrToDateDef('x', 7)), 'StrToDateDef default');
  CheckEqS('1900-01-06 00:00:00.000', DateText(StrToDateDef('x', 7, FS)), 'StrToDateDef(FS) default');
  CheckEqS('1899-12-30 06:00:00.000', DateText(StrToTimeDef('6:00', 7)), 'StrToTimeDef valid');
  CheckEqS('1900-01-06 00:00:00.000', DateText(StrToTimeDef('6:00 xm', 7, FS)), 'StrToTimeDef(FS) default');
  CheckEqS('2024-03-05 06:00:00.000', DateText(StrToDateTimeDef('2024-03-05 6:00', 7)), 'StrToDateTimeDef valid');
  CheckEqS('1900-01-06 00:00:00.000', DateText(StrToDateTimeDef('', 7, FS)), 'StrToDateTimeDef(FS) default');
end;

begin
  TestDates;
  TestTimes;
  TestDateTimes;
  TestDefaults;
  Done;
end.
