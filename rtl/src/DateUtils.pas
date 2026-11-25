// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ DateUtils - date and time arithmetic (docs: rtl/other-units.md section 4). RTL-630: all arithmetic works on integer
  milliseconds (date part * 86 400 000 + time-of-day milliseconds per RTL-240 / RTL-242) and converts back, so a
  negative TDateTime (day toward zero, absolute fraction = time of day) is handled like any other instant. Weeks
  start on Monday (ISO 8601). Win32 reference: GetTimeZoneInformation (learn.microsoft.com). }
unit DateUtils;

interface

uses
  Windows, SysUtils, Types, RTLConsts;

{ ---------------------------------------------------------------------------------------------------- parts }
function YearOf(const AValue: TDateTime): Word;
function MonthOf(const AValue: TDateTime): Word;
function DayOf(const AValue: TDateTime): Word;
function HourOf(const AValue: TDateTime): Word;
function MinuteOf(const AValue: TDateTime): Word;
function SecondOf(const AValue: TDateTime): Word;
function MilliSecondOf(const AValue: TDateTime): Word;
{ Int(AValue) }
function DateOf(const AValue: TDateTime): TDateTime;
{ Abs(Frac(AValue)) }
function TimeOf(const AValue: TDateTime): TDateTime;

{ ------------------------------------------------------------------------------------------------- validity }
function IsValidDate(const AYear, AMonth, ADay: Word): Boolean;
function IsValidTime(const AHour, AMinute, ASecond, AMilliSecond: Word): Boolean;
function IsValidDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word): Boolean;
function IsInLeapYear(const AValue: TDateTime): Boolean;
function DaysInMonth(const AValue: TDateTime): Word;
function DaysInAMonth(const AYear, AMonth: Word): Word;
function DaysInYear(const AValue: TDateTime): Word;
function DaysInAYear(const AYear: Word): Word;

{ --------------------------------------------------------------------------------------------- construction }
{ an invalid date or time raises EConvertError 'Invalid argument to date encode' / '... time encode' }
function EncodeDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word): TDateTime;
function TryEncodeDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word;
  out AValue: TDateTime): Boolean;
procedure DecodeDateTime(const AValue: TDateTime; out AYear, AMonth, ADay, AHour, AMinute, ASecond,
  AMilliSecond: Word);
{ one field replaced; an invalid result raises EConvertError as EncodeDateTime }
function RecodeYear(const AValue: TDateTime; const AYear: Word): TDateTime;
function RecodeMonth(const AValue: TDateTime; const AMonth: Word): TDateTime;
function RecodeDay(const AValue: TDateTime; const ADay: Word): TDateTime;
function RecodeHour(const AValue: TDateTime; const AHour: Word): TDateTime;
function RecodeMinute(const AValue: TDateTime; const AMinute: Word): TDateTime;
function RecodeSecond(const AValue: TDateTime; const ASecond: Word): TDateTime;
function RecodeMilliSecond(const AValue: TDateTime; const AMilliSecond: Word): TDateTime;
function RecodeDate(const AValue: TDateTime; const AYear, AMonth, ADay: Word): TDateTime;
function RecodeTime(const AValue: TDateTime; const AHour, AMinute, ASecond, AMilliSecond: Word): TDateTime;
function RecodeDateTime(const AValue: TDateTime; const AYear, AMonth, ADay, AHour, AMinute, ASecond,
  AMilliSecond: Word): TDateTime;

{ ----------------------------------------------------------------------------------------------- boundaries }
{ "start" = 00:00:00.000, "end" = 23:59:59.999 of the first / last day }
function StartOfTheYear(const AValue: TDateTime): TDateTime;
function EndOfTheYear(const AValue: TDateTime): TDateTime;
function StartOfTheMonth(const AValue: TDateTime): TDateTime;
function EndOfTheMonth(const AValue: TDateTime): TDateTime;
function StartOfTheWeek(const AValue: TDateTime): TDateTime;
function EndOfTheWeek(const AValue: TDateTime): TDateTime;
function StartOfTheDay(const AValue: TDateTime): TDateTime;
function EndOfTheDay(const AValue: TDateTime): TDateTime;

