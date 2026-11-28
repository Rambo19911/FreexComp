// phase: 8
// covers: RTL-1262, RTL-1272
program SU_IntHelpersNarrow;
{ sysutils.md section 12, task R-268 (F10-030): the helpers of ShortInt, SmallInt, Byte and Word (block C of
  standard-helpers.md section 5.2). Parse reads the text as an Integer and then checks the range of the type, so a
  hexadecimal text is a number, not a bit pattern of the narrow type; ToHexString shows the bit pattern in the width
  of the type; TryParse leaves 0 when it fails. }
uses
  SysUtils, TestKit;

var
  { out arguments of TryParse preset to a non-zero value (globals: a preset local would be reported as unused) }
  OutByte: Byte;
  OutShortInt: ShortInt;
  OutSmallInt: SmallInt;
  OutWord: Word;

function IntMessage(const Text: string): string;
begin
  Result := '''' + Text + ''' is not a valid integer value';
end;

procedure ShortIntFails(const Text: string);
var
  V: ShortInt;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := ShortInt.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('ShortInt.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure SmallIntFails(const Text: string);
var
  V: SmallInt;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := SmallInt.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('SmallInt.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure ByteFails(const Text: string);
var
  V: Byte;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := Byte.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Byte.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure WordFails(const Text: string);
var
  V: Word;
begin
  Expect('EConvertError', IntMessage(Text));
  try
    V := Word.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Word.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure TestByte;
var
  B, V: Byte;
begin
  Section('Byte');
  B := 200;
  CheckEqS('200', B.ToString, 'B.ToString');
  CheckEqS('200', Byte.ToString(B), 'Byte.ToString(B)');
  CheckEqS('C8', B.ToHexString, 'B.ToHexString');
  CheckEqS('00C8', B.ToHexString(4), 'B.ToHexString(4)');
  V := 10;
  CheckEqS('000A', V.ToHexString(4), 'Byte(10).ToHexString(4)');
  CheckEqI(255, Byte.Parse('255'), 'Byte.Parse(''255'')');
  CheckEqI(255, Byte.Parse('$FF'), 'Byte.Parse(''$FF'')');
  CheckEqI(0, Byte.Parse(' 0 '), 'Byte.Parse trims');
  ByteFails('256');
  ByteFails('-1');
  ByteFails('$100');
  ByteFails('x');
  OutByte := 7;
  Check(not Byte.TryParse('300', OutByte), 'Byte.TryParse(''300'')');
  CheckEqI(0, OutByte, 'Byte.TryParse leaves 0');
  Check(Byte.TryParse('12', V) and (V = 12), 'Byte.TryParse(''12'')');
  CheckEqI(1, Byte.Size, 'Byte.Size');
  V := Byte.MaxValue;
  CheckEqI(255, V, 'Byte.MaxValue');
  V := Byte.MinValue;
  CheckEqI(0, V, 'Byte.MinValue');
  Check(B.ToBoolean, 'B.ToBoolean');
  Check(B.ToDouble = 200.0, 'B.ToDouble');
end;

procedure TestShortInt;
var
  S, V: ShortInt;
begin
  Section('ShortInt');
  S := -1;
  CheckEqS('-1', S.ToString, 'S.ToString');
  CheckEqS('-1', ShortInt.ToString(S), 'ShortInt.ToString(S)');
  CheckEqS('FF', S.ToHexString, 'ShortInt(-1).ToHexString');
  CheckEqS('FF', S.ToHexString(1), 'ShortInt(-1).ToHexString(1)');
  CheckEqS('00FF', S.ToHexString(4), 'ShortInt(-1).ToHexString(4)');
  CheckEqI(-128, ShortInt.Parse('-128'), 'ShortInt.Parse(''-128'')');
  CheckEqI(127, ShortInt.Parse('127'), 'ShortInt.Parse(''127'')');
  ShortIntFails('128');
  ShortIntFails('$FF');
  ShortIntFails('-129');
  OutShortInt := 3;
  Check(not ShortInt.TryParse('$FF', OutShortInt), 'ShortInt.TryParse(''$FF'')');
  CheckEqI(0, OutShortInt, 'ShortInt.TryParse leaves 0');
  CheckEqI(1, ShortInt.Size, 'ShortInt.Size');
  V := ShortInt.MinValue;
  CheckEqI(-128, V, 'ShortInt.MinValue');
  V := ShortInt.MaxValue;
  CheckEqI(127, V, 'ShortInt.MaxValue');
  Check(S.ToSingle = -1.0, 'S.ToSingle');
end;

procedure TestSmallInt;
var
  S, V: SmallInt;
begin
  Section('SmallInt');
  S := -2;
  CheckEqS('-2', S.ToString, 'S.ToString');
  CheckEqS('FFFE', S.ToHexString, 'SmallInt(-2).ToHexString');
  CheckEqS('FFFE', S.ToHexString(1), 'SmallInt(-2).ToHexString(1)');
  CheckEqI(-32768, SmallInt.Parse('-32768'), 'SmallInt.Parse(''-32768'')');
  SmallIntFails('32768');
  SmallIntFails('$FFFF');
  OutSmallInt := 1;
  Check(not SmallInt.TryParse('32768', OutSmallInt), 'SmallInt.TryParse(''32768'')');
  CheckEqI(0, OutSmallInt, 'SmallInt.TryParse leaves 0');
  Check(SmallInt.TryParse('-7', V) and (V = -7), 'SmallInt.TryParse(''-7'')');
  CheckEqI(2, SmallInt.Size, 'SmallInt.Size');
  V := SmallInt.MinValue;
  CheckEqI(-32768, V, 'SmallInt.MinValue');
  V := SmallInt.MaxValue;
  CheckEqI(32767, V, 'SmallInt.MaxValue');
  CheckEqS('-2', SmallInt.ToString(S), 'SmallInt.ToString(S)');
end;

procedure TestWord;
var
  W, V: Word;
begin
  Section('Word');
  W := 4096;
  CheckEqS('4096', W.ToString, 'W.ToString');
  CheckEqS('1000', W.ToHexString(1), 'W.ToHexString(1)');
  CheckEqS('1000', W.ToHexString, 'W.ToHexString');
  CheckEqI(65535, Word.Parse('65535'), 'Word.Parse(''65535'')');
  CheckEqI(65535, Word.Parse('0xFFFF'), 'Word.Parse(''0xFFFF'')');
  WordFails('65536');
  WordFails('-1');
  OutWord := 2;
  Check(not Word.TryParse('65536', OutWord), 'Word.TryParse(''65536'')');
  CheckEqI(0, OutWord, 'Word.TryParse leaves 0');
  CheckEqI(2, Word.Size, 'Word.Size');
  V := Word.MaxValue;
  CheckEqI(65535, V, 'Word.MaxValue');
  CheckEqS('4096', Word.ToString(W), 'Word.ToString(W)');
  Check(W.ToExtended = 4096.0, 'W.ToExtended');
  V := 0;
  Check(not V.ToBoolean, 'Word(0).ToBoolean');
end;

begin
  TestByte;
  TestShortInt;
  TestSmallInt;
  TestWord;
  Done;
end.
