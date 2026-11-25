// covers: RTL-1023, RTL-1024
// phase: 8
program Win_RichEditLayouts;
{ Unit RichEdit (winapi.md section 4.1): the x64 layouts of the twelve records under the 4-byte packing of the
  platform header - sizes, the offsets the rule lists and the offset of every member that follows a pointer-sized
  one - and the aliases (upper-case name, unsuffixed name of the W records). }
uses Windows, RichEdit, TestKit;

function Offset(Base, Field: Pointer): Integer;
begin
  Result := Integer(NativeUInt(Field) - NativeUInt(Base));
end;

function StreamStub(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;
begin
  pcb := 0;
  Result := 0;
end;

var
  CR: TCharRange;
  TR: TTextRangeW;
  FT: TFindTextExW;
  CF: TCharFormat2W;
  PF: TParaFormat2;
  ES: TEditStream;
  GL: TGetTextLengthEx;
  GT: TGetTextEx;
  ST: TSetTextEx;
  EL: TENLink;
  SC: TSelChange;
  EP: TENProtected;
  Raw: PPointer;

begin
  Section('CHARRANGE, TEXTRANGEW, FINDTEXTEXW');
  CheckEqI(8, SizeOf(TCharRange), 'SizeOf(TCharRange)');
  CheckEqI(4, Offset(@CR, @CR.cpMax), 'cpMax');
  CheckEqI(16, SizeOf(TTextRangeW), 'SizeOf(TTextRangeW)');
  CheckEqI(8, Offset(@TR, @TR.lpstrText), 'TEXTRANGEW.lpstrText');
  CheckEqI(24, SizeOf(TFindTextExW), 'SizeOf(TFindTextExW)');
  CheckEqI(8, Offset(@FT, @FT.lpstrText), 'FINDTEXTEXW.lpstrText');
  CheckEqI(16, Offset(@FT, @FT.chrgText), 'chrgText');

  Section('CHARFORMAT2W');
  CheckEqI(116, SizeOf(TCharFormat2W), 'SizeOf(TCharFormat2W)');
  CheckEqI(4, Offset(@CF, @CF.dwMask), 'dwMask');
  CheckEqI(8, Offset(@CF, @CF.dwEffects), 'dwEffects');
  CheckEqI(12, Offset(@CF, @CF.yHeight), 'yHeight');
  CheckEqI(16, Offset(@CF, @CF.yOffset), 'yOffset');
  CheckEqI(20, Offset(@CF, @CF.crTextColor), 'crTextColor');
  CheckEqI(24, Offset(@CF, @CF.bCharSet), 'bCharSet');
  CheckEqI(25, Offset(@CF, @CF.bPitchAndFamily), 'bPitchAndFamily');
  CheckEqI(26, Offset(@CF, @CF.szFaceName), 'szFaceName');
  CheckEqI(90, Offset(@CF, @CF.wWeight), 'wWeight');
  CheckEqI(92, Offset(@CF, @CF.sSpacing), 'sSpacing');
  CheckEqI(96, Offset(@CF, @CF.crBackColor), 'crBackColor');
  CheckEqI(100, Offset(@CF, @CF.lcid), 'lcid');
  CheckEqI(104, Offset(@CF, @CF.dwCookie), 'dwCookie');
  CheckEqI(108, Offset(@CF, @CF.sStyle), 'sStyle');
  CheckEqI(110, Offset(@CF, @CF.wKerning), 'wKerning');
  CheckEqI(112, Offset(@CF, @CF.bUnderlineType), 'bUnderlineType');
  CheckEqI(115, Offset(@CF, @CF.bUnderlineColor), 'bUnderlineColor');

  Section('PARAFORMAT2');
  CheckEqI(188, SizeOf(TParaFormat2), 'SizeOf(TParaFormat2)');
  CheckEqI(8, Offset(@PF, @PF.wNumbering), 'wNumbering');
  CheckEqI(10, Offset(@PF, @PF.wEffects), 'wEffects');
  CheckEqI(12, Offset(@PF, @PF.dxStartIndent), 'dxStartIndent');
  CheckEqI(16, Offset(@PF, @PF.dxRightIndent), 'dxRightIndent');
  CheckEqI(20, Offset(@PF, @PF.dxOffset), 'dxOffset');
  CheckEqI(24, Offset(@PF, @PF.wAlignment), 'wAlignment');
  CheckEqI(26, Offset(@PF, @PF.cTabCount), 'cTabCount');
  CheckEqI(28, Offset(@PF, @PF.rgxTabs), 'rgxTabs');
  CheckEqI(156, Offset(@PF, @PF.dySpaceBefore), 'dySpaceBefore');
  CheckEqI(164, Offset(@PF, @PF.dyLineSpacing), 'dyLineSpacing');
  CheckEqI(168, Offset(@PF, @PF.sStyle), 'PARAFORMAT2.sStyle');
  CheckEqI(170, Offset(@PF, @PF.bLineSpacingRule), 'bLineSpacingRule');
  CheckEqI(172, Offset(@PF, @PF.wShadingWeight), 'wShadingWeight');
  CheckEqI(186, Offset(@PF, @PF.wBorders), 'wBorders');

  Section('EDITSTREAM');
  CheckEqI(20, SizeOf(TEditStream), 'SizeOf(TEditStream)');
  CheckEqI(8, Offset(@ES, @ES.dwError), 'dwError');
  { the address of a procedural member cannot be taken: the member is written and read back at byte 12 }
  FillChar(ES, SizeOf(ES), 0);
  ES.pfnCallback := @StreamStub;
  Raw := PPointer(NativeUInt(@ES) + 12);
  Check(Raw^ = Pointer(@StreamStub), 'pfnCallback at 12');

  Section('GETTEXTLENGTHEX, GETTEXTEX, SETTEXTEX');
  CheckEqI(8, SizeOf(TGetTextLengthEx), 'SizeOf(TGetTextLengthEx)');
  CheckEqI(4, Offset(@GL, @GL.codepage), 'GETTEXTLENGTHEX.codepage');
  CheckEqI(28, SizeOf(TGetTextEx), 'SizeOf(TGetTextEx)');
  CheckEqI(4, Offset(@GT, @GT.flags), 'GETTEXTEX.flags');
  CheckEqI(8, Offset(@GT, @GT.codepage), 'GETTEXTEX.codepage');
  CheckEqI(12, Offset(@GT, @GT.lpDefaultChar), 'lpDefaultChar');
  CheckEqI(20, Offset(@GT, @GT.lpUsedDefChar), 'lpUsedDefChar');
  CheckEqI(8, SizeOf(TSetTextEx), 'SizeOf(TSetTextEx)');
  CheckEqI(4, Offset(@ST, @ST.codepage), 'SETTEXTEX.codepage');

  Section('ENLINK, SELCHANGE, ENPROTECTED');
  CheckEqI(52, SizeOf(TENLink), 'SizeOf(TENLink)');
  CheckEqI(24, Offset(@EL, @EL.msg), 'ENLINK.msg');
  CheckEqI(28, Offset(@EL, @EL.wParam), 'ENLINK.wParam');
  CheckEqI(36, Offset(@EL, @EL.lParam), 'ENLINK.lParam');
  CheckEqI(44, Offset(@EL, @EL.chrg), 'ENLINK.chrg');
  CheckEqI(36, SizeOf(TSelChange), 'SizeOf(TSelChange)');
  CheckEqI(24, Offset(@SC, @SC.chrg), 'SELCHANGE.chrg');
  CheckEqI(32, Offset(@SC, @SC.seltyp), 'seltyp');
  CheckEqI(52, SizeOf(TENProtected), 'SizeOf(TENProtected)');
  CheckEqI(24, Offset(@EP, @EP.msg), 'ENPROTECTED.msg');
  CheckEqI(28, Offset(@EP, @EP.wParam), 'ENPROTECTED.wParam');
  CheckEqI(36, Offset(@EP, @EP.lParam), 'ENPROTECTED.lParam');
  CheckEqI(44, Offset(@EP, @EP.chrg), 'ENPROTECTED.chrg');

  Section('aliases (RTL-832)');
  CheckEqI(SizeOf(TCharRange), SizeOf(CHARRANGE), 'CHARRANGE');
  CheckEqI(SizeOf(TTextRangeW), SizeOf(TEXTRANGEW), 'TEXTRANGEW');
  CheckEqI(SizeOf(TTextRangeW), SizeOf(TTextRange), 'TTextRange');
  CheckEqI(SizeOf(TFindTextExW), SizeOf(FINDTEXTEXW), 'FINDTEXTEXW');
  CheckEqI(SizeOf(TFindTextExW), SizeOf(TFindTextEx), 'TFindTextEx');
  CheckEqI(SizeOf(TCharFormat2W), SizeOf(CHARFORMAT2W), 'CHARFORMAT2W');
  CheckEqI(SizeOf(TCharFormat2W), SizeOf(TCharFormat2), 'TCharFormat2');
  CheckEqI(SizeOf(TParaFormat2), SizeOf(PARAFORMAT2), 'PARAFORMAT2');
  CheckEqI(SizeOf(TEditStream), SizeOf(EDITSTREAM), 'EDITSTREAM');
  CheckEqI(SizeOf(TGetTextLengthEx), SizeOf(GETTEXTLENGTHEX), 'GETTEXTLENGTHEX');
  CheckEqI(SizeOf(TGetTextEx), SizeOf(GETTEXTEX), 'GETTEXTEX');
  CheckEqI(SizeOf(TSetTextEx), SizeOf(SETTEXTEX), 'SETTEXTEX');
  CheckEqI(SizeOf(TENLink), SizeOf(ENLINK), 'ENLINK');
  CheckEqI(SizeOf(TSelChange), SizeOf(SELCHANGE), 'SELCHANGE');
  CheckEqI(SizeOf(TENProtected), SizeOf(ENPROTECTED), 'ENPROTECTED');
  CheckEqI(SizeOf(Pointer), SizeOf(PCharRange) + SizeOf(PEditStream) - SizeOf(PENLink), 'pointer types');
  Done;
end.
