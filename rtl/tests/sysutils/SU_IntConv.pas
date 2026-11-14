// covers: RTL-202
program SU_IntConv;
{ sysutils.md section 3: integer and Boolean conversions without exceptions (the raising forms: SU_ConvertErrors). }
uses
  SysUtils, TestKit;

var
  I: Integer;
  I64: Int64;
  C: Cardinal;
  U64: UInt64;
  B: Boolean;
begin
  Section('IntToStr / UIntToStr');
  CheckEqS('0', IntToStr(0), 'IntToStr 0');
  CheckEqS('-42', IntToStr(-42), 'IntToStr -42');
  CheckEqS('2147483647', IntToStr(MaxInt), 'IntToStr MaxInt');
  CheckEqS('-2147483648', IntToStr(Integer(-2147483648)), 'IntToStr Low(Integer)');
  I64 := -9223372036854775807 - 1;
  CheckEqS('-9223372036854775808', IntToStr(I64), 'IntToStr Low(Int64)');
  CheckEqS('5000000000', IntToStr(Int64(5000000000)), 'IntToStr Int64');
  C := 4294967295;
  CheckEqS('4294967295', UIntToStr(C), 'UIntToStr Cardinal');
  U64 := 18446744073709551615;
  CheckEqS('18446744073709551615', UIntToStr(U64), 'UIntToStr UInt64');

  Section('IntToHex');
  CheckEqS('00FF', IntToHex(255, 4), 'IntToHex(255, 4)');
  CheckEqS('FFFFFFFF', IntToHex(-1, 1), 'IntToHex(-1, 1)');
  CheckEqS('0', IntToHex(0, 0), 'IntToHex(0, 0)');
  CheckEqS('ABCDEF', IntToHex($ABCDEF, 2), 'never truncated');
  CheckEqS('FFFFFFFFFFFFFFFF', IntToHex(Int64(-1), 1), 'IntToHex Int64 -1');
  CheckEqS('0000000000000010', IntToHex(Int64(16), 16), 'IntToHex Int64 padded');
  CheckEqS('8000000000000000', IntToHex(UInt64($8000000000000000), 4), 'IntToHex UInt64');
  CheckEqS('0000001F', IntToHex(31), 'IntToHex Integer default digits');
  CheckEqS('000000000000001F', IntToHex(Int64(31)), 'IntToHex Int64 default digits');

  Section('TryStrToInt and friends');
  Check(TryStrToInt('123', I) and (I = 123), 'TryStrToInt 123');
  Check(TryStrToInt('  -7 '#9, I) and (I = -7), 'TryStrToInt trims');
  Check(TryStrToInt('$1F', I) and (I = 31), 'TryStrToInt hex');
  Check(TryStrToInt('0x10', I) and (I = 16), 'TryStrToInt 0x');
  Check(TryStrToInt('-2147483648', I) and (I = -2147483647 - 1), 'TryStrToInt Low');
  Check(not TryStrToInt('2147483648', I), 'TryStrToInt overflow');
  Check(not TryStrToInt('$FFFFFFFF', I), 'TryStrToInt hex out of range');
  Check(not TryStrToInt('', I), 'TryStrToInt empty');
  Check(not TryStrToInt('12a', I), 'TryStrToInt junk');
  Check(not TryStrToInt('1 2', I), 'TryStrToInt inner space');
  Check(TryStrToInt64('-9223372036854775808', I64) and (I64 = -9223372036854775807 - 1), 'TryStrToInt64 Low');
  Check(not TryStrToInt64('9223372036854775808', I64), 'TryStrToInt64 overflow');
  Check(TryStrToInt64('$FFFFFFFFFFFFFFFF', I64) and (I64 = -1), 'TryStrToInt64 hex all bits');
  Check(TryStrToUInt('4294967295', C) and (C = 4294967295), 'TryStrToUInt max');
  Check(not TryStrToUInt('4294967296', C), 'TryStrToUInt overflow');
  Check(not TryStrToUInt('-1', C), 'TryStrToUInt minus');
  Check(not TryStrToUInt('-0', C), 'TryStrToUInt minus zero');
  Check(TryStrToUInt64('18446744073709551615', U64) and (U64 = 18446744073709551615), 'TryStrToUInt64 max');
  Check(not TryStrToUInt64('18446744073709551616', U64), 'TryStrToUInt64 overflow');
  Check(not TryStrToUInt64('-5', U64), 'TryStrToUInt64 minus');
  CheckEqI(5, StrToIntDef('x', 5), 'StrToIntDef default');
  CheckEqI(12, StrToIntDef(' 12', 5), 'StrToIntDef value');
  CheckEqI(-3, StrToInt64Def('', -3), 'StrToInt64Def default');
  CheckEqI(7, StrToUIntDef('-7', 7), 'StrToUIntDef default');
  CheckEqI(9, StrToUInt64Def('9', 1), 'StrToUInt64Def value');

  Section('Booleans');
  CheckEqS('-1', BoolToStr(True), 'BoolToStr True');
  CheckEqS('0', BoolToStr(False), 'BoolToStr False');
  CheckEqS('True', BoolToStr(True, True), 'BoolToStr True strs');
  CheckEqS('False', BoolToStr(False, True), 'BoolToStr False strs');
  Check(TryStrToBool('TRUE', B) and B, 'TryStrToBool TRUE');
  Check(TryStrToBool('false', B) and not B, 'TryStrToBool false');
  Check(TryStrToBool('-1', B) and B, 'TryStrToBool -1');
  Check(TryStrToBool('0', B) and not B, 'TryStrToBool 0');
  Check(TryStrToBool('2.5', B) and B, 'TryStrToBool 2.5');
  Check(TryStrToBool('0.0', B) and not B, 'TryStrToBool 0.0');
  Check(not TryStrToBool('yes', B), 'TryStrToBool yes');
  Check(not TryStrToBool('', B), 'TryStrToBool empty');
  CheckEqB(True, StrToBoolDef('maybe', True), 'StrToBoolDef default');
  CheckEqB(False, StrToBoolDef('False', True), 'StrToBoolDef value');
  Done;
end.
