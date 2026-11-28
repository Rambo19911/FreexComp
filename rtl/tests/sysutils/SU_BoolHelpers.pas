// phase: 8
// covers: RTL-1260, RTL-1274
program SU_BoolHelpers;
{ sysutils.md section 12, task R-268 (F10-030): the helpers of Boolean, ByteBool, WordBool and LongBool (block D of
  standard-helpers.md section 5.2). ToString gives the text of BoolToStr: '-1' / '0' by default, 'True' / 'False'
  with TUseBoolStrs.True; Parse is StrToBool (true / false in any case or a number); TryToParse and TryParse are
  TryStrToBool and leave False when they fail; ToInteger is 1 or 0; any non-zero value of a wide type is true. }
uses
  SysUtils, TestKit;

var
  { out arguments of TryParse preset to a non-zero value (globals: a preset local would be reported as unused) }
  OutBoolean: Boolean;
  OutLongBool: LongBool;

function BoolMessage(const Text: string): string;
begin
  Result := '''' + Text + ''' is not a valid boolean value';
end;

procedure BooleanFails(const Text: string);
var
  V: Boolean;
begin
  Expect('EConvertError', BoolMessage(Text));
  try
    V := Boolean.Parse(Text);
    Writeln('# no exception, ', V);
  except
    on E: Exception do CheckRaises('Boolean.Parse(''' + Text + ''')', E.ClassName, E.Message);
  end;
end;

procedure TestBoolean;
var
  T, F, V: Boolean;
begin
  Section('Boolean');
  T := True;
  F := False;
  CheckEqS('-1', T.ToString, 'T.ToString');
  CheckEqS('0', F.ToString, 'F.ToString');
  CheckEqS('True', T.ToString(TUseBoolStrs.True), 'T.ToString(TUseBoolStrs.True)');
  CheckEqS('False', F.ToString(TUseBoolStrs.True), 'F.ToString(TUseBoolStrs.True)');
  CheckEqS('-1', T.ToString(TUseBoolStrs.False), 'T.ToString(TUseBoolStrs.False)');
  CheckEqS('True', Boolean.ToString(T, TUseBoolStrs.True), 'Boolean.ToString(T, TUseBoolStrs.True)');
  CheckEqS('0', Boolean.ToString(F), 'Boolean.ToString(F)');
  CheckEqS(BoolToStr(T), T.ToString, 'T.ToString = BoolToStr(T)');
  Check(Boolean.Parse('true'), 'Parse(''true'')');
  Check(Boolean.Parse('TRUE'), 'Parse(''TRUE'')');
  Check(Boolean.Parse('1'), 'Parse(''1'')');
  Check(Boolean.Parse('-3'), 'Parse(''-3'')');
  Check(not Boolean.Parse('false'), 'Parse(''false'')');
  Check(not Boolean.Parse('0'), 'Parse(''0'')');
  Check(not Boolean.Parse('0.0'), 'Parse(''0.0'')');
  BooleanFails('yes');
  BooleanFails('');
  OutBoolean := True;
  Check(not Boolean.TryToParse('x', OutBoolean), 'TryToParse(''x'')');
  Check(not OutBoolean, 'TryToParse(''x'') leaves False');
  OutBoolean := True;
  Check(not Boolean.TryParse('x', OutBoolean), 'TryParse(''x'')');
  Check(not OutBoolean, 'TryParse(''x'') leaves False');
  Check(Boolean.TryParse('true', V) and V, 'TryParse(''true'')');
  Check(Boolean.TryToParse('TRUE', V) and V, 'TryToParse(''TRUE'')');
  Check(Boolean.TryParse('0', V) and not V, 'TryParse(''0'')');
  CheckEqI(1, T.ToInteger, 'T.ToInteger');
  CheckEqI(0, F.ToInteger, 'F.ToInteger');
  CheckEqI(1, Boolean.Size, 'Boolean.Size');
  CheckEqS('True', (1 < 2).ToString(TUseBoolStrs.True), 'a parenthesised expression as receiver');
end;

procedure TestWide;
var
  BB: ByteBool;
  WB: WordBool;
  LB: LongBool;
begin
  Section('ByteBool, WordBool, LongBool');
  CheckEqI(1, ByteBool.Size, 'ByteBool.Size');
  CheckEqI(2, WordBool.Size, 'WordBool.Size');
  CheckEqI(4, LongBool.Size, 'LongBool.Size');
  LB := LongBool(5);
  CheckEqS('-1', LB.ToString, 'LongBool(5).ToString');
  CheckEqI(1, LB.ToInteger, 'LongBool(5).ToInteger');
  CheckEqS('True', LB.ToString(TUseBoolStrs.True), 'LongBool(5).ToString(TUseBoolStrs.True)');
  CheckEqS('-1', LongBool.ToString(LB), 'LongBool.ToString(LB)');
  CheckEqI(1, WordBool.Parse('true').ToInteger, 'WordBool.Parse(''true'').ToInteger');
  CheckEqI(0, WordBool.Parse('0').ToInteger, 'WordBool.Parse(''0'').ToInteger');
  BB := False;
  CheckEqS('False', BB.ToString(TUseBoolStrs.True), 'ByteBool False: ToString(TUseBoolStrs.True)');
  CheckEqS('0', BB.ToString, 'ByteBool False: ToString');
  CheckEqI(0, BB.ToInteger, 'ByteBool False: ToInteger');
  BB := ByteBool(200);
  CheckEqI(1, BB.ToInteger, 'ByteBool(200).ToInteger');
  CheckEqS('True', ByteBool.ToString(BB, TUseBoolStrs.True), 'ByteBool.ToString(BB, TUseBoolStrs.True)');
  WB := WordBool(256);
  CheckEqS('-1', WB.ToString, 'WordBool(256).ToString');
  CheckEqI(1, WB.ToInteger, 'WordBool(256).ToInteger');
  OutLongBool := True;
  Check(not LongBool.TryParse('maybe', OutLongBool), 'LongBool.TryParse(''maybe'')');
  CheckEqI(0, OutLongBool.ToInteger, 'LongBool.TryParse leaves False');
  Check(ByteBool.TryToParse('false', BB) and (BB.ToInteger = 0), 'ByteBool.TryToParse(''false'')');
  Check(WordBool.TryParse('7', WB) and (WB.ToInteger = 1), 'WordBool.TryParse(''7'')');
  CheckEqI(1, LongBool.Parse('TRUE').ToInteger, 'LongBool.Parse(''TRUE'')');
  CheckEqI(1, ByteBool.Parse('-1').ToInteger, 'ByteBool.Parse(''-1'')');
end;

begin
  TestBoolean;
  TestWide;
  Done;
end.