{ --------------------------------------------------------------------------------------------------- weeks }
{ 1 = Monday ... 7 = Sunday (SysUtils.DayOfWeek: 1 = Sunday) }
function DayOfTheWeek(const AValue: TDateTime): Word;
{ ISO 8601: week 1 holds the year's first Thursday }
function WeekOfTheYear(const AValue: TDateTime): Word;
function DayOfTheYear(const AValue: TDateTime): Word;

{ ---------------------------------------------------------------------------------------------- increments }
{ Feb 29 becomes Feb 28 when the target year has no leap day (IncMonth clipping) }
function IncYear(const AValue: TDateTime; const ANumberOfYears: Integer = 1): TDateTime;
function IncWeek(const AValue: TDateTime; const ANumberOfWeeks: Integer = 1): TDateTime;
function IncDay(const AValue: TDateTime; const ANumberOfDays: Integer = 1): TDateTime;
function IncHour(const AValue: TDateTime; const ANumberOfHours: Int64 = 1): TDateTime;
function IncMinute(const AValue: TDateTime; const ANumberOfMinutes: Int64 = 1): TDateTime;
function IncSecond(const AValue: TDateTime; const ANumberOfSeconds: Int64 = 1): TDateTime;
function IncMilliSecond(const AValue: TDateTime; const ANumberOfMilliSeconds: Int64 = 1): TDateTime;

{ --------------------------------------------------------------------------------------------- differences }
{ whole units between the two instants, truncated, never negative (the order of the arguments does not matter);
  months and years count complete calendar months (IncMonth of the earlier instant, so Jan 31 + 1 month = Feb 29) }
function YearsBetween(const ANow, AThen: TDateTime): Integer;
function MonthsBetween(const ANow, AThen: TDateTime): Integer;
function WeeksBetween(const ANow, AThen: TDateTime): Integer;
function DaysBetween(const ANow, AThen: TDateTime): Integer;
function HoursBetween(const ANow, AThen: TDateTime): Int64;
function MinutesBetween(const ANow, AThen: TDateTime): Int64;
function SecondsBetween(const ANow, AThen: TDateTime): Int64;
function MilliSecondsBetween(const ANow, AThen: TDateTime): Int64;
{ fractional; a year is 365.25 days, a month 30.4375 days }
function YearSpan(const ANow, AThen: TDateTime): Double;
function MonthSpan(const ANow, AThen: TDateTime): Double;
function DaySpan(const ANow, AThen: TDateTime): Double;
function HourSpan(const ANow, AThen: TDateTime): Double;
function MinuteSpan(const ANow, AThen: TDateTime): Double;
function SecondSpan(const ANow, AThen: TDateTime): Double;

{ ---------------------------------------------------------------------------------------------- comparison }
{ millisecond resolution }
function CompareDateTime(const A, B: TDateTime): TValueRelationship;
function CompareDate(const A, B: TDateTime): TValueRelationship;
function CompareTime(const A, B: TDateTime): TValueRelationship;
function SameDateTime(const A, B: TDateTime): Boolean;
function SameDate(const A, B: TDateTime): Boolean;
function SameTime(const A, B: TDateTime): Boolean;
function IsToday(const AValue: TDateTime): Boolean;
function IsSameDay(const AValue, ABasis: TDateTime): Boolean;
function Today: TDateTime;
function Yesterday: TDateTime;
function Tomorrow: TDateTime;

{ ----------------------------------------------------------------------------------------- Unix, ISO 8601 }
{ whole seconds since 1970-01-01 00:00 UTC (rounded down); a local time (AInputIsUTC = False) is converted with the
  current time-zone bias of GetTimeZoneInformation (no historical rules) }
function DateTimeToUnix(const AValue: TDateTime; AInputIsUTC: Boolean = True): Int64;
function UnixToDateTime(const AValue: Int64; AReturnUTC: Boolean = True): TDateTime;
{ yyyy-mm-ddThh:nn:ss.zzzZ; a local time gets its offset +hh:mm / -hh:mm instead of Z }
function DateToISO8601(const ADate: TDateTime; AInputIsUTC: Boolean = True): string;
{ yyyy-mm-dd [Thh:nn [:ss [.f, .ff or .fff]] [Z | +hh:mm | -hh:mm | +hhmm | -hhmm]]; a text without a zone is UTC;
  AReturnUTC = False converts the result to local time. Bad text: EConvertError '<S>' is not a valid date and time }
function ISO8601ToDate(const AISODate: string; AReturnUTC: Boolean = True): TDateTime;
function TryISO8601ToDate(const AISODate: string; out Value: TDateTime; AReturnUTC: Boolean = True): Boolean;

implementation

{ TODO(compiler): Integer * Int64(constant) is computed in 32 bits (miscompile reported); the Integer and Word
  operands below are widened explicitly }
const
  MsPerDay = Int64(MSecsPerDay);
  MsPerHour = Int64(3600000);
  MsPerMinute = Int64(60000);
  MsPerSecond = Int64(1000);

{ RTL-630: the instant of AValue in milliseconds from 1899-12-30 00:00 }
function ToMs(const AValue: TDateTime): Int64;
var
  T: Int64;
begin
  T := Round(Abs(Frac(AValue)) * MSecsPerDay);
  if T >= MsPerDay then
    T := MsPerDay - 1;                 { RTL-242: no carry into the date }
  Result := Int64(Trunc(AValue)) * MsPerDay + T;
end;

function FromMs(Ms: Int64): TDateTime;
var
  Day, T: Int64;
begin
  Day := Ms div MsPerDay;
  T := Ms mod MsPerDay;
  if T < 0 then
  begin
    Day := Day - 1;
    T := T + MsPerDay;
  end;
  if Day >= 0 then
    Result := Day + T / MSecsPerDay
  else
    Result := Day - T / MSecsPerDay;
end;

{ the day number (date part) of AValue }
function DayNumber(const AValue: TDateTime): Int64;
begin
  Result := Trunc(AValue);
end;

function TimeMs(AHour, AMinute, ASecond, AMilliSecond: Word): Int64;
begin
  Result := Int64(AHour) * MsPerHour + Int64(AMinute) * MsPerMinute + Int64(ASecond) * MsPerSecond + AMilliSecond;
end;

{ ---------------------------------------------------------------------------------------------------- parts }

function YearOf(const AValue: TDateTime): Word;
var
  M, D: Word;
begin
  DecodeDate(AValue, Result, M, D);
end;

function MonthOf(const AValue: TDateTime): Word;
var
  Y, D: Word;
begin
  DecodeDate(AValue, Y, Result, D);
end;

function DayOf(const AValue: TDateTime): Word;
var
  Y, M: Word;
begin
  DecodeDate(AValue, Y, M, Result);
end;

function HourOf(const AValue: TDateTime): Word;
var
  N, S, MS: Word;
begin
  DecodeTime(AValue, Result, N, S, MS);
end;

function MinuteOf(const AValue: TDateTime): Word;
var
  H, S, MS: Word;
begin
  DecodeTime(AValue, H, Result, S, MS);
end;

function SecondOf(const AValue: TDateTime): Word;
var
  H, N, MS: Word;
begin
  DecodeTime(AValue, H, N, Result, MS);
end;

function MilliSecondOf(const AValue: TDateTime): Word;
var
  H, N, S: Word;
begin
  DecodeTime(AValue, H, N, S, Result);
end;

function DateOf(const AValue: TDateTime): TDateTime;
begin
  Result := System.Int(AValue);                { Windows declares the type INT }
end;

function TimeOf(const AValue: TDateTime): TDateTime;
begin
  Result := Abs(Frac(AValue));
end;

{ ------------------------------------------------------------------------------------------------- validity }

function IsValidDate(const AYear, AMonth, ADay: Word): Boolean;
begin
  Result := (AYear >= 1) and (AYear <= 9999) and (AMonth >= 1) and (AMonth <= 12) and (ADay >= 1) and
    (ADay <= DaysInAMonth(AYear, AMonth));
end;

function IsValidTime(const AHour, AMinute, ASecond, AMilliSecond: Word): Boolean;
begin
  Result := (AHour < 24) and (AMinute < 60) and (ASecond < 60) and (AMilliSecond < 1000);
end;

function IsValidDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word): Boolean;
begin
  Result := IsValidDate(AYear, AMonth, ADay) and IsValidTime(AHour, AMinute, ASecond, AMilliSecond);
