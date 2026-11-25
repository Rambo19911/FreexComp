// covers: UIF-1320, UIF-1321, UIF-1322, UIF-1323, UIF-1324
// needs: desktop
// phase: 8
program Ctl_RichEditFormat;
{ U-145, rich-edit.md sections 3 and 4: SelAttributes and DefAttributes round trips of every member against the
  engine's CHARFORMAT2W (UIF-1320, UIF-1321): face name (cut at 31 characters), size in points (rounded), the four
  styles, colour with the automatic colour, Protected; the format of an empty selection is the one the next
  character gets; a mixed selection: ConsistentAttributes and the values of the first character (UIF-1322); Assign
  from and to a TFont and between attribute objects; the control's Font is the default format and, with PlainText,
  the format of all text; the handle-less rules (UIF-1323). Paragraph (UIF-1324): alignment, the three indents in
  points, bullet numbering, tab stops (count, clamp, extension by an index), one mask bit per setter, Assign. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ComCtrls, RichEdit, TestKit;

const
  CRLF = #13#10;

procedure NativeSelect(H: HWND; First, Last: Integer);
var
  R: TCharRange;
begin
  R.cpMin := First;
  R.cpMax := Last;
  SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@R));
end;

procedure NativeFormat(R: TRichEdit; Scope: WPARAM; var CF: TCharFormat2W);
begin
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  SendMessageW(R.Handle, EM_GETCHARFORMAT, Scope, LPARAM(@CF));
end;

procedure NativePara(R: TRichEdit; var PF: TParaFormat2);
begin
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  SendMessageW(R.Handle, EM_GETPARAFORMAT, 0, LPARAM(@PF));
end;

function StyleStr(S: TFontStyles): string;
begin
  Result := '';
  if fsBold in S then
    Result := Result + 'B';
  if fsItalic in S then
    Result := Result + 'I';
  if fsUnderline in S then
    Result := Result + 'U';
  if fsStrikeOut in S then
    Result := Result + 'S';
  if Result = '' then
    Result := '-';
end;

function ConsistentStr(C: TConsistentAttributes): string;
const
  Names: array[TConsistentAttribute] of string = ('Bold', 'Color', 'Face', 'Italic', 'Size', 'StrikeOut',
    'Underline', 'Protected');
var
  A: TConsistentAttribute;
begin
  Result := '';
  for A := Low(TConsistentAttribute) to High(TConsistentAttribute) do
    if A in C then
      Result := Result + Names[A] + ' ';
  Result := Trim(Result);
end;

function AlignStr(A: TAlignment): string;
begin
  case A of
    taLeftJustify: Result := 'left';
    taRightJustify: Result := 'right';
  else
    Result := 'center';
  end;
end;

{ every member of an attribute object }
function Describe(A: TTextAttributes): string;
begin
  Result := Format('%s %d %s %s', [A.Name, A.Size, StyleStr(A.Style), ColorToString(A.Color)]);
  if A.Protected then
    Result := Result + ' protected';
end;

procedure Run;
var
  F: TForm;
  R, R2: TRichEdit;
  CF: TCharFormat2W;
  PF: TParaFormat2;
  Fnt: TFont;
  Long, Base: string;
  I: Integer;
