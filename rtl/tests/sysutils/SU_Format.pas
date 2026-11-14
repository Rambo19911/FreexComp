// covers: RTL-220, RTL-221, RTL-222, RTL-223, RTL-202
program SU_Format;
{ sysutils.md section 4: every normative example row, more conversions (and the PChar / untyped-buffer forms StrFmt,
  StrLFmt, FormatBuf), and every error path (EConvertError). }
uses
  SysUtils, TestKit;

type
  TThing = class
  end;

procedure Fails(const Fmt: string; const Args: array of const; const Expected, Name: string);
var
  Msg: string;
begin
  Msg := '(no exception)';
  try
    Format(Fmt, Args);
  except
    on E: EConvertError do
      Msg := E.Message;
  end;
  CheckEqS(Expected, Msg, Name);
end;

procedure Invalid(const Fmt: string; const Args: array of const; const Name: string);
begin
  Fails(Fmt, Args, 'Format ''' + Fmt + ''' invalid or incompatible with argument', Name);
end;

procedure Conversions;
var
  S: string;
  A: AnsiString;
  Short: ShortString;
  D: Double;
  Big: Int64;
  P: Pointer;
  O: TThing;
  PW: PChar;
  PA: PAnsiChar;
  Buf: array[0..63] of Char;
  Fb: string;
begin
  Section('more conversions');
  CheckEqS('-5', Format('%d', [-5]), 'd negative');
  CheckEqS('5000000000', Format('%d', [Int64(5000000000)]), 'd Int64');
  Big := -12;
  CheckEqS('-0012', Format('%.4d', [Big]), 'd Int64 prec');
  CheckEqS('42', Format('%.40d', [42]), 'd prec > 32 ignored');
  CheckEqS('4294967295', Format('%u', [-1]), 'u 32-bit pattern');
  Big := -1;
  CheckEqS('18446744073709551615', Format('%u', [Big]), 'u 64-bit pattern');
  CheckEqS('007', Format('%.3u', [7]), 'u prec');
  CheckEqS('FF', Format('%x', [255]), 'x');
  CheckEqS('00ff', LowerCase(Format('%.4X', [255])), 'X prec');
  CheckEqS('FFFFFFFFFFFFFFFF', Format('%x', [Big]), 'x Int64');
  CheckEqS('1.00000000000000E+003', Format('%e', [1000]), 'e from Integer');
  CheckEqS('1.2E+000', Format('%.2e', [1.25]), 'e prec');
  CheckEqS('1E020', Format('%g', [1e20]), 'g exponent digits');
  CheckEqS('2', Format('%.0f', [2.5]), 'f rounds half-even');
  CheckEqS('2.00', Format('%f', [Int64(2)]), 'f from Int64');
  CheckEqS('-1,234.00', Format('%n', [-1234.0]), 'n negative');
  CheckEqS('$1,234.57', Format('%m', [1234.567]), 'm default decimals');
  CheckEqS('-$1.5', Format('%.1m', [-1.5]), 'm prec');
  D := 0.5;
  CheckEqS('0.5', Format('%g', [D]), 'g variable');
  P := nil;
  CheckEqS('0000000000000000', Format('%p', [P]), 'p nil');
  P := Pointer(NativeInt($1234));
  CheckEqS('0000000000001234', Format('%p', [P]), 'p');
  O := TThing.Create;
  CheckEqS(IntToHex(Int64(NativeInt(O)), 16), Format('%p', [O]), 'p object');
  O.Free;
  CheckEqS(IntToHex(Int64(NativeInt(TThing)), 16), Format('%p', [TThing]), 'p class');
  S := 'uni';
  A := 'ansi';
  Short := 'short';
  PW := 'pw';
  PA := 'pa';
  CheckEqS('uni ansi short c d pw pa', Format('%s %s %s %s %s %s %s', [S, A, Short, 'c', AnsiChar('d'), PW, PA]),
    's of every string kind');
  PW := nil;
  CheckEqS('[]', Format('[%s]', [PW]), 's nil PChar');
  CheckEqS('a'#$D83D#$DE00, Format('%.3s', ['a'#$D83D#$DE00'b']), 's prec keeps a whole pair');
  CheckEqS('a', Format('%.2s', ['a'#$D83D#$DE00'b']), 's prec drops a split pair');
  CheckEqS('x|  y|z  ', Format('%s|%3s|%-3s', ['x', 'y', 'z']), 's width');
  CheckEqS('  7|7  ', Format('%*d|%*d', [3, 7, -3, 7]), 'negative * width left-justifies');
  CheckEqS('3.14', Format('%.*f', [2, 3.14159]), '* prec');
  CheckEqS('a|b', Format('%*:s|%s', [1, 'a', 'b']), '* index');
  CheckEqS('abc', Format('abc', [1, 2]), 'unused arguments');
  CheckEqS('%d', Format('%%d', []), '%% before a letter');
  CheckEqS('A', Format('%S', ['A']), 'type is case-insensitive');
  CheckEqS('1 2 1', Format('%d %d %0:d', [1, 2]), 'index resets the counter');
  FmtStr(S, '%d-%s', [1, 'x']);
  CheckEqS('1-x', S, 'FmtStr');
  FormatSettings.DecimalSeparator := ',';
  CheckEqS('1,50', Format('%.2f', [1.5]), 'uses FormatSettings');
  FormatSettings.DecimalSeparator := '.';
  FormatSettings.DecimalSeparator := ';';
  CheckEqS('1;5', Format('%g', [1.5], FormatSettings), 'explicit settings overload');
  FormatSettings.DecimalSeparator := '.';
  CheckEqS('n=5', string(StrFmt(@Buf[0], 'n=%d', [5])), 'StrFmt');
  CheckEqS('abc', string(StrLFmt(@Buf[0], 3, '%s', ['abcdef'])), 'StrLFmt truncates');
  { FormatBuf: untyped buffers of Char, lengths in characters, no terminator written }
  Fb := 'n=%d;rest';
  FillChar(Buf, SizeOf(Buf), 0);
  Buf[4] := 'Z';
  CheckEqI(4, FormatBuf(Buf, 20, Fb[1], 4, [42]), 'FormatBuf count (format length 4 of 9)');
  CheckEqS('n=42Z', string(PChar(@Buf[0])), 'FormatBuf text, nothing written after it');
  CheckEqI(3, FormatBuf(Buf, 3, Fb[1], 4, [12345]), 'FormatBuf truncates to BufLen');
  CheckEqS('n=1', Copy(string(PChar(@Buf[0])), 1, 3), 'FormatBuf truncated text');
  FormatSettings.DecimalSeparator := ',';
  Fb := '%.1f';
  CheckEqI(3, FormatBuf(Buf, 20, Fb[1], 4, [2.5], InvariantFormatSettings), 'FormatBuf settings overload');
  CheckEqS('2.5', Copy(string(PChar(@Buf[0])), 1, 3), 'FormatBuf uses the given settings');
  CheckEqI(3, FormatBuf(Buf, 20, Fb[1], 4, [2.5]), 'FormatBuf default settings');
  CheckEqS('2,5', Copy(string(PChar(@Buf[0])), 1, 3), 'FormatBuf uses FormatSettings');
  FormatSettings.DecimalSeparator := '.';
  CheckEqI(0, FormatBuf(Buf, 20, Fb[1], 0, []), 'FormatBuf empty format');
end;

begin
  Section('normative examples');
  CheckEqS('3 items', Format('%d items', [3]), 'Format(''%d items'', [3])');
  CheckEqS('   42|42   |', Format('%5d|%-5d|', [42, 42]), 'Format(''%5d|%-5d|'', [42, 42])');
  CheckEqS('-007', Format('%.3d', [-7]), 'Format(''%.3d'', [-7])');
  CheckEqS('FFFFFFFF', Format('%x', [-1]), 'Format(''%x'', [-1])');
  CheckEqS('3.14', Format('%.2f', [3.14159]), 'Format(''%.2f'', [3.14159])');
  CheckEqS('   2.500', Format('%8.3f', [2.5]), 'Format(''%8.3f'', [2.5])');
  CheckEqS('1.23450000000000E+003', Format('%e', [1234.5]), 'Format(''%e'', [1234.5])');
  CheckEqS('0.5', Format('%g', [0.5]), 'Format(''%g'', [0.5])');
  CheckEqS('1,234,567.89', Format('%n', [1234567.891]), 'Format(''%n'', [1234567.891])');
  CheckEqS('a=b', Format('%s=%s', ['a', 'b']), 'Format(''%s=%s'', [''a'', ''b''])');
  CheckEqS('b a b', Format('%1:s %0:s %s', ['a', 'b']), 'Format(''%1:s %0:s %s'', [''a'', ''b''])');
  CheckEqS('   7', Format('%*d', [4, 7]), 'Format(''%*d'', [4, 7])');
  CheckEqS('he', Format('%.2s', ['hello']), 'Format(''%.2s'', [''hello''])');
  CheckEqS('100%', Format('100%%', []), 'Format(''100%%'', [])');
  Invalid('%d', ['x'], 'Format(''%d'', [''x'']) raises');
  Conversions;

  Section('error paths');
  Invalid('%', [1], 'lone % at the end');
  Invalid('%5', [1], 'width without type');
  Invalid('%.', [1], 'dot without type');
  Invalid('%q', [1], 'unknown type');
  Invalid('%-', [1], 'minus without type');
  Invalid('%1:', [1], 'index without type');
  Invalid('%s', [True], 's of a Boolean');
  Invalid('%s', [42], 's of an Integer');
  Invalid('%d', [1.5], 'd of a real');
  Invalid('%x', [1.5], 'x of a real');
  Invalid('%u', ['u'], 'u of a string');
  Invalid('%f', ['x'], 'f of a string');
  Invalid('%e', [True], 'e of a Boolean');
  Invalid('%p', [1], 'p of an Integer');
  Invalid('%*d', ['w', 1], '* of a string');
  Invalid('%.*d', [1.5, 1], '* prec of a real');
  Fails('%d %d', [1], 'No argument for format ''%d %d''', 'missing argument');
  Fails('%2:d', [1, 2], 'No argument for format ''%2:d''', 'index beyond arguments');
  Fails('%*d', [4], 'No argument for format ''%*d''', '* consumes the last argument');
  Fails('%s', [], 'No argument for format ''%s''', 'no arguments');
  Done;
end.
