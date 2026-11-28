// covers: RTL-640, RTL-1276
program Char_Classify;
{ other-units.md section 5 (task R-058): one code unit of each of the 30 general categories, the Is* classes
  (surrogate code units are never letters etc.), surrogate tests, simple case mapping (Char and string forms),
  UTF-32 conversion with its errors, and checksums over all 65536 code units of GetUnicodeCategory / ToUpper / ToLower
  (the values printed by rtl/tools/GenCharTables.cs, so the table lookup is checked end to end).
  Task R-194: the tables come from UnicodeData.txt of the Unicode Character Database 18.0.0, so the section
  'Unicode 18.0 data' checks code points that the earlier tables classified or mapped differently, and
  'case mapping' the simple mappings of the dotted / dotless i, the long s and U+1C89. }
uses
  SysUtils, Character, TestKit;

var
  Msg: string;

procedure Cat(C: Char; Expected: TUnicodeCategory; const Name: string);
begin
  CheckEqI(Ord(Expected), Ord(GetUnicodeCategory(C)), Name);
end;

procedure TestCategories;
begin
  Section('categories');
  Cat('A', ucUppercaseLetter, 'Lu');
  Cat('a', ucLowercaseLetter, 'Ll');
  Cat(#$01C5, ucTitlecaseLetter, 'Lt');
  Cat(#$02B0, ucModifierLetter, 'Lm');
  Cat(#$05D0, ucOtherLetter, 'Lo');
  Cat(#$0301, ucNonSpacingMark, 'Mn');
  Cat(#$0903, ucCombiningMark, 'Mc');
  Cat(#$20DD, ucEnclosingMark, 'Me');
  Cat('7', ucDecimalNumber, 'Nd');
  Cat(#$2160, ucLetterNumber, 'Nl');
  Cat(#$00BD, ucOtherNumber, 'No');
  Cat('_', ucConnectPunctuation, 'Pc');
  Cat('-', ucDashPunctuation, 'Pd');
  Cat('(', ucOpenPunctuation, 'Ps');
  Cat(')', ucClosePunctuation, 'Pe');
  Cat(#$00AB, ucInitialPunctuation, 'Pi');
  Cat(#$00BB, ucFinalPunctuation, 'Pf');
  Cat('!', ucOtherPunctuation, 'Po');
  Cat('+', ucMathSymbol, 'Sm');
  Cat('$', ucCurrencySymbol, 'Sc');
  Cat('^', ucModifierSymbol, 'Sk');
  Cat(#$00A9, ucOtherSymbol, 'So');
  Cat(' ', ucSpaceSeparator, 'Zs');
  Cat(#$2028, ucLineSeparator, 'Zl');
  Cat(#$2029, ucParagraphSeparator, 'Zp');
  Cat(#7, ucControl, 'Cc');
  Cat(#$200B, ucFormat, 'Cf');
  Cat(#$D800, ucSurrogate, 'Cs');
  Cat(#$E000, ucPrivateUse, 'Co');
  Cat(#$0378, ucUnassigned, 'Cn');
  Cat(#$FFFF, ucUnassigned, 'U+FFFF');
end;

{ code points assigned or reclassified after Unicode 16.0, and the classes that follow from the category }
procedure TestUnicode18;
begin
  Section('Unicode 18.0 data');
  Cat(#$A7CE, ucUppercaseLetter, 'U+A7CE Lu');
  Cat(#$A7CF, ucLowercaseLetter, 'U+A7CF Ll');
  Cat(#$0295, ucOtherLetter, 'U+0295 Lo');
  Cat(#$A7F1, ucModifierLetter, 'U+A7F1 Lm');
  Cat(#$088F, ucOtherLetter, 'U+088F Lo');
  Cat(#$1ACF, ucNonSpacingMark, 'U+1ACF Mn');
  Cat(#$20C1, ucCurrencySymbol, 'U+20C1 Sc');
  Cat(#$FBC3, ucOtherSymbol, 'U+FBC3 So');
  Cat(#$2E60, ucOtherPunctuation, 'U+2E60 Po');
  Cat(#$2E62, ucOpenPunctuation, 'U+2E62 Ps');
  Cat(#$2E63, ucClosePunctuation, 'U+2E63 Pe');
  CheckEqB(True, IsLetter(#$AB6C), 'IsLetter U+AB6C');
  CheckEqB(True, IsUpper(#$A7D2), 'IsUpper U+A7D2');
  CheckEqB(True, IsLetter(#$0295), 'IsLetter U+0295');
  CheckEqB(False, IsLower(#$0295), 'IsLower U+0295');
  CheckEqB(True, IsSymbol(#$20C4), 'IsSymbol U+20C4');
  CheckEqB(True, IsPunctuation(#$2E61), 'IsPunctuation U+2E61');
  CheckEqS(#$A7CE, ToUpper(#$A7CF), 'ToUpper U+A7CF');
  CheckEqS(#$A7CF, ToLower(#$A7CE), 'ToLower U+A7CE');
  CheckEqS(#$A7DD, ToUpper(#$0277), 'ToUpper U+0277');
  CheckEqS(#$0277, ToLower(#$A7DD), 'ToLower U+A7DD');
  CheckEqS(#$A7D2, ToUpper(#$A7D3), 'ToUpper U+A7D3');
  CheckEqS(#$AB4B, ToLower(#$AB6C), 'ToLower U+AB6C');
end;

procedure TestClasses;
begin
  Section('classes');
  CheckEqB(True, IsLetter(#$00E9), 'IsLetter e acute');
  CheckEqB(True, IsLetter(#$4E2D), 'IsLetter CJK');
  CheckEqB(False, IsLetter('1'), 'IsLetter digit');
  CheckEqB(False, IsLetter(#$D83D), 'IsLetter high surrogate');
  CheckEqB(True, IsDigit(#$0663), 'IsDigit Arabic-Indic three');
  CheckEqB(False, IsDigit(#$00BD), 'IsDigit one half');
  CheckEqB(True, IsNumber(#$00BD), 'IsNumber one half');
  CheckEqB(True, IsNumber(#$2160), 'IsNumber Roman numeral');
  CheckEqB(True, IsLetterOrDigit('x'), 'IsLetterOrDigit letter');
  CheckEqB(True, IsLetterOrDigit('0'), 'IsLetterOrDigit digit');
  CheckEqB(False, IsLetterOrDigit('_'), 'IsLetterOrDigit underscore');
  CheckEqB(True, IsWhiteSpace(' '), 'IsWhiteSpace space');
  CheckEqB(True, IsWhiteSpace(#9), 'IsWhiteSpace tab');
  CheckEqB(True, IsWhiteSpace(#13), 'IsWhiteSpace CR');
  CheckEqB(True, IsWhiteSpace(#$0085), 'IsWhiteSpace NEL');
  CheckEqB(True, IsWhiteSpace(#$00A0), 'IsWhiteSpace no-break space');
  CheckEqB(True, IsWhiteSpace(#$2029), 'IsWhiteSpace paragraph separator');
  CheckEqB(False, IsWhiteSpace(#$200B), 'IsWhiteSpace zero width space (Cf)');
  CheckEqB(False, IsWhiteSpace('x'), 'IsWhiteSpace letter');
  CheckEqB(True, IsUpper(#$0178), 'IsUpper');
  CheckEqB(False, IsUpper(#$01C5), 'IsUpper titlecase');
  CheckEqB(True, IsLower(#$00DF), 'IsLower sharp s');
  CheckEqB(True, IsControl(#$001F), 'IsControl');
  CheckEqB(False, IsControl(#$200B), 'IsControl format character');
  CheckEqB(True, IsPunctuation(#$00BF), 'IsPunctuation inverted question mark');
  CheckEqB(False, IsPunctuation('+'), 'IsPunctuation plus');
  CheckEqB(True, IsSymbol('+'), 'IsSymbol plus');
  CheckEqB(True, IsSymbol(#$20AC), 'IsSymbol euro');
  CheckEqB(True, IsSeparator(#$3000), 'IsSeparator ideographic space');
  CheckEqB(False, IsSeparator(#9), 'IsSeparator tab');
end;

procedure TestSurrogates;
begin
  Section('surrogates');
  CheckEqB(True, IsHighSurrogate(#$D800), 'IsHighSurrogate D800');
  CheckEqB(True, IsHighSurrogate(#$DBFF), 'IsHighSurrogate DBFF');
  CheckEqB(False, IsHighSurrogate(#$DC00), 'IsHighSurrogate DC00');
  CheckEqB(True, IsLowSurrogate(#$DC00), 'IsLowSurrogate DC00');
  CheckEqB(True, IsLowSurrogate(#$DFFF), 'IsLowSurrogate DFFF');
  CheckEqB(False, IsLowSurrogate(#$E000), 'IsLowSurrogate E000');
  CheckEqB(True, IsSurrogate(#$DABC), 'IsSurrogate');
  CheckEqB(False, IsSurrogate(#$D7FF), 'IsSurrogate D7FF');
  CheckEqB(True, IsSurrogatePair(#$D83D, #$DE00), 'IsSurrogatePair');
  CheckEqB(False, IsSurrogatePair(#$DE00, #$D83D), 'IsSurrogatePair reversed');
end;

procedure TestCase;
begin
  Section('case mapping');
  CheckEqS('A', ToUpper('a'), 'ToUpper a');
  CheckEqS(#$00C9, ToUpper(#$00E9), 'ToUpper e acute');
  CheckEqS(#$0178, ToUpper(#$00FF), 'ToUpper y diaeresis');
  CheckEqS(#$00DF, ToUpper(#$00DF), 'ToUpper sharp s: no simple mapping');
  CheckEqS(#$03A3, ToUpper(#$03C2), 'ToUpper final sigma');
  CheckEqS(#$01C4, ToUpper(#$01C5), 'ToUpper titlecase');
  CheckEqS(#$01C6, ToLower(#$01C5), 'ToLower titlecase');
  CheckEqS('z', ToLower('Z'), 'ToLower Z');
  CheckEqS(#$0434, ToLower(#$0414), 'ToLower Cyrillic');
  CheckEqS('1', ToUpper('1'), 'ToUpper digit');
  CheckEqS(#$D83D, ToUpper(#$D83D), 'ToUpper surrogate unchanged');
  CheckEqS('ABC' + #$00C9 + '1' + #$D83D + #$DE00, ToUpper('abC' + #$00E9 + '1' + #$D83D + #$DE00), 'ToUpper string');
  CheckEqS('abc' + #$00E9, ToLower('ABc' + #$00C9), 'ToLower string');
  CheckEqS('', ToUpper(''), 'ToUpper empty string');
  { simple mappings of UnicodeData.txt, independent of any locale }
  CheckEqS('I', ToUpper('i'), 'ToUpper i');
  CheckEqS('i', ToLower('I'), 'ToLower I');
  CheckEqS('i', ToLower(#$0130), 'ToLower I with dot above');
  CheckEqS(#$0130, ToUpper(#$0130), 'ToUpper I with dot above');
  CheckEqS('I', ToUpper(#$0131), 'ToUpper dotless i');
  CheckEqS(#$0131, ToLower(#$0131), 'ToLower dotless i');
  CheckEqS('S', ToUpper(#$017F), 'ToUpper long s');
  CheckEqS(#$017F, ToLower(#$017F), 'ToLower long s');
  CheckEqS(#$1C8A, ToLower(#$1C89), 'ToLower U+1C89');
  CheckEqS(#$1C89, ToUpper(#$1C8A), 'ToUpper U+1C8A');
  CheckEqS(#$1C89, ToUpper(#$1C89), 'ToUpper U+1C89');
  CheckEqS(#$00DF, ToLower(#$1E9E), 'ToLower capital sharp s');
  CheckEqS('k', ToLower(#$212A), 'ToLower Kelvin sign');
  CheckEqS('DIZ', ToUpper('d' + #$0131 + 'z'), 'ToUpper string with dotless i');
  CheckEqS('ix', ToLower(#$0130 + 'X'), 'ToLower string with I with dot above');
end;

{ task R-266 (RTL-1276 (d), card F10-047): the card's sample as one string, both directions; the sharp s has no
  simple upper-case mapping and stays }
procedure TestSample;
var
  S: string;
begin
  Section('sample');
  S := #$00DF + ' stra' + #$00DF + 'e ' + #$0100 + #$0112 + #$012A + ' ' + #$0436;
  CheckEqS(#$00DF + ' STRA' + #$00DF + 'E ' + #$0100 + #$0112 + #$012A + ' ' + #$0416, ToUpper(S), 'ToUpper sample');
  CheckEqS(#$00DF + ' stra' + #$00DF + 'e ' + #$0101 + #$0113 + #$012B + ' ' + #$0436, ToLower(S), 'ToLower sample');
  CheckEqS(ToUpper(S), AnsiUpperCase(S), 'ToUpper sample = AnsiUpperCase');
  CheckEqS(ToLower(S), AnsiLowerCase(S), 'ToLower sample = AnsiLowerCase');
end;

procedure TestUtf32;
var
  S: string;
begin
  Section('UTF-32');
  CheckEqS('A', ConvertFromUtf32($41), 'ConvertFromUtf32 ASCII');
  CheckEqS(#$FFFD, ConvertFromUtf32($FFFD), 'ConvertFromUtf32 BMP');
  CheckEqS(#$D83D + #$DE00, ConvertFromUtf32($1F600), 'ConvertFromUtf32 supplementary');
  CheckEqS(#$DBFF + #$DFFF, ConvertFromUtf32($10FFFF), 'ConvertFromUtf32 last');
  S := 'a' + #$D83D + #$DE00 + 'b';
  CheckEqI($61, ConvertToUtf32(S, 1), 'ConvertToUtf32 ASCII');
  CheckEqI($1F600, ConvertToUtf32(S, 2), 'ConvertToUtf32 pair');
  CheckEqI($62, ConvertToUtf32(S, 4), 'ConvertToUtf32 after the pair');
  Msg := 'no exception';
  try ConvertFromUtf32($110000); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertFromUtf32 beyond U+10FFFF');
  Msg := 'no exception';
  try ConvertFromUtf32($D800); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertFromUtf32 surrogate');
  Msg := 'no exception';
  try ConvertToUtf32(S, 3); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertToUtf32 at a low surrogate');
  Msg := 'no exception';
  try ConvertToUtf32('x' + #$D83D, 2); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertToUtf32 unpaired high surrogate');
  Msg := 'no exception';
  try ConvertToUtf32(S, 5); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertToUtf32 index past the end');
  Msg := 'no exception';
  try ConvertToUtf32(S, 0); except on E: Exception do Msg := E.ClassName; end;
  CheckEqS('EArgumentOutOfRangeException', Msg, 'ConvertToUtf32 index 0');
end;

{$Q-}
{$R-}
procedure TestChecksums;
var
  C: Integer;
  SumCat, SumUp, SumLo, W: Cardinal;
begin
  Section('whole table');
  SumCat := 0;
  SumUp := 0;
  SumLo := 0;
  for C := 0 to 65535 do
  begin
    W := Cardinal(C mod 251 + 1);
    SumCat := SumCat + Cardinal(Ord(GetUnicodeCategory(Char(C)))) * W;
    SumUp := SumUp + Cardinal(Ord(ToUpper(Char(C)))) * W;
    SumLo := SumLo + Cardinal(Ord(ToLower(Char(C)))) * W;
  end;
  CheckEqI(63462575, SumCat, 'GetUnicodeCategory checksum');
  CheckEqI(4124417430, SumUp, 'ToUpper checksum');
  CheckEqI(269040699, SumLo, 'ToLower checksum');
end;

begin
  TestCategories;
  TestUnicode18;
  TestClasses;
  TestSurrogates;
  TestCase;
  TestSample;
  TestUtf32;
  TestChecksums;
  Done;
end.
