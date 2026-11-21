// covers: UIF-072, UIF-720, UIF-722, UIF-723, UIF-725, UIF-920
// needs: desktop
program Menu_MainMenu;
{ U-074, controls-standard.md section 12, actions-menus.md UIF-920: a TMainMenu on a form. WM_COMMAND with
  LParam = 0 (menu code 0 and accelerator code 1) is routed by the form to the item with that Command id, which
  Clicks (pop-up and command entries alike; nested items; disabled items ignored; unknown ids and control
  notifications left to default processing; core.md UIF-072: the form handles them, a child window does not); WM_INITMENUPOPUP for a sub-menu fires the owning item's OnClick
  first and then refreshes its native menu (changes made by the handler are visible), the window menu flag is
  left alone; a rebuild of the menu bar redraws it (DrawMenuBar: the form receives WM_NCCALCSIZE), a rebuild of
  a nested sub-menu does not; FindItem by command and by handle. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, Menus, TestKit;

type
  TSpyForm = class(TForm)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    FrameCalcs: Integer;
  end;

  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Popping(Sender: TObject);
  end;

var
  Log: TLog;
  Extra: TMenuItem;

procedure TSpyForm.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_NCCALCSIZE then
    Inc(FrameCalcs);
  inherited WndProc(Message);
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TMenuItem(Sender).Name + ';';
end;

{ the customary update hook: adds an item while the sub-menu opens }
procedure TLog.Popping(Sender: TObject);
begin
  Text := Text + 'popup:' + TMenuItem(Sender).Name + ';';
  if Extra.Parent = nil then
    TMenuItem(Sender).Add(Extra);
end;

function NewItem(AOwner: TComponent; AParent: TMenuItem; const AName: string): TMenuItem;
begin
  Result := TMenuItem.Create(AOwner);
  Result.Name := AName;
  Result.Caption := AName;
  Result.OnClick := Log.Clicked;
  AParent.Add(Result);
end;

function Cmd(F: TForm; Id: Word; Code: Word): LRESULT;
begin
  Result := SendMessageW(F.Handle, WM_COMMAND, MakeWParam(Id, Code), 0);
end;

procedure Run;
var
  F: TSpyForm;
  M: TMainMenu;
  FileItem, Open, Deep, Sub, Off, Help: TMenuItem;
  Child: TWinControl;
  Calls: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TSpyForm.CreateNew(nil);
  try
    F.SetBounds(60, 60, 500, 300);
    M := TMainMenu.Create(F);
    FileItem := NewItem(F, M.Items, 'FileItem');
    Open := NewItem(F, FileItem, 'Open');
    Sub := NewItem(F, FileItem, 'Sub');
    Deep := NewItem(F, Sub, 'Deep');
    Off := NewItem(F, FileItem, 'Off');
    Off.Enabled := False;
    Help := NewItem(F, M.Items, 'Help');
    Extra := TMenuItem.Create(F);
    Extra.Name := 'Extra';
    Extra.Caption := 'Extra';
    F.Menu := M;
    F.HandleNeeded;

    Section('WM_COMMAND routing (UIF-920, UIF-722)');
    Log.Text := '';
    Cmd(F, Open.Command, 0);
    CheckEqS('click:Open;', Log.Text, 'menu code 0: item clicked');
    Log.Text := '';
    Cmd(F, Open.Command, 1);
    CheckEqS('click:Open;', Log.Text, 'accelerator code 1: item clicked');
    Log.Text := '';
    Cmd(F, Deep.Command, 0);
    CheckEqS('click:Deep;', Log.Text, 'nested item');
    Log.Text := '';
    Cmd(F, Help.Command, 0);
    CheckEqS('click:Help;', Log.Text, 'command entry on the bar (UIF-725)');
    Log.Text := '';
    Cmd(F, Off.Command, 0);
    CheckEqS('', Log.Text, 'disabled item: no click');
    Cmd(F, Extra.Command, 0);
    CheckEqS('', Log.Text, 'id of an item outside the menu: nothing');
    SendMessageW(F.Handle, WM_COMMAND, MakeWParam(Open.Command, 0), LPARAM(F.Handle));
    CheckEqS('', Log.Text, 'LParam <> 0 is not a menu command');
    Child := TWinControl.Create(F);
    Child.SetBounds(0, 0, 50, 20);
    Child.Parent := F;
    SendMessageW(Child.Handle, WM_COMMAND, MakeWParam(Open.Command, 0), 0);
    CheckEqS('', Log.Text, 'LParam = 0 at a child window: not routed (UIF-072)');
    Cmd(F, Open.Command, 0);
    CheckEqS('click:Open;', Log.Text, 'LParam = 0 at the form: routed (UIF-072)');
    Log.Text := '';
    Child.Free;
    CheckEqB(True, M.FindItem(Deep.Command, fkCommand) = Deep, 'FindItem by command');
    CheckEqB(True, M.FindItem(0, fkCommand) = nil, 'FindItem: id 0 is none');
    CheckEqB(False, M.DispatchCommand(Extra.Command), 'DispatchCommand: unknown id');

    Section('WM_INITMENUPOPUP (UIF-723)');
    FileItem.OnClick := Log.Popping;
    Log.Text := '';
    CheckEqI(3, GetMenuItemCount(FileItem.Handle), 'three entries before');
    SendMessageW(F.Handle, WM_INITMENUPOPUP, WPARAM(FileItem.Handle), 0);
    CheckEqS('popup:FileItem;', Log.Text, 'owning item''s OnClick first');
    CheckEqI(4, GetMenuItemCount(FileItem.Handle), 'the handler''s change is in the native menu');
    CheckEqB(True, M.FindItem(NativeUInt(Sub.Handle), fkHandle) = Sub, 'FindItem by handle');
    Log.Text := '';
    SendMessageW(F.Handle, WM_INITMENUPOPUP, WPARAM(Sub.Handle), 0);
    CheckEqS('click:Sub;', Log.Text, 'nested sub-menu: its item''s OnClick');
    Log.Text := '';
    SendMessageW(F.Handle, WM_INITMENUPOPUP, WPARAM(FileItem.Handle), MakeLParam(0, 1));
    CheckEqS('', Log.Text, 'window menu flag: not a menu of ours');
    Log.Text := '';
    Cmd(F, FileItem.Command, 0);
    CheckEqS('popup:FileItem;', Log.Text, 'pop-up entry clicked by id');

    Section('DrawMenuBar after a rebuild (UIF-720)');
    Calls := F.FrameCalcs;
    Help.Caption := 'Help!';
    Check(F.FrameCalcs > Calls, 'bar entry changed: menu bar redrawn');
    Calls := F.FrameCalcs;
    NewItem(F, M.Items, 'Window');
    Check(F.FrameCalcs > Calls, 'bar item added: menu bar redrawn');
    CheckEqI(3, GetMenuItemCount(M.Handle), 'bar has the new entry');
    Calls := F.FrameCalcs;
    Deep.Caption := 'Deeper';
    CheckEqI(Calls, F.FrameCalcs, 'nested change: no menu bar redraw');
    F.Menu := nil;
    Calls := F.FrameCalcs;
    Help.Caption := 'Help';
    CheckEqI(Calls, F.FrameCalcs, 'detached menu: no redraw of the former form');
    Log.Text := '';
    Cmd(F, Open.Command, 0);
    CheckEqS('', Log.Text, 'without Menu the form routes nothing');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
