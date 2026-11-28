// covers: RTL-255, RTL-841, RTL-1276
program SU_Ansi;
{ sysutils.md section 7.3: the Ansi* family. AnsiUpperCase/AnsiLowerCase map case with the simple case mapping of the
  product's own table (RTL-1276, task R-266: SimpleUpperCase / SimpleLowerCase, CaseTables.inc; no OS call, so RTL-841:
  no user32 import), the same on every machine and in every locale. AnsiCompareStr/Text and AnsiSameStr/Text use
  CompareStringEx with the user locale, AnsiPos is Pos; for them only locale-independent facts are asserted: the sign
  of comparisons of ASCII text, reflexivity, empty strings. }
uses
  SysUtils, TestKit;

function Sign(V: Integer): Integer;
begin
  if V < 0 then
    Result := -1
  else if V > 0 then
    Result := 1
  else
    Result := 0;
end;

function Hex(V: Cardinal): string;
begin
  Result := 'U+' + IntToHex(V, 4);
end;

{ the upper-case mapping From -> Target of RTL-1276 (e), as code point and inside a string of one code unit }
procedure Up(From, Target: Cardinal);
begin
  CheckEqI(Target, SimpleUpperCase(From), 'SimpleUpperCase ' + Hex(From));
  CheckEqS(Char(Target), AnsiUpperCase(Char(From)), 'AnsiUpperCase ' + Hex(From));
end;

procedure Lo(From, Target: Cardinal);
begin
  CheckEqI(Target, SimpleLowerCase(From), 'SimpleLowerCase ' + Hex(From));
  CheckEqS(Char(Target), AnsiLowerCase(Char(From)), 'AnsiLowerCase ' + Hex(From));
end;

procedure TestCase;
var
  S: string;
begin
  Section('AnsiUpperCase / AnsiLowerCase');
  CheckEqS('HELLO, WORLD 42!', AnsiUpperCase('Hello, World 42!'), 'ASCII upper');
  CheckEqS('hello, world 42!', AnsiLowerCase('Hello, World 42!'), 'ASCII lower');
  CheckEqS('', AnsiUpperCase(''), 'empty upper');
  CheckEqS('', AnsiLowerCase(''), 'empty lower');
  S := 'caf' + Char($E9) + ' ' + Char($0434) + Char($0430);
  CheckEqS('CAF' + Char($C9) + ' ' + Char($0414) + Char($0410), AnsiUpperCase(S), 'Latin-1 and Cyrillic upper');
  CheckEqS(S, AnsiLowerCase(AnsiUpperCase(S)), 'lower of upper');
  CheckEqS('CAF' + Char($E9), UpperCase('caf' + Char($E9)), 'UpperCase stays ASCII-only');
  S := 'shared';
  CheckEqS('SHARED', AnsiUpperCase(S), 'argument kept');
  CheckEqS('shared', S, 'argument not changed');
  S := StringOfChar('a', 1000);
  CheckEqS(StringOfChar('A', 1000), AnsiUpperCase(S), 'long string');
end;

{ RTL-1276 (e): the normative pairs of UnicodeData 18.0.0, through all four functions }
procedure TestPairs;
begin
  Section('simple case mapping');
  Up(Ord('a'), Ord('A'));
  Lo(Ord('A'), Ord('a'));
  Up($00E9, $00C9);
  Lo($00C9, $00E9);
  Up($00FF, $0178);
  Lo($0178, $00FF);
  Up($00DF, $00DF);
  Lo($00DF, $00DF);
  Up($0131, Ord('I'));
  Lo($0131, $0131);
  Lo($0130, Ord('i'));
  Up($0130, $0130);
  Up($017F, Ord('S'));
  Lo($017F, $017F);
  Up($03C3, $03A3);
  Up($03C2, $03A3);
  Lo($03A3, $03C3);
  Up($0436, $0416);
  Lo($0416, $0436);
  Up($01C6, $01C4);
  Up($01C5, $01C4);
  Lo($01C4, $01C6);
  Lo($01C5, $01C6);
  Up($10D0, $1C90);
  Lo($1C90, $10D0);
  Up($AB70, $13A0);
  Lo($13A0, $AB70);
  Up(Ord('1'), Ord('1'));
  Lo(Ord('1'), Ord('1'));
end;

{ RTL-1276 (a), (b): values that are no code point, surrogates, the sample strings U and P of section 5.2, the same
  reference for unchanged text }
procedure TestStrings;
var
  S, T, U, P: string;
