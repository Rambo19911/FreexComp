// covers: UIF-910, UIF-913, UIF-921, UIF-922, UIF-923, UIF-924, UIF-925
// needs: desktop
program Act_ShortcutsIdle;
{ U-078, actions-menus.md sections 1.3 and 2: TCustomForm.UpdateActions updates each distinct action linked to a
  showing control or a main-menu bar item once (not hidden controls, not sub-menu items), guarded; Application.Idle
  runs it for visible forms when OnIdle leaves Done True. Shortcut dispatch through Application.ProcessMessage: the
  pop-up menu of the focused control, of its parents and of the form, then the main menu, then the form's action
  lists, then the main form; duplicates resolved by that order; disabled or invisible items (or items in a
  disabled parent) and actions whose Execute fails are skipped after their update hooks ran; suspended lists do
  not match; a consumed key produces no OnKeyDown / OnKeyPress; modifier keys alone never match; Alt shortcuts
  arrive as WM_SYSKEYDOWN. The Ctrl state is set with KeyStateKit (whole thread key state, written again when the
  key message is retrieved; no real input).
  U-121, UIF-910 (c): controls whose UpdatesActionWhenHidden is True (tool buttons) are updated also while hidden,
  so an action's OnUpdate can show its hidden tool button again. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, Menus, ActnList,
  ComCtrls, TestKit, KeyStateKit;

type
  TControlAccess = class(TControl);

  TMainF = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    RaiseIn: string;
    IdleDone: Boolean;
    procedure Exec(Sender: TObject);
    procedure Upd(Sender: TObject);
    procedure ShowAgain(Sender: TObject);
    procedure Clicked(Sender: TObject);
    procedure Hook(Sender: TObject);
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyPress(Sender: TObject; var Key: Char);
    procedure Idle(Sender: TObject; var Done: Boolean);
    procedure Error(Sender: TObject; E: Exception);
  end;

var
  Log: TLog;

constructor TMainF.Create(AOwner: TComponent);
begin
  inherited CreateNew(AOwner);
end;

procedure TLog.Exec(Sender: TObject);
var
  C: TComponent;
begin
  C := TBasicAction(Sender).ActionComponent;
  if C = nil then
    Text := Text + 'exec:' + TComponent(Sender).Name + '<nil;'
  else
    Text := Text + 'exec:' + TComponent(Sender).Name + '<' + C.Name + ';';
end;

procedure TLog.Upd(Sender: TObject);
begin
  Text := Text + 'upd:' + TComponent(Sender).Name + ';';
  if RaiseIn = TComponent(Sender).Name then
    raise Exception.Create('boom');
end;

procedure TLog.ShowAgain(Sender: TObject);
begin
  Text := Text + 'upd:' + TComponent(Sender).Name + ';';
  TAction(Sender).Visible := True;
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Hook(Sender: TObject);
begin
  Text := Text + 'hook:' + TComponent(Sender).Name + ';';
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + 'keydown;';
end;

procedure TLog.KeyPress(Sender: TObject; var Key: Char);
begin
  Text := Text + 'keypress;';
end;

procedure TLog.Idle(Sender: TObject; var Done: Boolean);
begin
  Text := Text + 'idle;';
  Done := IdleDone;
  PostMessageW(Application.Handle, WM_NULL, 0, 0);       { wakes WaitMessage at once }
end;

procedure TLog.Error(Sender: TObject; E: Exception);
begin
  Text := Text + 'error:' + E.Message + ';';
end;

procedure SetCtrl(Down: Boolean);
begin
  SetKeyState(False, Down);
end;

{ Ctrl+Key (or Alt+Key as WM_SYSKEYDOWN) posted to the control and pumped }
procedure Press(C: TWinControl; Key: Word; Ctrl: Boolean; Alt: Boolean = False);
begin
  Log.Text := '';
  SetCtrl(Ctrl);
  if Alt then
    PostKeyMessage(C.Handle, WM_SYSKEYDOWN, Key, $20000001)
  else
    PostKeyMessage(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
  SetCtrl(False);
end;

function NewAction(AOwner: TComponent; List: TActionList; const AName: string; AShortCut: TShortCut): TAction;
begin
  Result := TAction.Create(AOwner);
  Result.Name := AName;
  Result.ShortCut := AShortCut;
  Result.OnExecute := Log.Exec;
  if List <> nil then
    Result.ActionList := List;
end;

function NewItem(AOwner: TComponent; Parent: TMenuItem; const AName: string; AShortCut: TShortCut): TMenuItem;
begin
  Result := TMenuItem.Create(AOwner);
  Result.Name := AName;
  Result.Caption := AName;
  Result.ShortCut := AShortCut;
  Result.OnClick := Log.Clicked;
  Parent.Add(Result);
end;

function NewPopup(AOwner: TComponent; const AItem: string; AShortCut: TShortCut): TPopupMenu;
begin
  Result := TPopupMenu.Create(AOwner);
  NewItem(AOwner, Result.Items, AItem, AShortCut);
end;

procedure RunIdle;
var
  I: Integer;
begin
  Log.Text := '';
  for I := 1 to 50 do
  begin
    Application.HandleMessage;
    if Pos('idle;', Log.Text) > 0 then
      Break;
  end;
  Application.ProcessMessages;
end;

const
  CtrlO = scCtrl + Ord('O');
  CtrlT = scCtrl + Ord('T');
  AltX = scAlt + Ord('X');

procedure Shortcuts;
var
  F: TForm;
  MainF: TMainF;
  P: TPanel;
  E: TEdit;
  PE, PP, PF: TPopupMenu;
  PEi, PFi, MMi, MMtop, MMc, MMx: TMenuItem;
  MM: TMainMenu;
  AL, ML: TActionList;
  AO, AT, AM: TAction;
  Msg: TWMKey;
  Stale: TKeyboardState;
begin
  Application.CreateForm(TMainF, MainF);
  CheckEqB(True, Application.MainForm = MainF, 'main form');
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(100, 100, 320, 240);
    P := TPanel.Create(F);
    P.Name := 'P';
    P.SetBounds(8, 8, 200, 100);
    P.Parent := F;
    E := TEdit.Create(F);
    E.Name := 'E';
    E.SetBounds(8, 8, 100, 24);
    E.Parent := P;
    E.OnKeyDown := Log.KeyDown;
    E.OnKeyPress := Log.KeyPress;
    PE := NewPopup(F, 'PEi', CtrlO);
    PEi := PE.Items[0];
    E.PopupMenu := PE;
    PP := NewPopup(F, 'PPi', AltX);
    P.PopupMenu := PP;
    PF := NewPopup(F, 'PFi', scCtrl + Ord('F'));
    PFi := PF.Items[0];
    F.PopupMenu := PF;
    MM := TMainMenu.Create(F);
    MMi := NewItem(F, MM.Items, 'MMi', CtrlO);
    MMtop := NewItem(F, MM.Items, 'MMtop', 0);
    MMtop.OnClick := Log.Hook;
    MMc := NewItem(F, MMtop, 'MMc', 0);
    MMx := NewItem(F, MMtop, 'MMx', AltX);
    F.Menu := MM;
    AL := TActionList.Create(F);
    AO := NewAction(F, AL, 'AO', CtrlO);
    AT := NewAction(F, AL, 'AT', CtrlT);
    AT.OnUpdate := Log.Upd;
    MMc.OnClick := nil;
    MMc.Action := AT;
    CheckEqI(CtrlT, MMc.ShortCut, 'MMc takes AT''s shortcut');
    ML := TActionList.Create(MainF);
    AM := NewAction(MainF, ML, 'AM', CtrlO);
    F.ActiveControl := E;
    CheckEqB(True, F.ActiveControl = E, 'ActiveControl recorded (the form is not active)');

    Section('order (UIF-921, UIF-925)');
    Press(E, Ord('O'), True);
    CheckEqS('click:PEi;', Log.Text, 'Ctrl+O: the focused control''s pop-up menu first, no OnKeyDown/OnKeyPress');
    PEi.Enabled := False;
    Press(E, Ord('O'), True);
    CheckEqS('click:MMi;', Log.Text, 'disabled pop-up item skipped: the main menu');
    MMi.Visible := False;
    Press(E, Ord('O'), True);
    CheckEqS('exec:AO<nil;', Log.Text, 'invisible item skipped: the form''s action list (ActionComponent nil)');
    AO.Enabled := False;
    Press(E, Ord('O'), True);
    CheckEqS('exec:AM<nil;', Log.Text, 'disabled action (Execute False): the main form''s action list');
    MainF.Enabled := False;
    Press(E, Ord('O'), True);
    CheckEqS('keydown;keypress;', Log.Text, 'disabled main form: not consumed, OnKeyDown and the translated character');
    MainF.Enabled := True;
    ML.State := asSuspended;
    Press(E, Ord('O'), True);
    CheckEqS('keydown;keypress;', Log.Text, 'suspended list: no match (UIF-923)');
    ML.State := asNormal;
    Press(E, Ord('F'), True);
    CheckEqS('click:PFi;', Log.Text, 'the form''s pop-up menu (end of the parent chain)');
    Press(E, Ord('X'), False, True);
    CheckEqS('click:PPi;', Log.Text, 'Alt+X (WM_SYSKEYDOWN): the parent''s pop-up menu before the main menu');
    PP.Items[0].Enabled := False;
    Press(E, Ord('X'), False, True);
    CheckEqS('hook:MMtop;click:MMx;', Log.Text, 'Alt+X: the main menu''s nested item after its parent''s hook');
    Press(E, Ord('Q'), True);
    CheckEqS('keydown;keypress;', Log.Text, 'no match: OnKeyDown and OnKeyPress fire (UIF-924 counter-check)');
    { U-141: a modifier left in the thread key state by the real keyboard (here Alt, which turns Ctrl+Q into a key
      without a character) must not reach the next key of the test }
    GetKeyboardState(Stale);
    Stale[VK_MENU] := $80;
    SetKeyboardState(Stale);
    Press(E, Ord('Q'), True);
    CheckEqS('keydown;keypress;', Log.Text, 'no match with a stale Alt in the thread key state: same result');

    Section('menu items: hooks and updates first (UIF-922, UIF-913)');
    MMtop.Enabled := False;
    Press(E, Ord('T'), True);
    CheckEqS('hook:MMtop;upd:AT;upd:AT;exec:AT<nil;', Log.Text,
      'disabled parent: hook and update ran, item skipped; the action runs from the list (updated in Execute)');
    MMtop.Enabled := True;
    Press(E, Ord('T'), True);
    CheckEqS('hook:MMtop;upd:AT;upd:AT;exec:AT<MMc;', Log.Text, 'enabled: the item clicks its action');
    F.ActiveControl := nil;
    PEi.Enabled := True;
    MMi.Visible := True;
    Press(E, Ord('O'), True);
    CheckEqS('click:MMi;', Log.Text, 'no focused control: the search starts at the form');

    Section('direct calls (UIF-921, UIF-923)');
    Msg.Msg := WM_KEYDOWN;
    Msg.CharCode := VK_CONTROL;
    Msg.KeyData := 0;
    Msg.Result := 0;
    SetCtrl(True);
    Log.Text := '';
    CheckEqB(False, F.IsShortCut(Msg), 'a modifier key alone never matches');
    Msg.CharCode := Ord('O');
    AO.Enabled := True;
    MMi.Visible := False;
    CheckEqB(True, AL.IsShortCut(Msg), 'TActionList.IsShortCut: Execute returned True');
    CheckEqS('exec:AO<nil;', Log.Text, 'the matching action ran');
    AL.State := asSuspended;
    CheckEqB(False, AL.IsShortCut(Msg), 'suspended list: False');
    AL.State := asNormal;
    Msg.CharCode := Ord('Z');
    CheckEqB(False, AL.IsShortCut(Msg), 'no action with Ctrl+Z');
    SetCtrl(False);
  finally
    F.Free;
  end;
end;

procedure Updates;
var
  F: TForm;
  B1, B1b, B2: TButton;
  AU1, AU2, AU3, AU4, AF: TAction;
  MM: TMainMenu;
  Top, Sub: TMenuItem;
begin
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(120, 120, 320, 240);
    AU1 := NewAction(F, nil, 'AU1', 0);
    AU1.OnUpdate := Log.Upd;
    AU2 := NewAction(F, nil, 'AU2', 0);
    AU2.OnUpdate := Log.Upd;
    AU3 := NewAction(F, nil, 'AU3', 0);
    AU3.OnUpdate := Log.Upd;
    AU4 := NewAction(F, nil, 'AU4', 0);
    AU4.OnUpdate := Log.Upd;
    B1 := TButton.Create(F);
    B1.Parent := F;
    B1.Action := AU1;
    B1b := TButton.Create(F);
    B1b.Top := 40;
    B1b.Parent := F;
    B1b.Action := AU1;
    B2 := TButton.Create(F);
    B2.Top := 80;
    B2.Parent := F;
    B2.Action := AU2;
    B2.Visible := False;
    MM := TMainMenu.Create(F);
    Top := TMenuItem.Create(F);
    Top.Caption := 'Top';
    MM.Items.Add(Top);
    Top.Action := AU3;
    Sub := TMenuItem.Create(F);
    Top.Add(Sub);
    Sub.Action := AU4;
    F.Menu := MM;

    Section('UpdateActions (UIF-910)');
    Log.Text := '';
    F.UpdateActions;
    CheckEqS('upd:AU3;', Log.Text, 'hidden form: no control is showing; the menu bar item still counts');
    F.Visible := True;
    CheckEqB(True, B1.Showing, 'form shown (no activation)');
    Log.Text := '';
    F.UpdateActions;
    CheckEqS('upd:AU1;upd:AU3;', Log.Text,
      'each distinct action once; hidden control and sub-menu item excluded; controls before the menu bar');
    Log.RaiseIn := 'AU1';
    Application.OnException := Log.Error;
    Log.Text := '';
    F.UpdateActions;
    CheckEqS('upd:AU1;error:boom;upd:AU3;', Log.Text, 'an exception is reported and the rest still updates');
    Log.RaiseIn := '';

    Section('at idle (UIF-910, application-forms.md UIF-412)');
    Application.OnIdle := Log.Idle;
    Log.IdleDone := True;
    RunIdle;
    CheckEqS('idle;upd:AU1;upd:AU3;', Log.Text, 'Done: the visible form''s actions after OnIdle');
    Log.IdleDone := False;
    RunIdle;
    CheckEqS('idle;', Log.Text, 'Done False: no action updates');
    Log.IdleDone := True;
    F.Visible := False;
    RunIdle;
    CheckEqS('idle;', Log.Text, 'hidden form: not updated at idle');
    Application.OnIdle := nil;
    Application.OnException := nil;
    AF := NewAction(F, nil, 'AF', 0);
    AF.OnUpdate := Log.Upd;
    Log.Text := '';
    AF.Execute;
    CheckEqS('upd:AF;exec:AF<nil;', Log.Text, 'an action without a client updates inside Execute (UIF-913)');
  finally
    F.Free;
  end;
end;

{ U-121 }
procedure HiddenToolButtons;
var
  F: TForm;
  T, T2: TToolBar;
  TB, TB2: TToolButton;
  B: TButton;
  AH, AH2, AB: TAction;
begin
  Section('hidden tool buttons (UIF-910 (c))');
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(120, 120, 320, 240);
    B := TButton.Create(F);
    TB := TToolButton.Create(F);
    CheckEqB(False, TControlAccess(B).UpdatesActionWhenHidden, 'default: False');
    CheckEqB(True, TControlAccess(TB).UpdatesActionWhenHidden, 'TToolButton: True');
    T := TToolBar.Create(F);
    T.Parent := F;
    AH := NewAction(F, nil, 'AH', 0);
    TB.Name := 'TB';
    TB.Parent := T;
    TB.Action := AH;
    AH.Visible := False;
    CheckEqB(False, TB.Visible, 'the action hides its tool button');
    AH.OnUpdate := Log.ShowAgain;
    AB := NewAction(F, nil, 'AB', 0);
    AB.OnUpdate := Log.Upd;
    B.Parent := F;
    B.Action := AB;
    B.Visible := False;
    T2 := TToolBar.Create(F);
    T2.Parent := F;
    T2.Visible := False;
    AH2 := NewAction(F, nil, 'AH2', 0);
    AH2.OnUpdate := Log.Upd;
    TB2 := TToolButton.Create(F);
    TB2.Parent := T2;
    TB2.Action := AH2;
    F.Visible := True;
    Log.Text := '';
    F.UpdateActions;
    CheckEqS('upd:AH;upd:AH2;', Log.Text,
      'hidden tool buttons (also in a hidden bar) are updated; a hidden button is not');
    CheckEqB(True, TB.Visible, 'OnUpdate showed the tool button again');
    CheckEqB(True, TB.Showing, 'showing');
    Log.Text := '';
    F.UpdateActions;
    CheckEqS('upd:AH;upd:AH2;', Log.Text, 'a visible tool button: updated once');
  finally
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Log := TLog.Create;
  try
    Shortcuts;
    Updates;
    HiddenToolButtons;
  finally
    Log.Free;
  end;
  Done;
end.
