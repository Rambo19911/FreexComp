// covers: RTL-202, RTL-243, RTL-244
program SU_FormatDateTime;
{ sysutils.md section 6: FormatDateTime with every specifier of RTL-243 (case-insensitive outside quotes, m as minutes
  after h, 12-hour clock with an AM/PM specifier anywhere, literals, separators from the settings), the empty format
  (= 'c'), the expansion depth limit of RTL-244, negative values, and DateToStr / TimeToStr / DateTimeToStr /
  DateTimeToString with and without a TFormatSettings argument. }
uses
  SysUtils, TestKit;

var
  DT, Mid, Neg, Early, Late: TDateTime;

procedure TestFields;
begin
  Section('fields');
  { 2024-03-05 (a Tuesday) 14:07:09.045 }
  CheckEqS('5 05', FormatDateTime('d dd', DT), 'd dd');
  CheckEqS('Tue Tuesday', FormatDateTime('ddd dddd', DT), 'ddd dddd');
  CheckEqS('2024-03-05', FormatDateTime('ddddd', DT), 'ddddd = ShortDateFormat');
  CheckEqS('Tuesday, 5 March 2024', FormatDateTime('dddddd', DT), 'dddddd = LongDateFormat');
  CheckEqS('3 03 Mar March', FormatDateTime('m mm mmm mmmm', DT), 'm mm mmm mmmm');
  CheckEqS('24 24 2024 2024', FormatDateTime('y yy yyy yyyy', DT), 'y yy yyy yyyy');
  CheckEqS('14 14', FormatDateTime('h hh', DT), 'h hh');
  CheckEqS('7 07', FormatDateTime('n nn', DT), 'n nn');
  CheckEqS('9 09', FormatDateTime('s ss', DT), 's ss');
  CheckEqS('45 045', FormatDateTime('z zzz', DT), 'z zzz');
  CheckEqS('14:07 14:07:09', FormatDateTime('t tt', DT), 't tt');
  CheckEqS('2024-03-05 14:07:09', FormatDateTime('c', DT), 'c with a time');
  CheckEqS('2024-03-05', FormatDateTime('c', Mid), 'c at midnight');
  CheckEqS('2024-03-05 14:07:09', FormatDateTime('', DT), 'empty format = c');
  CheckEqS('5-3-2024 14:07', FormatDateTime('d/m/yyyy h:nn', DT), '/ and : from the invariant settings');
  CheckEqS('05.03.2024', FormatDateTime('dd"."mm"."yyyy', DT), 'double-quoted literals');
  CheckEqS('day 5 of m', FormatDateTime('''day'' d ''of m''', DT), 'single-quoted literal keeps specifier letters');
  CheckEqS('2024#03', FormatDateTime('yyyy#mm', DT), 'other characters copied');
  CheckEqS('Tuesday 05 Mar', FormatDateTime('DDDD DD MMM', DT), 'case-insensitive specifiers, names as set');
  CheckEqS('5 March', FormatDateTime('D MMMM', DT), 'upper-case month name specifier');
end;

procedure TestMinutes;
begin
  Section('m after h');
  CheckEqS('14:07', FormatDateTime('hh:mm', DT), 'hh:mm = minutes');
  CheckEqS('14 7', FormatDateTime('h m', DT), 'h m = minutes');
  CheckEqS('14h07', FormatDateTime('hh"h"mm', DT), 'quoted text between h and m');
  CheckEqS('14:07 05 03', FormatDateTime('hh:mm dd mm', DT), 'm after d is the month again');
  CheckEqS('03-14', FormatDateTime('mm-hh', DT), 'm before h is the month');
  CheckEqS('14:09:03', FormatDateTime('hh:ss:mm', DT), 'm after s is the month');
end;

procedure TestAmPm;
begin
  Section('12-hour clock');
  CheckEqS('2:07 pm', FormatDateTime('h:nn am/pm', DT), 'am/pm afternoon');
  CheckEqS('02:07 PM', FormatDateTime('hh:nn AM/PM', DT), 'AM/PM as written');
  CheckEqS('2 p', FormatDateTime('h a/p', DT), 'a/p');
  CheckEqS('2 P', FormatDateTime('h A/P', DT), 'A/P');
  CheckEqS('2 PM', FormatDateTime('h ampm', DT), 'ampm = TimePMString');
  CheckEqS('12:00 am', FormatDateTime('h:nn am/pm', Mid), 'midnight is 12 am');
  CheckEqS('1:30 AM', FormatDateTime('h:nn ampm', Early), '01:30 is AM');
  CheckEqS('12 pm', FormatDateTime('h am/pm', Late), 'noon is 12 pm');
  CheckEqS('pm 2', FormatDateTime('am/pm h', DT), 'the specifier may come before the hour');
  CheckEqS('Am 1', FormatDateTime('Am/Pm h', Early), 'mixed case as written (am half)');
  CheckEqS('Pm 2', FormatDateTime('Am/Pm h', DT), 'mixed case as written (pm half)');
  CheckEqS('14 am/', FormatDateTime('hh ''am/''', DT), 'quoted am/pm text is literal, 24-hour clock');
