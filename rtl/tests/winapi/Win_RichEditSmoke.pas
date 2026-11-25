// covers: RTL-1020, RTL-1021, RTL-1022, RTL-1026
// phase: 8
program Win_RichEditSmoke;
{ RichEdit unit (winapi.md sections 1 and 4.1): the values are proven against the system (RTL-1026). Through the
  shared unit rtl/tests/RichEditKit.pas the engine library is loaded from the system directory and one control of
  its class is created under a hidden top-level window; every message group is then used with a checked result:
  text, selection, character format, paragraph format, streams, find, range text, background colour, event mask,
  undo / redo, zoom, URL detection, text limit. A wrong message number, flag value or record layout fails one of
  the checks. The other format flags and the notifications: Win_RichEditFormat. }
uses Windows, Messages, RichEdit, RichEditKit, TestKit;

{$POINTERMATH ON}

var
  CR, Found: TCharRange;
  CF: TCharFormat2W;
  PF: TParaFormat2;
  ES: TEditStream;
  TR: TTextRangeW;
  Num, Den: Integer;
  Mask: DWORD;

begin
  Section('library and class');
  OpenEditor;

  Section('text (EM_SETTEXTEX, EM_GETTEXTLENGTHEX, EM_GETTEXTEX)');
  CheckEqI(1, SetText('one'#13'two', ST_DEFAULT), 'EM_SETTEXTEX');
  CheckEqI(7, TextLength(GTL_DEFAULT), 'GTL_DEFAULT');
  CheckEqI(8, TextLength(GTL_USECRLF or GTL_NUMCHARS), 'GTL_USECRLF');
  CheckEqS('one'#13'two', GetText(GT_DEFAULT), 'GT_DEFAULT');
  CheckEqS('one'#13#10'two', GetText(GT_USECRLF), 'GT_USECRLF');
  CheckEqI(1, SetText(Sample, ST_DEFAULT), 'EM_SETTEXTEX sample');
  CheckEqI(16, TextLength(GTL_NUMCHARS), 'GTL_NUMCHARS');
  CheckEqS(Sample, GetText(GT_DEFAULT), 'sample text');

  Section('selection (EM_EXSETSEL, EM_EXGETSEL, EM_GETSELTEXT, EM_HIDESELECTION)');
  CheckEqI(10, SelectRange(6, 10), 'EM_EXSETSEL');
  CR.cpMin := -5;
  CR.cpMax := -5;
  Send(EM_EXGETSEL, 0, LPARAM(@CR));
  CheckEqI(6, CR.cpMin, 'EM_EXGETSEL cpMin');
  CheckEqI(10, CR.cpMax, 'EM_EXGETSEL cpMax');
  FillChar(Chars, SizeOf(Chars), 0);
  CheckEqI(4, Send(EM_GETSELTEXT, 0, LPARAM(@Chars[0])), 'EM_GETSELTEXT');
  CheckEqS('rich', BufStr(@Chars[0]), 'selected text');
  Send(EM_HIDESELECTION, 1, 0);
  Send(EM_EXGETSEL, 0, LPARAM(@CR));
  CheckEqI(10, CR.cpMax, 'EM_HIDESELECTION keeps the selection');
  Send(EM_HIDESELECTION, 0, 0);

  Section('character format (EM_SETCHARFORMAT, EM_GETCHARFORMAT)');
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  CF.dwMask := CFM_BOLD or CFM_COLOR;
  CF.dwEffects := CFE_BOLD;
  CF.crTextColor := RGB(255, 0, 0);
  Check(Send(EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF)) <> 0, 'EM_SETCHARFORMAT bold + colour');
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_BOLD <> 0, 'CFE_BOLD read back');
  Check(CF.dwEffects and CFE_AUTOCOLOR = 0, 'CFE_AUTOCOLOR cleared by a colour');
  CheckEqI(RGB(255, 0, 0), CF.crTextColor, 'crTextColor');
  Mask := CFM_BOLD or CFM_ITALIC or CFM_UNDERLINE or CFM_STRIKEOUT or CFM_PROTECTED or CFM_LINK or CFM_SIZE or
    CFM_COLOR or CFM_FACE or CFM_CHARSET;
  Check(CF.dwMask and Mask = Mask, 'dwMask names every attribute of a uniform selection');
  Check(CF.dwEffects and (CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT or CFE_PROTECTED or CFE_LINK) = 0,
    'other effects are off');
  Check(CF.yHeight > 0, 'yHeight');
  Check(CF.szFaceName[0] <> #0, 'szFaceName');
  SelectRange(0, 5);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_BOLD = 0, 'text outside the range is not bold');
  Check(CF.dwEffects and CFE_AUTOCOLOR <> 0, 'CFE_AUTOCOLOR outside the range');
  GetFormat(CF, SCF_DEFAULT);
  Check(CF.dwEffects and CFE_BOLD = 0, 'SCF_DEFAULT is not bold');
  SelectRange(0, 16);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwMask and CFM_BOLD = 0, 'a mixed selection does not name CFM_BOLD');
  Check(CF.dwMask and CFM_ITALIC <> 0, 'a mixed selection still names CFM_ITALIC');

  Section('paragraph format (EM_SETPARAFORMAT, EM_GETPARAFORMAT)');
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  Send(EM_GETPARAFORMAT, 0, LPARAM(@PF));
  CheckEqI(PFA_LEFT, PF.wAlignment, 'PFA_LEFT is the initial alignment');
  CheckEqI(0, PF.cTabCount, 'no tab stop initially');
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  PF.dwMask := PFM_ALIGNMENT or PFM_TABSTOPS;
  PF.wAlignment := PFA_CENTER;
  PF.cTabCount := 1;
  PF.rgxTabs[0] := 1440;
  Check(Send(EM_SETPARAFORMAT, 0, LPARAM(@PF)) <> 0, 'EM_SETPARAFORMAT alignment + tab stop');
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  Mask := DWORD(Send(EM_GETPARAFORMAT, 0, LPARAM(@PF)));
  CheckEqI(PFA_CENTER, PF.wAlignment, 'PFA_CENTER');
  CheckEqI(1, PF.cTabCount, 'cTabCount');
  CheckEqI(1440, PF.rgxTabs[0], 'rgxTabs[0]');
  Check(Mask and (PFM_ALIGNMENT or PFM_TABSTOPS) = PFM_ALIGNMENT or PFM_TABSTOPS, 'EM_GETPARAFORMAT mask');
  CheckEqI(Mask, PF.dwMask, 'dwMask is the result');

  Section('streams (EM_STREAMOUT, EM_STREAMIN)');
  SelectRange(6, 10);
  Check(StreamOut(SF_TEXT, ES) > 0, 'EM_STREAMOUT SF_TEXT');
  CheckEqI(0, ES.dwError, 'dwError');
  CheckEqI(1234, LastCookie, 'dwCookie reaches the callback');
  CheckEqS(Sample, StreamStr(StreamLen), 'SF_TEXT bytes');
  StreamOut(SF_TEXT or SF_UNICODE, ES);
  CheckEqI(32, StreamLen, 'SF_UNICODE writes two bytes per character');
  StreamOut(SF_TEXT or SFF_SELECTION, ES);
  CheckEqS('rich', StreamStr(StreamLen), 'SFF_SELECTION');
  Check(StreamOut(SF_RTF, ES) > 0, 'EM_STREAMOUT SF_RTF');
  CheckEqI(0, ES.dwError, 'dwError after SF_RTF');
  CheckEqS('{\rtf', StreamStr(5), 'the stream starts with {\rtf');
  CheckEqI(1, SetText('', ST_DEFAULT), 'clear');
  CheckEqI(0, TextLength(GTL_DEFAULT), 'empty');
  Check(StreamIn(SF_RTF, ES) > 0, 'EM_STREAMIN SF_RTF');
  CheckEqI(0, ES.dwError, 'dwError after EM_STREAMIN');
  CheckEqI(4321, LastCookie, 'dwCookie reaches the read callback');
  CheckEqI(StreamLen, StreamPos, 'the whole stream was read');
  CheckEqS(Sample, GetText(GT_DEFAULT), 'text after the round trip');
  SelectRange(6, 10);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_BOLD <> 0, 'bold after the round trip');
  CheckEqI(RGB(255, 0, 0), CF.crTextColor, 'colour after the round trip');
  GetParagraph(PF);
  CheckEqI(PFA_CENTER, PF.wAlignment, 'alignment after the round trip');

  Section('find (EM_FINDTEXTEXW) and range text (EM_GETTEXTRANGE)');
  CheckEqI(6, Find('rich', FR_DOWN, 0, -1, Found), 'FR_DOWN');
  CheckEqI(6, Found.cpMin, 'chrgText.cpMin');
  CheckEqI(10, Found.cpMax, 'chrgText.cpMax');
  CheckEqI(6, Find('RICH', FR_DOWN, 0, -1, Found), 'case is ignored without FR_MATCHCASE');
  CheckEqI(-1, Find('RICH', FR_DOWN or FR_MATCHCASE, 0, -1, Found), 'FR_MATCHCASE');
  CheckEqI(6, Find('ric', FR_DOWN, 0, -1, Found), 'part of a word');
  CheckEqI(-1, Find('ric', FR_DOWN or FR_WHOLEWORD, 0, -1, Found), 'FR_WHOLEWORD');
  CheckEqI(4, Find('o', FR_DOWN, 0, -1, Found), 'forward finds the first');
  CheckEqI(12, Find('o', 0, 16, 0, Found), 'without FR_DOWN the search runs backwards');
  FillChar(Chars, SizeOf(Chars), 0);
  TR.chrg.cpMin := 11;
  TR.chrg.cpMax := 16;
  TR.lpstrText := @Chars[0];
  CheckEqI(5, Send(EM_GETTEXTRANGE, 0, LPARAM(@TR)), 'EM_GETTEXTRANGE');
  CheckEqS('world', BufStr(@Chars[0]), 'range text');

  Section('background colour (EM_SETBKGNDCOLOR)');
  Send(EM_SETBKGNDCOLOR, 0, RGB(16, 32, 48));
  CheckEqI(RGB(16, 32, 48), Send(EM_SETBKGNDCOLOR, 0, RGB(1, 2, 3)), 'EM_SETBKGNDCOLOR returns the previous colour');

  Section('event mask (EM_SETEVENTMASK, EM_GETEVENTMASK)');
  CheckEqI(ENM_NONE, Send(EM_GETEVENTMASK, 0, 0), 'ENM_NONE initially');
  CheckEqI(ENM_NONE, Send(EM_SETEVENTMASK, 0, ENM_CHANGE or ENM_SELCHANGE), 'EM_SETEVENTMASK returns the previous mask');
  CheckEqI(ENM_CHANGE or ENM_SELCHANGE, Send(EM_GETEVENTMASK, 0, 0), 'EM_GETEVENTMASK');
  CheckEqI(ENM_CHANGE or ENM_SELCHANGE, Send(EM_SETEVENTMASK, 0, ENM_NONE), 'mask off');

  Section('undo and redo (EM_SETUNDOLIMIT, EM_REDO, EM_CANREDO, ST_KEEPUNDO, ST_SELECTION)');
  CheckEqI(50, Send(EM_SETUNDOLIMIT, 50, 0), 'EM_SETUNDOLIMIT');
  SetText(Sample, ST_DEFAULT);
  CheckEqI(0, Send(EM_CANUNDO, 0, 0), 'ST_DEFAULT empties the undo list');
  CheckEqI(0, Send(EM_CANREDO, 0, 0), 'EM_CANREDO with nothing undone');
  SelectRange(0, 5);
  SetText('Howdy', ST_SELECTION or ST_KEEPUNDO);
  CheckEqS('Howdy rich world', GetText(GT_DEFAULT), 'ST_SELECTION replaces the selection only');
  Check(Send(EM_CANUNDO, 0, 0) <> 0, 'ST_KEEPUNDO keeps the change undoable');
  Check(Send(EM_UNDO, 0, 0) <> 0, 'EM_UNDO');
  CheckEqS(Sample, GetText(GT_DEFAULT), 'text after undo');
  Check(Send(EM_CANREDO, 0, 0) <> 0, 'EM_CANREDO');
  Check(Send(EM_REDO, 0, 0) <> 0, 'EM_REDO');
  CheckEqS('Howdy rich world', GetText(GT_DEFAULT), 'text after redo');
  CheckEqI(0, Send(EM_CANREDO, 0, 0), 'EM_CANREDO after redo');

  Section('zoom (EM_SETZOOM, EM_GETZOOM)');
  Check(Send(EM_SETZOOM, 3, 2) <> 0, 'EM_SETZOOM');
  Num := 0;
  Den := 0;
  Check(Send(EM_GETZOOM, WPARAM(@Num), LPARAM(@Den)) <> 0, 'EM_GETZOOM');
  CheckEqI(3, Num, 'numerator');
  CheckEqI(2, Den, 'denominator');
  Check(Send(EM_SETZOOM, 0, 0) <> 0, 'zoom off');

  Section('URL detection (EM_AUTOURLDETECT, EM_GETAUTOURLDETECT)');
  CheckEqI(0, Send(EM_GETAUTOURLDETECT, 0, 0), 'off initially');
  CheckEqI(0, Send(EM_AUTOURLDETECT, 1, 0), 'EM_AUTOURLDETECT on');
  Check(Send(EM_GETAUTOURLDETECT, 0, 0) <> 0, 'EM_GETAUTOURLDETECT');
  SetText('see http://example.test/ now', ST_DEFAULT);
  SelectRange(4, 24);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_LINK <> 0, 'a detected URL carries CFE_LINK');
  SelectRange(0, 3);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_LINK = 0, 'plain text does not');
  CheckEqI(0, Send(EM_AUTOURLDETECT, 0, 0), 'EM_AUTOURLDETECT off');
  CheckEqI(0, Send(EM_GETAUTOURLDETECT, 0, 0), 'off again');

  Section('text limit and target device (EM_EXLIMITTEXT, EM_SETTARGETDEVICE)');
  Send(EM_EXLIMITTEXT, 0, 1000);
  CheckEqI(1000, Send(EM_GETLIMITTEXT, 0, 0), 'EM_EXLIMITTEXT');
  Check(Send(EM_SETTARGETDEVICE, 0, 0) <> 0, 'EM_SETTARGETDEVICE: wrap to the window');

  Section('clean up');
  CloseEditor;
  Done;
end.
