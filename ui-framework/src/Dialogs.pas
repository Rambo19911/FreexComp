// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ Dialogs - message boxes and simple dialogs (application-forms.md section 9); the common dialogs
  (controls-standard.md section 28). }
unit Dialogs;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, CommDlg, UIConsts;

type
  TMsgDlgType = (mtWarning, mtError, mtInformation, mtConfirmation, mtCustom);
  TMsgDlgBtn = (mbYes, mbNo, mbOK, mbCancel, mbAbort, mbRetry, mbIgnore, mbAll, mbNoToAll, mbYesToAll, mbHelp,
    mbClose);
  TMsgDlgButtons = set of TMsgDlgBtn;

const
  mbYesNo = [mbYes, mbNo];
  mbYesNoCancel = [mbYes, mbNo, mbCancel];
  mbOKCancel = [mbOK, mbCancel];
  mbAbortRetryIgnore = [mbAbort, mbRetry, mbIgnore];

type
  { receives the built InputQuery form instead of ShowModal and returns the modal result }
  TInputQueryHook = function(Form: TForm): Integer;

  TCommonDialog = class;

  { replaces the comdlg32 call of a common dialog: Data points at the prepared structure (TOpenFilenameW for open and
    save, TChooseColorW, TChooseFontW), which the hook may fill as the system dialog would; returns the dialog's
    result and sets ExtendedError as CommDlgExtendedError would report it }
  TCommonDialogHook = function(Dialog: TCommonDialog; ParentWnd: HWND; Data: Pointer;
    var ExtendedError: DWORD): Boolean;

  { controls-standard.md 28: the base of the common dialogs (non-visual) }
  TCommonDialog = class(TComponent)
  protected
    { prepares the structure, calls RunDialog and copies the result back }
    function DoExecute(ParentWnd: HWND): Boolean; virtual; abstract;
    { the comdlg32 entry point of the dialog }
    function CallDialog(Data: Pointer): Boolean; virtual; abstract;
    { UIF-850 ... UIF-852: non-interactive mode, the hook or the system dialog with the task windows disabled, the
      extended error }
    function RunDialog(ParentWnd: HWND; Data: Pointer): Boolean;
  public
    { the owner is the active form's window, or 0 }
    function Execute: Boolean; overload;
    function Execute(ParentWnd: HWND): Boolean; overload;
  end;

  TOpenOption = (ofReadOnly, ofOverwritePrompt, ofHideReadOnly, ofNoChangeDir, ofShowHelp, ofNoValidate,
    ofAllowMultiSelect, ofExtensionDifferent, ofPathMustExist, ofFileMustExist, ofCreatePrompt, ofShareAware,
    ofNoReadOnlyReturn, ofNoTestFileCreate, ofNoNetworkButton, ofNoDereferenceLinks, ofEnableSizing,
    ofDontAddToRecent, ofForceShowHidden, ofOldStyleDialog, ofNoLongNames, ofEnableIncludeNotify);
  TOpenOptions = set of TOpenOption;

  { controls-standard.md 28.1 }
  TOpenDialog = class(TCommonDialog)
  private
    FDefaultExt: string;
    FFileName: TFileName;
    FFilter: string;
    FFilterIndex: Integer;
    FInitialDir: string;
    FOptions: TOpenOptions;
    FTitle: string;
    FFiles: TStrings;
  protected
    function DoExecute(ParentWnd: HWND): Boolean; override;
    { UIF-863: GetOpenFileNameW }
    function CallDialog(Data: Pointer): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-862: every selected full path }
    property Files: TStrings read FFiles;
  published
    property DefaultExt: string read FDefaultExt write FDefaultExt;
    property FileName: TFileName read FFileName write FFileName;
    property Filter: string read FFilter write FFilter;
    property FilterIndex: Integer read FFilterIndex write FFilterIndex default 1;
    property InitialDir: string read FInitialDir write FInitialDir;
    property Options: TOpenOptions read FOptions write FOptions default [ofHideReadOnly, ofEnableSizing];
    property Title: string read FTitle write FTitle;
  end;

  { UIF-863, UIF-864 }
  TSaveDialog = class(TOpenDialog)
  protected
    { GetSaveFileNameW }
    function CallDialog(Data: Pointer): Boolean; override;
  end;

  TColorDialogOption = (cdFullOpen, cdPreventFullOpen, cdShowHelp, cdSolidColor, cdAnyColor);
  TColorDialogOptions = set of TColorDialogOption;

  { controls-standard.md 28.2 }
  TColorDialog = class(TCommonDialog)
  private
    FColor: TColor;
    FCustomColors: TStrings;
    FOptions: TColorDialogOptions;
    procedure SetCustomColors(Value: TStrings);
  protected
    { UIF-870 }
    function DoExecute(ParentWnd: HWND): Boolean; override;
    function CallDialog(Data: Pointer): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Color: TColor read FColor write FColor default clBlack;
    { 16 entries ColorA=BBGGRR ... ColorP=BBGGRR (hex); missing or invalid entries are white }
    property CustomColors: TStrings read FCustomColors write SetCustomColors;
    property Options: TColorDialogOptions read FOptions write FOptions default [];
  end;

  TFontDialogOption = (fdAnsiOnly, fdTrueTypeOnly, fdEffects, fdFixedPitchOnly, fdForceFontExist, fdNoFaceSel,
    fdNoSizeSel, fdNoStyleSel, fdNoSimulations, fdNoVectorFonts, fdScalableOnly, fdShowHelp, fdWysiwyg, fdLimitSize,
    fdApplyButton, fdNoOEMFonts);
  TFontDialogOptions = set of TFontDialogOption;

  { controls-standard.md 28.3 }
  TFontDialog = class(TCommonDialog)
  private
    FFont: TFont;
    FMaxFontSize: Integer;
    FMinFontSize: Integer;
    FOptions: TFontDialogOptions;
    procedure SetFont(Value: TFont);
  protected
    { UIF-875 }
    function DoExecute(ParentWnd: HWND): Boolean; override;
    function CallDialog(Data: Pointer): Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
  published
    property Font: TFont read FFont write SetFont;
    property MaxFontSize: Integer read FMaxFontSize write FMaxFontSize default 0;
    property MinFontSize: Integer read FMinFontSize write FMinFontSize default 0;
    property Options: TFontDialogOptions read FOptions write FOptions default [fdEffects];
  end;