begin
  Application.Initialize;
  F := TForm.CreateNew(nil);
  Fnt := TFont.Create;
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    F.Font.Name := 'Tahoma';
    F.Font.Size := 10;
    R := TRichEdit.Create(F);
    R.Parent := F;
    R.HandleNeeded;
    R.Text := 'plain bold rest';
    Base := 'Tahoma 10 - clWindowText';

    Section('SelAttributes round trips (UIF-1320, UIF-1321)');
    Check(R.SelAttributes <> nil, 'SelAttributes exists');
    Check(R.SelAttributes = R.SelAttributes, 'one object for the life of the control');
    NativeSelect(R.Handle, 6, 10);
    CheckEqS(Base, Describe(R.SelAttributes), 'the text has the font of the control');
    R.SelAttributes.Style := [fsBold];
    CheckEqS('B', StyleStr(R.SelAttributes.Style), 'Style [fsBold]');
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqB(True, (CF.dwEffects and CFE_BOLD) <> 0, 'CFE_BOLD in the engine');
    R.SelAttributes.Style := [fsItalic, fsUnderline, fsStrikeOut];
    CheckEqS('IUS', StyleStr(R.SelAttributes.Style), 'Style italic, underline, strike-out (bold removed)');
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqI(CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT,
      CF.dwEffects and (CFE_BOLD or CFE_ITALIC or CFE_UNDERLINE or CFE_STRIKEOUT), 'the four effect bits');
    R.SelAttributes.Style := [fsBold];
    R.SelAttributes.Name := 'Courier New';
    CheckEqS('Courier New', R.SelAttributes.Name, 'Name');
    R.SelAttributes.Size := 14;
    CheckEqI(14, R.SelAttributes.Size, 'Size');
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqI(280, CF.yHeight, 'yHeight = Size x 20 twips');
    R.SelAttributes.Color := clRed;
    CheckEqS('clRed', ColorToString(R.SelAttributes.Color), 'Color');
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqI(ColorToRGB(clRed), CF.crTextColor, 'crTextColor');
    CheckEqB(False, (CF.dwEffects and CFE_AUTOCOLOR) <> 0, 'no CFE_AUTOCOLOR');
    CheckEqS('Courier New 14 B clRed', Describe(R.SelAttributes), 'all members, each setter left the others alone');
    NativeSelect(R.Handle, 0, 5);
    CheckEqS(Base, Describe(R.SelAttributes), 'the text before the run is unchanged');
    NativeSelect(R.Handle, 11, 15);
    CheckEqS(Base, Describe(R.SelAttributes), 'the text after the run is unchanged');

    Section('automatic colour (UIF-1321)');
    NativeSelect(R.Handle, 6, 10);
    R.SelAttributes.Color := clWindowText;
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqB(True, (CF.dwEffects and CFE_AUTOCOLOR) <> 0, 'clWindowText is written as CFE_AUTOCOLOR');
    CheckEqS('clWindowText', ColorToString(R.SelAttributes.Color), 'an automatic colour reads as clWindowText');
    R.SelAttributes.Color := clBlue;
    R.SelAttributes.Color := clDefault;
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqB(True, (CF.dwEffects and CFE_AUTOCOLOR) <> 0, 'clDefault is written as CFE_AUTOCOLOR');
    R.SelAttributes.Color := clBtnFace;
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqI(ColorToRGB(clBtnFace), CF.crTextColor, 'a system colour is written as its RGB value');
    R.SelAttributes.Color := clRed;

    Section('name length and size rounding (UIF-1321)');
    Long := 'A face name that is longer than thirty-one characters';
    R.SelAttributes.Name := Long;
    CheckEqS(Copy(Long, 1, 31), R.SelAttributes.Name, 'a long name is cut at 31 characters');
    R.SelAttributes.Name := 'Courier New';
    FillChar(CF, SizeOf(CF), 0);
    CF.cbSize := SizeOf(CF);
    CF.dwMask := CFM_SIZE;
    CF.yHeight := 250;
    SendMessageW(R.Handle, EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF));
    CheckEqI(13, R.SelAttributes.Size, '250 twips read as 13 points (nearest)');
    CF.yHeight := 249;
    SendMessageW(R.Handle, EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF));
    CheckEqI(12, R.SelAttributes.Size, '249 twips read as 12 points');
    R.SelAttributes.Size := 14;

    Section('Protected (UIF-1321)');
    CheckEqB(False, R.SelAttributes.Protected, 'not protected');
    R.SelAttributes.Protected := True;
    CheckEqB(True, R.SelAttributes.Protected, 'Protected');
    NativeFormat(R, SCF_SELECTION, CF);
    CheckEqB(True, (CF.dwEffects and CFE_PROTECTED) <> 0, 'CFE_PROTECTED in the engine');
    CheckEqS('Courier New 14 B clRed protected', Describe(R.SelAttributes), 'the other members are unchanged');
    R.SelAttributes.Protected := False;
    CheckEqB(False, R.SelAttributes.Protected, 'the protection can be taken away again');

    Section('mixed selection (UIF-1322)');
    NativeSelect(R.Handle, 6, 10);
    CheckEqS('Bold Color Face Italic Size StrikeOut Underline Protected',
      ConsistentStr(R.SelAttributes.ConsistentAttributes), 'one run: everything is consistent');
    NativeSelect(R.Handle, 0, 15);
    CheckEqS('Italic StrikeOut Underline Protected', ConsistentStr(R.SelAttributes.ConsistentAttributes),
      'plain + formatted run: bold, colour, face and size differ');
    CheckEqS(Base, Describe(R.SelAttributes), 'a mixed selection reads the values of its first character');
    NativeSelect(R.Handle, 8, 15);
    CheckEqS('Courier New 14 B clRed', Describe(R.SelAttributes), 'first character inside the run');
    CheckEqS('Italic StrikeOut Underline Protected', ConsistentStr(R.SelAttributes.ConsistentAttributes),
      'ConsistentAttributes is still that of the whole selection');
    CheckEqS('8+7', Format('%d+%d', [R.SelStart, R.SelLength]), 'reading leaves the selection alone');

    Section('empty selection (UIF-1320)');
    NativeSelect(R.Handle, 8, 8);
    CheckEqS('Courier New 14 B clRed', Describe(R.SelAttributes), 'the caret inside the run has its format');
    NativeSelect(R.Handle, 2, 2);
    R.SelAttributes.Style := [fsItalic];
    R.SelText := 'Z';
    NativeSelect(R.Handle, 2, 3);
    CheckEqS('I', StyleStr(R.SelAttributes.Style), 'the next character gets the format set at the caret');
    NativeSelect(R.Handle, 0, 2);
    CheckEqS('-', StyleStr(R.SelAttributes.Style), 'the characters around it keep theirs');
    R.Text := 'plain bold rest';
    NativeSelect(R.Handle, 6, 10);
    R.SelAttributes.Style := [fsBold];
    R.SelAttributes.Color := clRed;

    Section('DefAttributes (UIF-1320, UIF-1322)');
    CheckEqS(Base, Describe(R.DefAttributes), 'the default format is the font of the control');
    CheckEqS('Bold Color Face Italic Size StrikeOut Underline Protected',
      ConsistentStr(R.DefAttributes.ConsistentAttributes), 'always the full set');
    R.DefAttributes.Size := 20;
    NativeFormat(R, SCF_DEFAULT, CF);
    CheckEqI(400, CF.yHeight, 'Size reaches the default format of the engine');
    R.DefAttributes.Name := 'Arial';
    R.DefAttributes.Style := [fsUnderline];
    R.DefAttributes.Color := clGreen;
    CheckEqS('Arial 20 U clGreen', Describe(R.DefAttributes), 'every member');
    R.Text := 'new text';
    NativeSelect(R.Handle, 0, 8);
    CheckEqS('Arial 20 U clGreen', Describe(R.SelAttributes), 'text set afterwards has the default format');
    R.DefAttributes.Color := clWindowText;
    NativeFormat(R, SCF_DEFAULT, CF);
    CheckEqB(True, (CF.dwEffects and CFE_AUTOCOLOR) <> 0, 'automatic colour as the default');

    Section('Assign (UIF-1320)');
    Fnt.Name := 'Verdana';
    Fnt.Size := 16;
    Fnt.Style := [fsBold, fsUnderline];
    Fnt.Color := clBlue;
    NativeSelect(R.Handle, 0, 3);
    R.SelAttributes.Assign(Fnt);
    CheckEqS('Verdana 16 BU clBlue', Describe(R.SelAttributes), 'Assign from a TFont');
    Fnt.Name := 'Tahoma';
    Fnt.Size := 8;
    Fnt.Style := [];
    Fnt.Color := clBlack;
    Fnt.Assign(R.SelAttributes);
    CheckEqS('Verdana 16 BU clBlue', Format('%s %d %s %s', [Fnt.Name, Fnt.Size, StyleStr(Fnt.Style),
      ColorToString(Fnt.Color)]), 'Assign to a TFont');
    R.SelAttributes.Protected := True;
    NativeSelect(R.Handle, 4, 8);
    R.SelAttributes := R.DefAttributes;
    CheckEqS('Arial 20 U clWindowText', Describe(R.SelAttributes), 'SelAttributes := DefAttributes');
    R.DefAttributes := R.SelAttributes;
    CheckEqS('Arial 20 U clWindowText', Describe(R.DefAttributes), 'DefAttributes := SelAttributes');
    NativeSelect(R.Handle, 0, 3);
    R.DefAttributes.Assign(R.SelAttributes);
    CheckEqS('Verdana 16 BU clBlue protected', Describe(R.DefAttributes),
      'Assign from an attribute object copies Protected too');
    R.DefAttributes.Protected := False;
    R.SelAttributes.Protected := False;

    Section('font of the control (UIF-1321)');
    R.Text := 'plain bold rest';
    NativeSelect(R.Handle, 6, 10);
    R.SelAttributes.Style := [fsBold];
    R.SelAttributes.Color := clRed;
    R.SelAttributes.Name := 'Courier New';
    R.Font.Name := 'Georgia';
    R.Font.Size := 18;
    R.Font.Style := [fsItalic];
    R.Font.Color := clNavy;
    CheckEqS('Georgia 18 I clNavy', Describe(R.DefAttributes), 'a font change is assigned to DefAttributes');
    NativeSelect(R.Handle, 6, 10);
    CheckEqS('Courier New', R.SelAttributes.Name, 'rich text: a formatted run keeps its face');
    CheckEqS('clRed', ColorToString(R.SelAttributes.Color), 'and its colour');
    R.SelText := 'typed';
    R.Text := 'set later';
    NativeSelect(R.Handle, 0, 9);
    CheckEqS('Georgia 18 I clNavy', Describe(R.SelAttributes), 'new text has the new font');
    R.Text := 'plain bold rest';
    NativeSelect(R.Handle, 6, 10);
    R.SelAttributes.Style := [fsBold];
    R.SelAttributes.Color := clRed;
    R.PlainText := True;
    NativeSelect(R.Handle, 0, 15);
    CheckEqS('Face Size StrikeOut Underline Protected', ConsistentStr(R.SelAttributes.ConsistentAttributes),
      'PlainText alone changes no formatting (UIF-1325)');
    R.Font.Style := [];
    NativeSelect(R.Handle, 0, 15);
    CheckEqS('Bold Color Face Italic Size StrikeOut Underline Protected',
      ConsistentStr(R.SelAttributes.ConsistentAttributes), 'PlainText: the next font change formats all text');
    CheckEqS('Georgia 18 - clNavy', Describe(R.SelAttributes), 'with the font of the control');
    R.PlainText := False;
    R.ParentFont := True;
    CheckEqS('Tahoma 10 - clWindowText', Describe(R.DefAttributes), 'ParentFont: the font of the parent');
    F.Font.Size := 12;
    CheckEqS('Tahoma 12 - clWindowText', Describe(R.DefAttributes), 'a font change of the parent');
    F.Font.Size := 10;

    Section('paragraph: alignment and indents (UIF-1324)');
    R.Text := 'first' + CRLF + 'second' + CRLF + 'third';
    Check(R.Paragraph <> nil, 'Paragraph exists');
    NativeSelect(R.Handle, 7, 7);
    CheckEqS('left 0 0 0', Format('%s %d %d %d', [AlignStr(R.Paragraph.Alignment), R.Paragraph.FirstIndent,
      R.Paragraph.LeftIndent, R.Paragraph.RightIndent]), 'a new paragraph');
    R.Paragraph.Alignment := taCenter;
    CheckEqS('center', AlignStr(R.Paragraph.Alignment), 'Alignment taCenter');
    NativePara(R, PF);
    CheckEqI(PFA_CENTER, PF.wAlignment, 'PFA_CENTER in the engine');
    R.Paragraph.Alignment := taRightJustify;
    NativePara(R, PF);
    CheckEqI(PFA_RIGHT, PF.wAlignment, 'PFA_RIGHT');
    R.Paragraph.FirstIndent := 10;
    R.Paragraph.LeftIndent := 5;
    R.Paragraph.RightIndent := 7;
    CheckEqS('right 10 5 7', Format('%s %d %d %d', [AlignStr(R.Paragraph.Alignment), R.Paragraph.FirstIndent,
      R.Paragraph.LeftIndent, R.Paragraph.RightIndent]), 'the three indents in points; each setter sends its own member');
    NativePara(R, PF);
    CheckEqS('200 100 140', Format('%d %d %d', [PF.dxStartIndent, PF.dxOffset, PF.dxRightIndent]),
      'dxStartIndent, dxOffset, dxRightIndent in twips');
    NativeSelect(R.Handle, 0, 0);
    CheckEqS('left 0 0 0', Format('%s %d %d %d', [AlignStr(R.Paragraph.Alignment), R.Paragraph.FirstIndent,
      R.Paragraph.LeftIndent, R.Paragraph.RightIndent]), 'the paragraph before is unchanged');
    NativeSelect(R.Handle, 2, 15);
    R.Paragraph.Alignment := taCenter;
    NativeSelect(R.Handle, 0, 0);
    CheckEqS('center', AlignStr(R.Paragraph.Alignment), 'a selection formats every paragraph it touches: the first');
    NativeSelect(R.Handle, 16, 16);
    CheckEqS('center', AlignStr(R.Paragraph.Alignment), 'and the last');
    R.Paragraph.Alignment := taLeftJustify;
    CheckEqS('left', AlignStr(R.Paragraph.Alignment), 'taLeftJustify');
    NativeSelect(R.Handle, 7, 7);
    R.Paragraph.Alignment := taRightJustify;

    Section('paragraph: numbering (UIF-1324)');
    NativeSelect(R.Handle, 7, 7);
    CheckEqB(True, R.Paragraph.Numbering = nsNone, 'nsNone');
    R.Paragraph.Numbering := nsBullet;
    CheckEqB(True, R.Paragraph.Numbering = nsBullet, 'nsBullet');
    NativePara(R, PF);
    CheckEqI(PFN_BULLET, PF.wNumbering, 'PFN_BULLET in the engine');
    PF.dwMask := PFM_NUMBERING;
    PF.wNumbering := 2;
    SendMessageW(R.Handle, EM_SETPARAFORMAT, 0, LPARAM(@PF));
    CheckEqB(True, R.Paragraph.Numbering = nsBullet, 'another numbering kind reads as nsBullet');
    R.Paragraph.Numbering := nsNone;
    NativePara(R, PF);
    CheckEqI(0, PF.wNumbering, 'nsNone: wNumbering 0');
    CheckEqS('right 10', Format('%s %d', [AlignStr(R.Paragraph.Alignment), R.Paragraph.FirstIndent]),
      'numbering left alignment and indent alone');

    Section('paragraph: tabs (UIF-1324)');
    CheckEqI(0, R.Paragraph.TabCount, 'no tab stops');
    CheckEqI(0, R.Paragraph.Tab[0], 'Tab[0] without tab stops');
    R.Paragraph.Tab[0] := 20;
    CheckEqI(1, R.Paragraph.TabCount, 'writing Tab[0] extends TabCount to 1');
    R.Paragraph.Tab[2] := 60;
    R.Paragraph.Tab[1] := 40;
    CheckEqI(3, R.Paragraph.TabCount, 'writing Tab[2] extends TabCount to 3');
    CheckEqS('20 40 60', Format('%d %d %d', [R.Paragraph.Tab[0], R.Paragraph.Tab[1], R.Paragraph.Tab[2]]),
      'tab positions in points');
    NativePara(R, PF);
    CheckEqS('3: 400 800 1200', Format('%d: %d %d %d', [PF.cTabCount, PF.rgxTabs[0], PF.rgxTabs[1], PF.rgxTabs[2]]),
      'cTabCount and rgxTabs in twips');
    CheckEqI(0, R.Paragraph.Tab[3], 'an index >= TabCount reads 0');
    R.Paragraph.TabCount := 2;
    CheckEqI(2, R.Paragraph.TabCount, 'TabCount := 2');
    CheckEqI(0, R.Paragraph.Tab[2], 'the removed tab stop reads 0');
    for I := 0 to MAX_TAB_STOPS - 1 do
      R.Paragraph.Tab[Byte(I)] := (I + 1) * 10;
    CheckEqI(MAX_TAB_STOPS, R.Paragraph.TabCount, 'MAX_TAB_STOPS tab stops');
    R.Paragraph.Tab[40] := 999;
    R.Paragraph.TabCount := 100;
    CheckEqI(MAX_TAB_STOPS, R.Paragraph.TabCount, 'a larger count is clamped');
    CheckEqI(320, R.Paragraph.Tab[31], 'the last tab stop');
    R.Paragraph.TabCount := -5;
    CheckEqI(0, R.Paragraph.TabCount, 'a negative count is 0');
    CheckEqS('right 10 5 7', Format('%s %d %d %d', [AlignStr(R.Paragraph.Alignment), R.Paragraph.FirstIndent,
      R.Paragraph.LeftIndent, R.Paragraph.RightIndent]), 'the tabs left the other members alone');

    Section('paragraph: Assign (UIF-1324)');
    R.Paragraph.Numbering := nsBullet;
    R.Paragraph.Tab[0] := 30;
    R.Paragraph.Tab[1] := 90;
    R2 := TRichEdit.Create(F);
    R2.Parent := F;
    R2.Text := 'other';
    R2.HandleNeeded;
    R2.Paragraph := R.Paragraph;
    CheckEqS('right 10 5 7 bullet 2: 30 90', Format('%s %d %d %d %s %d: %d %d', [AlignStr(R2.Paragraph.Alignment),
      R2.Paragraph.FirstIndent, R2.Paragraph.LeftIndent, R2.Paragraph.RightIndent,
      Copy('none  bullet', Ord(R2.Paragraph.Numbering) * 6 + 1, 6), R2.Paragraph.TabCount, R2.Paragraph.Tab[0],
      R2.Paragraph.Tab[1]]), 'Paragraph := copies every member');
    R2.Free;

    Section('without a handle (UIF-1323, UIF-1324)');
    R2 := TRichEdit.Create(nil);
    try
      R2.Font.Name := 'Arial';
      R2.Font.Size := 11;
      R2.Text := 'no window';
      R2.SelectAll;
      CheckEqS('Arial 11 - clWindowText', Describe(R2.DefAttributes), 'DefAttributes reads the font');
      CheckEqS('Arial 11 - clWindowText', Describe(R2.SelAttributes), 'SelAttributes reads the values of DefAttributes');
      CheckEqS('Bold Color Face Italic Size StrikeOut Underline Protected',
        ConsistentStr(R2.SelAttributes.ConsistentAttributes), 'everything is consistent');
      R2.SelAttributes.Name := 'Verdana';
      R2.SelAttributes.Size := 30;
      R2.SelAttributes.Style := [fsBold];
      R2.SelAttributes.Color := clRed;
      R2.SelAttributes.Protected := True;
      R2.SelAttributes.Assign(Fnt);
      CheckEqS('Arial 11 - clWindowText', Describe(R2.SelAttributes), 'SelAttributes writes are ignored');
      R2.DefAttributes.Name := 'Georgia';
      R2.DefAttributes.Size := 22;
      R2.DefAttributes.Style := [fsBold, fsItalic];
      R2.DefAttributes.Color := clTeal;
      R2.DefAttributes.Protected := True;
      CheckEqS('Georgia 22 BI clTeal', Format('%s %d %s %s', [R2.Font.Name, R2.Font.Size, StyleStr(R2.Font.Style),
        ColorToString(R2.Font.Color)]), 'DefAttributes writes act on the font');
      CheckEqB(False, R2.DefAttributes.Protected, 'Protected stays False');
      R2.DefAttributes.Assign(Fnt);
      CheckEqS('Verdana 16 BU clBlue', Describe(R2.DefAttributes), 'DefAttributes.Assign(TFont) sets the font');
      CheckEqS('left 0 0 0 0', Format('%s %d %d %d %d', [AlignStr(R2.Paragraph.Alignment), R2.Paragraph.FirstIndent,
        R2.Paragraph.LeftIndent, R2.Paragraph.RightIndent, R2.Paragraph.TabCount]), 'Paragraph reads the defaults');
      CheckEqB(True, R2.Paragraph.Numbering = nsNone, 'Numbering nsNone');
      R2.Paragraph.Alignment := taCenter;
      R2.Paragraph.FirstIndent := 5;
      R2.Paragraph.LeftIndent := 5;
      R2.Paragraph.RightIndent := 5;
      R2.Paragraph.Numbering := nsBullet;
      R2.Paragraph.Tab[0] := 5;
      R2.Paragraph.TabCount := 3;
      R2.Paragraph.Assign(R.Paragraph);
      CheckEqS('left 0 0 0 0', Format('%s %d %d %d %d', [AlignStr(R2.Paragraph.Alignment), R2.Paragraph.FirstIndent,
        R2.Paragraph.LeftIndent, R2.Paragraph.RightIndent, R2.Paragraph.TabCount]), 'Paragraph writes are ignored');
      Check(not R2.HandleAllocated, 'no handle was created');
      R2.Parent := F;
      R2.HandleNeeded;
      NativeSelect(R2.Handle, 0, 9);
      CheckEqS('Verdana 16 BU clBlue', Describe(R2.SelAttributes), 'the window starts with that font as its format');
    finally
      R2.Free;
    end;
  finally
    Fnt.Free;
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
