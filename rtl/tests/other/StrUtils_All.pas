// covers: RTL-620
program StrUtils_All;
{ other-units.md section 3 (task R-055): every function of the StrUtils table, ordinal and locale-independent ("text"
  forms ASCII case-insensitive), including the edge cases of each row, and the Ansi* aliases (RTL-620: same ordinal
  behaviour, non-ASCII letters are not folded). }
uses
  SysUtils, Types, StrUtils, TestKit;

function Joined(const A: TStringDynArray): string;
var
  I: Integer;
begin
  Result := IntToStr(Length(A)) + ':';
  for I := 0 to High(A) do
    Result := Result + '[' + A[I] + ']';
end;

procedure TestParts;
begin
  Section('LeftStr, RightStr, MidStr');
  CheckEqS('ab', LeftStr('abcde', 2), 'LeftStr');
  CheckEqS('abcde', LeftStr('abcde', 9), 'LeftStr longer than S');
  CheckEqS('', LeftStr('abcde', 0), 'LeftStr 0');
  CheckEqS('', LeftStr('abcde', -1), 'LeftStr negative');
  CheckEqS('de', RightStr('abcde', 2), 'RightStr');
  CheckEqS('abcde', RightStr('abcde', 5), 'RightStr = Length');
  CheckEqS('abcde', RightStr('abcde', 9), 'RightStr longer than S');
  CheckEqS('', RightStr('abcde', 0), 'RightStr 0');
  CheckEqS('', RightStr('abcde', -3), 'RightStr negative');
  CheckEqS('bcd', MidStr('abcde', 2, 3), 'MidStr');
  CheckEqS('de', MidStr('abcde', 4, 9), 'MidStr clipped');
  CheckEqS('', MidStr('abcde', 2, 0), 'MidStr count 0');
  CheckEqS('', MidStr('abcde', 9, 2), 'MidStr past the end');
  CheckEqS('ab', MidStr('abcde', 0, 2), 'MidStr start 0 (Copy semantics)');
end;

