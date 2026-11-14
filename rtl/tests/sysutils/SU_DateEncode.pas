// covers: RTL-240, RTL-241, RTL-242
program SU_DateEncode;
{ sysutils.md section 6: TDateTime encoding and decoding (proleptic Gregorian, negative values, rounding of the time
  of day), DayOfWeek, IncMonth, IsLeapYear and the TSystemTime conversions. }
uses
  Windows, SysUtils, TestKit;

function DateText(DT: TDateTime): string;
var
  Y, M, D: Word;
begin
  DecodeDate(DT, Y, M, D);
  Result := IntToStr(Y) + '-' + IntToStr(M) + '-' + IntToStr(D);
end;

function TimeText(DT: TDateTime): string;
var
  H, N, S, MS: Word;
begin
  DecodeTime(DT, H, N, S, MS);
  Result := IntToStr(H) + ':' + IntToStr(N) + ':' + IntToStr(S) + '.' + IntToStr(MS);
end;

procedure Enc(Y, M, D: Word; Expected: Double);
var
  DT: TDateTime;
begin
  if TryEncodeDate(Y, M, D, DT) then
    CheckEqF(Expected, DT, 0, 'TryEncodeDate ' + IntToStr(Y) + '-' + IntToStr(M) + '-' + IntToStr(D))
  else
    Check(False, 'TryEncodeDate ' + IntToStr(Y) + '-' + IntToStr(M) + '-' + IntToStr(D));
end;

procedure NoDate(Y, M, D: Word);
var
  DT: TDateTime;
begin
  Check(not TryEncodeDate(Y, M, D, DT), 'invalid ' + IntToStr(Y) + '-' + IntToStr(M) + '-' + IntToStr(D));
end;

var
  DT: TDateTime;
  Y, M, D, DOW: Word;
  ST: TSystemTime;
  I, Bad: Integer;