begin
  Section('strings and code units');
  CheckEqI($D800, SimpleUpperCase($D800), 'SimpleUpperCase high surrogate');
  CheckEqI($DFFF, SimpleLowerCase($DFFF), 'SimpleLowerCase low surrogate');
  CheckEqI($110000, SimpleUpperCase($110000), 'SimpleUpperCase beyond U+10FFFF');
  CheckEqI($110000, SimpleLowerCase($110000), 'SimpleLowerCase beyond U+10FFFF');
  CheckEqI($FFFFFFFF, SimpleUpperCase($FFFFFFFF), 'SimpleUpperCase $FFFFFFFF');
  CheckEqI($FFFFFFFF, SimpleLowerCase($FFFFFFFF), 'SimpleLowerCase $FFFFFFFF');
  CheckEqI($FFFF, SimpleUpperCase($FFFF), 'SimpleUpperCase U+FFFF');
  CheckEqI(0, SimpleLowerCase(0), 'SimpleLowerCase U+0000');
  U := 'Gr' + #$00FC + #$00DF + 'e ' + #$0416 + #$0438;
  CheckEqS('GR' + #$00DC + #$00DF + 'E ' + #$0416 + #$0418, AnsiUpperCase(U), 'AnsiUpperCase U');
  CheckEqS('gr' + #$00FC + #$00DF + 'e ' + #$0436 + #$0438, AnsiLowerCase(U), 'AnsiLowerCase U');
  CheckEqI(8, Length(AnsiUpperCase(U)), 'length kept');
  P := 'a' + #$D83D + #$DE00 + 'b';
  CheckEqS('A' + #$D83D + #$DE00 + 'B', AnsiUpperCase(P), 'AnsiUpperCase P');
  CheckEqS(P, AnsiLowerCase(P), 'AnsiLowerCase P');
  S := #$DE00 + #$D83D;
  CheckEqS(#$DE00 + #$D83D, AnsiUpperCase(S), 'lone surrogates upper');
  CheckEqS(#$DE00 + #$D83D, AnsiLowerCase(S), 'lone surrogates lower');
  S := 'x' + #$D800;
  CheckEqS('X' + #$D800, AnsiUpperCase(S), 'high surrogate at the end');
  S := #$DC00 + 'y';
  CheckEqS(#$DC00 + 'Y', AnsiUpperCase(S), 'low surrogate at the start');
  { unchanged text: the result is the argument itself }
  S := 'ABC ' + IntToStr(123);
  T := AnsiUpperCase(S);
  CheckEqB(True, Pointer(T) = Pointer(S), 'AnsiUpperCase unchanged: same reference');
  S := 'abc ' + IntToStr(123);
  T := AnsiLowerCase(S);
  CheckEqB(True, Pointer(T) = Pointer(S), 'AnsiLowerCase unchanged: same reference');
  S := #$DE00 + #$D83D + IntToStr(1);
  T := AnsiUpperCase(S);
  CheckEqB(True, Pointer(T) = Pointer(S), 'lone surrogates: same reference');
  S := 'abc ' + IntToStr(123);
  T := AnsiUpperCase(S);
  CheckEqB(False, Pointer(T) = Pointer(S), 'changed text: a new string');
  CheckEqS('abc 123', S, 'changed text: argument not changed');
  CheckEqS('ABC 123', AnsiUpperCase('ABC 123'), 'unchanged literal');
end;

procedure TestCompare;
begin
  Section('AnsiCompareStr / AnsiCompareText / AnsiSame*');
  CheckEqI(-1, Sign(AnsiCompareStr('apple', 'banana')), 'CompareStr less');
  CheckEqI(1, Sign(AnsiCompareStr('banana', 'apple')), 'CompareStr greater');
  CheckEqI(0, AnsiCompareStr('same', 'same'), 'CompareStr equal');
  CheckEqI(0, AnsiCompareStr('', ''), 'CompareStr empty');
  CheckEqI(-1, Sign(AnsiCompareStr('', 'a')), 'CompareStr empty first');
  CheckEqI(1, Sign(AnsiCompareStr('ab', 'a')), 'CompareStr longer');
  CheckEqB(True, AnsiCompareStr('abc', 'ABC') <> 0, 'CompareStr is case-sensitive');
  CheckEqI(0, AnsiCompareText('Hello', 'hELLO'), 'CompareText ignores case');
  CheckEqI(-1, Sign(AnsiCompareText('ALPHA', 'beta')), 'CompareText less');
  CheckEqI(1, Sign(AnsiCompareText('gamma', 'BETA')), 'CompareText greater');
  CheckEqI(0, AnsiCompareText('', ''), 'CompareText empty');
  CheckEqB(True, AnsiSameStr('text', 'text'), 'SameStr equal');
  CheckEqB(False, AnsiSameStr('text', 'Text'), 'SameStr case');
  CheckEqB(True, AnsiSameText('text', 'TEXT'), 'SameText case');
  CheckEqB(False, AnsiSameText('text', 'texts'), 'SameText length');
  CheckEqB(True, AnsiSameText('', ''), 'SameText empty');
  CheckEqI(-1, AnsiCompareStr('a', 'b'), 'result is -1, 0 or 1');
  CheckEqI(1, AnsiCompareText('b', 'A'), 'result is -1, 0 or 1 (text)');
end;

procedure TestPos;
begin
  Section('AnsiPos');
  CheckEqI(3, AnsiPos('cd', 'abcdef'), 'found');
  CheckEqI(0, AnsiPos('x', 'abcdef'), 'not found');
  CheckEqI(0, AnsiPos('', 'abc'), 'empty substring');
  CheckEqI(1, AnsiPos('ab', 'abab'), 'first occurrence');
end;

begin
  TestCase;
  TestPairs;
  TestStrings;
  TestCompare;
  TestPos;
  Done;
end.
