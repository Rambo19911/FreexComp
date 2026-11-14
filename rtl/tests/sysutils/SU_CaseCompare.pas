// covers: RTL-200, RTL-250
program SU_CaseCompare;
{ sysutils.md section 7.1 and Trim* of 7.2: locale-independent case conversion and comparison. }
uses
  SysUtils, TestKit;

procedure SameRef;
var
  S, U: string;
begin
  S := 'ABC 123';
  S := S + '!';
  U := UpperCase(S);
  Check(Pointer(U) = Pointer(S), 'UpperCase unchanged returns the same reference');
  U := LowerCase('abc');
  CheckEqS('abc', U, 'LowerCase unchanged');
  U := Trim(S);
  Check(Pointer(U) = Pointer(S), 'Trim unchanged returns the same reference');
end;

var
  A, B: array[0..3] of Byte;
begin
  Section('UpperCase / LowerCase');
  CheckEqS('HELLO, WORLD 42', UpperCase('Hello, World 42'), 'UpperCase');
  CheckEqS('hello, world 42', LowerCase('Hello, World 42'), 'LowerCase');
  CheckEqS('ÄÖÜ ÉSZETT', UpperCase('ÄÖÜ ÉSZETT'), 'UpperCase leaves non-ASCII');
  CheckEqS('äöü x', LowerCase('äöü X'), 'LowerCase leaves non-ASCII');
  CheckEqS('', UpperCase(''), 'UpperCase empty');
  SameRef;

  Section('CompareStr / CompareText');
  CheckEqI(0, CompareStr('abc', 'abc'), 'CompareStr equal');
  CheckEqI(-1, CompareStr('abc', 'abd'), 'CompareStr less');
  CheckEqI(1, CompareStr('b', 'a'), 'CompareStr greater');
  CheckEqI(-1, CompareStr('ab', 'abc'), 'CompareStr prefix');
  CheckEqI(1, CompareStr('abc', ''), 'CompareStr empty');
  CheckEqI(-1, CompareStr('Z', 'a'), 'CompareStr ordinal case');
  CheckEqI(1, CompareStr('z', 'A'), 'CompareStr normalised to 1');
  CheckEqI(0, CompareText('HeLLo', 'hello'), 'CompareText equal');
  CheckEqI(1, CompareText('Z', 'a'), 'CompareText ignores case');
  CheckEqI(-1, CompareText('abc', 'ABD'), 'CompareText less');
  CheckEqI(1, CompareText('_', 'a'), 'CompareText after upper-casing: _ > A');
  CheckEqI(-1, CompareText('Ä', 'ä'), 'CompareText non-ASCII is ordinal');
  CheckEqB(True, SameStr('x', 'x'), 'SameStr');
  CheckEqB(False, SameStr('x', 'X'), 'SameStr case');
  CheckEqB(True, SameText('Straße', 'STRAßE'), 'SameText');
  CheckEqB(False, SameText('ab', 'abc'), 'SameText length');

  Section('CompareMem');
  A[0] := 1; A[1] := 2; A[2] := 3; A[3] := 4;
  B[0] := 1; B[1] := 2; B[2] := 3; B[3] := 5;
  CheckEqB(True, CompareMem(@A, @B, 3), 'CompareMem equal prefix');
  CheckEqB(False, CompareMem(@A, @B, 4), 'CompareMem differs');
  CheckEqB(True, CompareMem(@A, @B, 0), 'CompareMem 0');
  CheckEqB(True, CompareMem(nil, nil, -1), 'CompareMem negative');

  Section('Trim');
  CheckEqS('a b', Trim('  a b'#9#13#10), 'Trim');
  CheckEqS('a b'#9, TrimLeft(#1' a b'#9), 'TrimLeft');
  CheckEqS(' a b', TrimRight(' a b  '#0), 'TrimRight');
  CheckEqS('', Trim('   '), 'Trim blanks');
  CheckEqS('', TrimLeft(''), 'TrimLeft empty');
  CheckEqS('x'#$A0, Trim(' x'#$A0), 'Trim keeps U+00A0');
  Done;
end.
