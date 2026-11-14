// covers: RTL-240
program SU_Now;
{ sysutils.md section 6 / 8.1: Now, Date, Time, CurrentYear from GetLocalTime; Sleep and GetTickCount64 as thin
  wrappers; GetEnvironmentVariable ('' when not defined). Sanity checks and round trips only: the clock is not
  controlled (Now is compared against GetLocalTime read around it). }
uses
  Windows, SysUtils, TestKit;

function LocalNow: TDateTime;
var
  ST: TSystemTime;
begin
  GetLocalTime(ST);
  Result := SystemTimeToDateTime(ST);
end;

procedure TestClock;
var
  Before, N, After, D, T: TDateTime;
  Y, M, Dd: Word;
begin
  Section('Now, Date, Time');
  Before := LocalNow;
  N := Now;
  D := Date;
  T := Time;
  After := LocalNow;
  Check((N >= Before) and (N <= After), 'Now between two GetLocalTime readings');
  Check(Frac(D) = 0, 'Date has no time part');
  Check((D = System.Int(Before)) or (D = System.Int(After)), 'Date = today');
  Check((T >= 0) and (T < 1), 'Time is a time of day');
  Check((Abs(T - Frac(N)) < 1 / 1440) or (Abs(T - Frac(N)) > 1 - 1 / 1440), 'Time close to the time of Now');
  DecodeDate(N, Y, M, Dd);
  CheckEqI(Y, CurrentYear, 'CurrentYear = year of Now');
  Check((CurrentYear >= 2024) and (CurrentYear <= 9999), 'CurrentYear plausible');
end;

procedure TestTicks;
var
  A, B: UInt64;
begin
  Section('GetTickCount64, Sleep');
  A := GetTickCount64;
  Sleep(0);
  B := GetTickCount64;
  Check(A > 0, 'tick count positive');
  Check(B >= A, 'tick count does not go back');
end;

procedure TestEnvironment;
var
  Name: string;
begin
  Section('GetEnvironmentVariable');
  Name := 'FREEXCOMP_SU_NOW_TEST';
  CheckEqS('', GetEnvironmentVariable(Name), 'undefined variable gives empty');
  Check(SetEnvironmentVariableW(PChar(Name), PChar('value 1')), 'variable set');
  CheckEqS('value 1', GetEnvironmentVariable(Name), 'defined variable');
  Check(SetEnvironmentVariableW(PChar(Name), PChar(StringOfChar('x', 600))), 'long variable set');
  CheckEqS(StringOfChar('x', 600), GetEnvironmentVariable(Name), 'longer than a first buffer');
  Check(SetEnvironmentVariableW(PChar(Name), PChar('')), 'empty value set');
  CheckEqS('', GetEnvironmentVariable(Name), 'empty value');
  CheckEqS('', GetEnvironmentVariable(''), 'empty name');
end;

begin
  TestClock;
  TestTicks;
  TestEnvironment;
  Done;
end.
