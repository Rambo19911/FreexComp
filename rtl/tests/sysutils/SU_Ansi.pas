// covers: RTL-255, RTL-841
program SU_Ansi;
{ sysutils.md section 7.3: the OS-collation family. AnsiUpperCase/AnsiLowerCase map case with LCMapStringEx (kernel32;
  RTL-841: no user32 import), AnsiCompareStr/Text and AnsiSameStr/Text use CompareStringEx with the user locale, AnsiPos
  is Pos. Only locale-independent facts are asserted: ASCII, Latin-1 and Cyrillic letters (one case pair in every
  locale), the sign of comparisons of ASCII text, reflexivity, empty strings. }
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
  TestCompare;
  TestPos;
  Done;
end.