end;

function IsInLeapYear(const AValue: TDateTime): Boolean;
begin
  Result := IsLeapYear(YearOf(AValue));
end;

function DaysInAMonth(const AYear, AMonth: Word): Word;
begin
  case AMonth of
    2:
      if IsLeapYear(AYear) then
        Result := 29
      else
        Result := 28;
    4, 6, 9, 11:
      Result := 30;
  else
    Result := 31;
  end;
end;

function DaysInMonth(const AValue: TDateTime): Word;
var
  Y, M, D: Word;
begin
  DecodeDate(AValue, Y, M, D);
  Result := DaysInAMonth(Y, M);
end;

function DaysInAYear(const AYear: Word): Word;
begin
  if IsLeapYear(AYear) then
    Result := 366
  else
    Result := 365;
end;

function DaysInYear(const AValue: TDateTime): Word;
begin
  Result := DaysInAYear(YearOf(AValue));
end;

{ --------------------------------------------------------------------------------------------- construction }

function TryEncodeDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word;
  out AValue: TDateTime): Boolean;
var
  D: TDateTime;
begin
  AValue := 0;
  Result := False;
  if not IsValidTime(AHour, AMinute, ASecond, AMilliSecond) then
    Exit;
  Result := TryEncodeDate(AYear, AMonth, ADay, D);
  if Result then
    AValue := FromMs(DayNumber(D) * MsPerDay + TimeMs(AHour, AMinute, ASecond, AMilliSecond));