procedure TestSearch;
begin
  Section('Starts, Ends, Contains');
  CheckEqB(True, StartsStr('ab', 'abc'), 'StartsStr');
  CheckEqB(False, StartsStr('AB', 'abc'), 'StartsStr case-sensitive');
  CheckEqB(True, StartsStr('', 'abc'), 'StartsStr empty');
  CheckEqB(False, StartsStr('abcd', 'abc'), 'StartsStr longer');
  CheckEqB(True, EndsStr('bc', 'abc'), 'EndsStr');
  CheckEqB(False, EndsStr('BC', 'abc'), 'EndsStr case-sensitive');
  CheckEqB(True, EndsStr('', 'abc'), 'EndsStr empty');
  CheckEqB(True, ContainsStr('abcd', 'bc'), 'ContainsStr');
  CheckEqB(False, ContainsStr('abcd', 'BC'), 'ContainsStr case-sensitive');
  CheckEqB(False, ContainsStr('abcd', ''), 'ContainsStr empty is False');
  CheckEqB(True, StartsText('AB', 'abc'), 'StartsText');
  CheckEqB(True, StartsText('', 'abc'), 'StartsText empty');
  CheckEqB(True, EndsText('BC', 'abc'), 'EndsText');
  CheckEqB(False, EndsText('xBC', 'bc'), 'EndsText longer');
  CheckEqB(True, ContainsText('abcd', 'BC'), 'ContainsText');
  CheckEqB(False, ContainsText('abcd', ''), 'ContainsText empty is False');
  CheckEqB(False, StartsText(#$00C4, #$00E4 + 'x'), 'StartsText: non-ASCII letters are not folded');
  CheckEqI(4, PosEx('b', 'abcb', 3), 'PosEx with offset');
  CheckEqI(2, PosEx('b', 'abcb'), 'PosEx default offset');
  CheckEqI(0, PosEx('b', 'abcb', 5), 'PosEx past the end');
  CheckEqI(2, PosEx('b', 'abcb', 0), 'PosEx offset below 1');
end;

procedure TestEdit;
begin
  Section('Replace, Reverse, Dupe, Stuff, IfThen');
  CheckEqS('x-x-x', ReplaceStr('a-a-a', 'a', 'x'), 'ReplaceStr all');
  CheckEqS('A-x', ReplaceStr('A-a', 'a', 'x'), 'ReplaceStr case-sensitive');
  CheckEqS('x-x', ReplaceText('A-a', 'a', 'x'), 'ReplaceText case-insensitive');
  CheckEqS('cba', ReverseString('abc'), 'ReverseString');
  CheckEqS('', ReverseString(''), 'ReverseString empty');
  CheckEqS('b' + #$D83D + #$DE00 + 'a', ReverseString('a' + #$D83D + #$DE00 + 'b'),
    'ReverseString keeps a surrogate pair in order');
  CheckEqS(#$DE00 + 'a' + #$D800, ReverseString(#$D800 + 'a' + #$DE00), 'ReverseString: lone surrogates are single units');
  CheckEqS('ababab', DupeString('ab', 3), 'DupeString');
  CheckEqS('', DupeString('ab', 0), 'DupeString 0');
  CheckEqS('', DupeString('ab', -2), 'DupeString negative');
  CheckEqS('aXYde', StuffString('abcde', 2, 2, 'XY'), 'StuffString replace');
  CheckEqS('aXYbcde', StuffString('abcde', 2, 0, 'XY'), 'StuffString insert');
  CheckEqS('ae', StuffString('abcde', 2, 3, ''), 'StuffString delete');
  CheckEqS('abcdeZ', StuffString('abcde', 6, 1, 'Z'), 'StuffString at the end');
  CheckEqS('yes', IfThen(True, 'yes', 'no'), 'IfThen True');
  CheckEqS('no', IfThen(False, 'yes', 'no'), 'IfThen False');
  CheckEqS('', IfThen(False, 'yes'), 'IfThen default');
end;

procedure TestIndex;
begin
  Section('IndexStr, IndexText, MatchStr, MatchText');
  CheckEqI(1, IndexStr('b', ['a', 'b', 'b']), 'IndexStr first match');
  CheckEqI(-1, IndexStr('B', ['a', 'b']), 'IndexStr case-sensitive');
  CheckEqI(-1, IndexStr('x', []), 'IndexStr empty list');
  CheckEqI(1, IndexText('B', ['a', 'b']), 'IndexText');
  CheckEqB(True, MatchStr('b', ['a', 'b']), 'MatchStr');
  CheckEqB(False, MatchStr('B', ['a', 'b']), 'MatchStr case-sensitive');
  CheckEqB(True, MatchText('B', ['a', 'b']), 'MatchText');
  CheckEqB(False, MatchText('c', ['a', 'b']), 'MatchText no match');
end;

procedure TestSplit;
begin
  Section('SplitString');
  CheckEqS('3:[a][b][c]', Joined(SplitString('a,b;c', ',;')), 'each delimiter character');
  CheckEqS('3:[a][][b]', Joined(SplitString('a,,b', ',')), 'adjacent delimiters');
  CheckEqS('2:[][]', Joined(SplitString(',', ',')), 'only a delimiter');
  CheckEqS('2:[a][]', Joined(SplitString('a,', ',')), 'trailing delimiter');
  CheckEqS('1:[abc]', Joined(SplitString('abc', ',')), 'no delimiter');
  CheckEqS('1:[a,b]', Joined(SplitString('a,b', '')), 'no delimiter characters');
  CheckEqS('0:', Joined(SplitString('', ',')), 'empty string');
end;

procedure TestAnsi;
begin
  Section('Ansi aliases');
  CheckEqS('ab', AnsiLeftStr('abc', 2), 'AnsiLeftStr');
  CheckEqS('bc', AnsiRightStr('abc', 2), 'AnsiRightStr');
  CheckEqS('b', AnsiMidStr('abc', 2, 1), 'AnsiMidStr');
  CheckEqB(True, AnsiStartsStr('ab', 'abc'), 'AnsiStartsStr');
  CheckEqB(True, AnsiEndsStr('bc', 'abc'), 'AnsiEndsStr');
  CheckEqB(True, AnsiContainsStr('abc', 'b'), 'AnsiContainsStr');
  CheckEqB(True, AnsiStartsText('AB', 'abc'), 'AnsiStartsText');
  CheckEqB(True, AnsiEndsText('BC', 'abc'), 'AnsiEndsText');
  CheckEqB(True, AnsiContainsText('abc', 'B'), 'AnsiContainsText');
  CheckEqB(False, AnsiContainsText(#$00E4, #$00C4), 'AnsiContainsText is ordinal (no OS collation)');
  CheckEqS('axc', AnsiReplaceStr('abc', 'b', 'x'), 'AnsiReplaceStr');
  CheckEqS('axc', AnsiReplaceText('aBc', 'b', 'x'), 'AnsiReplaceText');
  CheckEqI(1, AnsiIndexStr('b', ['a', 'b']), 'AnsiIndexStr');
  CheckEqI(1, AnsiIndexText('B', ['a', 'b']), 'AnsiIndexText');
  CheckEqB(True, AnsiMatchStr('a', ['a', 'b']), 'AnsiMatchStr');
  CheckEqB(True, AnsiMatchText('A', ['a', 'b']), 'AnsiMatchText');
  CheckEqS('cba', AnsiReverseString('abc'), 'AnsiReverseString');
end;

begin
  TestParts;
  TestSearch;
  TestEdit;
  TestIndex;
  TestSplit;
  TestAnsi;
  Done;
end.