var
  { while assigned, InputQuery / InputBox build their form and call it instead of ShowModal, in both modes, and
    write no stderr line (tests, tools) }
  InputQueryHook: TInputQueryHook;
  { while assigned, every common dialog calls it instead of comdlg32, in both modes, and writes no stderr line
    (tests, tools) }
  CommonDialogHook: TCommonDialogHook;

{ UIF-477 }
procedure ShowMessage(const Msg: string);
procedure ShowMessageFmt(const Msg: string; const Params: array of const);
{ UIF-478 }
function MessageDlg(const Msg: string; DlgType: TMsgDlgType; Buttons: TMsgDlgButtons; HelpCtx: Longint): Integer;
{ UIF-479 }
function InputQuery(const ACaption, APrompt: string; var Value: string): Boolean;
function InputBox(const ACaption, APrompt, ADefault: string): string;

implementation

{ ---------------------------------------------------------------- TCommonDialog }

type
  TTaskWindows = record
    List: TList;
  end;
  PTaskWindows = ^TTaskWindows;

{ UIF-850 = application-forms.md UIF-445 step 3: every visible and enabled top-level window of the thread }
function DisableTaskWindow(Wnd: HWND; Data: LPARAM): BOOL; stdcall;
var
  P: PTaskWindows;
begin
  P := PTaskWindows(Pointer(Data));
  if IsWindowVisible(Wnd) and IsWindowEnabled(Wnd) then
  begin
    P^.List.Add(Pointer(Wnd));
    EnableWindow(Wnd, False);
  end;
  Result := True;
end;

function TCommonDialog.Execute: Boolean;
var
  Owner: HWND;
begin
  Owner := 0;
  if (Screen <> nil) and (Screen.ActiveForm <> nil) and Screen.ActiveForm.HandleAllocated
    and IsWindowVisible(Screen.ActiveForm.Handle) then
    Owner := Screen.ActiveForm.Handle;
  Result := Execute(Owner);
end;

function TCommonDialog.Execute(ParentWnd: HWND): Boolean;
begin
  Result := DoExecute(ParentWnd);
end;