end;

function EncodeDateTime(const AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond: Word): TDateTime;
begin
  if not IsValidDate(AYear, AMonth, ADay) then
    raise EConvertError.Create(SDateEncodeError);
  if not TryEncodeDateTime(AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond, Result) then
    raise EConvertError.Create(STimeEncodeError);
end;

procedure DecodeDateTime(const AValue: TDateTime; out AYear, AMonth, ADay, AHour, AMinute, ASecond,
  AMilliSecond: Word);
begin
  DecodeDate(AValue, AYear, AMonth, ADay);
  DecodeTime(AValue, AHour, AMinute, ASecond, AMilliSecond);
end;

function RecodeDateTime(const AValue: TDateTime; const AYear, AMonth, ADay, AHour, AMinute, ASecond,
  AMilliSecond: Word): TDateTime;
begin
  Result := EncodeDateTime(AYear, AMonth, ADay, AHour, AMinute, ASecond, AMilliSecond);
end;

function RecodeYear(const AValue: TDateTime; const AYear: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(AYear, M, D, H, N, S, MS);
end;

function RecodeMonth(const AValue: TDateTime; const AMonth: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, AMonth, D, H, N, S, MS);
end;

function RecodeDay(const AValue: TDateTime; const ADay: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, ADay, H, N, S, MS);
end;

function RecodeHour(const AValue: TDateTime; const AHour: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, D, AHour, N, S, MS);
end;

function RecodeMinute(const AValue: TDateTime; const AMinute: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, D, H, AMinute, S, MS);
end;

function RecodeSecond(const AValue: TDateTime; const ASecond: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, D, H, N, ASecond, MS);
end;

function RecodeMilliSecond(const AValue: TDateTime; const AMilliSecond: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, D, H, N, S, AMilliSecond);
end;

function RecodeDate(const AValue: TDateTime; const AYear, AMonth, ADay: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(AYear, AMonth, ADay, H, N, S, MS);
end;

function RecodeTime(const AValue: TDateTime; const AHour, AMinute, ASecond, AMilliSecond: Word): TDateTime;
var
  Y, M, D, H, N, S, MS: Word;
begin
  DecodeDateTime(AValue, Y, M, D, H, N, S, MS);
  Result := EncodeDateTime(Y, M, D, AHour, AMinute, ASecond, AMilliSecond);
end;

{ ----------------------------------------------------------------------------------------------- boundaries }

function StartOfDayNumber(Day: Int64): TDateTime;
begin
  Result := FromMs(Day * MsPerDay);
end;

function EndOfDayNumber(Day: Int64): TDateTime;
begin
  Result := FromMs(Day * MsPerDay + MsPerDay - 1);
end;

function StartOfTheYear(const AValue: TDateTime): TDateTime;
begin
  Result := EncodeDate(YearOf(AValue), 1, 1);
end;

function EndOfTheYear(const AValue: TDateTime): TDateTime;
begin
  Result := EndOfDayNumber(DayNumber(EncodeDate(YearOf(AValue), 12, 31)));
end;

function StartOfTheMonth(const AValue: TDateTime): TDateTime;
var
  Y, M, D: Word;
begin
  DecodeDate(AValue, Y, M, D);
  Result := EncodeDate(Y, M, 1);
end;

function EndOfTheMonth(const AValue: TDateTime): TDateTime;
var
  Y, M, D: Word;
begin
  DecodeDate(AValue, Y, M, D);
  Result := EndOfDayNumber(DayNumber(EncodeDate(Y, M, DaysInAMonth(Y, M))));
end;

function StartOfTheWeek(const AValue: TDateTime): TDateTime;
begin
  Result := StartOfDayNumber(DayNumber(AValue) - DayOfTheWeek(AValue) + 1);
end;

function EndOfTheWeek(const AValue: TDateTime): TDateTime;
begin
  Result := EndOfDayNumber(DayNumber(AValue) - DayOfTheWeek(AValue) + 7);
end;

function StartOfTheDay(const AValue: TDateTime): TDateTime;
begin
  Result := StartOfDayNumber(DayNumber(AValue));
end;

function EndOfTheDay(const AValue: TDateTime): TDateTime;
begin
  Result := EndOfDayNumber(DayNumber(AValue));
end;

{ --------------------------------------------------------------------------------------------------- weeks }

{ day 0 (1899-12-30) is a Saturday }
function DayOfTheWeek(const AValue: TDateTime): Word;
var
  R: Int64;
begin
  R := (DayNumber(AValue) + 5) mod 7;
  if R < 0 then
    R := R + 7;
  Result := R + 1;
end;

function DayOfTheYear(const AValue: TDateTime): Word;
begin
  Result := DayNumber(AValue) - DayNumber(EncodeDate(YearOf(AValue), 1, 1)) + 1;
end;

function WeekOfTheYear(const AValue: TDateTime): Word;
var
  Thursday: Int64;
  Y: Word;
begin
  Thursday := DayNumber(AValue) - DayOfTheWeek(AValue) + 4;
  Y := YearOf(Thursday);
  Result := (Thursday - DayNumber(EncodeDate(Y, 1, 1))) div 7 + 1;
end;

{ ---------------------------------------------------------------------------------------------- increments }

function IncYear(const AValue: TDateTime; const ANumberOfYears: Integer): TDateTime;
begin
  Result := IncMonth(AValue, ANumberOfYears * 12);
end;

function IncWeek(const AValue: TDateTime; const ANumberOfWeeks: Integer): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + Int64(ANumberOfWeeks) * 7 * MsPerDay);
end;

function IncDay(const AValue: TDateTime; const ANumberOfDays: Integer): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + Int64(ANumberOfDays) * MsPerDay);
end;