begin
  Section('constants');
  CheckEqI(693594, DateDelta, 'DateDelta');
  CheckEqI(25569, UnixDateDelta, 'UnixDateDelta');
  CheckEqI(86400000, MSecsPerDay, 'MSecsPerDay');
  CheckEqI(86400, SecsPerDay, 'SecsPerDay');
  CheckEqI(1440, MinsPerDay, 'MinsPerDay');
  CheckEqF(-693593.0, MinDateTime, 0, 'MinDateTime');
  CheckEqF(2958465.99999999, MaxDateTime, 0, 'MaxDateTime');

  Section('TryEncodeDate');
  Enc(1899, 12, 30, 0);
  Enc(1900, 1, 1, 2);
  Enc(1970, 1, 1, 25569);
  Enc(2000, 2, 29, 36585);
  Enc(1, 1, 1, -693593);
  Enc(9999, 12, 31, 2958465);
  Enc(1899, 12, 29, -1);
  NoDate(0, 1, 1);
  NoDate(10000, 1, 1);
  NoDate(2001, 2, 29);
  NoDate(1900, 2, 29);
  NoDate(2000, 13, 1);
  NoDate(2000, 0, 1);
  NoDate(2000, 4, 31);
  NoDate(2000, 1, 0);

  Section('DecodeDate');
  CheckEqS('1899-12-30', DateText(0), 'DecodeDate 0');
  CheckEqS('1-1-1', DateText(-693593), 'DecodeDate min');
  CheckEqS('9999-12-31', DateText(2958465.5), 'DecodeDate max');
  CheckEqS('2024-2-29', DateText(EncodeDate(2024, 2, 29) + 0.75), 'DecodeDate leap day with time');
  CheckEqS('1899-12-29', DateText(-1.25), 'DecodeDate negative uses the integer part');
  Bad := 0;
  for I := -693593 to 2958465 do
    if I mod 997 = 0 then
    begin
      DecodeDate(I, Y, M, D);
      if EncodeDate(Y, M, D) <> I then
        Bad := Bad + 1;
    end;
  CheckEqI(0, Bad, 'round trip every 997th day');
  CheckEqB(True, DecodeDateFully(EncodeDate(2000, 1, 1), Y, M, D, DOW), 'DecodeDateFully leap year');
  CheckEqI(7, DOW, 'DecodeDateFully 2000-01-01 is a Saturday');
  CheckEqB(False, DecodeDateFully(EncodeDate(1900, 6, 15), Y, M, D, DOW), 'DecodeDateFully 1900 not leap');

  Section('time');
  Check(TryEncodeTime(12, 30, 15, 500, DT), 'TryEncodeTime');
  CheckEqF((12 * 3600000 + 30 * 60000 + 15 * 1000 + 500) / 86400000, DT, 0, 'TryEncodeTime value');
  Check(not TryEncodeTime(24, 0, 0, 0, DT), 'hour 24 invalid');
  Check(not TryEncodeTime(0, 60, 0, 0, DT), 'minute 60 invalid');
  Check(not TryEncodeTime(0, 0, 60, 0, DT), 'second 60 invalid');
  Check(not TryEncodeTime(0, 0, 0, 1000, DT), 'ms 1000 invalid');
  CheckEqS('12:30:15.500', TimeText(EncodeTime(12, 30, 15, 500)), 'DecodeTime');
  CheckEqS('23:59:59.999', TimeText(EncodeTime(23, 59, 59, 999)), 'DecodeTime last ms');
  CheckEqS('6:0:0.0', TimeText(-1.25), 'DecodeTime negative: absolute fraction');
  CheckEqS('23:59:59.999', TimeText(0.99999999999), 'DecodeTime clamps, no carry');
  CheckEqS('0:0:0.0', TimeText(5), 'DecodeTime midnight');

  Section('DayOfWeek / IsLeapYear');
  CheckEqI(7, DayOfWeek(0), '1899-12-30 Saturday');
  CheckEqI(1, DayOfWeek(EncodeDate(2024, 3, 3)), '2024-03-03 Sunday');
  CheckEqI(2, DayOfWeek(EncodeDate(1, 1, 1)), '0001-01-01 Monday');
  CheckEqI(6, DayOfWeek(-1.5), 'negative date Friday');
  CheckEqB(True, IsLeapYear(2000), 'IsLeapYear 2000');
  CheckEqB(False, IsLeapYear(1900), 'IsLeapYear 1900');
  CheckEqB(True, IsLeapYear(2024), 'IsLeapYear 2024');
  CheckEqB(False, IsLeapYear(2023), 'IsLeapYear 2023');

  Section('IncMonth');
  CheckEqS('2024-2-29', DateText(IncMonth(EncodeDate(2024, 1, 31))), 'IncMonth clips to Feb 29');
  CheckEqS('2023-2-28', DateText(IncMonth(EncodeDate(2023, 1, 31))), 'IncMonth clips to Feb 28');
  CheckEqS('2025-1-15', DateText(IncMonth(EncodeDate(2024, 11, 15), 2)), 'IncMonth over the year end');
  CheckEqS('2023-12-15', DateText(IncMonth(EncodeDate(2024, 3, 15), -3)), 'IncMonth backwards');
  CheckEqS('2000-2-28', DateText(IncMonth(EncodeDate(2001, 2, 28), -12)), 'IncMonth -12');
  CheckEqS('2001-2-28', DateText(IncMonth(EncodeDate(2000, 2, 29), 12)), 'IncMonth +12 clips');
  CheckEqS('18:0:0.0', TimeText(IncMonth(EncodeDate(2024, 1, 1) + 0.75, 1)), 'IncMonth keeps the time');

  Section('TSystemTime');
  DateTimeToSystemTime(EncodeDate(2024, 3, 3) + EncodeTime(13, 14, 15, 16), ST);
  CheckEqS('2024-3-3 0 13:14:15.16', IntToStr(ST.wYear) + '-' + IntToStr(ST.wMonth) + '-' + IntToStr(ST.wDay) + ' ' +
    IntToStr(ST.wDayOfWeek) + ' ' + IntToStr(ST.wHour) + ':' + IntToStr(ST.wMinute) + ':' + IntToStr(ST.wSecond) +
    '.' + IntToStr(ST.wMilliseconds), 'DateTimeToSystemTime');
  DT := SystemTimeToDateTime(ST);
  CheckEqF(EncodeDate(2024, 3, 3) + EncodeTime(13, 14, 15, 16), DT, 0, 'SystemTimeToDateTime');
  Done;
end.
