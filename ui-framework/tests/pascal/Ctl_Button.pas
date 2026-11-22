// covers: UIF-640, UIF-641, UIF-642, UIF-643
// needs: desktop
program Ctl_Button;
{ U-056, controls-standard.md section 4: TButton defaults (75 x 25, TabStop); a sub-classed system BUTTON with
  BS_PUSHBUTTON / BS_DEFPUSHBUTTON and BS_MULTILINE (WordWrap recreates); caption and enabled state pushed and
  read back; OnClick exactly once per BN_CLICKED (BM_CLICK, mouse), from code and from the accelerator;
  ModalResult reaches the form before OnClick and mrNone leaves it alone (UIF-640); Return clicks the focused
  button or else the first usable default button in tab order, Escape the cancel button (UIF-642, UIF-643);
  the default-button appearance follows the focus (UIF-641); streaming; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    Form: TForm;
    ResetModal: Boolean;
    procedure Clicked(Sender: TObject);
  end;

  TButtonForm = class(TForm)
  published
    Ok: TButton;
    procedure OkClick(Sender: TObject);
  end;


{$FORM Ctl_Button.form}

var
  Log: TLog;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TComponent(Sender).Name;
  if Form <> nil then
    Text := Text + '(' + IntToStr(Form.ModalResult) + ')';
  Text := Text + ';';
  if ResetModal and (Form <> nil) then
    Form.ModalResult := mrNone;
end;

procedure TButtonForm.OkClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed click;';
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

function NativeText(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 128, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

{ the push-button type bits of the window style }
function Kind(B: TButton): Integer;
begin
  Result := Integer(GetWindowLongPtrW(B.Handle, GWL_STYLE) and $F);
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Press(C: TWinControl; Key: Word);
begin
  Log.Text := '';
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
end;

function Btn(F: TForm; const Name: string; X: Integer): TButton;
begin
  Result := TButton.Create(F);
  Result.Name := Name;
  Result.Caption := Name;
  Result.SetBounds(X, 50, 60, 25);
  Result.OnClick := Log.Clicked;
  Result.Parent := F;
end;

procedure Run;
var
  F: TForm;
  B, B1, B2, B3, B4: TButton;
  E: TEdit;
  SF: TButtonForm;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    B := TButton.Create(F);
    B.Name := 'B';

    Section('defaults');
    CheckEqI(75, B.Width, 'Width 75');
    CheckEqI(25, B.Height, 'Height 25');
    CheckEqB(False, B.Cancel, 'Cancel');
    CheckEqS('B', B.Caption, 'Caption empty, so it follows the Name (UIF-1250)');
    CheckEqB(False, B.Default, 'Default');
    CheckEqI(mrNone, B.ModalResult, 'ModalResult mrNone');
    CheckEqB(True, B.ParentFont, 'ParentFont');
    CheckEqI(-1, B.TabOrder, 'TabOrder');
    CheckEqB(True, B.TabStop, 'TabStop True');
    CheckEqB(False, B.WordWrap, 'WordWrap');
    Check(GetClass('TButton') <> nil, 'registered');

    Section('handle-less state, window');
    B.Caption := 'Early';
    B.Enabled := False;
    B.Parent := F;
    H := B.Handle;
    CheckEqS('Button', ClassOf(H), 'system class BUTTON');
    CheckEqI(BS_PUSHBUTTON, Kind(B), 'BS_PUSHBUTTON');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_STYLE) and WS_TABSTOP) <> 0, 'WS_TABSTOP');
    CheckEqB(False, (GetWindowLongPtrW(H, GWL_STYLE) and BS_MULTILINE) <> 0, 'no BS_MULTILINE');
    CheckEqS('Early', NativeText(H), 'caption pushed');
    CheckEqB(False, IsWindowEnabled(H), 'disabled pushed');
    B.Enabled := True;
    B.WordWrap := True;
    Check(B.Handle <> H, 'WordWrap recreates');
    CheckEqB(True, (GetWindowLongPtrW(B.Handle, GWL_STYLE) and BS_MULTILINE) <> 0, 'BS_MULTILINE');
    CheckEqS('Early', NativeText(B.Handle), 'caption kept');
    B.WordWrap := False;
    B.Default := True;
    CheckEqI(BS_DEFPUSHBUTTON, Kind(B), 'Default with no focused button: BS_DEFPUSHBUTTON');
    H := B.Handle;
    B.Default := False;
    CheckEqI(BS_PUSHBUTTON, Kind(B), 'Default False: BS_PUSHBUTTON');
    Check(B.Handle = H, 'Default keeps the window');

    Section('native -> object');
    SetWindowTextW(B.Handle, 'Native');
    CheckEqS('Native', B.Caption, 'Caption reads the window');

    Section('click (UIF-640)');
    B.OnClick := Log.Clicked;
    Log.Text := '';
    SendMessageW(B.Handle, BM_CLICK, 0, 0);
    CheckEqS('click:B;', Log.Text, 'BM_CLICK: OnClick once');
    Log.Text := '';
    SendMessageW(B.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(5, 5));
    SendMessageW(B.Handle, WM_LBUTTONUP, 0, MakeLParam(5, 5));
    CheckEqS('click:B;', Log.Text, 'mouse click: OnClick once');
    Log.Text := '';
    B.Click;
    CheckEqS('click:B;', Log.Text, 'Click from code');
    Log.Form := F;
    B.ModalResult := mrOk;
    F.ModalResult := mrNone;
    Log.Text := '';
    B.Click;
    CheckEqS('click:B(' + IntToStr(mrOk) + ');', Log.Text, 'ModalResult set before OnClick');
    CheckEqI(mrOk, F.ModalResult, 'form ModalResult');
    Log.ResetModal := True;
    F.ModalResult := mrNone;
    B.Click;
    CheckEqI(mrNone, F.ModalResult, 'the handler may reset the form''s value');
    Log.ResetModal := False;
    B.ModalResult := mrNone;
    F.ModalResult := mrCancel;
    B.Click;
    CheckEqI(mrCancel, F.ModalResult, 'mrNone never touches the form');
    F.ModalResult := mrNone;
    Log.Form := nil;
    B.Free;

    Section('Return and Escape (UIF-115, UIF-642, UIF-643)');
    E := TEdit.Create(F);
    E.Name := 'E';
    E.Parent := F;
    B1 := Btn(F, 'B1', 10);
    B2 := Btn(F, 'B2', 80);
    B3 := Btn(F, 'B3', 150);
    B1.Default := True;
    B2.Cancel := True;
    F.Show;
    E.SetFocus;
    Press(E, VK_RETURN);
    CheckEqS('click:B1;', Log.Text, 'Return in an edit: the default button');
    Press(E, VK_ESCAPE);
    CheckEqS('click:B2;', Log.Text, 'Escape: the cancel button');
    B3.SetFocus;
    Press(B3, VK_RETURN);
    CheckEqS('click:B3;', Log.Text, 'Return on a focused button: that button');
    E.SetFocus;
    B1.Enabled := False;
    Press(E, VK_RETURN);
    CheckEqS('', Log.Text, 'disabled default button: skipped');
    B1.Enabled := True;
    B2.Visible := False;
    Press(E, VK_ESCAPE);
    CheckEqS('', Log.Text, 'invisible cancel button: skipped');
    B2.Visible := True;
    B4 := Btn(F, 'B4', 220);
    B4.Default := True;
    B4.TabOrder := 0;
    E.SetFocus;
    Press(E, VK_RETURN);
    CheckEqS('click:B4;', Log.Text, 'several defaults: the first in tab order');
    B4.Default := False;

    Section('appearance (UIF-641)');
    E.SetFocus;
    CheckEqI(BS_DEFPUSHBUTTON, Kind(B1), 'focus on an edit: the default button looks default');
    CheckEqI(BS_PUSHBUTTON, Kind(B3), 'other button: normal');
    B3.SetFocus;
    CheckEqI(BS_DEFPUSHBUTTON, Kind(B3), 'focused button looks default');
    CheckEqI(BS_PUSHBUTTON, Kind(B1), 'default button while another button is focused: normal');
    E.SetFocus;
    CheckEqI(BS_DEFPUSHBUTTON, Kind(B1), 'focus back on the edit: default again');
    CheckEqI(BS_PUSHBUTTON, Kind(B3), 'previously focused button: normal');

    { Space is handled by the native button (it needs the Win32 focus, which an unattended host may not give the
      form) and reports BN_CLICKED like BM_CLICK above }
    Section('accelerator');
    B2.Caption := '&Go';
    E.SetFocus;
    Log.Text := '';
    PostMessageW(E.Handle, WM_SYSCHAR, Ord('g'), $20000001);
    Application.ProcessMessages;
    CheckEqS('click:B2;', Log.Text, 'accelerator clicks');
    CheckEqB(True, F.ActiveControl = B2, 'accelerator focuses the button');
    F.Hide;

    Section('streaming');
    Log.Text := '';
    SF := TButtonForm.Create(nil);
    try
      CheckEqB(True, SF.Ok.Cancel, 'Cancel');
      CheckEqS('O&K', SF.Ok.Caption, 'Caption');
      CheckEqB(True, SF.Ok.Default, 'Default');
      CheckEqI(-15, SF.Ok.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Ok.ParentFont, 'ParentFont False');
      CheckEqI(mrOk, SF.Ok.ModalResult, 'ModalResult');
      CheckEqB(False, SF.Ok.TabStop, 'TabStop False');
      CheckEqB(True, SF.Ok.WordWrap, 'WordWrap');
      CheckEqS('', Log.Text, 'no click while loading');
      SF.Ok.Click;
      CheckEqS('streamed click;', Log.Text, 'OnClick handler of the form');
      CheckEqI(mrOk, SF.ModalResult, 'ModalResult reaches the streamed form');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      B := TButton.Create(nil);
      B.Caption := 'Leak';
      B.Parent := F;
      B.HandleNeeded;
      B.Free;
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
