// covers: RTL-1021, RTL-1022, RTL-1026
// phase: 8
program Win_RichEditFormat;
{ RichEdit unit, second part of Win_RichEditSmoke (one program grew too long): every character effect, size, face
  and character set, SCF_ALL, the indents and the bullet of the paragraph format, and the notifications the event
  mask enables - EN_SELCHANGE with its record, EN_CHANGE, and EN_PROTECTED with its record for a refused change of
  protected text. The control and the helpers come from rtl/tests/RichEditKit.pas. }
uses Windows, Messages, RichEdit, RichEditKit, TestKit;

{$POINTERMATH ON}

var
  CF: TCharFormat2W;
  PF: TParaFormat2;
  Face: string;
  I: Integer;

begin
  Section('library and class');
  OpenEditor;
  CheckEqI(1, SetText(Sample, ST_DEFAULT), 'EM_SETTEXTEX sample');

  Section('character effects, size, face, character set');
  SelectRange(0, 5);
  Check(SetEffects(CFM_ITALIC, CFE_ITALIC, SCF_SELECTION) <> 0, 'set italic');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(CFE_ITALIC, CF.dwEffects and (CFE_BOLD or CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT), 'CFE_ITALIC');
  Check(SetEffects(CFM_ITALIC or CFM_UNDERLINE, CFE_UNDERLINE, SCF_SELECTION) <> 0, 'set underline');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(CFE_UNDERLINE, CF.dwEffects and (CFE_BOLD or CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT), 'CFE_UNDERLINE');
  Check(SetEffects(CFM_UNDERLINE or CFM_STRIKEOUT, CFE_STRIKEOUT, SCF_SELECTION) <> 0, 'set strikeout');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(CFE_STRIKEOUT, CF.dwEffects and (CFE_BOLD or CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT), 'CFE_STRIKEOUT');
  Check(SetEffects(CFM_STRIKEOUT or CFM_LINK, CFE_LINK, SCF_SELECTION) <> 0, 'set link');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(CFE_LINK, CF.dwEffects and (CFE_STRIKEOUT or CFE_LINK or CFE_PROTECTED), 'CFE_LINK');
  Check(SetEffects(CFM_LINK, 0, SCF_SELECTION) <> 0, 'clear link');

  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  CF.dwMask := CFM_SIZE or CFM_FACE;
  CF.yHeight := 400;
  Face := 'Courier New';
  for I := 1 to Length(Face) do
    CF.szFaceName[I - 1] := Face[I];
  Check(Send(EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF)) <> 0, 'set size and face');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(400, CF.yHeight, 'CFM_SIZE');
  CheckEqS('Courier New', BufStr(@CF.szFaceName[0]), 'CFM_FACE');
  { the character set is set on its own: a face named in the same call decides the character set itself }
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  CF.dwMask := CFM_CHARSET;
  CF.bCharSet := GREEK_CHARSET;
  Check(Send(EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF)) <> 0, 'set character set');
  GetFormat(CF, SCF_SELECTION);
  CheckEqI(GREEK_CHARSET, CF.bCharSet, 'CFM_CHARSET');
  CheckEqI(400, CF.yHeight, 'size kept when CFM_SIZE is not named');
  SelectRange(0, 16);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwMask and CFM_CHARSET = 0, 'a selection of two character sets does not name CFM_CHARSET');
  SelectRange(11, 16);
  GetFormat(CF, SCF_SELECTION);
  Check(CF.yHeight <> 400, 'size of the other text is unchanged');

  Check(SetEffects(CFM_STRIKEOUT, CFE_STRIKEOUT, SCF_ALL) <> 0, 'SCF_ALL');
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_STRIKEOUT <> 0, 'SCF_ALL reaches text outside the selection');
  Check(SetEffects(CFM_STRIKEOUT, 0, SCF_ALL) <> 0, 'SCF_ALL clear');

  Section('paragraph indents and bullet');
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  PF.dwMask := PFM_ALIGNMENT or PFM_STARTINDENT or PFM_RIGHTINDENT or PFM_OFFSET or PFM_NUMBERING;
  PF.wAlignment := PFA_RIGHT;
  PF.dxStartIndent := 720;
  PF.dxRightIndent := 360;
  PF.dxOffset := 240;
  PF.wNumbering := PFN_BULLET;
  Check(Send(EM_SETPARAFORMAT, 0, LPARAM(@PF)) <> 0, 'EM_SETPARAFORMAT indents + bullet');
  GetParagraph(PF);
  CheckEqI(PFA_RIGHT, PF.wAlignment, 'PFA_RIGHT');
  CheckEqI(720, PF.dxStartIndent, 'PFM_STARTINDENT');
  CheckEqI(360, PF.dxRightIndent, 'PFM_RIGHTINDENT');
  CheckEqI(240, PF.dxOffset, 'PFM_OFFSET');
  CheckEqI(PFN_BULLET, PF.wNumbering, 'PFN_BULLET');
  CheckEqI(32, MAX_TAB_STOPS, 'MAX_TAB_STOPS');
  CheckEqI(MAX_TAB_STOPS * 4, SizeOf(PF.rgxTabs), 'rgxTabs holds MAX_TAB_STOPS positions');

  Section('event mask and notifications');
  CheckEqI(ENM_NONE, Send(EM_GETEVENTMASK, 0, 0), 'ENM_NONE initially');
  SelCount := 0;
  ChangeCount := 0;
  SelectRange(0, 3);
  SetText(Sample, ST_DEFAULT);
  CheckEqI(0, SelCount, 'no EN_SELCHANGE without its mask');
  CheckEqI(0, ChangeCount, 'no EN_CHANGE without its mask');
  CheckEqI(ENM_NONE, Send(EM_SETEVENTMASK, 0, ENM_CHANGE or ENM_SELCHANGE or ENM_PROTECTED or ENM_LINK),
    'EM_SETEVENTMASK returns the previous mask');
  CheckEqI(ENM_CHANGE or ENM_SELCHANGE or ENM_PROTECTED or ENM_LINK, Send(EM_GETEVENTMASK, 0, 0), 'EM_GETEVENTMASK');
  SelectRange(6, 10);
  Check(SelCount > 0, 'EN_SELCHANGE');
  CheckEqI(EN_SELCHANGE, LastSel.nmhdr.code, 'SELCHANGE.nmhdr.code');
  Check(LastSel.nmhdr.hwndFrom = Edit, 'SELCHANGE.nmhdr.hwndFrom');
  CheckEqI(6, LastSel.chrg.cpMin, 'SELCHANGE.chrg.cpMin');
  CheckEqI(10, LastSel.chrg.cpMax, 'SELCHANGE.chrg.cpMax');
  Check(LastSel.seltyp and SEL_TEXT <> 0, 'SEL_TEXT');
  SelectRange(2, 2);
  CheckEqI(SEL_EMPTY, LastSel.seltyp, 'SEL_EMPTY');
  CheckEqI(2, LastSel.chrg.cpMin, 'empty selection position');
  ChangeCount := 0;
  SelectRange(16, 16);
  ReplaceSelection('!');
  Check(ChangeCount > 0, 'EN_CHANGE under ENM_CHANGE');
  CheckEqS(Sample + '!', GetText(GT_DEFAULT), 'EM_REPLACESEL');

  SelectRange(0, 5);
  Check(SetEffects(CFM_PROTECTED, CFE_PROTECTED, SCF_SELECTION) <> 0, 'set protected');
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_PROTECTED <> 0, 'CFE_PROTECTED');
  ProtCount := 0;
  RefuseProtected := True;
  ReplaceSelection('Bye');
  Check(ProtCount > 0, 'EN_PROTECTED');
  CheckEqI(EN_PROTECTED, LastProt.nmhdr.code, 'ENPROTECTED.nmhdr.code');
  Check(LastProt.nmhdr.hwndFrom = Edit, 'ENPROTECTED.nmhdr.hwndFrom');
  CheckEqI(0, LastProt.chrg.cpMin, 'ENPROTECTED.chrg.cpMin');
  CheckEqI(5, LastProt.chrg.cpMax, 'ENPROTECTED.chrg.cpMax');
  CheckEqS(Sample + '!', GetText(GT_DEFAULT), 'the refused change left the text alone');
  { a format change of protected text asks too: the notification carries the message and its wParam }
  ProtCount := 0;
  CheckEqI(0, SetEffects(CFM_PROTECTED, 0, SCF_SELECTION), 'a refused format change fails');
  Check(ProtCount > 0, 'EN_PROTECTED for a format change');
  CheckEqI(EM_SETCHARFORMAT, LastProt.msg, 'ENPROTECTED.msg');
  CheckEqI(SCF_SELECTION, LastProt.wParam, 'ENPROTECTED.wParam');
  Check(LastProt.lParam <> 0, 'ENPROTECTED.lParam');
  RefuseProtected := False;
  Check(SetEffects(CFM_PROTECTED, 0, SCF_SELECTION) <> 0, 'clear protected');
  GetFormat(CF, SCF_SELECTION);
  Check(CF.dwEffects and CFE_PROTECTED = 0, 'protection removed');
  CheckEqI(ENM_CHANGE or ENM_SELCHANGE or ENM_PROTECTED or ENM_LINK, Send(EM_SETEVENTMASK, 0, ENM_NONE), 'mask off');

  Section('clean up');
  CloseEditor;
  Done;
end.
