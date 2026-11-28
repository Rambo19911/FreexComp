// phase: 8
// covers: RTL-1260, RTL-1262, RTL-1272
program SU_IntHelpers;
{ sysutils.md section 12, task R-268 (F10-030): the helpers of Integer, Int64, Cardinal, UInt64, NativeInt and
  NativeUInt. Block C of standard-helpers.md section 5.2 with variables, function results, a parenthesised expression
  and class functions through the type name as receivers: MaxValue / MinValue, Size, ToString (both forms), Parse
  (grammar of the classic routine, range, the exact EConvertError message), TryParse (False leaves 0), ToHexString
  (the bit pattern in the width of the type, padding, never shortened), ToBoolean and the float conversions.
  The narrow types: SU_IntHelpersNarrow. }
uses
  SysUtils, TestKit;

var
  { out arguments of TryParse preset to a non-zero value (globals: a preset local would be reported as unused) }
  OutInteger: Integer;
  OutInt64: Int64;
  OutCardinal: Cardinal;
  OutUInt64: UInt64;
  OutNativeUInt: NativeUInt;

function IntMessage(const Text: string): string;
begin
  Result := '''' + Text + ''' is not a valid integer value';
end;

procedure IntegerFails(const Text: string);
var
  V: Integer;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := Integer.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Integer.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure Int64Fails(const Text: string);
var
  V: Int64;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := Int64.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Int64.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure CardinalFails(const Text: string);
var
  V: Cardinal;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := Cardinal.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Cardinal.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure UInt64Fails(const Text: string);
