// covers: RTL-630
program DU_Between;
{ other-units.md section 4 (task R-057): whole-unit differences (truncated, order-independent, calendar months),
  fractional spans, comparison at millisecond resolution, Today / Yesterday / Tomorrow (sanity only, RTL-903), Unix
  time (UTC exact; local only as a round trip), ISO 8601 output and parsing (every accepted form, offsets, errors). }
uses
  SysUtils, DateUtils, TestKit;

var
  Msg: string;

function F(const D: TDateTime): string;
begin
  Result := FormatDateTime('yyyy-mm-dd hh:nn:ss.zzz', D);
end;

function DT(Y, M, D, H, N, S, MS: Word): TDateTime;
begin
  Result := EncodeDateTime(Y, M, D, H, N, S, MS);
end;

procedure TestBetween;
var
  A, B: TDateTime;
begin
  Section('differences');
  A := DT(2024, 1, 15, 12, 0, 0, 0);
  CheckEqI(1, MonthsBetween(A, DT(2024, 3, 15, 11, 59, 59, 999)), 'MonthsBetween one short of 2');
  CheckEqI(2, MonthsBetween(A, DT(2024, 3, 15, 12, 0, 0, 0)), 'MonthsBetween exactly 2');
  CheckEqI(2, MonthsBetween(DT(2024, 3, 15, 12, 0, 0, 0), A), 'MonthsBetween reversed');
  CheckEqI(1, MonthsBetween(DT(2024, 1, 31, 0, 0, 0, 0), DT(2024, 2, 29, 0, 0, 0, 0)), 'MonthsBetween Jan 31 - Feb 29');
  CheckEqI(0, MonthsBetween(A, A), 'MonthsBetween same instant');
  CheckEqI(1, YearsBetween(DT(2020, 2, 29, 0, 0, 0, 0), DT(2021, 2, 28, 0, 0, 0, 0)), 'YearsBetween Feb 29 - Feb 28');
  CheckEqI(0, YearsBetween(DT(2020, 3, 1, 0, 0, 0, 0), DT(2021, 2, 28, 23, 59, 59, 999)), 'YearsBetween short');
  CheckEqI(10, YearsBetween(DT(2034, 6, 1, 0, 0, 0, 0), DT(2024, 6, 1, 0, 0, 0, 0)), 'YearsBetween reversed');
  CheckEqI(1, DaysBetween(DT(2024, 1, 1, 12, 0, 0, 0), DT(2024, 1, 3, 11, 59, 59, 999)), 'DaysBetween truncated');
  CheckEqI(2, WeeksBetween(DT(2024, 1, 1, 0, 0, 0, 0), DT(2024, 1, 15, 0, 0, 0, 0)), 'WeeksBetween');
  CheckEqI(1, WeeksBetween(DT(2024, 1, 1, 0, 0, 0, 0), DT(2024, 1, 14, 23, 0, 0, 0)), 'WeeksBetween truncated');
  B := IncMinute(A, 90);
  CheckEqI(1, HoursBetween(A, B), 'HoursBetween');
  CheckEqI(90, MinutesBetween(B, A), 'MinutesBetween reversed');
  CheckEqI(5400, SecondsBetween(A, B), 'SecondsBetween');
  CheckEqI(5400000, MilliSecondsBetween(A, B), 'MilliSecondsBetween');
  CheckEqI(1, MilliSecondsBetween(A, IncMilliSecond(A)), 'MilliSecondsBetween 1');
  CheckEqI(24, HoursBetween(-1.25, 0.25), 'HoursBetween across 1899-12-30 (instants, not raw values)');
  CheckEqI(1, DaysBetween(-1.25, 0.25), 'DaysBetween across 1899-12-30');
end;

procedure TestSpans;
var
  A: TDateTime;
