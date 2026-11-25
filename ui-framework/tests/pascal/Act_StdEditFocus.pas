// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1369, UIF-1372, UIF-1373
// phase: 8
// needs: desktop
program Act_StdEditFocus;
{ U-152, standard-actions.md section 4 (card acceptance 3): a TEditCopy without Control acts on the focused edit.
  A shown form with two edits, a plain button, a main-menu bar item and a button linked to the action. After an
  idle cycle of the application the action and its two clients are enabled exactly while the focused control is
  an edit with a selection: not without a selection, not for a selection in the edit that has no focus, not
  while a button has the focus. Ctrl+C with the focus in an edit that has a selection is consumed by the action,
  which runs once (the clipboard text is read back); without a target (no selection, or the focus on a button)
  the key is not consumed and reaches the focused control as an ordinary key.
  The form is activated with WM_ACTIVATE, whether or not the desktop made its window the active one; the focus is
  the form's ActiveControl. The Ctrl state is set with KeyStateKit (no real input). The clipboard: the program
  copies its own text and asserts on that text only; ClipKit repeats the key while another program has the
  clipboard open. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, Menus, ActnList, StdActns, TestKit, KeyStateKit,
  ClipKit;

type
  { counts the executions on a target }
  TCountCopy = class(TEditCopy)
  public
    Count: Integer;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TLog = class
  public
    Text: string;
    Idles: Integer;
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure Idle(Sender: TObject; var Done: Boolean);
  end;

var
  Log: TLog;
  F: TForm;
  E1, E2: TEdit;
  Linked, Other: TButton;
  Item: TMenuItem;
  Copy: TCountCopy;

procedure TCountCopy.ExecuteTarget(Target: TObject);
begin
  Inc(Count);
  inherited ExecuteTarget(Target);
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + 'down:' + TComponent(Sender).Name + ';';
end;

{ a message for the application, so that the wait at the end of the idle cycle returns }
procedure TLog.Idle(Sender: TObject; var Done: Boolean);
begin
  Inc(Idles);
  PostMessageW(Application.Handle, WM_NULL, 0, 0);
end;

{ one idle cycle of the application: pending messages first }
procedure RunIdle;
var
  I, Before: Integer;
begin
  Before := Log.Idles;
  for I := 1 to 1000 do
  begin
    Application.HandleMessage;
    if Log.Idles > Before then
      Break;
  end;
  Application.ProcessMessages;
end;

{ Ctrl+C posted to the control and pumped }
procedure PressCtrlC(C: TWinControl);
begin
  Log.Text := '';
  Copy.Count := 0;
  SetKeyState(False, True);
  PostKeyMessage(C.Handle, WM_KEYDOWN, Ord('C'), 1);
  Application.ProcessMessages;
  SetKeyState(False, False);
end;

procedure Focus(C: TWinControl; AStart, ALength: Integer);
begin
  C.SetFocus;
  if C is TEdit then
  begin
    TEdit(C).SelStart := AStart;                             { after the focus: entering an edit selects all }
    TEdit(C).SelLength := ALength;
  end;
end;

{ the action, the menu item and the linked button }
function States: string;

  function Letter(B: Boolean): string;
  begin
    if B then
      Result := 'T'
    else
      Result := 'F';
  end;

begin
  Result := Letter(Copy.Enabled) + Letter(Item.Enabled) + Letter(Linked.Enabled);
end;

procedure Run;
var
  MM: TMainMenu;
  List: TActionList;
  Start: DWORD;
begin
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(100, 100, 400, 300);
    E1 := TEdit.Create(F);
    E1.Name := 'E1';
    E1.SetBounds(8, 8, 200, 24);
    E1.Parent := F;
    E2 := TEdit.Create(F);
    E2.Name := 'E2';
    E2.SetBounds(8, 40, 200, 24);
    E2.Parent := F;
    Linked := TButton.Create(F);
    Linked.Name := 'Linked';
    Linked.SetBounds(8, 80, 90, 25);
    Linked.Parent := F;
    Other := TButton.Create(F);
    Other.Name := 'Other';
    Other.SetBounds(110, 80, 90, 25);
    Other.Parent := F;
    E1.OnKeyDown := Log.KeyDown;
    E2.OnKeyDown := Log.KeyDown;
    Other.OnKeyDown := Log.KeyDown;
    List := TActionList.Create(F);
    Copy := TCountCopy.Create(F);
    Copy.ActionList := List;
    Copy.Caption := '&Copy';
    Copy.ShortCut := scCtrl or Ord('C');
    MM := TMainMenu.Create(F);
    Item := TMenuItem.Create(F);
    MM.Items.Add(Item);
    Item.Action := Copy;
    F.Menu := MM;
    Linked.Action := Copy;
    CheckEqI(scCtrl or Ord('C'), Item.ShortCut, 'the menu item has the shortcut of the action');
    Check(Copy.Control = nil, 'no Control: the target is found by the search');
    E1.Text := 'first';
    E2.Text := 'second';
    Application.OnIdle := Log.Idle;
    F.Show;
    F.Perform(WM_ACTIVATE, WA_ACTIVE, 0);
    Check(Screen.ActiveCustomForm = F, 'the form is the active form');

    Section('no selection (UIF-1369, UIF-1373)');
    Focus(E1, 0, 0);
    Check(F.ActiveControl = E1, 'the first edit has the focus');
    RunIdle;
    CheckEqS('FFF', States, 'disabled: the action, the menu item, the button');

    Section('a selection in the focused edit (card acceptance 3)');
    E1.SelStart := 1;
    E1.SelLength := 3;
    CheckEqS('FFF', States, 'nothing changes before the idle cycle');
    RunIdle;
    CheckEqS('TTT', States, 'one idle cycle: enabled');
    E1.SelLength := 0;
    RunIdle;
    CheckEqS('FFF', States, 'the selection removed: disabled');
    E1.SelLength := 3;
    RunIdle;
    CheckEqS('TTT', States, 'selected again: enabled');

    Section('the focused edit is the target (UIF-1369)');
    Focus(E2, 0, 0);
    CheckEqI(3, E1.SelLength, 'the first edit keeps its selection');
    RunIdle;
    CheckEqS('FFF', States, 'the focused edit has no selection: disabled');
    E2.SelLength := 2;
    RunIdle;
    CheckEqS('TTT', States, 'a selection in the second edit: enabled');

    Section('the focus on a button (UIF-1372)');
    Focus(Other, 0, 0);
    Check(F.ActiveControl = Other, 'the plain button has the focus');
    RunIdle;
    CheckEqS('FFF', States, 'disabled: no edit has the focus');

    Section('Ctrl+C (UIF-1372)');
    PressCtrlC(Other);
    CheckEqS('down:Other;', Log.Text, 'no target: the key is not consumed, the button receives it');
    CheckEqI(0, Copy.Count, 'the action did not run');
    Focus(E1, 1, 3);
    CheckEqB(False, Copy.Enabled, 'the action is still disabled: no idle cycle since the focus moved');
    Start := GetTickCount;
    repeat
      PressCtrlC(E1);
    until (ClipboardTextNow = 'irs') or ClipWaitOver(Start);
    CheckEqS('', Log.Text, 'the focused edit with a selection: the key is consumed');
    CheckEqI(1, Copy.Count, 'the action ran once');
    CheckEqS('irs', ClipboardText('irs'), 'the selection is on the clipboard');
    CheckEqS('TTT', States, 'the search for the key updated the action');
    Focus(E1, 2, 0);
    PressCtrlC(E1);
    CheckEqS('down:E1;', Log.Text, 'no selection: the key is not consumed, the edit receives it');
    CheckEqI(0, Copy.Count, 'the action did not run');
    CheckEqS('FFF', States, 'and is disabled');
    Focus(E2, 0, 6);
    Start := GetTickCount;
    repeat
      PressCtrlC(E2);
    until (ClipboardTextNow = 'second') or ClipWaitOver(Start);
    CheckEqS('', Log.Text, 'the other edit focused: consumed');
    CheckEqI(1, Copy.Count, 'the action ran once');
    CheckEqS('second', ClipboardText('second'), 'the text of the focused edit is on the clipboard');
  finally
    Application.OnIdle := nil;
    F.Free;
    Log.Free;
  end;
end;

begin
  Application.Initialize;
  Run;
  Done;
end.
