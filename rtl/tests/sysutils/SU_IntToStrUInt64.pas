// covers: RTL-202
program SU_IntToStrUInt64;
{ sysutils.md section 3: IntToStr has an UInt64 overload, so unsigned 64-bit values (WPARAM, NativeUInt on x64,
  UInt64) print unsigned; Byte / Word / Integer / Cardinal / Int64 arguments resolve as before. }
uses
  Windows, SysUtils, TestKit;

var
  U64: UInt64;
  W: WPARAM;
  N: NativeUInt;
  I: Integer;
  I64: Int64;
  C: Cardinal;
  B: Byte;
  Wd: Word;
  Sh: ShortInt;
begin
  Section('UInt64 overload');
  U64 := 18446744073709551615;
  CheckEqS('18446744073709551615', IntToStr(U64), 'IntToStr High(UInt64)');
  U64 := 9223372036854775808;
  CheckEqS('9223372036854775808', IntToStr(U64), 'IntToStr 2^63');
  U64 := 0;
  CheckEqS('0', IntToStr(U64), 'IntToStr UInt64 0');
  W := WPARAM(-1);
  CheckEqS('18446744073709551615', IntToStr(W), 'IntToStr WPARAM all bits');
  N := 42;
  CheckEqS('42', IntToStr(N), 'IntToStr NativeUInt');

  Section('other argument types unchanged');
  I := -5;
  CheckEqS('-5', IntToStr(I), 'Integer');
  I64 := -9223372036854775807 - 1;
  CheckEqS('-9223372036854775808', IntToStr(I64), 'Int64');
  C := 4294967295;
  CheckEqS('4294967295', IntToStr(C), 'Cardinal');
  B := 255;
  CheckEqS('255', IntToStr(B), 'Byte');
  Wd := 65535;
  CheckEqS('65535', IntToStr(Wd), 'Word');
  Sh := -128;
  CheckEqS('-128', IntToStr(Sh), 'ShortInt');
  CheckEqS('-1', IntToStr(-1), 'literal -1');
  CheckEqS('5000000000', IntToStr(5000000000), 'Int64 literal');
  CheckEqS('7', IntToStr(3 + 4), 'Integer expression');
  Done;
end.