end;

procedure TestNegativeAndDepth;
var
  FS: TFormatSettings;
begin
  Section('negative values and expansion depth');
  CheckEqS('1899-12-29 06:00:00', FormatDateTime('yyyy-mm-dd hh:nn:ss', Neg), '-1.25 = 1899-12-29 06:00');
  FS := InvariantFormatSettings;
  FS.ShortDateFormat := 'ddddd';
  CheckEqS('', FormatDateTime('ddddd', DT, FS), 'self-referencing ShortDateFormat expands to nothing');
  FS.ShortDateFormat := 'dd/mm';
  FS.LongDateFormat := '[ddddd]';
  CheckEqS('[05-03]', FormatDateTime('dddddd', DT, FS), 'two levels of expansion');
  FS.ShortTimeFormat := 'tt';
  FS.LongTimeFormat := 't';
  CheckEqS('', FormatDateTime('t', DT, FS), 'self-referencing time formats expand to nothing');
end;

procedure TestSettings;
var
  FS: TFormatSettings;
  S: string;
begin
  Section('format settings and the ToStr routines');
  FS := InvariantFormatSettings;
  FS.DateSeparator := '.';
  FS.TimeSeparator := '-';
  FS.ShortDateFormat := 'dd/mm/yyyy';
  FS.LongTimeFormat := 'hh:nn:ss.zzz';
  FS.TimeAMString := 'vorm';
  FS.TimePMString := 'nachm';
  FS.LongMonthNames[3] := 'Maerz';
  FS.ShortDayNames[3] := 'Di';
  CheckEqS('05.03.2024', FormatDateTime('dd/mm/yyyy', DT, FS), '/ = DateSeparator');
  CheckEqS('14-07', FormatDateTime('hh:nn', DT, FS), ': = TimeSeparator');
  CheckEqS('Di Maerz nachm', FormatDateTime('ddd mmmm ampm', DT, FS), 'names from the settings');
  CheckEqS('05.03.2024', DateToStr(DT, FS), 'DateToStr(FS)');
  CheckEqS('14-07-09.045', TimeToStr(DT, FS), 'TimeToStr(FS) = LongTimeFormat');
  CheckEqS('05.03.2024 14-07-09.045', DateTimeToStr(DT, FS), 'DateTimeToStr(FS)');
  CheckEqS('05.03.2024', DateTimeToStr(Mid, FS), 'DateTimeToStr(FS) at midnight');
  CheckEqS('2024-03-05', DateToStr(DT), 'DateToStr');
  CheckEqS('14:07:09', TimeToStr(DT), 'TimeToStr');
  CheckEqS('2024-03-05 14:07:09', DateTimeToStr(DT), 'DateTimeToStr');
  S := 'old';
  DateTimeToString(S, 'yyyymmdd', DT);
  CheckEqS('20240305', S, 'DateTimeToString');
  DateTimeToString(S, 'dd/mm', DT, FS);
  CheckEqS('05.03', S, 'DateTimeToString(FS)');
  FormatSettings.DateSeparator := '/';
  CheckEqS('2024/03/05', FormatDateTime('yyyy/mm/dd', DT), 'the global FormatSettings');
  FormatSettings := InvariantFormatSettings;
end;

begin
  DT := EncodeDate(2024, 3, 5) + EncodeTime(14, 7, 9, 45);
  Mid := EncodeDate(2024, 3, 5);
  Early := EncodeDate(2024, 3, 5) + EncodeTime(1, 30, 0, 0);
  Late := EncodeDate(2024, 3, 5) + EncodeTime(12, 0, 0, 0);
  Neg := -1.25;
  TestFields;
  TestMinutes;
  TestAmPm;
  TestNegativeAndDepth;
  TestSettings;
  Done;
end.