function TCommonDialog.RunDialog(ParentWnd: HWND; Data: Pointer): Boolean;
var
  Task: TTaskWindows;
  Err: DWORD;
  I: Integer;
begin
  Result := False;
  if Application.NonInteractive and not Assigned(CommonDialogHook) then
  begin
    NonInteractiveLine('[Dialog]', ClassName + '.Execute');
    Exit;
  end;
  Err := 0;
  Task.List := TList.Create;
  try
    EnumThreadWindows(MainThreadID, DisableTaskWindow, LPARAM(@Task));
    if Assigned(CommonDialogHook) then
      Result := CommonDialogHook(Self, ParentWnd, Data, Err)
    else
    begin
      Result := CallDialog(Data);
      if not Result then
        Err := CommDlgExtendedError;
    end;
  finally
    { UIF-445 step 7 }
    for I := 0 to Task.List.Count - 1 do
      if IsWindow(HWND(Task.List[I])) then
        EnableWindow(HWND(Task.List[I]), True);
    Task.List.Free;
  end;
  if not Result and (Err <> 0) then
    raise EInvalidOperation.CreateFmt(SCommonDialogError, [Err]);
end;

{ ---------------------------------------------------------------- TOpenDialog }

const
  MaxFileChars = 32768;                                      { UIF-861 }
  OpenFlags: array[TOpenOption] of DWORD = (OFN_READONLY, OFN_OVERWRITEPROMPT, OFN_HIDEREADONLY, OFN_NOCHANGEDIR,
    0, OFN_NOVALIDATE, OFN_ALLOWMULTISELECT, 0, OFN_PATHMUSTEXIST, OFN_FILEMUSTEXIST, OFN_CREATEPROMPT,
    OFN_SHAREAWARE, OFN_NOREADONLYRETURN, OFN_NOTESTFILECREATE, 0, OFN_NODEREFERENCELINKS, OFN_ENABLESIZING,
    OFN_DONTADDTORECENT, OFN_FORCESHOWHIDDEN, 0, 0, 0);

constructor TOpenDialog.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FFilterIndex := 1;
  FOptions := [ofHideReadOnly, ofEnableSizing];
  FFiles := TStringList.Create;
end;

destructor TOpenDialog.Destroy;
begin
  FFiles.Free;
  inherited Destroy;
end;

function NilIfEmpty(const S: string): PWideChar;
begin
  if S = '' then
    Result := nil
  else
    Result := PWideChar(S);
end;

{ UIF-860 ... UIF-862 }
function TOpenDialog.DoExecute(ParentWnd: HWND): Boolean;
var
  OFN: TOpenFilenameW;
  Buffer: array of WideChar;
  FilterText, Dir, Name: string;
  Opt: TOpenOption;
  I, N: Integer;
  P: PWideChar;