var
  V: UInt64;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := UInt64.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('UInt64.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure NativeUIntFails(const Text: string);
var
  V: NativeUInt;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := NativeUInt.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('NativeUInt.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

function FortyTwo: Integer;
begin
  Result := 42;
end;

procedure TestInteger;
var
  I, N, Z, V: Integer;
begin
  Section('Integer');
  I := 42;
  N := -1;
  Z := 0;
  CheckEqS('42', I.ToString, 'I.ToString');
  CheckEqS('-1', N.ToString, 'N.ToString');
  CheckEqS('42', Integer.ToString(I), 'Integer.ToString(I)');
  CheckEqS('43', (I + 1).ToString, '(I + 1).ToString');
  CheckEqS('42', FortyTwo.ToString, 'a function result as receiver');
  CheckEqI(4, Integer.Size, 'Integer.Size');
  CheckEqS('0000002A', I.ToHexString, 'I.ToHexString');
  CheckEqS('2A', I.ToHexString(1), 'I.ToHexString(1)');
  CheckEqS('002A', I.ToHexString(4), 'I.ToHexString(4)');
  CheckEqS('FFFFFFFF', N.ToHexString(1), 'N.ToHexString(1)');
  CheckEqS('FFFFFFFF', N.ToHexString, 'N.ToHexString');
  CheckEqS('0', Z.ToHexString(0), 'Z.ToHexString(0)');
  CheckEqS('0', Z.ToHexString(-3), 'a negative MinDigits pads nothing');
  V := 255;
  CheckEqS('000000FF', V.ToHexString, 'Integer(255).ToHexString');
  CheckEqS('FF', V.ToHexString(1), 'Integer(255).ToHexString(1)');
  Check(I.ToBoolean, 'I.ToBoolean');
  Check(not Z.ToBoolean, 'Z.ToBoolean');
  Check(N.ToBoolean, 'N.ToBoolean');
  Check(I.ToDouble = 42.0, 'I.ToDouble');
  Check(I.ToSingle = 42.0, 'I.ToSingle');
  Check(I.ToExtended = 42.0, 'I.ToExtended');
  Check(N.ToDouble = -1.0, 'N.ToDouble');
  V := Integer.MaxValue;
  CheckEqI(2147483647, V, 'Integer.MaxValue');
  V := Integer.MinValue;
  CheckEqI(-2147483648, V, 'Integer.MinValue');
  CheckEqI(12, Integer.Parse('12'), 'Integer.Parse(''12'')');
  CheckEqI(12, Integer.Parse(' 12 '), 'Parse trims');
  CheckEqI(16, Integer.Parse('$10'), 'Parse $10');
  CheckEqI(16, Integer.Parse('0x10'), 'Parse 0x10');
  CheckEqI(-2147483648, Integer.Parse('-2147483648'), 'Parse Low(Integer)');
  { the text is read as StrToInt reads it: a hexadecimal text must fit Integer as a number }
  IntegerFails('$FFFFFFFF');
  IntegerFails('2147483648');
  IntegerFails('x');
  IntegerFails('');
  IntegerFails('1 2');
  OutInteger := 5;
  Check(not Integer.TryParse('x', OutInteger), 'TryParse(''x'')');
  CheckEqI(0, OutInteger, 'TryParse(''x'') leaves 0');
  Check(Integer.TryParse('7', V), 'TryParse(''7'')');
  CheckEqI(7, V, 'TryParse(''7'') value');
end;

procedure TestInt64;
var
  I, V: Int64;
begin
  Section('Int64');
  I := 9000000000;
  CheckEqS('9000000000', I.ToString, 'I.ToString');
  CheckEqS('9000000000', Int64.ToString(I), 'Int64.ToString');
  CheckEqI(9000000000, Int64.Parse('9000000000'), 'Parse 9000000000');
  V := Int64.Parse('-9223372036854775808');
  Check(V = Int64.MinValue, 'Parse Low(Int64) = MinValue');
  CheckEqS('-9223372036854775808', V.ToString, 'MinValue text');
  V := Int64.MaxValue;
  CheckEqS('9223372036854775807', V.ToString, 'MaxValue text');
  Int64Fails('9223372036854775808');
  Int64Fails('abc');
  V := -1;
  CheckEqS('FFFFFFFFFFFFFFFF', V.ToHexString, 'ToHexString of -1');
  CheckEqS('0000000000000010', (V + 17).ToHexString, '(V + 17).ToHexString');
  CheckEqI(8, Int64.Size, 'Int64.Size');
  OutInt64 := 3;
  Check(not Int64.TryParse('9223372036854775808', OutInt64), 'TryParse out of range');
  CheckEqI(0, OutInt64, 'TryParse leaves 0');
  Check(Int64.TryParse('-5', V) and (V = -5), 'TryParse(''-5'')');
  Check(I.ToDouble = 9000000000.0, 'I.ToDouble');
end;

procedure TestCardinal;
var
  C, V: Cardinal;
begin
  Section('Cardinal');
  C := 4294967295;
  CheckEqS('4294967295', C.ToString, 'C.ToString');
  CheckEqS('4294967295', Cardinal.ToString(C), 'Cardinal.ToString');
  Check(Cardinal.Parse('4294967295') = C, 'Parse 4294967295');
  CheckEqS('FFFFFFFF', C.ToHexString, 'ToHexString');
  CheckEqI(4, Cardinal.Size, 'Cardinal.Size');
  V := Cardinal.MaxValue;
  Check(V = C, 'Cardinal.MaxValue');
  V := Cardinal.MinValue;
  CheckEqI(0, V, 'Cardinal.MinValue');
  CardinalFails('-1');
  CardinalFails('4294967296');
  OutCardinal := 9;
  Check(not Cardinal.TryParse('-1', OutCardinal), 'TryParse(''-1'')');
  CheckEqI(0, OutCardinal, 'TryParse leaves 0');
  Check(C.ToDouble = 4294967295.0, 'C.ToDouble');
end;

procedure TestUInt64;
var
  U, V: UInt64;
begin
  Section('UInt64');
  U := 18446744073709551615;
  CheckEqS('18446744073709551615', U.ToString, 'U.ToString');
  CheckEqS('18446744073709551615', UInt64.ToString(U), 'UInt64.ToString');
  Check(UInt64.Parse('18446744073709551615') = U, 'Parse max');
  Check(UInt64.MaxValue = U, 'UInt64.MaxValue');
  CheckEqS('FFFFFFFFFFFFFFFF', U.ToHexString, 'ToHexString of max');
  UInt64Fails('18446744073709551616');
  UInt64Fails('-5');
  V := 255;
  CheckEqS('FF', V.ToHexString(1), 'ToHexString(1) of 255');
  CheckEqI(8, UInt64.Size, 'UInt64.Size');
  OutUInt64 := 1;
  Check(not UInt64.TryParse('18446744073709551616', OutUInt64), 'TryParse overflow');
  Check(OutUInt64 = 0, 'TryParse leaves 0');
  Check(U.ToBoolean, 'U.ToBoolean');
end;

procedure TestNative;
var
  N, NV: NativeInt;
  U, UV: NativeUInt;
begin
  Section('NativeInt and NativeUInt');
  CheckEqI(SizeOf(Pointer), NativeInt.Size, 'NativeInt.Size');
  CheckEqI(SizeOf(Pointer), NativeUInt.Size, 'NativeUInt.Size');
  N := NativeInt.Parse('-5');
  CheckEqI(-5, N, 'NativeInt.Parse(''-5'')');
  CheckEqS('-5', N.ToString, 'N.ToString');
  CheckEqS('-5', NativeInt.ToString(N), 'NativeInt.ToString');
  NV := NativeInt.MinValue;
  Check(NV = Low(NativeInt), 'NativeInt.MinValue');
  NV := NativeInt.MaxValue;
  Check(NV = High(NativeInt), 'NativeInt.MaxValue');
  N := -1;
  CheckEqI(2 * SizeOf(Pointer), Length(N.ToHexString), 'NativeInt ToHexString width');
  CheckEqS('F', Copy(N.ToHexString(1), 1, 1), 'NativeInt -1 is all bits');
  NativeUIntFails('-1');
  U := NativeUInt.Parse('4294967295');
  CheckEqS('4294967295', U.ToString, 'NativeUInt.Parse and ToString');
  CheckEqS('4294967295', NativeUInt.ToString(U), 'NativeUInt.ToString');
  CheckEqS('FFFFFFFF', U.ToHexString(1), 'NativeUInt ToHexString(1)');
  UV := NativeUInt.MaxValue;
  Check(UV = High(NativeUInt), 'NativeUInt.MaxValue');
  UV := NativeUInt.MinValue;
  Check(UV = 0, 'NativeUInt.MinValue');
  OutNativeUInt := 7;
  Check(not NativeUInt.TryParse('-1', OutNativeUInt), 'NativeUInt.TryParse(''-1'')');
  Check(OutNativeUInt = 0, 'NativeUInt.TryParse leaves 0');
  Check(NativeInt.TryParse('12', NV) and (NV = 12), 'NativeInt.TryParse(''12'')');
  Check(N.ToBoolean, 'N.ToBoolean');
end;

begin
  TestInteger;
  TestInt64;
  TestCardinal;
  TestUInt64;
  TestNative;
  Done;
end.