function IncHour(const AValue: TDateTime; const ANumberOfHours: Int64): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + ANumberOfHours * MsPerHour);
end;

function IncMinute(const AValue: TDateTime; const ANumberOfMinutes: Int64): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + ANumberOfMinutes * MsPerMinute);
end;

function IncSecond(const AValue: TDateTime; const ANumberOfSeconds: Int64): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + ANumberOfSeconds * MsPerSecond);
end;

function IncMilliSecond(const AValue: TDateTime; const ANumberOfMilliSeconds: Int64): TDateTime;
begin
  Result := FromMs(ToMs(AValue) + ANumberOfMilliSeconds);
end;

{ --------------------------------------------------------------------------------------------- differences }

function MsBetween(const ANow, AThen: TDateTime): Int64;
begin
  Result := Abs(ToMs(ANow) - ToMs(AThen));
end;

function MonthsBetween(const ANow, AThen: TDateTime): Integer;
var
  Lo, Hi: Int64;
  Y1, M1, D1, Y2, M2, D2: Word;
begin
  Lo := ToMs(ANow);
  Hi := ToMs(AThen);
  if Lo > Hi then
  begin
    Lo := Hi;
    Hi := ToMs(ANow);
  end;
  DecodeDate(FromMs(Lo), Y1, M1, D1);
  DecodeDate(FromMs(Hi), Y2, M2, D2);
  Result := (Integer(Y2) - Integer(Y1)) * 12 + Integer(M2) - Integer(M1);
  while (Result > 0) and (ToMs(IncMonth(FromMs(Lo), Result)) > Hi) do
    Result := Result - 1;
