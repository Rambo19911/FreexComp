// covers: RTL-202, RTL-238, RTL-245
program SU_ConvertErrors;
{ The raising conversion routines (R-068): StrToInt, StrToInt64, StrToUInt, StrToUInt64, StrToFloat, StrToBool,
  StrToDate, StrToTime, StrToDateTime give the value of their Try form, or raise EConvertError with the exact
  message (the original, untrimmed text quoted); EncodeDate / EncodeTime and Format errors raise EConvertError too. }
uses
  SysUtils, TestKit;

var
  Caught: string;

procedure Expect(const Expected, Name: string);
begin
  CheckEqS(Expected, Caught, Name);
  Caught := '';
end;

procedure TryInt(const S: string);
begin
  try
    StrToInt(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryInt64(const S: string);
begin
  try
    StrToInt64(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryUInt(const S: string);
begin
  try
    StrToUInt(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryUInt64(const S: string);
begin
  try
    StrToUInt64(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryFloat(const S: string);
begin
  try
    StrToFloat(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryBool(const S: string);
begin
  try
    StrToBool(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryDate(const S: string);
begin
  try
    StrToDate(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryTime(const S: string);
begin
  try
    StrToTime(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TryDateTime(const S: string);
begin
  try
    StrToDateTime(S);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
end;

procedure TestValues;
var
  FS: TFormatSettings;
begin
  Section('values');
  CheckEqI(-42, StrToInt(' -42 '), 'StrToInt trims');
  CheckEqI(255, StrToInt('$FF'), 'StrToInt hex');
  CheckEqI(High(Int64), StrToInt64('9223372036854775807'), 'StrToInt64');
  CheckEqI(4294967295, StrToUInt('4294967295'), 'StrToUInt');
  CheckEqB(True, StrToUInt64('18446744073709551615') = High(UInt64), 'StrToUInt64');
  CheckEqF(2.5, StrToFloat('2.5'), 0, 'StrToFloat');
  FS := InvariantFormatSettings;
  FS.DecimalSeparator := ',';
  CheckEqF(2.5, StrToFloat('2,5', FS), 0, 'StrToFloat(FS)');
  CheckEqB(True, StrToBool('TRUE'), 'StrToBool true');
  CheckEqB(False, StrToBool('0'), 'StrToBool 0');
  CheckEqB(True, StrToBool('-1'), 'StrToBool -1');
  CheckEqS('2024-03-05', FormatDateTime('yyyy-mm-dd', StrToDate('2024-03-05')), 'StrToDate');
  FS := InvariantFormatSettings;
  FS.ShortDateFormat := 'dd/mm/yyyy';
  CheckEqS('2024-03-05', FormatDateTime('yyyy-mm-dd', StrToDate('05/03/2024', FS)), 'StrToDate(FS)');
  CheckEqS('14:07:09', FormatDateTime('hh:nn:ss', StrToTime('14:07:09')), 'StrToTime');
  CheckEqS('14:07:09', FormatDateTime('hh:nn:ss', StrToTime('2:07:09 PM', FS)), 'StrToTime(FS)');
  CheckEqS('2024-03-05 14:07', FormatDateTime('yyyy-mm-dd hh:nn', StrToDateTime('2024-03-05 14:07')), 'StrToDateTime');
  CheckEqS('2024-03-05 14:07', FormatDateTime('yyyy-mm-dd hh:nn', StrToDateTime('5/3/2024 14:07', FS)),
    'StrToDateTime(FS)');
end;

procedure TestErrors;
begin
  Section('EConvertError messages');
  TryInt(' 12x ');
  Expect(''' 12x '' is not a valid integer value', 'StrToInt keeps the untrimmed text');
  TryInt('2147483648');
  Expect('''2147483648'' is not a valid integer value', 'StrToInt out of range');
  TryInt('');
  Expect(''''' is not a valid integer value', 'StrToInt empty');
  TryInt64('9223372036854775808');
  Expect('''9223372036854775808'' is not a valid integer value', 'StrToInt64 out of range');
  TryUInt('-1');
  Expect('''-1'' is not a valid integer value', 'StrToUInt negative');
  TryUInt64('-5');
  Expect('''-5'' is not a valid integer value', 'StrToUInt64 negative');
  TryFloat('1.2.3');
  Expect('''1.2.3'' is not a valid floating point value', 'StrToFloat');
  TryBool('maybe');
  Expect('''maybe'' is not a valid boolean value', 'StrToBool');
  TryDate('2024-02-30');
  Expect('''2024-02-30'' is not a valid date', 'StrToDate');
  TryTime('25:00');
  Expect('''25:00'' is not a valid time', 'StrToTime');
  TryDateTime('2024-03-05 99');
  Expect('''2024-03-05 99'' is not a valid date and time', 'StrToDateTime');
end;

procedure TestEncodeAndFormat;
begin
  Section('EncodeDate, EncodeTime, Format');
  try
    EncodeDate(2023, 2, 29);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
  Expect('Invalid argument to date encode', 'EncodeDate');
  try
    EncodeTime(24, 0, 0, 0);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
  Expect('Invalid argument to time encode', 'EncodeTime');
  try
    Format('%d', ['text']);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
  Expect('Format ''%d'' invalid or incompatible with argument', 'Format type mismatch');
  try
    Format('%s %s', ['one']);
    Caught := 'no exception';
  except
    on E: EConvertError do
      Caught := E.Message;
  end;
  Expect('No argument for format ''%s %s''', 'Format missing argument');
end;

begin
  TestValues;
  TestErrors;
  TestEncodeAndFormat;
  Done;
end.
