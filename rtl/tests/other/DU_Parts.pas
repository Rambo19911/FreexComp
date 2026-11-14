// covers: RTL-630
program DU_Parts;
{ other-units.md section 4 (task R-056): parts, validity, construction (EncodeDateTime errors), recode, boundaries
  (end = last millisecond, week from Monday), DayOfTheWeek / WeekOfTheYear (ISO 8601) / DayOfTheYear, and the
  increments (IncYear clipping Feb 29, millisecond arithmetic across the negative TDateTime range, RTL-630). }
uses
  SysUtils, DateUtils, TestKit;

var
  DT: TDateTime;
  Msg: string;

function F(const D: TDateTime): string;
begin
  Result := FormatDateTime('yyyy-mm-dd hh:nn:ss.zzz', D);
end;

procedure Caught(E: Exception);
begin
  Msg := E.ClassName + ': ' + E.Message;
end;

procedure Expect(const Expected, Name: string);
begin
  CheckEqS(Expected, Msg, Name);
  Msg := 'no exception';
end;

procedure TestParts;
begin
  Section('parts');
  CheckEqI(2024, YearOf(DT), 'YearOf');
  CheckEqI(2, MonthOf(DT), 'MonthOf');
  CheckEqI(29, DayOf(DT), 'DayOf');
  CheckEqI(13, HourOf(DT), 'HourOf');
  CheckEqI(45, MinuteOf(DT), 'MinuteOf');
  CheckEqI(30, SecondOf(DT), 'SecondOf');
  CheckEqI(250, MilliSecondOf(DT), 'MilliSecondOf');
  CheckEqF(EncodeDate(2024, 2, 29), DateOf(DT), 0, 'DateOf');
  CheckEqS('13:45:30.250', FormatDateTime('hh:nn:ss.zzz', TimeOf(DT)), 'TimeOf');
  CheckEqF(-1, DateOf(-1.25), 0, 'DateOf(-1.25)');
  CheckEqF(0.25, TimeOf(-1.25), 0, 'TimeOf(-1.25)');
  CheckEqI(6, HourOf(-1.25), 'HourOf(-1.25)');
end;

procedure TestValidity;
begin
  Section('validity');
  CheckEqB(True, IsValidDate(2024, 2, 29), 'IsValidDate leap day');
  CheckEqB(False, IsValidDate(2023, 2, 29), 'IsValidDate no leap day');
  CheckEqB(False, IsValidDate(0, 1, 1), 'IsValidDate year 0');
  CheckEqB(True, IsValidDate(9999, 12, 31), 'IsValidDate 9999-12-31');
  CheckEqB(False, IsValidDate(2024, 13, 1), 'IsValidDate month 13');
  CheckEqB(True, IsValidTime(23, 59, 59, 999), 'IsValidTime last millisecond');
  CheckEqB(False, IsValidTime(24, 0, 0, 0), 'IsValidTime hour 24');
  CheckEqB(False, IsValidTime(12, 60, 0, 0), 'IsValidTime minute 60');
  CheckEqB(False, IsValidTime(12, 0, 0, 1000), 'IsValidTime ms 1000');
  CheckEqB(True, IsValidDateTime(2024, 2, 29, 23, 59, 59, 999), 'IsValidDateTime');
  CheckEqB(False, IsValidDateTime(2024, 2, 30, 0, 0, 0, 0), 'IsValidDateTime bad date');
  CheckEqB(False, IsValidDateTime(2024, 2, 29, 0, 0, 60, 0), 'IsValidDateTime bad time');
  CheckEqB(True, IsInLeapYear(DT), 'IsInLeapYear');
  CheckEqB(False, IsInLeapYear(EncodeDate(1900, 6, 1)), 'IsInLeapYear 1900');
  CheckEqI(29, DaysInMonth(DT), 'DaysInMonth');
  CheckEqI(28, DaysInAMonth(2023, 2), 'DaysInAMonth(2023, 2)');
  CheckEqI(29, DaysInAMonth(2000, 2), 'DaysInAMonth(2000, 2)');
  CheckEqI(28, DaysInAMonth(1900, 2), 'DaysInAMonth(1900, 2)');
  CheckEqI(31, DaysInAMonth(2023, 12), 'DaysInAMonth(2023, 12)');
  CheckEqI(30, DaysInAMonth(2023, 4), 'DaysInAMonth(2023, 4)');
  CheckEqI(366, DaysInYear(DT), 'DaysInYear');
  CheckEqI(365, DaysInAYear(2023), 'DaysInAYear(2023)');
end;

procedure TestConstruction;
var
  V: TDateTime;
  Y, M, D, H, N, S, MS: Word;