end;

function YearsBetween(const ANow, AThen: TDateTime): Integer;
begin
  Result := MonthsBetween(ANow, AThen) div 12;
end;

function WeeksBetween(const ANow, AThen: TDateTime): Integer;
begin
  Result := MsBetween(ANow, AThen) div (7 * MsPerDay);
end;

function DaysBetween(const ANow, AThen: TDateTime): Integer;
begin
  Result := MsBetween(ANow, AThen) div MsPerDay;
end;

function HoursBetween(const ANow, AThen: TDateTime): Int64;
begin
  Result := MsBetween(ANow, AThen) div MsPerHour;
end;

function MinutesBetween(const ANow, AThen: TDateTime): Int64;
begin
  Result := MsBetween(ANow, AThen) div MsPerMinute;
end;

function SecondsBetween(const ANow, AThen: TDateTime): Int64;
begin
  Result := MsBetween(ANow, AThen) div MsPerSecond;
end;

function MilliSecondsBetween(const ANow, AThen: TDateTime): Int64;
begin
  Result := MsBetween(ANow, AThen);
end;

function DaySpan(const ANow, AThen: TDateTime): Double;
begin
  Result := MsBetween(ANow, AThen) / MSecsPerDay;
end;

function YearSpan(const ANow, AThen: TDateTime): Double;
begin
  Result := DaySpan(ANow, AThen) / 365.25;
end;

function MonthSpan(const ANow, AThen: TDateTime): Double;
begin
  Result := DaySpan(ANow, AThen) / 30.4375;
end;

function HourSpan(const ANow, AThen: TDateTime): Double;
begin
  Result := MsBetween(ANow, AThen) / MsPerHour;
end;

function MinuteSpan(const ANow, AThen: TDateTime): Double;
begin
  Result := MsBetween(ANow, AThen) / MsPerMinute;
end;

function SecondSpan(const ANow, AThen: TDateTime): Double;
begin
  Result := MsBetween(ANow, AThen) / MsPerSecond;
end;

{ ---------------------------------------------------------------------------------------------- comparison }

function Compare64(A, B: Int64): TValueRelationship;
begin
  if A < B then
    Result := LessThanValue
  else if A > B then
    Result := GreaterThanValue
  else
    Result := EqualsValue;
