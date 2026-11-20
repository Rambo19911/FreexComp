// covers: UIF-905, UIF-906, UIF-907, UIF-908, UIF-909, UIF-911, UIF-912
// needs: desktop
program Act_Links;
{ U-077, actions-menus.md section 1.2 and UIF-911: action links of controls (TButton, TCheckBox, TRadioButton) and
  menu items. Assigning an action copies each linkable property only into a client value that is still at its
  default; later action changes reach a property only while the client still has the action's previous value;
  Click runs the action (ActionComponent = the client) unless OnClick is the client's own handler; Checked copied
  from an action fires no OnClick; loading records the link and Loaded copies (stored values win); a freed or
  removed action leaves the copied values; the actions of a menu's items are updated before a sub-menu or a
  pop-up menu opens.
  No test blocks in the modal menu loop: before Popup a WM_CANCELMODE is posted to the owner window. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, Menus, ActnList, TestKit;

type
  TLinksForm = class(TForm)
  published
    Button1: TButton;
    CheckBox1: TCheckBox;
    MainMenu1: TMainMenu;
    Item1: TMenuItem;
    ActionList1: TActionList;
    Act1: TAction;
    Act2: TAction;
    procedure Act1Execute(Sender: TObject);
  end;

  TLog = class
  public
    Text: string;
    DisableOnUpdate: Boolean;
    procedure Exec(Sender: TObject);
    procedure Own(Sender: TObject);
    procedure Clicked(Sender: TObject);
    procedure Upd(Sender: TObject);
  end;

  TControlAccess = class(TControl);

{$FORM Act_Links.form}

var
  Log: TLog;

procedure TLinksForm.Act1Execute(Sender: TObject);
begin
  Log.Text := Log.Text + 'act1;';
end;

procedure TLog.Exec(Sender: TObject);
var
  C: TComponent;
begin
  if not (Sender is TBasicAction) then
  begin
    Text := Text + 'plain:' + TComponent(Sender).Name + ';';
    Exit;
  end;
  C := TBasicAction(Sender).ActionComponent;
  if C = nil then
    Text := Text + 'exec:' + TComponent(Sender).Name + '<nil;'
  else
    Text := Text + 'exec:' + TComponent(Sender).Name + '<' + C.Name + ';';
end;

procedure TLog.Own(Sender: TObject);
begin
  Text := Text + 'own:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Upd(Sender: TObject);
begin
  Text := Text + 'upd:' + TComponent(Sender).Name + ';';
  if DisableOnUpdate then
    TAction(Sender).Enabled := False;
end;

function NewAction(AOwner: TComponent; const AName: string): TAction;
begin
  Result := TAction.Create(AOwner);
  Result.Name := AName;
end;

function NativeState(Menu: HMENU; Command: Word): UINT;
var
  Info: TMenuItemInfoW;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := MIIM_STATE;
  if GetMenuItemInfoW(Menu, Command, False, Info) then
    Result := Info.fState
  else
    Result := $FFFFFFFF;
end;

procedure Controls_;
var
  F: TForm;
  A, B2: TAction;
  B, Mine: TButton;
  Exec, OnClick: TNotifyEvent;
begin
  F := TForm.CreateNew(nil);
  try
    A := NewAction(F, 'A');
    A.Caption := 'Go';
    A.Enabled := False;
    A.Hint := 'Go|Runs it';
    A.OnExecute := Log.Exec;
    B := TButton.Create(F);
    B.Name := 'B';
    B.Caption := '';
    B.Parent := F;

    Section('assignment (UIF-906)');
    CheckEqB(True, TControlAccess(B).Action = nil, 'no action by default');
    B.Action := A;
    CheckEqB(True, B.Action = A, 'Action');
    CheckEqS('Go', B.Caption, 'empty Caption takes the action''s');
    CheckEqB(False, B.Enabled, 'Enabled (default True) takes the action''s');
    CheckEqS('Go|Runs it', B.Hint, 'empty Hint takes the action''s');
    CheckEqB(True, B.Visible, 'Visible');
    Exec := A.OnExecute;
    OnClick := B.OnClick;
    CheckEqB(True, SameEvent(Exec, OnClick), 'unassigned OnClick takes OnExecute');
    Check(csActionClient in B.ControlStyle, 'csActionClient');
    Mine := TButton.Create(F);
    Mine.Name := 'Mine';
    Mine.Caption := 'Own';
    Mine.Enabled := False;
    Mine.Hint := 'mine';
    Mine.OnClick := Log.Own;
    Mine.Parent := F;
    A.Enabled := True;
    Mine.Action := A;
    CheckEqS('Own', Mine.Caption, 'own Caption kept');
    CheckEqB(False, Mine.Enabled, 'Enabled False (not the default) kept');
    CheckEqS('mine', Mine.Hint, 'own Hint kept');
    OnClick := Mine.OnClick;
    CheckEqB(False, SameEvent(Exec, OnClick), 'own OnClick kept');
    Mine.Caption := 'Mine';
    CheckEqS('Mine', Mine.Caption, 'Caption equal to the Name');
    Mine.Action := nil;
    Mine.Action := A;
    CheckEqS('Go', Mine.Caption, 'Caption equal to the Name counts as default');

    Section('later changes (UIF-907)');
    CheckEqB(True, B.Enabled, 'linked Enabled followed A.Enabled := True');
    A.Caption := 'Go2';
    CheckEqS('Go2', B.Caption, 'linked Caption follows');
    B.Caption := 'Independent';
    A.Caption := 'Go3';
    CheckEqS('Independent', B.Caption, 'independently changed Caption stays');
    B.Enabled := False;
    A.Enabled := False;
    A.Enabled := True;
    CheckEqB(True, B.Enabled, 'a client value equal to the old value is linked again');
    A.Hint := 'New';
    CheckEqS('New', B.Hint, 'Hint follows');
    A.Visible := False;
    CheckEqB(False, B.Visible, 'Visible follows');
    A.Visible := True;
    CheckEqB(True, B.Visible, 'Visible back');
    A.OnExecute := Log.Own;
    A.Caption := 'Go4';
    OnClick := B.OnClick;
    Exec := Log.Own;
    CheckEqB(True, SameEvent(Exec, OnClick), 'OnClick follows OnExecute at the next change');
    A.OnExecute := Log.Exec;
    A.Caption := 'Go5';

    Section('click (UIF-909)');
    Log.Text := '';
    B.Click;
    CheckEqS('exec:A<B;', Log.Text, 'Click executes the action with ActionComponent = the client');
    CheckEqB(True, A.ActionComponent = nil, 'ActionComponent cleared after the call');
    Mine.OnClick := Log.Own;
    Log.Text := '';
    Mine.Click;
    CheckEqS('own:Mine;', Log.Text, 'own OnClick: the action is not executed');
    Mine.OnClick := nil;
    Log.Text := '';
    Mine.Click;
    CheckEqS('exec:A<Mine;', Log.Text, 'unassigned OnClick: the action runs');
    B.HandleNeeded;
    Log.Text := '';
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(GetDlgCtrlID(B.Handle), BN_CLICKED), LPARAM(B.Handle));
    CheckEqS('exec:A<B;', Log.Text, 'BN_CLICKED: the action runs');
    A.Enabled := False;
    Log.Text := '';
    B.Click;
    CheckEqS('', Log.Text, 'disabled action: Execute does nothing');
    A.Enabled := True;

    Section('removal (UIF-912)');
    B2 := NewAction(F, 'B2');
    B2.Caption := 'Second';
    B.Action := B2;
    CheckEqB(True, B.Action = B2, 'reassigned');
    A.Caption := 'Go6';
    CheckEqS('Independent', B.Caption, 'the old action no longer reaches the client');
    B2.Free;
    CheckEqB(True, B.Action = nil, 'freed action: link cleared');
    CheckEqS('Independent', B.Caption, 'copied values untouched');
    Log.Text := '';
    B.Click;
    CheckEqS('plain:B;', Log.Text, 'OnClick copied earlier still runs as a plain handler');
    Check(not (csActionClient in B.ControlStyle), 'csActionClient cleared');
    Mine.Action := nil;
    CheckEqB(True, Mine.Action = nil, 'Action := nil');
    Mine.Caption := 'X';
    A.Caption := 'Go7';
    CheckEqS('X', Mine.Caption, 'no link after Action := nil');
  finally
    F.Free;
  end;