begin
  Section('spans');
  A := EncodeDate(2024, 1, 1);
  CheckEqF(1.5, DaySpan(A, IncHour(A, 36)), 0, 'DaySpan');
  CheckEqF(1.5, DaySpan(IncHour(A, 36), A), 0, 'DaySpan reversed');
  CheckEqF(36, HourSpan(A, IncHour(A, 36)), 0, 'HourSpan');
  CheckEqF(2160, MinuteSpan(A, IncHour(A, 36)), 0, 'MinuteSpan');
  CheckEqF(129600, SecondSpan(A, IncHour(A, 36)), 0, 'SecondSpan');
  CheckEqF(1, YearSpan(A, IncHour(A, 8766)), 0, 'YearSpan: 365.25 days');
  CheckEqF(1, MonthSpan(A, IncMinute(A, 43830)), 0, 'MonthSpan: 30.4375 days');
  CheckEqF(1, DaySpan(-1.25, 0.25), 0, 'DaySpan across 1899-12-30');
end;

procedure TestCompare;
var
  A, T: TDateTime;
begin
  Section('comparison');
  A := DT(2024, 1, 1, 23, 0, 0, 0);
  CheckEqI(-1, CompareDateTime(A, IncMilliSecond(A)), 'CompareDateTime less');
  CheckEqI(1, CompareDateTime(IncMilliSecond(A), A), 'CompareDateTime greater');
  CheckEqI(0, CompareDateTime(A, A + 1E-10), 'CompareDateTime below a millisecond');
  CheckEqI(0, CompareDate(A, DT(2024, 1, 1, 1, 0, 0, 0)), 'CompareDate same day');
  CheckEqI(-1, CompareDate(A, DT(2024, 1, 2, 0, 0, 0, 0)), 'CompareDate earlier day');
  CheckEqI(1, CompareTime(A, DT(2020, 5, 5, 1, 0, 0, 0)), 'CompareTime ignores the date');
  CheckEqI(0, CompareTime(A, DT(2020, 5, 5, 23, 0, 0, 0)), 'CompareTime equal');
  CheckEqB(True, SameDateTime(A, A + 1E-10), 'SameDateTime');
  CheckEqB(False, SameDateTime(A, IncMilliSecond(A)), 'SameDateTime 1 ms apart');
  CheckEqB(True, SameDate(A, DT(2024, 1, 1, 0, 0, 0, 0)), 'SameDate');
  CheckEqB(True, SameTime(A, DT(1999, 9, 9, 23, 0, 0, 0)), 'SameTime');
  CheckEqB(True, IsSameDay(A, DT(2024, 1, 1, 0, 0, 0, 1)), 'IsSameDay');
  CheckEqB(False, IsSameDay(A, DT(2024, 1, 2, 0, 0, 0, 0)), 'IsSameDay next day');
  CheckEqB(True, IsSameDay(-1.25, -1.75), 'IsSameDay negative values');
  T := Today;
  Check(T = Int(T), 'Today has no time');
  Check(T >= EncodeDate(2026, 1, 1), 'Today sanity bound');
  Check((Yesterday = T - 1) or (Yesterday = T), 'Yesterday');
  Check((Tomorrow = T + 1) or (Tomorrow = T + 2), 'Tomorrow');
  Check(IsToday(T) or (Today <> T), 'IsToday(Today)');
  CheckEqB(False, IsToday(T - 2), 'IsToday two days ago');
end;

procedure TestUnix;
var
  X: TDateTime;
  L: Int64;
begin
  Section('Unix time');
  CheckEqI(0, DateTimeToUnix(EncodeDate(1970, 1, 1)), 'epoch');
  CheckEqI(1000000000, DateTimeToUnix(DT(2001, 9, 9, 1, 46, 40, 0)), '1E9 seconds');
  CheckEqI(-1, DateTimeToUnix(DT(1969, 12, 31, 23, 59, 59, 0)), 'one second before');
  CheckEqI(0, DateTimeToUnix(DT(1970, 1, 1, 0, 0, 0, 500)), 'half a second after: whole seconds down');
  CheckEqI(-1, DateTimeToUnix(DT(1969, 12, 31, 23, 59, 59, 500)), 'half a second before: whole seconds down');
  CheckEqI(-2209161600, DateTimeToUnix(0), 'TDateTime 0');
  CheckEqS('2001-09-09 01:46:40.000', F(UnixToDateTime(1000000000)), 'UnixToDateTime');
  CheckEqS('1969-12-31 00:00:00.000', F(UnixToDateTime(-86400)), 'UnixToDateTime negative');
  X := DT(2024, 7, 1, 12, 30, 15, 0);
  L := DateTimeToUnix(X, False);
  CheckEqI(0, (L - DateTimeToUnix(X)) mod 60, 'local offset is whole minutes');
  CheckEqS(F(X), F(UnixToDateTime(L, False)), 'local round trip');
  CheckEqS(F(X), F(UnixToDateTime(DateTimeToUnix(X))), 'UTC round trip');