begin
  SetLength(Buffer, MaxFileChars + 1);
  for I := 0 to MaxFileChars do
    Buffer[I] := #0;
  N := Length(FFileName);
  if N > MaxFileChars - 1 then
    N := MaxFileChars - 1;
  for I := 1 to N do
    Buffer[I - 1] := FFileName[I];
  FilterText := '';
  if FFilter <> '' then
  begin
    FilterText := FFilter;
    for I := 1 to Length(FilterText) do
      if FilterText[I] = '|' then
        FilterText[I] := #0;
    FilterText := FilterText + #0#0;
  end;
  FillChar(OFN, SizeOf(OFN), 0);
  OFN.lStructSize := SizeOf(OFN);
  OFN.hwndOwner := ParentWnd;
  OFN.lpstrFilter := NilIfEmpty(FilterText);
  OFN.nFilterIndex := FFilterIndex;
  OFN.lpstrFile := @Buffer[0];
  OFN.nMaxFile := MaxFileChars;
  OFN.lpstrInitialDir := NilIfEmpty(FInitialDir);
  OFN.lpstrTitle := NilIfEmpty(FTitle);
  OFN.lpstrDefExt := NilIfEmpty(FDefaultExt);
  OFN.Flags := OFN_EXPLORER or OFN_NOCHANGEDIR;
  for Opt := Low(TOpenOption) to High(TOpenOption) do
    if Opt in FOptions then
      OFN.Flags := OFN.Flags or OpenFlags[Opt];
  Result := RunDialog(ParentWnd, @OFN);
  if not Result then
    Exit;
  FFiles.Clear;
  P := @Buffer[0];
  Dir := P;
  Inc(P, Length(Dir) + 1);
  if (ofAllowMultiSelect in FOptions) and (P^ <> #0) then
    while P^ <> #0 do
    begin
      Name := P;
      FFiles.Add(IncludeTrailingPathDelimiter(Dir) + Name);
      Inc(P, Length(Name) + 1);
    end
  else
    FFiles.Add(Dir);
  FFileName := FFiles[0];
  FFilterIndex := OFN.nFilterIndex;
  if (OFN.Flags and OFN_READONLY) <> 0 then
    Include(FOptions, ofReadOnly)
  else
    Exclude(FOptions, ofReadOnly);
  if (OFN.Flags and OFN_EXTENSIONDIFFERENT) <> 0 then
    Include(FOptions, ofExtensionDifferent)
  else
    Exclude(FOptions, ofExtensionDifferent);
end;

function TOpenDialog.CallDialog(Data: Pointer): Boolean;
begin
  Result := GetOpenFileNameW(POpenFilenameW(Data)^);
end;

{ ---------------------------------------------------------------- TSaveDialog }

function TSaveDialog.CallDialog(Data: Pointer): Boolean;
begin
  Result := GetSaveFileNameW(POpenFilenameW(Data)^);
end;

{ ---------------------------------------------------------------- TColorDialog }

const
  ColorFlags: array[TColorDialogOption] of DWORD = (CC_FULLOPEN, CC_PREVENTFULLOPEN, 0, CC_SOLIDCOLOR, CC_ANYCOLOR);

constructor TColorDialog.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FColor := clBlack;
  FCustomColors := TStringList.Create;
end;

destructor TColorDialog.Destroy;
begin
  FCustomColors.Free;
  inherited Destroy;
end;

procedure TColorDialog.SetCustomColors(Value: TStrings);
begin
  FCustomColors.Assign(Value);
end;

function TColorDialog.DoExecute(ParentWnd: HWND): Boolean;
var
  CC: TChooseColorW;
  Custom: array[0..15] of Cardinal;
  Opt: TColorDialogOption;
  I, V: Integer;
  Key: string;
begin
  for I := 0 to 15 do
  begin
    Key := 'Color' + Chr(Ord('A') + I);
    if FCustomColors.IndexOfName(Key) >= 0 then
      V := StrToIntDef('$' + FCustomColors.Values[Key], -1)
    else
      V := -1;
    if (V < 0) or (V > $FFFFFF) then
      V := $FFFFFF;
    Custom[I] := Cardinal(V);
  end;
  FillChar(CC, SizeOf(CC), 0);
  CC.lStructSize := SizeOf(CC);
  CC.hwndOwner := ParentWnd;
  CC.rgbResult := COLORREF(ColorToRGB(FColor));
  CC.lpCustColors := @Custom[0];
  CC.Flags := CC_RGBINIT;
  for Opt := Low(TColorDialogOption) to High(TColorDialogOption) do
    if Opt in FOptions then
      CC.Flags := CC.Flags or ColorFlags[Opt];
  Result := RunDialog(ParentWnd, @CC);
  if not Result then
    Exit;
  FColor := TColor(CC.rgbResult);
  FCustomColors.BeginUpdate;
  try
    FCustomColors.Clear;
    for I := 0 to 15 do
      FCustomColors.Add('Color' + Chr(Ord('A') + I) + '=' + IntToHex(Integer(Custom[I]), 6));
  finally
    FCustomColors.EndUpdate;
  end;
end;

function TColorDialog.CallDialog(Data: Pointer): Boolean;
begin
  Result := ChooseColorW(PChooseColorW(Data)^);
end;

{ ---------------------------------------------------------------- TFontDialog }

const
  FontFlags: array[TFontDialogOption] of DWORD = (CF_ANSIONLY, CF_TTONLY, CF_EFFECTS, CF_FIXEDPITCHONLY,
    CF_FORCEFONTEXIST, CF_NOFACESEL, CF_NOSIZESEL, CF_NOSTYLESEL, CF_NOSIMULATIONS, CF_NOVECTORFONTS, CF_SCALABLEONLY,
    0, CF_WYSIWYG, CF_LIMITSIZE, 0, 0);

constructor TFontDialog.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FFont := TFont.Create;
  FOptions := [fdEffects];
end;

destructor TFontDialog.Destroy;
begin
  FFont.Free;
  inherited Destroy;
end;

procedure TFontDialog.SetFont(Value: TFont);
begin
  FFont.Assign(Value);
end;

{ the DPI of the owner: its form's PixelsPerInch, else the window's DPI; the system DPI without an owner }
function OwnerDPI(ParentWnd: HWND): Integer;
var
  C: TWinControl;
  F: TCustomForm;
begin
  Result := 0;
  if ParentWnd <> 0 then
  begin
    C := FindControl(ParentWnd);
    if C <> nil then
    begin
      F := GetParentForm(C);
      if F is TForm then
        Result := TForm(F).PixelsPerInch;
    end;
    if Result <= 0 then
      Result := GetDpiForWindow(ParentWnd);
  end;
  if Result <= 0 then
    Result := GetDpiForSystem;
end;

function TFontDialog.DoExecute(ParentWnd: HWND): Boolean;
var
  CF: TChooseFontW;
  LF: TLogFontW;
  Opt: TFontDialogOption;
  DPI, I: Integer;
  Chosen: TFont;
  Face: string;
begin
  DPI := OwnerDPI(ParentWnd);
  FillChar(LF, SizeOf(LF), 0);
  LF.lfHeight := MulDiv(FFont.Height, DPI, FFont.PixelsPerInch);
  if fsBold in FFont.Style then
    LF.lfWeight := FW_BOLD
  else
    LF.lfWeight := FW_NORMAL;
  LF.lfItalic := Byte(fsItalic in FFont.Style);
  LF.lfUnderline := Byte(fsUnderline in FFont.Style);
  LF.lfStrikeOut := Byte(fsStrikeOut in FFont.Style);
  LF.lfCharSet := FFont.Charset;
  for I := 1 to Length(FFont.Name) do
    if I <= LF_FACESIZE - 1 then
      LF.lfFaceName[I - 1] := FFont.Name[I];
  FillChar(CF, SizeOf(CF), 0);
  CF.lStructSize := SizeOf(CF);
  CF.hwndOwner := ParentWnd;
  CF.lpLogFont := @LF;
  CF.rgbColors := COLORREF(ColorToRGB(FFont.Color));
  CF.Flags := CF_SCREENFONTS or CF_INITTOLOGFONTSTRUCT;
  for Opt := Low(TFontDialogOption) to High(TFontDialogOption) do
    if Opt in FOptions then
      CF.Flags := CF.Flags or FontFlags[Opt];
  if (FMinFontSize <> 0) and (FMaxFontSize <> 0) then
    CF.Flags := CF.Flags or CF_LIMITSIZE;
  if (CF.Flags and CF_LIMITSIZE) <> 0 then
  begin
    CF.nSizeMin := FMinFontSize;
    CF.nSizeMax := FMaxFontSize;
  end;
  Result := RunDialog(ParentWnd, @CF);
  if not Result then
    Exit;
  { one OnChange: the result is built on a copy and assigned at once }
  Chosen := TFont.Create;
  try
    Chosen.PixelsPerInch := FFont.PixelsPerInch;
    Chosen.Assign(FFont);
    Face := '';
    I := 0;
    while (I < LF_FACESIZE) and (LF.lfFaceName[I] <> #0) do
    begin
      Face := Face + LF.lfFaceName[I];
      Inc(I);
    end;
    Chosen.Name := Face;
    Chosen.Height := MulDiv(LF.lfHeight, FFont.PixelsPerInch, DPI);
    Chosen.Style := [];
    if LF.lfWeight >= FW_BOLD then
      Chosen.Style := Chosen.Style + [fsBold];
    if LF.lfItalic <> 0 then
      Chosen.Style := Chosen.Style + [fsItalic];
    if LF.lfUnderline <> 0 then
      Chosen.Style := Chosen.Style + [fsUnderline];
    if LF.lfStrikeOut <> 0 then
      Chosen.Style := Chosen.Style + [fsStrikeOut];
    Chosen.Charset := LF.lfCharSet;
    Chosen.Color := TColor(CF.rgbColors);
    FFont.Assign(Chosen);
  finally
    Chosen.Free;
  end;
end;

function TFontDialog.CallDialog(Data: Pointer): Boolean;
begin
  Result := ChooseFontW(PChooseFontW(Data)^);
end;

{ ---------------------------------------------------------------- message boxes }

procedure ShowMessage(const Msg: string);
begin
  FrameworkMessageBox(Msg, Application.Title, MB_OK, '[ShowMessage]', Msg);
end;

procedure ShowMessageFmt(const Msg: string; const Params: array of const);
begin
  ShowMessage(Format(Msg, Params));
end;

function MessageDlg(const Msg: string; DlgType: TMsgDlgType; Buttons: TMsgDlgButtons; HelpCtx: Longint): Integer;
var
  Flags: Longint;
  Caption: string;
begin
  Buttons := Buttons - [mbHelp];
  if (Buttons = []) or (Buttons = [mbOK]) or (Buttons = [mbClose]) then
    Flags := MB_OK
  else if Buttons = [mbOK, mbCancel] then
    Flags := MB_OKCANCEL
  else if Buttons = [mbYes, mbNo] then
    Flags := MB_YESNO
  else if Buttons = [mbYes, mbNo, mbCancel] then
    Flags := MB_YESNOCANCEL
  else if Buttons = [mbRetry, mbCancel] then
    Flags := MB_RETRYCANCEL
  else if Buttons = [mbAbort, mbRetry, mbIgnore] then
    Flags := MB_ABORTRETRYIGNORE
  else
    raise EInvalidOperation.Create(SUnsupportedButtons);
  case DlgType of
    mtWarning:
      begin
        Flags := Flags or MB_ICONWARNING;
        Caption := SMsgDlgWarning;
      end;
    mtError:
      begin
        Flags := Flags or MB_ICONERROR;
        Caption := SMsgDlgError;
      end;
    mtInformation:
      begin
        Flags := Flags or MB_ICONINFORMATION;
        Caption := SMsgDlgInformation;
      end;
    mtConfirmation:
      begin
        Flags := Flags or MB_ICONQUESTION;
        Caption := SMsgDlgConfirm;
      end;
  else
    Caption := Application.Title;
  end;
  Result := FrameworkMessageBox(Msg, Caption, Flags, '[MessageDlg]', Msg);
end;

function InputQuery(const ACaption, APrompt: string; var Value: string): Boolean;
var
  F: TForm;
  L: TLabel;
  E: TEdit;
  B: TButton;
  R: Integer;
begin
  Result := False;
  if Application.NonInteractive and not Assigned(InputQueryHook) then
  begin
    NonInteractiveLine('[InputQuery]', ACaption + ': ' + APrompt);
    Exit;
  end;
  F := TForm.CreateNew(nil);
  try
    F.BorderStyle := bsDialog;
    F.Position := poScreenCenter;
    F.Caption := ACaption;
    F.ClientWidth := 320;
    F.ClientHeight := 97;
    L := TLabel.Create(F);
    L.AutoSize := True;
    L.Left := 8;
    L.Top := 8;
    L.Caption := APrompt;
    L.Parent := F;
    E := TEdit.Create(F);
    E.Parent := F;
    E.SetBounds(8, 28, 304, 23);
    E.Text := Value;
    E.TabOrder := 0;
    E.SelectAll;
    B := TButton.Create(F);
    B.Parent := F;
    B.SetBounds(164, 64, 75, 25);
    B.Caption := SOKButton;
    B.Default := True;
    B.ModalResult := mrOk;
    B.TabOrder := 1;
    B := TButton.Create(F);
    B.Parent := F;
    B.SetBounds(245, 64, 75, 25);
    B.Caption := SCancelButton;
    B.Cancel := True;
    B.ModalResult := mrCancel;
    B.TabOrder := 2;
    F.ScaleForCurrentDpi;
    if Assigned(InputQueryHook) then
      R := InputQueryHook(F)
    else
      R := F.ShowModal;
    if R = mrOk then
    begin
      Value := E.Text;
      Result := True;
    end;
  finally
    F.Free;
  end;
end;

function InputBox(const ACaption, APrompt, ADefault: string): string;
begin
  Result := ADefault;
  InputQuery(ACaption, APrompt, Result);
end;

initialization
  RegisterClasses([TOpenDialog, TSaveDialog, TColorDialog, TFontDialog]);

end.