begin
  Section('construction');
  CheckEqS('2024-02-29 13:45:30.250', F(EncodeDateTime(2024, 2, 29, 13, 45, 30, 250)), 'EncodeDateTime');
  CheckEqF(-1.25, EncodeDateTime(1899, 12, 29, 6, 0, 0, 0), 0, 'EncodeDateTime before 1899-12-30');
  CheckEqF(0.25, EncodeDateTime(1899, 12, 30, 6, 0, 0, 0), 0, 'EncodeDateTime on 1899-12-30');
  Msg := 'no exception';
  try EncodeDateTime(2023, 2, 29, 0, 0, 0, 0); except on E: Exception do Caught(E); end;
  Expect('EConvertError: Invalid argument to date encode', 'EncodeDateTime bad date');
  try EncodeDateTime(2024, 1, 1, 24, 0, 0, 0); except on E: Exception do Caught(E); end;
  Expect('EConvertError: Invalid argument to time encode', 'EncodeDateTime bad time');
  CheckEqB(True, TryEncodeDateTime(2024, 2, 29, 1, 2, 3, 4, V), 'TryEncodeDateTime valid');
  CheckEqS('2024-02-29 01:02:03.004', F(V), 'TryEncodeDateTime value');
  CheckEqB(False, TryEncodeDateTime(2024, 2, 29, 1, 2, 3, 1000, V), 'TryEncodeDateTime invalid');
  DecodeDateTime(DT, Y, M, D, H, N, S, MS);
  CheckEqS('2024 2 29 13 45 30 250', IntToStr(Y) + ' ' + IntToStr(M) + ' ' + IntToStr(D) + ' ' + IntToStr(H) + ' ' +
    IntToStr(N) + ' ' + IntToStr(S) + ' ' + IntToStr(MS), 'DecodeDateTime');
  DecodeDateTime(-1.25, Y, M, D, H, N, S, MS);
  CheckEqS('1899 12 29 6', IntToStr(Y) + ' ' + IntToStr(M) + ' ' + IntToStr(D) + ' ' + IntToStr(H),
    'DecodeDateTime(-1.25)');
end;

procedure TestRecode;
begin
  Section('recode');
  CheckEqS('2028-02-29 13:45:30.250', F(RecodeYear(DT, 2028)), 'RecodeYear');
  CheckEqS('2024-03-29 13:45:30.250', F(RecodeMonth(DT, 3)), 'RecodeMonth');
  CheckEqS('2024-02-01 13:45:30.250', F(RecodeDay(DT, 1)), 'RecodeDay');
  CheckEqS('2024-02-29 00:45:30.250', F(RecodeHour(DT, 0)), 'RecodeHour');
  CheckEqS('2024-02-29 13:00:30.250', F(RecodeMinute(DT, 0)), 'RecodeMinute');
  CheckEqS('2024-02-29 13:45:59.250', F(RecodeSecond(DT, 59)), 'RecodeSecond');
  CheckEqS('2024-02-29 13:45:30.999', F(RecodeMilliSecond(DT, 999)), 'RecodeMilliSecond');
  CheckEqS('2000-01-02 13:45:30.250', F(RecodeDate(DT, 2000, 1, 2)), 'RecodeDate keeps the time');
  CheckEqS('2024-02-29 01:02:03.004', F(RecodeTime(DT, 1, 2, 3, 4)), 'RecodeTime keeps the date');
  CheckEqS('2001-02-03 04:05:06.007', F(RecodeDateTime(DT, 2001, 2, 3, 4, 5, 6, 7)), 'RecodeDateTime');
  Msg := 'no exception';
  try RecodeYear(DT, 2023); except on E: Exception do Caught(E); end;
  Expect('EConvertError: Invalid argument to date encode', 'RecodeYear to a year without Feb 29');
  try RecodeHour(DT, 24); except on E: Exception do Caught(E); end;
  Expect('EConvertError: Invalid argument to time encode', 'RecodeHour(24)');
end;

procedure TestBoundaries;
var
  Sunday: TDateTime;
