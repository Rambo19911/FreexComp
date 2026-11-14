// covers: RTL-079
program Sys_ValInt;
{ Val for integers (system.md RTL-079, LANG-950; helpers _ValInt/_ValUInt): leading spaces (U+0020 only), a sign,
  decimal or $ / 0x / 0X hex digits, nothing after them. A failure gives 0 and the 1-based index of the first
  offending character (Length + 1 when the text ends too early); a value outside the variable gives Code =
  Length(S). Hex digits fill 64 bits for Int64. }
var
  I: Integer;
  B: Byte;
  Q: Int64;
  U: UInt64;
  C: Cardinal;
  Code: Integer;

procedure VI(const S: string);
begin
  Val(S, I, Code);
  Writeln('Integer [', S, '] ', I, ' ', Code);
end;

procedure VQ(const S: string);
begin
  Val(S, Q, Code);
  Writeln('Int64 [', S, '] ', Q, ' ', Code);
end;

procedure VU(const S: string);
begin
  Val(S, U, Code);
  Writeln('UInt64 [', S, '] ', U, ' ', Code);
end;

begin
  VI('0');
  VI('123');
  VI('   42');
  VI('-0');
  VI('+15');
  VI('007');
  VI('$7fffFFFF');
  VI('0x1A');
  VI('0X1a');
  VI('-$10');
  VI('$');
  VI('0x');
  VI('0');
  VI('$g');
  VI('0xg');
  VI('');
  VI('  ');
  VI('+');
  VI('- 1');
  VI('1-');
  VI('12a');
  VI('12 ');
  VI(#9'1');
  VI('1.0');
  VI('2147483647');
  VI('-2147483648');
  VI('2147483648');
  VI('-2147483649');
  VI('99999999999999999999999');
  VI('99999999999999999999999x1');
  VQ('9223372036854775807');
  VQ('-9223372036854775808');
  VQ('9223372036854775808');
  VQ('-9223372036854775809');
  VQ('$FFFFFFFFFFFFFFFF');
  VQ('$8000000000000000');
  VQ('-$1');
  VQ('$10000000000000000');
  VQ('0x7FFFFFFFFFFFFFFF');
  VU('18446744073709551615');
  VU('18446744073709551616');
  VU('$FFFFFFFFFFFFFFFF');
  VU('-0');
  VU('-1');
  VU('0x0000000000000000000001');
  Val('255', B, Code);
  Writeln('Byte 255 ', B, ' ', Code);
  Val('256', B, Code);
  Writeln('Byte 256 ', B, ' ', Code);
  Val('-1', B, Code);
  Writeln('Byte -1 ', B, ' ', Code);
  Val('4294967295', C, Code);
  Writeln('Cardinal 4294967295 ', C, ' ', Code);
  Val('4294967296', C, Code);
  Writeln('Cardinal 4294967296 ', C, ' ', Code);
end.