end;

procedure Buttons;
var
  F: TForm;
  A, R: TAction;
  C: TCheckBox;
  Rb, Rb2: TRadioButton;
begin
  F := TForm.CreateNew(nil);
  try
    Section('check box and radio button Checked (UIF-905)');
    A := NewAction(F, 'A');
    A.Caption := 'Check';
    A.Checked := True;
    A.AutoCheck := True;
    C := TCheckBox.Create(F);
    C.Name := 'C';
    C.Parent := F;
    C.OnClick := nil;
    Log.Text := '';
    C.Action := A;
    CheckEqB(True, C.Checked, 'unchecked box takes Checked');
    CheckEqS('', Log.Text, 'copying Checked fires no OnClick');
    CheckEqB(True, A.Checked, 'no Click: the action was not executed (AutoCheck would toggle it)');
    A.Checked := False;
    CheckEqB(False, C.Checked, 'linked Checked follows');
    CheckEqS('', Log.Text, 'still no click');
    A.OnExecute := Log.Exec;
    A.Caption := 'Check2';                                     { a change: OnClick follows OnExecute }
    C.HandleNeeded;
    Log.Text := '';
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(GetDlgCtrlID(C.Handle), BN_CLICKED), LPARAM(C.Handle));
    CheckEqB(True, C.Checked, 'user toggle checks the box');
    CheckEqS('exec:A<C;', Log.Text, 'the toggle executes the action once');
    CheckEqB(True, A.Checked, 'AutoCheck: the action is checked too');
    A.Checked := False;
    CheckEqB(False, C.Checked, 'linked again: unchecking the action unchecks the box');
    CheckEqI(BST_UNCHECKED, SendMessageW(C.Handle, BM_GETCHECK, 0, 0), 'native state follows');

    R := NewAction(F, 'R');
    R.Checked := True;
    Rb := TRadioButton.Create(F);
    Rb.Name := 'Rb';
    Rb.Parent := F;
    Rb2 := TRadioButton.Create(F);
    Rb2.Name := 'Rb2';
    Rb2.Parent := F;
    Rb2.Checked := True;
    Rb.OnClick := Log.Clicked;
    Log.Text := '';
    Rb.Action := R;
    CheckEqB(True, Rb.Checked, 'radio button takes Checked');
    CheckEqB(False, Rb2.Checked, 'the group is resolved');
    CheckEqS('', Log.Text, 'no OnClick');
    R.Checked := False;
    CheckEqB(False, Rb.Checked, 'radio Checked follows');
    R.Checked := True;
    CheckEqB(True, Rb.Checked, 'radio Checked follows again');
    CheckEqS('', Log.Text, 'still no OnClick');
  finally
    F.Free;
  end;