begin
  Section('boundaries');
  CheckEqS('2024-01-01 00:00:00.000', F(StartOfTheYear(DT)), 'StartOfTheYear');
  CheckEqS('2024-12-31 23:59:59.999', F(EndOfTheYear(DT)), 'EndOfTheYear');
  CheckEqS('2024-02-01 00:00:00.000', F(StartOfTheMonth(DT)), 'StartOfTheMonth');
  CheckEqS('2024-02-29 23:59:59.999', F(EndOfTheMonth(DT)), 'EndOfTheMonth');
  CheckEqS('2024-02-26 00:00:00.000', F(StartOfTheWeek(DT)), 'StartOfTheWeek: Monday');
  CheckEqS('2024-03-03 23:59:59.999', F(EndOfTheWeek(DT)), 'EndOfTheWeek: Sunday');
  Sunday := EncodeDateTime(2024, 3, 3, 8, 0, 0, 0);
  CheckEqS('2024-02-26 00:00:00.000', F(StartOfTheWeek(Sunday)), 'StartOfTheWeek of a Sunday');
  CheckEqS('2024-03-03 23:59:59.999', F(EndOfTheWeek(Sunday)), 'EndOfTheWeek of a Sunday');
  CheckEqS('2024-02-29 00:00:00.000', F(StartOfTheDay(DT)), 'StartOfTheDay');
  CheckEqS('2024-02-29 23:59:59.999', F(EndOfTheDay(DT)), 'EndOfTheDay');
  CheckEqF(-1, StartOfTheDay(-1.25), 0, 'StartOfTheDay(-1.25)');
  CheckEqS('1899-12-29 23:59:59.999', F(EndOfTheDay(-1.25)), 'EndOfTheDay(-1.25)');
end;

procedure TestWeek;
begin
  Section('week');
  CheckEqI(4, DayOfTheWeek(DT), 'DayOfTheWeek Thursday');
  CheckEqI(1, DayOfTheWeek(EncodeDate(2024, 2, 26)), 'DayOfTheWeek Monday');
  CheckEqI(7, DayOfTheWeek(EncodeDate(2024, 3, 3)), 'DayOfTheWeek Sunday');
  CheckEqI(5, DayOfTheWeek(-1.25), 'DayOfTheWeek 1899-12-29 (Friday)');
  CheckEqI(60, DayOfTheYear(DT), 'DayOfTheYear');
  CheckEqI(365, DayOfTheYear(EncodeDate(2023, 12, 31)), 'DayOfTheYear 2023-12-31');
  CheckEqI(1, DayOfTheYear(EncodeDate(2024, 1, 1)), 'DayOfTheYear Jan 1');
  CheckEqI(9, WeekOfTheYear(DT), 'WeekOfTheYear');
  CheckEqI(53, WeekOfTheYear(EncodeDate(2021, 1, 1)), 'WeekOfTheYear 2021-01-01 (week 53 of 2020)');
  CheckEqI(53, WeekOfTheYear(EncodeDate(2020, 12, 31)), 'WeekOfTheYear 2020-12-31');
  CheckEqI(1, WeekOfTheYear(EncodeDate(2024, 12, 30)), 'WeekOfTheYear 2024-12-30 (week 1 of 2025)');
  CheckEqI(1, WeekOfTheYear(EncodeDate(2026, 1, 1)), 'WeekOfTheYear 2026-01-01');
  CheckEqI(52, WeekOfTheYear(EncodeDate(2023, 1, 1)), 'WeekOfTheYear 2023-01-01 (week 52 of 2022)');
end;

procedure TestIncrements;
var
  A: Int64;
begin
  Section('increments');
  CheckEqS('2025-02-28 13:45:30.250', F(IncYear(DT)), 'IncYear clips Feb 29');
  CheckEqS('2028-02-29 13:45:30.250', F(IncYear(DT, 4)), 'IncYear(4)');
  CheckEqS('2023-02-28 13:45:30.250', F(IncYear(DT, -1)), 'IncYear(-1)');
  CheckEqS('2024-03-07 13:45:30.250', F(IncWeek(DT)), 'IncWeek');
  CheckEqS('2023-12-31 13:45:30.250', F(IncDay(DT, -60)), 'IncDay(-60)');
  CheckEqS('2024-03-01 00:45:30.250', F(IncHour(DT, 11)), 'IncHour(11)');
  CheckEqS('2024-02-29 12:59:30.250', F(IncMinute(DT, -46)), 'IncMinute(-46)');
  CheckEqS('2024-02-29 13:46:00.250', F(IncSecond(DT, 30)), 'IncSecond(30)');
  CheckEqS('2024-02-29 13:45:31.000', F(IncMilliSecond(DT, 750)), 'IncMilliSecond(750)');
  A := 86400000;
  CheckEqS('2025-03-01 13:45:30.250', F(IncMilliSecond(DT, A * 366)), 'IncMilliSecond with an Int64 count');
  CheckEqF(-1.875, IncHour(EncodeDateTime(1899, 12, 30, 3, 0, 0, 0), -6), 0, 'IncHour across 1899-12-30');
  CheckEqF(0.125, IncHour(-1.875, 6), 0, 'IncHour back across 1899-12-30');
  CheckEqF(-2.5, IncDay(-1.5, -1), 0, 'IncDay on a negative value');
end;

begin
  DT := EncodeDateTime(2024, 2, 29, 13, 45, 30, 250);
  TestParts;
  TestValidity;
  TestConstruction;
  TestRecode;
  TestBoundaries;
  TestWeek;
  TestIncrements;
  Done;
end.
