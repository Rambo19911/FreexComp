// covers: RTL-200
program SU_Editing;
{ sysutils.md section 7.2: quoting, StringReplace, delimiters, WrapText, AdjustLineBreaks, IsValidIdent, CharInSet. }
uses
  SysUtils, TestKit;

procedure ExtractQuoted;
var
  S: string;
  P: PChar;
begin
  S := '''it''''s'' rest';
  P := PChar(S);
  CheckEqS('it''s', AnsiExtractQuotedStr(P, ''''), 'AnsiExtractQuotedStr token');
  CheckEqS(' rest', string(P), 'AnsiExtractQuotedStr advances');
  S := 'plain';
  P := PChar(S);
  CheckEqS('', AnsiExtractQuotedStr(P, ''''), 'AnsiExtractQuotedStr no quote');
  CheckEqS('plain', string(P), 'AnsiExtractQuotedStr no quote keeps Src');
end;

begin
  Section('quoting');
  CheckEqS('''abc''', QuotedStr('abc'), 'QuotedStr');
  CheckEqS('''it''''s''', QuotedStr('it''s'), 'QuotedStr doubles');
  CheckEqS('''''', QuotedStr(''), 'QuotedStr empty');
  CheckEqS('"a""b"', AnsiQuotedStr('a"b', '"'), 'AnsiQuotedStr');
  CheckEqS('a"b', AnsiDequotedStr('"a""b"', '"'), 'AnsiDequotedStr');
  CheckEqS('abc', AnsiDequotedStr('"abc', '"'), 'AnsiDequotedStr missing close');
  CheckEqS('abc"', AnsiDequotedStr('abc"', '"'), 'AnsiDequotedStr not quoted');
  CheckEqS('', AnsiDequotedStr('', '"'), 'AnsiDequotedStr empty');
  ExtractQuoted;

  Section('StringReplace');
  CheckEqS('xbcabc', StringReplace('abcabc', 'a', 'x', []), 'first only');
  CheckEqS('xbcxbc', StringReplace('abcabc', 'a', 'x', [rfReplaceAll]), 'all');
  CheckEqS('abcabc', StringReplace('abcabc', 'A', 'x', [rfReplaceAll]), 'case sensitive');
  CheckEqS('xbcxbc', StringReplace('abcAbc', 'a', 'x', [rfReplaceAll, rfIgnoreCase]), 'ignore case');
  CheckEqS('ba', StringReplace('aaa', 'aa', 'b', [rfReplaceAll]), 'non-overlapping');
  CheckEqS('abc', StringReplace('abc', '', 'x', [rfReplaceAll]), 'empty pattern');
  CheckEqS('ac', StringReplace('abc', 'b', '', []), 'delete');
  CheckEqS('a--b--', StringReplace('a-b-', '-', '--', [rfReplaceAll]), 'growing');

  Section('delimiters');
  CheckEqB(True, IsDelimiter('\/', 'a\b', 2), 'IsDelimiter');
  CheckEqB(False, IsDelimiter('\/', 'a\b', 1), 'IsDelimiter not');
  CheckEqB(False, IsDelimiter('\/', 'a\b', 0), 'IsDelimiter index 0');
  CheckEqB(False, IsDelimiter('\/', 'a\b', 4), 'IsDelimiter beyond');
  CheckEqI(4, LastDelimiter('\/', 'a\b/c'), 'LastDelimiter');
  CheckEqI(0, LastDelimiter('\/', 'abc'), 'LastDelimiter none');

  Section('WrapText');
  CheckEqS('aaa bbb'#13#10'ccc', WrapText('aaa bbb ccc', 8), 'WrapText at space');
  CheckEqS('one-'#13#10'two', WrapText('one-two', 5), 'WrapText at dash');
  CheckEqS('short', WrapText('short', 45), 'WrapText short');
  CheckEqS('''a b c d'''#13#10'e', WrapText('''a b c d'' e', 4), 'WrapText keeps quoted text');
  CheckEqS('aa|bb|cc', WrapText('aa bb cc', '|', [' '], 3), 'WrapText BreakStr');
  CheckEqS('abcdefgh', WrapText('abcdefgh', 3), 'WrapText no break char');

  Section('AdjustLineBreaks');
  CheckEqS('a'#13#10'b'#13#10'c'#13#10'd', AdjustLineBreaks('a'#13'b'#10'c'#13#10'd'), 'to CRLF');
  CheckEqS('a'#10'b'#10'c'#10'd', AdjustLineBreaks('a'#13'b'#10'c'#13#10'd', tlbsLF), 'to LF');
  CheckEqS(#13#10#13#10, AdjustLineBreaks(#10#13), 'LF CR is two breaks');

  Section('IsValidIdent');
  CheckEqB(True, IsValidIdent('_abc1'), 'ident');
  CheckEqB(False, IsValidIdent('1abc'), 'digit first');
  CheckEqB(False, IsValidIdent(''), 'empty');
  CheckEqB(False, IsValidIdent('a.b'), 'dot not allowed');
  CheckEqB(True, IsValidIdent('a.b', True), 'dots allowed');
  CheckEqB(False, IsValidIdent('a..b', True), 'empty part');
  CheckEqB(False, IsValidIdent('a.', True), 'trailing dot');
  CheckEqB(False, IsValidIdent('ä', False), 'ASCII only');

  Section('CharInSet');
  CheckEqB(True, CharInSet('a', ['a'..'z']), 'CharInSet');
  CheckEqB(False, CharInSet('A', ['a'..'z']), 'CharInSet not');
  CheckEqB(True, CharInSet(#$FF, [#$FF]), 'CharInSet U+00FF');
  CheckEqB(False, CharInSet(#$1FF, [#$FF]), 'CharInSet above U+00FF');
  Done;
end.
