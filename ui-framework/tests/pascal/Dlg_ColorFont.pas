// covers: UIF-850, UIF-851, UIF-852, UIF-870, UIF-875
// needs: desktop
program Dlg_ColorFont;
{ U-094, controls-standard.md 28.2 and 28.3: TColorDialog and TFontDialog.
  Non-interactive mode (the harness sets FXC_UI_NONINTERACTIVE=1): Execute shows nothing, writes
  '[Dialog] <ClassName>.Execute' to stderr (checked by Dlg_ColorFont.err.txt) and returns False.
  Everything else is observed through Dialogs.CommonDialogHook, which replaces the comdlg32 call while assigned: it
  records the prepared CHOOSECOLORW / CHOOSEFONTW, may write a result into it as the system dialog would, and returns
  the result and the extended error. No real dialog window is ever opened. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, CommDlg, Dialogs, UIConsts, TestKit;

type
  TDlgForm = class(TForm)
  published
    ColorDialog1: TColorDialog;
    FontDialog1: TFontDialog;
  end;

  TCounter = class
  public
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

{$FORM Dlg_ColorFont.form}

procedure TCounter.Changed(Sender: TObject);
begin
  Inc(Count);
end;

var
  HookCalls: Integer;
  HookClass: string;
  HookSize, HookFlags: DWORD;
  HookOwner: HWND;
  HookRGB: COLORREF;
  HookCustom: string;
  HookFont: string;
  HookSizeMin, HookSizeMax: Integer;
  AnswerOk: Boolean;
  AnswerError: DWORD;
  AnswerRGB: COLORREF;
  AnswerCustom5: COLORREF;
  AnswerFace: string;
  AnswerHeight, AnswerWeight: Integer;
  AnswerItalic, AnswerUnderline, AnswerStrikeOut, AnswerCharset: Byte;

function FaceText(const LF: TLogFontW): string;
var
  I: Integer;
begin
  Result := '';
  I := 0;
  while (I < LF_FACESIZE) and (LF.lfFaceName[I] <> #0) do
  begin
    Result := Result + LF.lfFaceName[I];
    Inc(I);
  end;
end;

function ColorHook(C: PChooseColorW; var ExtendedError: DWORD): Boolean;
var
  I: Integer;
  P: PCardinal;
begin
  HookSize := C^.lStructSize;
  HookOwner := C^.hwndOwner;
  HookRGB := C^.rgbResult;
  HookFlags := C^.Flags;
  HookCustom := '';
  P := C^.lpCustColors;
  for I := 0 to 15 do
  begin
    HookCustom := HookCustom + IntToHex(P^, 6) + ';';
    Inc(P);
  end;
  Result := AnswerOk;
  ExtendedError := AnswerError;
  if AnswerOk then
  begin
    C^.rgbResult := AnswerRGB;
    P := C^.lpCustColors;
    Inc(P, 5);
    P^ := AnswerCustom5;
  end;
end;

function FontHook(C: PChooseFontW; var ExtendedError: DWORD): Boolean;
var
  LF: PLogFontW;
  I: Integer;
begin
  HookSize := C^.lStructSize;
  HookOwner := C^.hwndOwner;
  HookRGB := C^.rgbColors;
  HookFlags := C^.Flags;
  HookSizeMin := C^.nSizeMin;
  HookSizeMax := C^.nSizeMax;
  LF := C^.lpLogFont;
  HookFont := Format('%s h%d w%d i%d u%d s%d c%d', [FaceText(LF^), LF^.lfHeight, LF^.lfWeight, LF^.lfItalic,
    LF^.lfUnderline, LF^.lfStrikeOut, LF^.lfCharSet]);
  Result := AnswerOk;
  ExtendedError := AnswerError;
  if AnswerOk then
  begin
    FillChar(LF^.lfFaceName, SizeOf(LF^.lfFaceName), 0);
    for I := 1 to Length(AnswerFace) do
      LF^.lfFaceName[I - 1] := AnswerFace[I];
    LF^.lfHeight := AnswerHeight;
    LF^.lfWeight := AnswerWeight;
    LF^.lfItalic := AnswerItalic;
    LF^.lfUnderline := AnswerUnderline;
    LF^.lfStrikeOut := AnswerStrikeOut;
    LF^.lfCharSet := AnswerCharset;
    C^.rgbColors := AnswerRGB;
  end;
end;

function TestHook(Dialog: TCommonDialog; ParentWnd: HWND; Data: Pointer; var ExtendedError: DWORD): Boolean;
begin
  Inc(HookCalls);
  HookClass := Dialog.ClassName;
  if Dialog is TColorDialog then
    Result := ColorHook(PChooseColorW(Data), ExtendedError)
  else
    Result := FontHook(PChooseFontW(Data), ExtendedError);
end;

procedure Answer(Ok: Boolean; AError: DWORD);
begin
  AnswerOk := Ok;
  AnswerError := AError;
  HookCalls := 0;
end;

function ErrorOf(D: TCommonDialog): string;
begin
  Result := '';
  try
    D.Execute(0);
  except
    on E: EInvalidOperation do
      Result := E.Message;
  end;
end;

const
  White = 'FFFFFF;';

var
  F: TForm;
  CD: TColorDialog;
  FD: TFontDialog;
  SF: TDlgForm;
  Counter: TCounter;
  DPI: Integer;
  ExpectedHeight: Integer;
begin
  Application.Initialize;
  Counter := TCounter.Create;
  F := TForm.CreateNew(nil);
  try
    CD := TColorDialog.Create(F);
    FD := TFontDialog.Create(F);

    Section('defaults (28.2, 28.3)');
    CheckEqI(clBlack, CD.Color, 'Color clBlack');
    CheckEqI(0, CD.CustomColors.Count, 'CustomColors empty');
    CheckEqB(True, CD.Options = [], 'TColorDialog.Options []');
    CheckEqS('Segoe UI', FD.Font.Name, 'Font: the default font');
    CheckEqI(-12, FD.Font.Height, 'Font.Height');
    CheckEqI(0, FD.MaxFontSize, 'MaxFontSize 0');
    CheckEqI(0, FD.MinFontSize, 'MinFontSize 0');
    CheckEqB(True, FD.Options = [fdEffects], 'TFontDialog.Options [fdEffects]');
    Check(CD is TCommonDialog, 'TColorDialog is a TCommonDialog');
    Check(FD is TCommonDialog, 'TFontDialog is a TCommonDialog');

    Section('non-interactive (UIF-852)');
    CheckEqB(True, Application.NonInteractive, 'NonInteractive (FXC_UI_NONINTERACTIVE=1)');
    CheckEqB(False, CD.Execute, 'TColorDialog.Execute: False');
    CheckEqB(False, FD.Execute(0), 'TFontDialog.Execute(0): False');

    CommonDialogHook := TestHook;
    F.HandleNeeded;

    Section('the CHOOSECOLORW (UIF-870)');
    CD.Color := clBtnFace;
    CD.CustomColors.Add('ColorA=0000FF');
    CD.CustomColors.Add('ColorC=00FF00');
    CD.CustomColors.Add('ColorD=zz');
    Answer(False, 0);
    CheckEqB(False, CD.Execute(F.Handle), 'cancelled: False');
    CheckEqI(1, HookCalls, 'the hook replaced the dialog');
    CheckEqS('TColorDialog', HookClass, 'with the dialog');
    CheckEqI(72, HookSize, 'lStructSize 72');
    CheckEqB(True, HookOwner = F.Handle, 'hwndOwner = ParentWnd');
    CheckEqI(ColorToRGB(clBtnFace), HookRGB, 'rgbResult = ColorToRGB(Color)');
    CheckEqI(CC_RGBINIT, HookFlags, 'CC_RGBINIT, no options');
    CheckEqS('0000FF;' + White + '00FF00;' + White + White + White + White + White + White + White + White + White
      + White + White + White + White, HookCustom, '16 custom colours; missing and invalid entries white');
    CheckEqI(clBtnFace, CD.Color, 'cancel keeps Color');
    CheckEqI(3, CD.CustomColors.Count, 'and CustomColors');
    CD.Options := [cdFullOpen, cdPreventFullOpen, cdShowHelp, cdSolidColor, cdAnyColor];
    Answer(False, 0);
    CD.Execute(0);
    CheckEqI(CC_RGBINIT or CC_FULLOPEN or CC_PREVENTFULLOPEN or CC_SOLIDCOLOR or CC_ANYCOLOR, HookFlags,
      'every option its flag, cdShowHelp ignored');
    CD.Options := [];

    Section('a colour chosen (28.2)');
    AnswerRGB := $123456;
    AnswerCustom5 := $ABCDEF;
    Answer(True, 0);
    CheckEqB(True, CD.Execute(0), 'Execute True');
    CheckEqI($123456, CD.Color, 'Color = the chosen RGB');
    CheckEqI(16, CD.CustomColors.Count, 'CustomColors rewritten with 16 entries');
    CheckEqS('ColorA=0000FF', CD.CustomColors[0], 'ColorA');
    CheckEqS('ColorB=FFFFFF', CD.CustomColors[1], 'ColorB white');
    CheckEqS('ColorD=FFFFFF', CD.CustomColors[3], 'ColorD: the invalid entry became white');
    CheckEqS('ColorF=ABCDEF', CD.CustomColors[5], 'ColorF: the colour the dialog changed');
    CheckEqS('ColorP=FFFFFF', CD.CustomColors[15], 'ColorP');
    Answer(False, 0);
    CheckEqS('', ErrorOf(CD), 'a cancel (extended error 0) raises nothing');
    Answer(False, CDERR_INITIALIZATION);
    CheckEqS('Common dialog error 0x0002', ErrorOf(CD), 'CDERR_INITIALIZATION raises EInvalidOperation');

    Section('the CHOOSEFONTW (UIF-875)');
    DPI := F.PixelsPerInch;
    FD.Font.Name := 'Arial';
    FD.Font.Height := -16;
    FD.Font.Style := [fsBold, fsStrikeOut];
    FD.Font.Charset := 0;
    FD.Font.Color := clRed;
    Answer(False, 0);
    CheckEqB(False, FD.Execute(F.Handle), 'cancelled: False');
    CheckEqS('TFontDialog', HookClass, 'with the dialog');
    CheckEqI(104, HookSize, 'lStructSize 104');
    CheckEqB(True, HookOwner = F.Handle, 'hwndOwner = ParentWnd');
    CheckEqI(CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT or CF_EFFECTS, HookFlags,
      'CF_SCREENFONTS, CF_INITTOLOGFONTSTRUCT and the default CF_EFFECTS');
    CheckEqS(Format('Arial h%d w700 i0 u0 s1 c0', [MulDiv(-16, DPI, 96)]), HookFont,
      'LOGFONTW from Font, height in pixels of the owner form''s DPI');
    CheckEqI(ColorToRGB(clRed), HookRGB, 'rgbColors = ColorToRGB(Font.Color)');
    CheckEqS('Arial', FD.Font.Name, 'cancel keeps Font');
    Answer(False, 0);
    FD.Execute(0);
    CheckEqS(Format('Arial h%d w700 i0 u0 s1 c0', [MulDiv(-16, Integer(GetDpiForSystem), 96)]), HookFont,
      'no owner: the system DPI');
    FD.Options := [fdAnsiOnly, fdTrueTypeOnly, fdEffects, fdFixedPitchOnly, fdForceFontExist, fdNoFaceSel,
      fdNoSizeSel, fdNoStyleSel, fdNoSimulations, fdNoVectorFonts, fdScalableOnly, fdShowHelp, fdWysiwyg,
      fdApplyButton, fdNoOEMFonts];
    Answer(False, 0);
    FD.Execute(0);
    CheckEqI(CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT or CF_ANSIONLY or CF_TTONLY or CF_EFFECTS or CF_FIXEDPITCHONLY
      or CF_FORCEFONTEXIST or CF_NOFACESEL or CF_NOSIZESEL or CF_NOSTYLESEL or CF_NOSIMULATIONS or CF_NOVECTORFONTS
      or CF_SCALABLEONLY or CF_WYSIWYG, HookFlags, 'every option its flag; fdShowHelp, fdApplyButton, fdNoOEMFonts ignored');
    FD.Options := [fdEffects];
    FD.MinFontSize := 8;
    FD.MaxFontSize := 20;
    Answer(False, 0);
    FD.Execute(0);
    CheckEqI(CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT or CF_EFFECTS or CF_LIMITSIZE, HookFlags,
      'a non-zero size pair: CF_LIMITSIZE');
    CheckEqI(8, HookSizeMin, 'nSizeMin');
    CheckEqI(20, HookSizeMax, 'nSizeMax');
    FD.MinFontSize := 0;
    Answer(False, 0);
    FD.Execute(0);
    CheckEqI(CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT or CF_EFFECTS, HookFlags, 'only one size set: no limit');
    FD.Options := [fdEffects, fdLimitSize];
    Answer(False, 0);
    FD.Execute(0);
    CheckEqI(CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT or CF_EFFECTS or CF_LIMITSIZE, HookFlags,
      'fdLimitSize: CF_LIMITSIZE');
    CheckEqI(0, HookSizeMin, 'with the sizes as set (min)');
    CheckEqI(20, HookSizeMax, 'with the sizes as set (max)');
    FD.Options := [fdEffects];
    FD.MaxFontSize := 0;

    Section('a font chosen (UIF-875)');
    AnswerFace := 'Courier New';
    AnswerHeight := -30;
    AnswerWeight := 400;
    AnswerItalic := 1;
    AnswerUnderline := 1;
    AnswerStrikeOut := 0;
    AnswerCharset := 1;
    AnswerRGB := $0000FF;
    Counter.Count := 0;
    FD.Font.OnChange := Counter.Changed;
    Answer(True, 0);
    CheckEqB(True, FD.Execute(F.Handle), 'Execute True');
    CheckEqI(1, Counter.Count, 'one OnChange');
    CheckEqS('Courier New', FD.Font.Name, 'Name');
    ExpectedHeight := MulDiv(-30, 96, DPI);
    CheckEqI(ExpectedHeight, FD.Font.Height, 'Height back in the font''s own pixels');
    CheckEqB(True, FD.Font.Style = [fsItalic, fsUnderline], 'Style');
    CheckEqI(1, FD.Font.Charset, 'Charset');
    CheckEqI($0000FF, FD.Font.Color, 'Color');
    FD.Font.OnChange := nil;
    Answer(False, CFERR_NOFONTS);
    CheckEqS('Common dialog error 0x2001', ErrorOf(FD), 'CFERR_NOFONTS raises EInvalidOperation');

    Section('streaming');
    SF := TDlgForm.Create(nil);
    try
      CheckEqI(clRed, SF.ColorDialog1.Color, 'Color');
      CheckEqB(True, SF.ColorDialog1.Options = [cdFullOpen], 'TColorDialog.Options');
      CheckEqS('ColorA=00FF00', SF.ColorDialog1.CustomColors[0], 'CustomColors');
      CheckEqS('Courier New', SF.FontDialog1.Font.Name, 'Font.Name');
      CheckEqI(6, SF.FontDialog1.MinFontSize, 'MinFontSize');
      CheckEqI(48, SF.FontDialog1.MaxFontSize, 'MaxFontSize');
      CheckEqB(True, SF.FontDialog1.Options = [fdEffects, fdTrueTypeOnly], 'TFontDialog.Options');
    finally
      SF.Free;
    end;
    CommonDialogHook := nil;
  finally
    F.Free;
    Counter.Free;
  end;
  Done;
end.