end;

procedure CheckBad(const S: string);
var
  V: TDateTime;
begin
  Msg := 'no exception';
  try
    ISO8601ToDate(S);
  except
    on E: Exception do
      Msg := E.ClassName + ': ' + E.Message;
  end;
  CheckEqS('EConvertError: ''' + S + ''' is not a valid date and time', Msg, 'rejects ' + QuoteStr(S));
  CheckEqB(False, TryISO8601ToDate(S, V), 'TryISO8601ToDate rejects ' + QuoteStr(S));
end;

procedure TestISO;
var
  X, V: TDateTime;
  S: string;
begin
  Section('ISO 8601');
  X := DT(2024, 2, 29, 13, 45, 30, 250);
  CheckEqS('2024-02-29T13:45:30.250Z', DateToISO8601(X), 'DateToISO8601 UTC');
  CheckEqS('0001-01-01T00:00:00.000Z', DateToISO8601(EncodeDate(1, 1, 1)), 'DateToISO8601 year 1');
  S := DateToISO8601(X, False);
  CheckEqI(29, Length(S), 'DateToISO8601 local: length');
  CheckEqS('2024-02-29T13:45:30.250', Copy(S, 1, 23), 'DateToISO8601 local: the clock is unchanged');
  Check((S[24] = '+') or (S[24] = '-'), 'DateToISO8601 local: offset sign');
  CheckEqS(':', Copy(S, 27, 1), 'DateToISO8601 local: offset colon');
  CheckEqS(F(X), F(ISO8601ToDate(S, False)), 'local text back to local time');
  CheckEqS(F(X), F(ISO8601ToDate(DateToISO8601(X))), 'UTC round trip');
  CheckEqS('2024-02-29 00:00:00.000', F(ISO8601ToDate('2024-02-29')), 'date only');
  CheckEqS('2024-02-29 13:45:00.000', F(ISO8601ToDate('2024-02-29T13:45')), 'hours and minutes');
  CheckEqS('2024-02-29 13:45:30.000', F(ISO8601ToDate('2024-02-29T13:45:30')), 'seconds');
  CheckEqS('2024-02-29 13:45:30.250', F(ISO8601ToDate('2024-02-29T13:45:30.250Z')), 'milliseconds and Z');
  CheckEqS('2024-02-29 13:45:30.500', F(ISO8601ToDate('2024-02-29T13:45:30.5Z')), 'one fraction digit');
  CheckEqS('2024-02-29 11:45:30.000', F(ISO8601ToDate('2024-02-29T13:45:30+02:00')), '+hh:mm to UTC');
  CheckEqS('2024-02-29 02:30:00.000', F(ISO8601ToDate('2024-02-29T01:00:00-0130')), '-hhmm to UTC');
  CheckEqS('2024-02-28 23:30:00.000', F(ISO8601ToDate('2024-02-29T00:30:00+01:00')), 'offset crosses midnight');
  CheckEqB(True, TryISO8601ToDate('2024-02-29T13:45Z', V), 'TryISO8601ToDate');
  CheckEqS('2024-02-29 13:45:00.000', F(V), 'TryISO8601ToDate value');
  CheckBad('2024-02-30');
  CheckBad('2024-2-29');
  CheckBad('2024-02-29T25:00');
  CheckBad('2024-02-29T13');
  CheckBad('2024-02-29T13:45:30.250X');
  CheckBad('2024-02-29T13:45:30.1234');
  CheckBad('2024-02-29T13:45+2');
  CheckBad('');
end;

begin
  TestBetween;
  TestSpans;
  TestCompare;
  TestUnix;
  TestISO;
  Done;
end.