end;

procedure MenuItems;
var
  F: TForm;
  A: TAction;
  M: TMainMenu;
  Top, Item, Own: TMenuItem;
  Exec, OnClick: TNotifyEvent;
begin
  F := TForm.CreateNew(nil);
  try
    Section('menu item (UIF-905, UIF-906, UIF-907, UIF-909)');
    A := NewAction(F, 'A');
    A.Caption := '&Save';
    A.Checked := True;
    A.Enabled := False;
    A.Hint := 'Save|Saves the file';
    A.ImageIndex := 3;
    A.ShortCut := 16467;
    A.OnExecute := Log.Exec;
    M := TMainMenu.Create(F);
    Top := TMenuItem.Create(F);
    Top.Caption := '&File';
    M.Items.Add(Top);
    Item := TMenuItem.Create(F);
    Item.Name := 'Item';
    Top.Add(Item);
    Item.Action := A;
    CheckEqB(True, Item.Action = A, 'Action');
    CheckEqS('&Save', Item.Caption, 'Caption');
    CheckEqB(True, Item.Checked, 'Checked');
    CheckEqB(False, Item.Enabled, 'Enabled');
    CheckEqS('Save|Saves the file', Item.Hint, 'Hint');
    CheckEqI(3, Item.ImageIndex, 'ImageIndex');
    CheckEqI(16467, Item.ShortCut, 'ShortCut');
    CheckEqB(True, Item.Visible, 'Visible');
    Exec := A.OnExecute;
    OnClick := Item.OnClick;
    CheckEqB(True, SameEvent(Exec, OnClick), 'OnClick');
    A.ShortCut := 16466;
    CheckEqI(16466, Item.ShortCut, 'ShortCut follows');
    A.ImageIndex := 4;
    CheckEqI(4, Item.ImageIndex, 'ImageIndex follows');
    A.Checked := False;
    CheckEqB(False, Item.Checked, 'Checked follows');
    A.Enabled := True;
    CheckEqB(True, Item.Enabled, 'Enabled follows');
    CheckEqI(0, NativeState(Top.Handle, Item.Command) and MFS_GRAYED, 'native entry rebuilt');
    A.Visible := False;
    CheckEqB(False, Item.Visible, 'Visible follows');
    A.Visible := True;
    Item.Caption := 'Mine';
    A.Caption := 'Other';
    CheckEqS('Mine', Item.Caption, 'independent Caption stays');
    Log.Text := '';
    Item.Click;
    CheckEqS('exec:A<Item;', Log.Text, 'Click executes the action');
    Log.Text := '';
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(Item.Command, 0), 0);
    CheckEqS('', Log.Text, 'menu not on the form: not routed');
    F.Menu := M;
    Log.Text := '';
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(Item.Command, 0), 0);
    CheckEqS('exec:A<Item;', Log.Text, 'WM_COMMAND: the action runs');
    Own := TMenuItem.Create(F);
    Own.Name := 'Own';
    Own.Caption := 'Kept';
    Own.ShortCut := 1;
    Own.OnClick := Log.Own;
    Top.Add(Own);
    Own.Action := A;
    CheckEqS('Kept', Own.Caption, 'own Caption kept');
    CheckEqI(1, Own.ShortCut, 'own ShortCut kept');
    Log.Text := '';
    Own.Click;
    CheckEqS('own:Own;', Log.Text, 'own OnClick: no action');
    A.Free;
    CheckEqB(True, Item.Action = nil, 'freed action: item link cleared (UIF-912)');
    CheckEqS('Mine', Item.Caption, 'values kept');
  finally
    F.Free;
  end;
