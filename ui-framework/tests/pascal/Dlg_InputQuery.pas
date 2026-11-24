// covers: UIF-470, UIF-479
// needs: desktop
program Dlg_InputQuery;
{ U-081, application-forms.md UIF-479: InputQuery / InputBox.
  Non-interactive mode (the harness sets FXC_UI_NONINTERACTIVE=1): no window, the stderr line
  '[InputQuery] <caption>: <prompt>' (Dlg_InputQuery.err.txt), InputQuery False with Value unchanged, InputBox
  returns ADefault.
  The generated form is observed through Dialogs.InputQueryHook, which receives the built form instead of
  ShowModal being called (both modes, no stderr line). The hook checks the specified properties and bounds (logical
  pixels scaled by edges to the form's PixelsPerInch, layout.md UIF-280) and then runs the real ShowModal, driven by
  key messages posted from OnShow: Enter presses the default OK button, Escape the Cancel button. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, StdCtrls, Dialogs, TestKit, KeyStateKit;

type
  TDriver = class
  public
    Key: Word;
    Typed: string;
    SelStart, SelEnd: Integer;
    Checked: Boolean;
    procedure Shown(Sender: TObject);
  end;

var
  Driver: TDriver;
  HookCalls: Integer;
  UseModal: Boolean;
  DirectResult: Integer;

function S(F: TForm; V: Integer): Integer;
begin
  Result := MulDiv(V, F.PixelsPerInch, 96);
end;

{ edges scaled, as layout.md UIF-280 }
procedure CheckBounds(F: TForm; C: TControl; L, T, W, H: Integer; const Name: string);
begin
  CheckEqI(S(F, L), C.Left, Name + ' Left');
  CheckEqI(S(F, T), C.Top, Name + ' Top');
  if W >= 0 then
    CheckEqI(S(F, L + W) - S(F, L), C.Width, Name + ' Width');
  if H >= 0 then
    CheckEqI(S(F, T + H) - S(F, T), C.Height, Name + ' Height');
end;

procedure TDriver.Shown(Sender: TObject);
var
  F: TForm;
  E: TEdit;
  A, B: DWORD;
begin
  F := TForm(Sender);
  E := TEdit(F.Controls[1]);
  SendMessageW(E.Handle, EM_GETSEL, WPARAM(@A), LPARAM(@B));
  SelStart := A;
  SelEnd := B;
  if Typed <> '' then
    E.Text := Typed;
  PostKeyMessage(E.Handle, WM_KEYDOWN, Key, 1);
  PostKeyMessage(E.Handle, WM_KEYUP, Key, LPARAM($C0000001));
end;

function TestHook(Form: TForm): Integer;
var
  L: TLabel;
  E: TEdit;
  OK, Cancel: TButton;
begin
  Inc(HookCalls);
  if not Driver.Checked then
  begin
    Driver.Checked := True;
    Section('generated form (UIF-479)');
    CheckEqS('Enter name', Form.Caption, 'Caption = ACaption');
    CheckEqB(True, Form.BorderStyle = bsDialog, 'BorderStyle bsDialog');
    CheckEqB(True, Form.Position = poScreenCenter, 'Position poScreenCenter');
    CheckEqI(S(Form, 320), Form.ClientWidth, 'ClientWidth 320 scaled');
    CheckEqI(S(Form, 97), Form.ClientHeight, 'ClientHeight 97 scaled');
    CheckEqI(4, Form.ControlCount, 'four controls');
    CheckEqB(True, Form.Controls[0] is TLabel, 'a label');
    CheckEqB(True, Form.Controls[1] is TEdit, 'an edit');
    CheckEqB(True, Form.Controls[2] is TButton, 'OK button');
    CheckEqB(True, Form.Controls[3] is TButton, 'Cancel button');
    L := TLabel(Form.Controls[0]);
    E := TEdit(Form.Controls[1]);
    OK := TButton(Form.Controls[2]);
    Cancel := TButton(Form.Controls[3]);
    CheckEqS('Your name:', L.Caption, 'label caption = APrompt');
    CheckEqB(True, L.AutoSize, 'label AutoSize');
    CheckBounds(Form, L, 8, 8, -1, -1, 'label');
    CheckEqS('old', E.Text, 'edit text = Value');
    CheckBounds(Form, E, 8, 28, 304, 23, 'edit');
    CheckEqI(0, E.TabOrder, 'edit TabOrder 0');
    CheckEqI(0, E.SelStart, 'all text selected (start)');
    CheckEqI(3, E.SelLength, 'all text selected (length)');
    CheckEqS('OK', OK.Caption, 'OK caption');
    CheckBounds(Form, OK, 164, 64, 75, 25, 'OK');
    CheckEqB(True, OK.Default, 'OK Default');
    CheckEqB(False, OK.Cancel, 'OK not Cancel');
    CheckEqI(mrOk, OK.ModalResult, 'OK ModalResult mrOk');
    CheckEqI(1, OK.TabOrder, 'OK TabOrder 1');
    CheckEqS('Cancel', Cancel.Caption, 'Cancel caption');
    CheckBounds(Form, Cancel, 245, 64, 75, 25, 'Cancel');
    CheckEqB(True, Cancel.Cancel, 'Cancel Cancel');
    CheckEqB(False, Cancel.Default, 'Cancel not Default');
    CheckEqI(mrCancel, Cancel.ModalResult, 'Cancel ModalResult mrCancel');
    CheckEqI(2, Cancel.TabOrder, 'Cancel TabOrder 2');
    CheckEqB(False, Form.Visible, 'not shown yet');
  end;
  if UseModal then
  begin
    Form.OnShow := Driver.Shown;
    Result := Form.ShowModal;
  end
  else
    Result := DirectResult;
end;

procedure Run;
var
  V: string;
  B: Boolean;
begin
  Section('non-interactive (UIF-470)');
  CheckEqB(True, Application.NonInteractive, 'NonInteractive');
  V := 'keep';
  CheckEqB(False, InputQuery('Query caption', 'Query prompt', V), 'InputQuery False');
  CheckEqS('keep', V, 'Value unchanged');
  CheckEqS('fallback', InputBox('Box caption', 'Box'#13#10'prompt', 'fallback'), 'InputBox returns ADefault');

  InputQueryHook := TestHook;
  Section('OK by Enter (UIF-479)');
  UseModal := True;
  Driver.Key := VK_RETURN;
  Driver.Typed := 'Ann';
  V := 'old';
  B := InputQuery('Enter name', 'Your name:', V);
  CheckEqI(1, HookCalls, 'one form');
  CheckEqI(0, Driver.SelStart, 'shown with the text selected (start)');
  CheckEqI(3, Driver.SelEnd, 'shown with the text selected (end)');
  CheckEqB(True, B, 'mrOk: True');
  CheckEqS('Ann', V, 'Value updated');

  Section('Cancel by Escape (UIF-479)');
  Driver.Key := VK_ESCAPE;
  Driver.Typed := 'Bob';
  V := 'old';
  CheckEqB(False, InputQuery('Enter name', 'Your name:', V), 'mrCancel: False');
  CheckEqS('old', V, 'Value unchanged');

  Section('InputBox (UIF-479)');
  Driver.Key := VK_RETURN;
  Driver.Typed := 'typed';
  CheckEqS('typed', InputBox('c', 'p', 'dflt'), 'OK: the entered text');
  Driver.Key := VK_ESCAPE;
  CheckEqS('dflt', InputBox('c', 'p', 'dflt'), 'Cancel: ADefault');
  Driver.Key := VK_RETURN;
  Driver.Typed := '';
  CheckEqS('dflt', InputBox('c', 'p', 'dflt'), 'OK without typing: the default text');

  Section('results other than mrOk (UIF-479)');
  UseModal := False;
  DirectResult := mrYes;
  V := 'old';
  CheckEqB(False, InputQuery('c', 'p', V), 'mrYes: False');
  CheckEqS('old', V, 'Value unchanged');
  DirectResult := mrOk;
  CheckEqB(True, InputQuery('c', 'p', V), 'mrOk: True');
  CheckEqS('old', V, 'unchanged text');
  InputQueryHook := nil;
  CheckEqI(0, Screen.FormCount, 'the dialog form is freed');
end;

begin
  Application.Initialize;
  Driver := TDriver.Create;
  try
    Run;
  finally
    Driver.Free;
  end;
  Done;
end.