end;

{ the time of day in milliseconds }
function DayMs(const AValue: TDateTime): Int64;
begin
  Result := ToMs(AValue) - DayNumber(AValue) * MsPerDay;
end;

function CompareDateTime(const A, B: TDateTime): TValueRelationship;
begin
  Result := Compare64(ToMs(A), ToMs(B));
end;

function CompareDate(const A, B: TDateTime): TValueRelationship;
begin
  Result := Compare64(DayNumber(A), DayNumber(B));
end;

function CompareTime(const A, B: TDateTime): TValueRelationship;
begin
  Result := Compare64(DayMs(A), DayMs(B));
end;

function SameDateTime(const A, B: TDateTime): Boolean;
begin
  Result := ToMs(A) = ToMs(B);
end;

function SameDate(const A, B: TDateTime): Boolean;
begin
  Result := DayNumber(A) = DayNumber(B);
end;

function SameTime(const A, B: TDateTime): Boolean;
begin
  Result := DayMs(A) = DayMs(B);
end;

function IsSameDay(const AValue, ABasis: TDateTime): Boolean;
begin
  Result := DayNumber(AValue) = DayNumber(ABasis);
end;

function IsToday(const AValue: TDateTime): Boolean;
begin
  Result := IsSameDay(AValue, Date);
end;

function Today: TDateTime;
begin
  Result := Date;
end;

function Yesterday: TDateTime;
begin
  Result := Date - 1;
end;

function Tomorrow: TDateTime;
begin
  Result := Date + 1;
end;

{ ----------------------------------------------------------------------------------------- Unix, ISO 8601 }

const
  UnixEpochMs = Int64(UnixDateDelta) * MsPerDay;

{ minutes: UTC = local time + bias (the daylight or standard bias in effect now) }
function CurrentBias: Int64;
var
  TZ: TTimeZoneInformation;
begin
  case GetTimeZoneInformation(TZ) of
    TIME_ZONE_ID_STANDARD: Result := Int64(TZ.Bias) + TZ.StandardBias;
    TIME_ZONE_ID_DAYLIGHT: Result := Int64(TZ.Bias) + TZ.DaylightBias;
    TIME_ZONE_ID_UNKNOWN: Result := TZ.Bias;
  else
    Result := 0;
  end;
end;

function DateTimeToUnix(const AValue: TDateTime; AInputIsUTC: Boolean): Int64;
var
  Ms: Int64;
begin
  Ms := ToMs(AValue) - UnixEpochMs;
  if not AInputIsUTC then
    Ms := Ms + CurrentBias * MsPerMinute;
  Result := Ms div MsPerSecond;
  if Ms mod MsPerSecond < 0 then
    Result := Result - 1;
end;

function UnixToDateTime(const AValue: Int64; AReturnUTC: Boolean): TDateTime;
var
  Ms: Int64;
begin
  Ms := UnixEpochMs + AValue * MsPerSecond;
  if not AReturnUTC then
    Ms := Ms - CurrentBias * MsPerMinute;
  Result := FromMs(Ms);
end;

function Pad(V: Int64; Digits: Integer): string;
begin
  Result := IntToStr(V);
  while Length(Result) < Digits do
    Result := '0' + Result;
end;

function DateToISO8601(const ADate: TDateTime; AInputIsUTC: Boolean): string;
var
  Y, M, D, H, N, S, MS: Word;
  Offset: Int64;
begin
  DecodeDateTime(ADate, Y, M, D, H, N, S, MS);
  Result := Pad(Y, 4) + '-' + Pad(M, 2) + '-' + Pad(D, 2) + 'T' + Pad(H, 2) + ':' + Pad(N, 2) + ':' + Pad(S, 2) +
    '.' + Pad(MS, 3);
  if AInputIsUTC then
    Result := Result + 'Z'
  else
  begin
    Offset := -CurrentBias;
    if Offset < 0 then
    begin
      Result := Result + '-';
      Offset := -Offset;
    end
    else
      Result := Result + '+';
    Result := Result + Pad(Offset div 60, 2) + ':' + Pad(Offset mod 60, 2);
  end;