end;

procedure CancelNextMenu;
begin
  PostMessageW(PopupListWindow, WM_CANCELMODE, 0, 0);
end;

procedure MenuUpdates;
var
  F: TForm;
  U, P: TAction;
  PM: TPopupMenu;
  Sub, Child, PItem: TMenuItem;
  M: TMainMenu;
  MTop, MChild: TMenuItem;
begin
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(80, 80, 300, 200);
    Section('updates before a menu opens (UIF-911)');
    U := NewAction(F, 'U');
    U.OnUpdate := Log.Upd;
    U.OnExecute := Log.Exec;
    P := NewAction(F, 'P');
    P.OnUpdate := Log.Upd;
    PM := TPopupMenu.Create(F);
    PItem := TMenuItem.Create(F);
    PItem.Action := P;
    PM.Items.Add(PItem);
    Sub := TMenuItem.Create(F);
    Sub.Name := 'Sub';
    Sub.Caption := 'Sub';
    Sub.OnClick := Log.Clicked;
    PM.Items.Add(Sub);
    Child := TMenuItem.Create(F);
    Child.Action := U;
    Sub.Add(Child);
    Log.Text := '';
    CancelNextMenu;
    PM.Popup(100, 100);
    Check(Pos('upd:P;', Log.Text) = 1, 'Popup: the items'' actions are updated first (' + Log.Text + ')');
    CheckEqI(0, Pos('upd:U;', Log.Text), 'not the sub-menu''s yet');
    Log.Text := '';
    Log.DisableOnUpdate := True;
    SendMessageW(PopupListWindow, WM_INITMENUPOPUP, WPARAM(Sub.Handle), 0);
    CheckEqS('click:Sub;upd:U;', Log.Text, 'sub-menu: OnClick hook, then the children''s actions');
    CheckEqB(False, Child.Enabled, 'the update reached the item');
    CheckEqI(MFS_GRAYED, NativeState(Sub.Handle, Child.Command) and MFS_GRAYED, 'native entry grayed');
    Log.DisableOnUpdate := False;
    U.Enabled := True;

    M := TMainMenu.Create(F);
    MTop := TMenuItem.Create(F);
    MTop.Name := 'MTop';
    MTop.Caption := '&Edit';
    M.Items.Add(MTop);
    MChild := TMenuItem.Create(F);
    MChild.Action := U;
    MTop.Add(MChild);
    F.Menu := M;
    F.HandleNeeded;
    Log.Text := '';
    SendMessageW(F.Handle, WM_INITMENUPOPUP, WPARAM(MTop.Handle), 0);
    CheckEqS('upd:U;', Log.Text, 'main menu sub-menu: the children''s actions');
  finally
    F.Free;
  end;
end;

procedure Loading;
var
  F: TLinksForm;
  Exec, OnClick: TNotifyEvent;
begin
  Section('loading (UIF-908)');
  F := TLinksForm.Create(nil);
  try
    CheckEqB(True, F.Button1.Action = F.Act1, 'Button1.Action resolved');
    CheckEqS('Stored', F.Button1.Caption, 'stored Caption wins');
    CheckEqB(False, F.Button1.Enabled, 'default Enabled takes the action''s');
    CheckEqS('Short|Long', F.Button1.Hint, 'Hint from the action');
    Exec := F.Act1.OnExecute;
    OnClick := F.Button1.OnClick;
    CheckEqB(True, SameEvent(Exec, OnClick), 'OnClick from the action');
    CheckEqS('Check', F.CheckBox1.Caption, 'CheckBox1 Caption from the action');
    CheckEqB(True, F.CheckBox1.Checked, 'CheckBox1 Checked from the action');
    CheckEqS('ActCap', F.Item1.Caption, 'menu item Caption');
    CheckEqI(16463, F.Item1.ShortCut, 'menu item ShortCut');
    CheckEqB(False, F.Item1.Enabled, 'menu item Enabled');
    F.Act1.Enabled := True;
    CheckEqB(True, F.Button1.Enabled, 'linked after loading: Button1');
    CheckEqB(True, F.Item1.Enabled, 'linked after loading: Item1');
    Log.Text := '';
    F.Button1.Click;
    F.Item1.Click;
    CheckEqS('act1;act1;', Log.Text, 'clicks run the form''s handler through the action');
  finally
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Log := TLog.Create;
  try
    Controls_;
    Buttons;
    MenuItems;
    MenuUpdates;
    Loading;
  finally
    Log.Free;
  end;
  Done;
end.
