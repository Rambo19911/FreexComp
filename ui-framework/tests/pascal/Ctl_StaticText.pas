// covers: UIF-611
// needs: desktop
program Ctl_StaticText;
{ U-064, controls-standard.md section 18: TStaticText defaults (65 x 17, clBtnFace, ParentColor, AutoSize, no
  TabStop); a sub-classed system STATIC with SS_NOTIFY, SS_LEFT / SS_CENTER / SS_RIGHT, SS_NOPREFIX, WS_BORDER and
  SS_SUNKEN, Alignment, BorderStyle and ShowAccelChar re-creating the window and keeping the caption; the caption
  as window text; Color through WM_CTLCOLORSTATIC; the accelerator focusing FocusControl as TLabel does (UIF-611);
  OnClick from the mouse; streaming (FocusControl reference); leak check. Measured auto-size is U-065. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
  end;

  TStaticForm = class(TForm)
  published
    Prompt: TStaticText;
    Value: TEdit;
    procedure PromptClick(Sender: TObject);
  end;

{$FORM Ctl_StaticText.form}

var
  Log: TLog;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TStaticForm.PromptClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'form;';
end;

function ClassOf(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := GetClassNameW(H, @Buf[0], 128);
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function WindowText(H: HWND): string;
var
  Buf: array[0..255] of WideChar;
  N: Integer;
begin
  N := GetWindowTextW(H, @Buf[0], 256);
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function StyleOf(C: TWinControl): NativeInt;
begin
  Result := GetWindowLongPtrW(C.Handle, GWL_STYLE);
end;

{ the background colour the parent's WM_CTLCOLORSTATIC answer selects }
function CtlColor(C: TWinControl): COLORREF;
var
  DC: HDC;
begin
  DC := GetDC(C.Handle);
  try
    SendMessageW(C.Parent.Handle, WM_CTLCOLORSTATIC, WPARAM(DC), LPARAM(C.Handle));
    Result := GetBkColor(DC);
  finally
    ReleaseDC(C.Handle, DC);
  end;
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  S: TStaticText;
  E: TEdit;
  SF: TStaticForm;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    S := TStaticText.Create(F);

    Section('defaults');
    CheckEqI(65, S.Width, 'Width 65');
    CheckEqI(17, S.Height, 'Height 17');
    CheckEqB(True, S.Alignment = taLeftJustify, 'Alignment taLeftJustify');
    CheckEqB(True, S.AutoSize, 'AutoSize True');
    CheckEqB(True, S.BorderStyle = sbsNone, 'BorderStyle sbsNone');
    CheckEqS('', S.Caption, 'Caption');
    CheckEqB(True, S.Color = clBtnFace, 'Color clBtnFace');
    CheckEqB(True, S.FocusControl = nil, 'FocusControl nil');
    CheckEqB(True, S.ParentColor, 'ParentColor True');
    CheckEqB(True, S.ParentFont, 'ParentFont');
    CheckEqB(True, S.ShowAccelChar, 'ShowAccelChar True');
    CheckEqI(-1, S.TabOrder, 'TabOrder');
    CheckEqB(False, S.TabStop, 'TabStop False');
    Check(GetClass('TStaticText') <> nil, 'registered');

    Section('window');
    S.Caption := 'Text';
    S.Parent := F;
    H := S.Handle;
    CheckEqS('Static', ClassOf(H), 'system class STATIC');
    CheckEqB(True, (StyleOf(S) and SS_NOTIFY) <> 0, 'SS_NOTIFY');
    CheckEqI(SS_LEFT, Integer(StyleOf(S) and SS_TYPEMASK), 'SS_LEFT');
    CheckEqB(False, (StyleOf(S) and SS_NOPREFIX) <> 0, 'ShowAccelChar: no SS_NOPREFIX');
    CheckEqB(False, (StyleOf(S) and (WS_BORDER or SS_SUNKEN)) <> 0, 'sbsNone: no border');
    CheckEqS('Text', WindowText(H), 'caption as window text');
    S.Caption := 'Other';
    CheckEqS('Other', WindowText(H), 'caption with a handle');
    S.Alignment := taCenter;
    Check(S.Handle <> H, 'Alignment recreates');
    CheckEqI(SS_CENTER, Integer(StyleOf(S) and SS_TYPEMASK), 'SS_CENTER');
    CheckEqS('Other', WindowText(S.Handle), 'caption kept');
    S.Alignment := taRightJustify;
    CheckEqI(SS_RIGHT, Integer(StyleOf(S) and SS_TYPEMASK), 'SS_RIGHT');
    S.Alignment := taLeftJustify;
    H := S.Handle;
    S.ShowAccelChar := False;
    Check(S.Handle <> H, 'ShowAccelChar recreates');
    CheckEqB(True, (StyleOf(S) and SS_NOPREFIX) <> 0, 'SS_NOPREFIX');
    S.ShowAccelChar := True;
    H := S.Handle;
    S.BorderStyle := sbsSingle;
    Check(S.Handle <> H, 'BorderStyle recreates');
    CheckEqB(True, (StyleOf(S) and WS_BORDER) <> 0, 'sbsSingle: WS_BORDER');
    S.BorderStyle := sbsSunken;
    CheckEqB(True, (StyleOf(S) and SS_SUNKEN) <> 0, 'sbsSunken: SS_SUNKEN');
    CheckEqB(False, (StyleOf(S) and WS_BORDER) <> 0, 'sbsSunken: no WS_BORDER');
    S.BorderStyle := sbsNone;
    F.Color := clAqua;
    CheckEqB(True, S.Color = clAqua, 'ParentColor follows the form');
    CheckEqI(Integer(ColorToRGB(clAqua)), Integer(CtlColor(S)), 'WM_CTLCOLORSTATIC: the parent colour');
    S.Color := clYellow;
    CheckEqB(False, S.ParentColor, 'own Color: ParentColor False');
    CheckEqI(Integer(ColorToRGB(clYellow)), Integer(CtlColor(S)), 'WM_CTLCOLORSTATIC: Color');

    Section('accelerator (UIF-611)');
    E := TEdit.Create(F);
    E.Top := 40;
    E.Parent := F;
    S.Caption := '&Name';
    F.Show;
    CheckEqI(0, S.Perform(CM_DIALOGCHAR, Ord('n'), 0), 'no FocusControl: not consumed');
    S.FocusControl := E;
    CheckEqI(0, S.Perform(CM_DIALOGCHAR, Ord('x'), 0), 'other character: not consumed');
    CheckEqI(1, S.Perform(CM_DIALOGCHAR, Ord('n'), 0), 'accelerator consumed');
    CheckEqB(True, F.ActiveControl = E, 'FocusControl focused');
    S.ShowAccelChar := False;
    CheckEqI(0, S.Perform(CM_DIALOGCHAR, Ord('n'), 0), 'ShowAccelChar False: not consumed');
    S.ShowAccelChar := True;
    S.Enabled := False;
    CheckEqI(0, S.Perform(CM_DIALOGCHAR, Ord('n'), 0), 'disabled: not consumed');
    S.Enabled := True;
    E.Free;
    CheckEqB(True, S.FocusControl = nil, 'freeing FocusControl clears the reference');

    Section('mouse');
    S.OnClick := Log.Clicked;
    Log.Text := '';
    SendMessageW(S.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(2, 2));
    SendMessageW(S.Handle, WM_LBUTTONUP, 0, MakeLParam(2, 2));
    CheckEqS('click;', Log.Text, 'SS_NOTIFY: the mouse reaches the control, OnClick');
    F.Hide;

    Section('streaming');
    Log.Text := '';
    SF := TStaticForm.Create(nil);
    try
      CheckEqS('10,10,80,20', Format('%d,%d,%d,%d', [SF.Prompt.Left, SF.Prompt.Top, SF.Prompt.Width,
        SF.Prompt.Height]), 'stored bounds');
      CheckEqB(True, SF.Prompt.Alignment = taCenter, 'Alignment');
      CheckEqB(False, SF.Prompt.AutoSize, 'AutoSize False');
      CheckEqB(True, SF.Prompt.BorderStyle = sbsSunken, 'BorderStyle');
      CheckEqS('&Value', SF.Prompt.Caption, 'Caption');
      CheckEqB(True, SF.Prompt.Color = clYellow, 'Color');
      CheckEqB(True, SF.Prompt.FocusControl = SF.Value, 'FocusControl resolved');
      CheckEqI(-15, SF.Prompt.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Prompt.ParentColor, 'ParentColor False');
      CheckEqB(False, SF.Prompt.ShowAccelChar, 'ShowAccelChar False');
      CheckEqB(True, SF.Prompt.TabStop, 'TabStop True');
      Check(Assigned(SF.Prompt.OnClick), 'OnClick bound');
      CheckEqI(SS_CENTER, Integer(StyleOf(SF.Prompt) and SS_TYPEMASK), 'styles of the loaded control');
      CheckEqB(True, (StyleOf(SF.Prompt) and (SS_NOPREFIX or SS_SUNKEN)) = (SS_NOPREFIX or SS_SUNKEN),
        'SS_NOPREFIX, SS_SUNKEN');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      S := TStaticText.Create(nil);
      S.Caption := 'Leak';
      S.Parent := F;
      S.HandleNeeded;
      S.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