end;

{ Count digits at S[P..] as a number; False (P unchanged) if they are not all digits or the text ends }
function Digits(const S: string; var P: Integer; Count: Integer; out V: Integer): Boolean;
var
  I: Integer;
begin
  V := 0;
  Result := P + Count - 1 <= Length(S);
  if not Result then
    Exit;
  for I := P to P + Count - 1 do
  begin
    if (S[I] < '0') or (S[I] > '9') then
    begin
      Result := False;
      Exit;
    end;
    V := V * 10 + Ord(S[I]) - Ord('0');
  end;
  P := P + Count;
end;

function Expect(const S: string; var P: Integer; C: Char): Boolean;
begin
  Result := (P <= Length(S)) and (S[P] = C);
  if Result then
    P := P + 1;
end;

function TryISO8601ToDate(const AISODate: string; out Value: TDateTime; AReturnUTC: Boolean): Boolean;
var
  P, Y, M, D, H, N, S, MS, K, ZH, ZM, Sign: Integer;
  Day: TDateTime;
  Ms64: Int64;
begin
  Value := 0;
  Result := False;
  P := 1;
  H := 0;
  N := 0;
  S := 0;
  MS := 0;
  ZH := 0;
  ZM := 0;
  Sign := 0;
  if not (Digits(AISODate, P, 4, Y) and Expect(AISODate, P, '-') and Digits(AISODate, P, 2, M) and
    Expect(AISODate, P, '-') and Digits(AISODate, P, 2, D)) then
    Exit;
  if Expect(AISODate, P, 'T') then
  begin
    if not (Digits(AISODate, P, 2, H) and Expect(AISODate, P, ':') and Digits(AISODate, P, 2, N)) then
      Exit;
    if Expect(AISODate, P, ':') then
    begin
      if not Digits(AISODate, P, 2, S) then
        Exit;
      if Expect(AISODate, P, '.') then
      begin
        K := 0;
        while (P <= Length(AISODate)) and (AISODate[P] >= '0') and (AISODate[P] <= '9') do
        begin
          K := K + 1;
          if K > 3 then
            Exit;
          MS := MS * 10 + Ord(AISODate[P]) - Ord('0');
          P := P + 1;
        end;
        if K = 0 then
          Exit;
        while K < 3 do
        begin
          MS := MS * 10;
          K := K + 1;
        end;
      end;
    end;
    if Expect(AISODate, P, 'Z') then
      Sign := 0
    else if Expect(AISODate, P, '+') then
      Sign := 1
    else if Expect(AISODate, P, '-') then
      Sign := -1;
    if Sign <> 0 then
    begin
      if not Digits(AISODate, P, 2, ZH) then
        Exit;
      Expect(AISODate, P, ':');
      if not Digits(AISODate, P, 2, ZM) then
        Exit;
      if (ZH > 23) or (ZM > 59) then
        Exit;
    end;
  end;
  if P <= Length(AISODate) then
    Exit;
  if not IsValidDate(Y, M, D) or not IsValidTime(H, N, S, MS) then
    Exit;
  Day := EncodeDate(Y, M, D);
  Ms64 := DayNumber(Day) * MsPerDay + TimeMs(H, N, S, MS) - Int64(Sign) * (Int64(ZH) * 60 + ZM) * MsPerMinute;
  if not AReturnUTC then
    Ms64 := Ms64 - CurrentBias * MsPerMinute;
  Value := FromMs(Ms64);
  Result := True;
end;

function ISO8601ToDate(const AISODate: string; AReturnUTC: Boolean): TDateTime;
begin
  if not TryISO8601ToDate(AISODate, Result, AReturnUTC) then
    raise EConvertError.CreateFmt(SInvalidDateTime, [AISODate]);
end;

end.
