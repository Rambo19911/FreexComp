// covers: RTL-200
program SU_PChar;
{ sysutils.md section 7.4: the PChar routines (comparisons return -1 / 0 / 1, case-insensitive forms are ASCII). }
uses
  SysUtils, TestKit;

{ string temporaries of the main block live until program end, so the leak count is taken in a routine }
procedure Allocation;
var
  P, Q: PChar;
  Before: NativeInt;
begin
  Section('allocation');
  Before := _LiveAllocCount;
  P := StrAlloc(10);
  CheckEqI(10, StrBufSize(P), 'StrBufSize');
  StrCopy(P, 'x');
  StrDispose(P);
  Q := StrNew('copy me');
  Check(StrComp(Q, 'copy me') = 0, 'StrNew');      { no string temporary before the leak count }
  CheckEqI(8, StrBufSize(Q), 'StrNew size includes terminator');
  StrDispose(Q);
  Check(StrNew(nil) = nil, 'StrNew nil');
  StrDispose(nil);
  CheckEqI(0, _LiveAllocCount - Before, 'no leaks');
end;

var
  Buf, Buf2: array[0..31] of Char;
  P: PChar;
begin
  Section('length and copy');
  CheckEqI(0, StrLen(nil), 'StrLen nil');
  CheckEqI(3, StrLen('abc'), 'StrLen');
  P := 'abc';
  Check(StrEnd(P) = P + 3, 'StrEnd');
  CheckEqS('hello', string(StrCopy(@Buf[0], 'hello')), 'StrCopy');
  CheckEqS('hel', string(StrLCopy(@Buf[0], 'hello', 3)), 'StrLCopy cuts');
  CheckEqS('hi', string(StrLCopy(@Buf[0], 'hi', 10)), 'StrLCopy short source');
  CheckEqS('from string', string(StrPCopy(@Buf[0], 'from string')), 'StrPCopy');
  CheckEqS('fro', string(StrPLCopy(@Buf[0], 'from string', 3)), 'StrPLCopy');
  StrCopy(@Buf[0], 'ab');
  CheckEqS('abcd', string(StrCat(@Buf[0], 'cd')), 'StrCat');
  CheckEqS('abcde', string(StrLCat(@Buf[0], 'efgh', 5)), 'StrLCat total length');
  CheckEqS('abcde', string(StrLCat(@Buf[0], 'xyz', 4)), 'StrLCat already full');

  Section('comparison');
  CheckEqI(0, StrComp('abc', 'abc'), 'StrComp equal');
  CheckEqI(-1, StrComp('abc', 'abd'), 'StrComp less');
  CheckEqI(1, StrComp('abcd', 'abc'), 'StrComp longer');
  CheckEqI(-1, StrComp('B', 'a'), 'StrComp ordinal');
  CheckEqI(0, StrIComp('HeLLo', 'hello'), 'StrIComp');
  CheckEqI(1, StrIComp('b', 'A'), 'StrIComp greater');
  CheckEqI(0, StrLComp('abcX', 'abcY', 3), 'StrLComp prefix');
  CheckEqI(-1, StrLComp('abcX', 'abcY', 4), 'StrLComp less');
  CheckEqI(0, StrLIComp('ABcX', 'abCY', 3), 'StrLIComp');
  CheckEqI(1, StrLIComp('abz', 'ABA', 3), 'StrLIComp greater');

  Section('search');
  P := 'a,b,c';
  Check(StrScan(P, ',') = P + 1, 'StrScan');
  Check(StrScan(P, 'x') = nil, 'StrScan missing');
  Check(StrScan(P, #0) = P + 5, 'StrScan terminator');
  Check(StrRScan(P, ',') = P + 3, 'StrRScan');
  Check(StrRScan(P, 'x') = nil, 'StrRScan missing');
  Check(StrPos(P, 'b,c') = P + 2, 'StrPos');
  Check(StrPos(P, 'cc') = nil, 'StrPos missing');

  Section('case');
  StrCopy(@Buf[0], 'MiXed 1ä');
  CheckEqS('MIXED 1ä', string(StrUpper(@Buf[0])), 'StrUpper ASCII only');
  CheckEqS('mixed 1ä', string(StrLower(@Buf[0])), 'StrLower ASCII only');
  CheckEqS('mixed 1ä', string(PChar(@Buf[0])), 'in place');

  Allocation;
  StrCopy(@Buf2[0], 'pas');
  CheckEqS('pas', StrPas(@Buf2[0]), 'StrPas');
  Done;
end.
