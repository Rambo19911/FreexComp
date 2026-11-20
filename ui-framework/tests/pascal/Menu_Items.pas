// covers: UIF-720, UIF-721, UIF-722, UIF-725, UIF-934
// needs: desktop
program Menu_Items;
{ U-073, controls-standard.md section 12: TMenuItem defaults; the item tree (Add, Insert, Delete, Remove, Clear,
  IndexOf, MenuIndex, Find without accelerator marks, Parent, GetParentMenu, IsLine, moving between parents,
  cycles refused, freeing a child or a parent); unique command ids 1..65535 released on destruction, exhaustion
  raising EOutOfResources; native menus built lazily (menu bar for a main menu, pop-up per item with children),
  each child's entry carrying its Command id, caption + tab + ShortCutToText, separator, radio check, breaks,
  checked / default / grayed state, sub-menu, item bitmap from the menu's Images; invisible items left out;
  every change rebuilding the parent's native menu with the same HMENU; Click (disabled, AutoCheck, radio);
  radio groups; streaming of nested items in a .form (SetParentComponent, GetChildren, GetParentComponent, every
  published property) and F0103 for a wrong parent; no GDI/USER objects leak. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, ImgList, Controls, Forms, ExtCtrls, Menus, TestKit;

type
  TMenuForm = class(TForm)
  published
    MainMenu1: TMainMenu;
    FileMenu: TMenuItem;
    FileOpen: TMenuItem;
    N1: TMenuItem;
    FileRecent: TMenuItem;
    Recent1: TMenuItem;
    Recent2: TMenuItem;
    FileExit: TMenuItem;
    HelpItem: TMenuItem;
    procedure FileOpenClick(Sender: TObject);
  end;

  TMenuAccess = class(TMenu);
  TMenuItemAccess = class(TMenuItem);

  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Child(Child: TComponent);
  end;

{$FORM Menu_Items.form}

var
  Log: TLog;

procedure TMenuForm.FileOpenClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'open;';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + TMenuItem(Sender).Caption + ';';
end;

procedure TLog.Child(Child: TComponent);
begin
  Text := Text + Child.Name + ',';
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

function NewItem(AParent: TMenuItem; const ACaption: string): TMenuItem;
begin
  Result := TMenuItem.Create(nil);
  Result.Caption := ACaption;
  if AParent <> nil then
    AParent.Add(Result);
end;

function ChildNames(Item: TMenuItem): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Item.Count - 1 do
    Result := Result + Item[I].Caption + ',';
end;

function GetInfo(H: HMENU; Position: Integer; var Info: TMenuItemInfoW; var Text: string): Boolean;
var
  Buffer: array[0..255] of WideChar;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := MIIM_FTYPE or MIIM_STATE or MIIM_ID or MIIM_STRING or MIIM_SUBMENU or MIIM_BITMAP;
  Buffer[0] := #0;
  Info.dwTypeData := @Buffer[0];
  Info.cch := Length(Buffer);
  Result := GetMenuItemInfoW(H, Position, True, Info);
  Text := PWideChar(@Buffer[0]);
end;

{ the native entry at Position: text and decoded flags (ids are checked separately) }
function Native(H: HMENU; Position: Integer): string;
var
  Info: TMenuItemInfoW;
  Text: string;
begin
  if not GetInfo(H, Position, Info, Text) then
  begin
    Result := 'none';
    Exit;
  end;
  if Info.fType and MFT_SEPARATOR <> 0 then
    Result := 'sep'
  else
    Result := '"' + StringReplace(Text, #9, '<tab>', []) + '"';
  if Info.fType and MFT_RADIOCHECK <> 0 then
    Result := Result + ' radio';
  if Info.fType and MFT_MENUBREAK <> 0 then
    Result := Result + ' break';
  if Info.fType and MFT_MENUBARBREAK <> 0 then
    Result := Result + ' barbreak';
  if Info.fState and MFS_CHECKED <> 0 then
    Result := Result + ' checked';
  if Info.fState and MFS_DEFAULT <> 0 then
    Result := Result + ' default';
  if (Info.fState and MFS_GRAYED <> 0) and (Info.fType and MFT_SEPARATOR = 0) then  { Win32 reports separators grayed }
    Result := Result + ' grayed';
  if Info.hSubMenu <> 0 then
    Result := Result + ' sub';
  if Info.hbmpItem <> 0 then
    Result := Result + ' bitmap';
end;

function NativeId(H: HMENU; Position: Integer): Integer;
var
  Info: TMenuItemInfoW;
  Text: string;
begin
  GetInfo(H, Position, Info, Text);
  Result := Info.wID;
end;

function NativeSub(H: HMENU; Position: Integer): HMENU;
var
  Info: TMenuItemInfoW;
  Text: string;
begin
  GetInfo(H, Position, Info, Text);
  Result := Info.hSubMenu;
end;

function NewImages(Owner: TComponent): TImageList;
var
  B: TBitmap;
begin
  Result := TImageList.Create(Owner);
  B := TBitmap.Create;
  try
    B.SetSize(16, 16);
    B.Canvas.Brush.Color := clRed;
    B.Canvas.FillRect(Rect(0, 0, 16, 16));
    Result.Add(B, nil);
    Result.Add(B, nil);
  finally
    B.Free;
  end;
end;

procedure DefaultsAndTree;
var
  Root, A, B, C, D, Sub: TMenuItem;
  Holder: TComponent;
  M: TMainMenu;
begin
  Section('defaults');
  A := TMenuItem.Create(nil);
  try
    CheckEqS('', A.Caption, 'Caption empty');
    CheckEqB(True, A.Enabled, 'Enabled True');
    CheckEqB(True, A.Visible, 'Visible True');
    CheckEqB(False, A.Checked, 'Checked False');
    CheckEqB(False, A.AutoCheck, 'AutoCheck False');
    CheckEqB(False, A.Default, 'Default False');
    CheckEqB(False, A.RadioItem, 'RadioItem False');
    CheckEqB(True, A.Break = mbNone, 'Break mbNone');
    CheckEqI(0, A.GroupIndex, 'GroupIndex 0');
    CheckEqI(-1, A.ImageIndex, 'ImageIndex -1');
    CheckEqI(0, A.ShortCut, 'ShortCut 0');
    CheckEqS('', A.Hint, 'Hint empty');
    CheckEqI(0, A.Count, 'no children');
    CheckEqB(True, A.Parent = nil, 'no parent');
    CheckEqI(-1, A.MenuIndex, 'MenuIndex -1 without parent');
    CheckEqB(True, A.HasParent, 'HasParent');
    CheckEqB(True, A.GetParentMenu = nil, 'no menu');
    CheckEqB(False, A.HandleAllocated, 'no handle yet');
    Check((A.Command >= 1) and (A.Command <= 65535), 'Command in 1..65535');
    CheckEqB(False, A.IsLine, 'IsLine False');
    A.Caption := '-';
    CheckEqB(True, A.IsLine, 'IsLine for "-"');
    Check(GetClass('TMenuItem') <> nil, 'TMenuItem registered');
    Check(GetClass('TMainMenu') <> nil, 'TMainMenu registered');
  finally
    A.Free;
  end;

  Section('tree');
  Root := TMenuItem.Create(nil);
  try
    A := NewItem(Root, 'A');
    C := NewItem(Root, 'C');
    B := TMenuItem.Create(nil);
    B.Caption := '&B';
    Root.Insert(1, B);
    CheckEqS('A,&B,C,', ChildNames(Root), 'Add and Insert');
    CheckEqI(3, Root.Count, 'Count');
    CheckEqB(True, Root[1] = B, 'Items[]');
    CheckEqB(True, B.Parent = Root, 'Parent');
    CheckEqI(1, B.MenuIndex, 'MenuIndex');
    CheckEqI(2, Root.IndexOf(C), 'IndexOf');
    CheckEqB(True, Root.Find('b') = B, 'Find: accelerator marks stripped, case-insensitive');
    CheckEqB(True, Root.Find('&c') = C, 'Find: marks in the argument stripped');
    CheckEqB(True, Root.Find('X') = nil, 'Find: no match');
    CheckEqS('A&B', StripHotkey('&A&&&B'), 'StripHotkey');
    B.MenuIndex := 0;
    CheckEqS('&B,A,C,', ChildNames(Root), 'MenuIndex moves');
    B.MenuIndex := 99;
    CheckEqS('A,C,&B,', ChildNames(Root), 'MenuIndex clamped to the end');
    Root.Delete(0);
    CheckEqS('C,&B,', ChildNames(Root), 'Delete removes');
    CheckEqB(True, A.Parent = nil, 'Delete clears Parent (not freed)');
    Root.Remove(B);
    CheckEqS('C,', ChildNames(Root), 'Remove');
    Root.Add(A);
    Root.Add(B);
    Sub := NewItem(A, 'A1');
    C.Add(Sub);
    CheckEqB(True, Sub.Parent = C, 'Add moves an item from its old parent');
    CheckEqI(0, A.Count, 'old parent lost it');
    Expect('EInvalidOperation', 'Menu item "" cannot be inserted here');
    try
      Sub.Add(C);
      CheckRaises('an ancestor cannot be added', '', '');
    except
      on E: Exception do
        CheckRaises('an ancestor cannot be added', E.ClassName, E.Message);
    end;
    try
      C.Add(C);
      CheckRaises('an item cannot be added to itself', '', '');
    except
      on E: Exception do
        CheckRaises('an item cannot be added to itself', E.ClassName, E.Message);
    end;
    Expect('EListError', 'List index out of bounds (5)');
    try
      Root.Insert(5, TMenuItem.Create(Root));
      CheckRaises('Insert beyond Count', '', '');
    except
      on E: Exception do
        CheckRaises('Insert beyond Count', E.ClassName, E.Message);
    end;
    Sub.Free;
    CheckEqI(0, C.Count, 'a freed child leaves its parent');
    Holder := TComponent.Create(nil);
    try
      D := TMenuItem.Create(Holder);
      D.Add(TMenuItem.Create(Holder));
      Root.Add(D);
      CheckEqI(2, Holder.ComponentCount, 'two owned items');
      Root.Clear;
      CheckEqI(0, Root.Count, 'Clear empties');
      CheckEqI(0, Holder.ComponentCount, 'Clear frees the children and their children');
    finally
      Holder.Free;
    end;
  finally
    Root.Free;
  end;

  M := TMainMenu.Create(nil);
  try
    A := NewItem(M.Items, 'A');
    B := NewItem(A, 'B');
    CheckEqB(True, B.GetParentMenu = M, 'GetParentMenu');
    CheckEqB(True, A.GetParentComponent = M, 'GetParentComponent of a top-level item = the menu');
    CheckEqB(True, B.GetParentComponent = A, 'GetParentComponent of a nested item = its parent');
    Expect('EInvalidOperation', 'Menu item "" cannot be inserted here');
    try
      A.Add(M.Items);
      CheckRaises('a menu root cannot be inserted', '', '');
    except
      on E: Exception do
        CheckRaises('a menu root cannot be inserted', E.ClassName, E.Message);
    end;
  finally
    M.Free;
  end;
end;

procedure CommandIds;
var
  A, B, C: TMenuItem;
  Items: TList;
  Ok: Boolean;
  Freed: Word;
  I: Integer;
begin
  Section('command ids (UIF-720)');
  A := TMenuItem.Create(nil);
  B := TMenuItem.Create(nil);
  C := TMenuItem.Create(nil);
  CheckEqB(True, (A.Command <> B.Command) and (B.Command <> C.Command) and (A.Command <> C.Command),
    'distinct ids');
  A.Free;
  B.Free;
  C.Free;
  Items := TList.Create;
  try
    Ok := True;
    try
      while True do
        Items.Add(TMenuItem.Create(nil));
    except
      on E: EOutOfResources do
        Ok := E.Message = 'Out of system resources';
    end;
    Check(Ok, 'all ids in use: EOutOfResources');
    Ok := True;
    for I := 0 to Items.Count - 1 do
      if (TMenuItem(Items[I]).Command < 1) then
        Ok := False;
    Check(Ok and (Items.Count <= 65535) and (Items.Count > 65000), 'up to 65535 ids, all >= 1');
    Freed := TMenuItem(Items[100]).Command;
    TMenuItem(Items[100]).Free;
    Items.Delete(100);
    A := TMenuItem.Create(nil);
    CheckEqI(Freed, A.Command, 'a released id is allocated again');
    Items.Add(A);
  finally
    for I := 0 to Items.Count - 1 do
      TMenuItem(Items[I]).Free;
    Items.Free;
  end;
  A := TMenuItem.Create(nil);
  Check(A.Command <> 0, 'ids available again after release');
  A.Free;
end;

procedure NativeMenus;
var
  M: TMainMenu;
  FileItem, Open, Sep, Radio, Br, Hidden, Help, Sub1: TMenuItem;
  Bar, FileH: HMENU;
  Images: TImageList;
  Before, I: Integer;
begin
  Section('native menus (UIF-720, UIF-725, UIF-934)');
  M := TMainMenu.Create(nil);
  try
    FileItem := NewItem(M.Items, '&File');
    Open := NewItem(FileItem, '&Open...');
    Open.ShortCut := ShortCut(Ord('O'), [ssCtrl]);
    Sep := NewItem(FileItem, '-');
    Radio := NewItem(FileItem, 'Radio');
    Radio.RadioItem := True;
    Radio.Checked := True;
    Br := NewItem(FileItem, 'Next column');
    Br.Break := mbBreak;
    Br.Default := True;
    Br.Enabled := False;
    Hidden := NewItem(FileItem, 'Hidden');
    Hidden.Visible := False;
    Help := NewItem(M.Items, '&Help');
    CheckEqB(False, M.HandleAllocated, 'built lazily');
    CheckEqB(False, FileItem.HandleAllocated, 'sub-menus built lazily');
    Bar := M.Handle;
    Check(Bar <> 0, 'main menu handle');
    CheckEqB(True, M.Items.Handle = Bar, 'Handle = Items.Handle');
    CheckEqI(2, GetMenuItemCount(Bar), 'bar: two top-level entries');
    CheckEqS('"&File" sub', Native(Bar, 0), 'item with children: pop-up');
    CheckEqS('"&Help"', Native(Bar, 1), 'item without children: command on the bar');
    CheckEqI(FileItem.Command, NativeId(Bar, 0), 'pop-up entry id = Command');
    CheckEqI(Help.Command, NativeId(Bar, 1), 'command entry id = Command');
    FileH := FileItem.Handle;
    CheckEqB(True, NativeSub(Bar, 0) = FileH, 'sub-menu = the item''s Handle');
    CheckEqI(4, GetMenuItemCount(FileH), 'invisible items are not inserted');
    CheckEqS('"&Open...<tab>Ctrl+O"', Native(FileH, 0), 'caption, tab, shortcut text');
    CheckEqI(Open.Command, NativeId(FileH, 0), 'id = Command');
    CheckEqS('sep', Native(FileH, 1), 'separator');
    CheckEqS('"Radio" radio checked', Native(FileH, 2), 'radio check, checked');
    CheckEqS('"Next column" break default grayed', Native(FileH, 3), 'break, default, grayed');

    Section('changes rebuild the parent menu');
    Open.Caption := 'Open';
    CheckEqS('"Open<tab>Ctrl+O"', Native(FileH, 0), 'caption change');
    Open.ShortCut := 0;
    CheckEqS('"Open"', Native(FileH, 0), 'no shortcut: no tab');
    Br.Break := mbBarBreak;
    Br.Default := False;
    Br.Enabled := True;
    CheckEqS('"Next column" barbreak', Native(FileH, 3), 'bar break, enabled, not default');
    Hidden.Visible := True;
    CheckEqI(5, GetMenuItemCount(FileH), 'made visible: inserted');
    CheckEqS('"Hidden"', Native(FileH, 4), 'at its position');
    Radio.Visible := False;
    CheckEqI(4, GetMenuItemCount(FileH), 'made invisible: removed');
    CheckEqS('"Next column" barbreak', Native(FileH, 2), 'later entries move up');
    Radio.Visible := True;
    Radio.Checked := False;
    CheckEqS('"Radio" radio', Native(FileH, 2), 'unchecked');
    Hidden.MenuIndex := 0;
    CheckEqS('"Hidden"', Native(FileH, 0), 'MenuIndex change reorders');
    Sub1 := NewItem(Help, 'About');
    CheckEqS('"&Help" sub', Native(Bar, 1), 'first child turns a command into a pop-up');
    CheckEqB(True, M.Handle = Bar, 'the bar keeps its HMENU');
    CheckEqB(True, FileItem.Handle = FileH, 'the sub-menu keeps its HMENU');
    CheckEqS('"About"', Native(Help.Handle, 0), 'new sub-menu built');
    Sub1.Free;
    CheckEqS('"&Help"', Native(Bar, 1), 'last child freed: a command again');
    Help.Free;
    CheckEqI(1, GetMenuItemCount(Bar), 'freed item leaves the bar');
    NewItem(M.Items, 'Window');
    CheckEqI(2, GetMenuItemCount(Bar), 'added item appears');

    Section('item bitmaps');
    Images := NewImages(nil);
    try
      Open.ImageIndex := 1;
      CheckEqS('"Open"', Native(FileH, 1), 'no Images: no bitmap');
      M.Images := Images;
      CheckEqS('"Open" bitmap', Native(FileH, 1), 'Images assigned: bitmap from the image list');
      Open.ImageIndex := 5;
      CheckEqS('"Open"', Native(FileH, 1), 'index out of range: no bitmap');
      Open.ImageIndex := 0;
      CheckEqS('"Open" bitmap', Native(FileH, 1), 'index back in range');
    finally
      Images.Free;
    end;
    CheckEqB(True, M.Images = nil, 'freed image list cleared');
    CheckEqS('"Open"', Native(FileH, 1), 'freed image list: bitmap removed');
  finally
    M.Free;
  end;

  Section('leaks');
  Before := GuiObjects;
  for I := 1 to 50 do
  begin
    Images := NewImages(nil);
    M := TMainMenu.Create(nil);
    try
      M.Images := Images;
      FileItem := NewItem(M.Items, 'File');
      Open := NewItem(FileItem, 'Open');
      Open.ImageIndex := 0;
      NewItem(NewItem(FileItem, 'More'), 'Deep').ImageIndex := 1;
      Bar := M.Handle;
      Open.Caption := 'Open again';
      FileItem.Items[1].Free;
    finally
      M.Free;
      Images.Free;
    end;
  end;
  CheckEqI(Before, GuiObjects, '50 menus with handles and bitmaps: no GDI/USER objects leak');
end;

procedure Clicks;
var
  Root, A, R1, R2, R3: TMenuItem;
begin
  Section('Click (UIF-722)');
  Root := TMenuItem.Create(nil);
  try
    A := NewItem(Root, 'A');
    A.OnClick := Log.Clicked;
    Log.Text := '';
    A.Click;
    CheckEqS('click:A;', Log.Text, 'OnClick');
    A.Enabled := False;
    A.Click;
    CheckEqS('click:A;', Log.Text, 'disabled: nothing');
    A.Enabled := True;
    A.AutoCheck := True;
    A.Click;
    CheckEqB(True, A.Checked, 'AutoCheck toggles on');
    A.Click;
    CheckEqB(False, A.Checked, 'AutoCheck toggles off');

    Section('radio groups');
    R1 := NewItem(Root, 'R1');
    R2 := NewItem(Root, 'R2');
    R3 := NewItem(Root, 'R3');
    R1.RadioItem := True;
    R2.RadioItem := True;
    R3.RadioItem := True;
    R3.GroupIndex := 2;
    A.Checked := True;
    R1.Checked := True;
    R3.Checked := True;
    R2.Checked := True;
    CheckEqS('A=1 R1=0 R2=1 R3=1', Format('A=%d R1=%d R2=%d R3=%d',
      [Ord(A.Checked), Ord(R1.Checked), Ord(R2.Checked), Ord(R3.Checked)]),
      'checking unchecks the radio items with the same GroupIndex only');
    R1.AutoCheck := True;
    R1.Click;
    R1.Click;
    CheckEqS('R1=1 R2=0', Format('R1=%d R2=%d', [Ord(R1.Checked), Ord(R2.Checked)]),
      'AutoCheck on a radio item checks (stays checked)');
    R3.GroupIndex := 0;
    CheckEqS('R1=0 R3=1', Format('R1=%d R3=%d', [Ord(R1.Checked), Ord(R3.Checked)]),
      'moving a checked radio item into a group unchecks the others');
  finally
    Root.Free;
  end;
end;

procedure Streaming;
var
  F: TMenuForm;
  P: TPanel;
  Ok: Boolean;
  Msg: string;
begin
  Section('streaming (UIF-721)');
  F := TMenuForm.Create(nil);
  try
    CheckEqI(2, F.MainMenu1.Items.Count, 'two top-level items');
    CheckEqB(True, F.MainMenu1.Items[0] = F.FileMenu, 'FileMenu first');
    CheckEqB(True, F.MainMenu1.Items[1] = F.HelpItem, 'HelpItem second');
    CheckEqS('&Open...,-,&Recent,E&xit,', ChildNames(F.FileMenu), 'nested in display order');
    CheckEqB(True, F.Recent2.Parent = F.FileRecent, 'third level');
    CheckEqB(True, F.FileOpen.Owner = F, 'items owned by the form');
    CheckEqB(True, F.FileMenu.GetParentMenu = F.MainMenu1, 'GetParentMenu');
    CheckEqI(16, F.MainMenu1.DesignInfo and $FFFF, 'DesignLeft');
    CheckEqI(200, F.MainMenu1.DesignInfo shr 16, 'DesignTop');
    CheckEqI(16463, F.FileOpen.ShortCut, 'ShortCut');
    CheckEqS('Open|Opens a file', F.FileOpen.Hint, 'Hint');
    CheckEqB(True, F.N1.IsLine, 'separator');
    CheckEqB(True, F.Recent1.RadioItem and F.Recent2.RadioItem, 'RadioItem');
    CheckEqI(1, F.Recent1.GroupIndex, 'GroupIndex');
    CheckEqB(True, F.Recent1.Checked and F.Recent2.Checked, 'stored Checked values win while loading');
    CheckEqB(True, F.FileExit.Break = mbBarBreak, 'Break');
    CheckEqB(True, F.FileExit.Default, 'Default');
    CheckEqB(True, F.FileExit.AutoCheck, 'AutoCheck');
    CheckEqI(2, F.FileExit.ImageIndex, 'ImageIndex');
    CheckEqB(False, F.FileExit.Enabled, 'Enabled');
    CheckEqB(False, F.FileExit.Visible, 'Visible');
    Log.Text := '';
    F.FileOpen.Click;
    CheckEqS('open;', Log.Text, 'OnClick bound to the form method');
    Log.Text := '';
    TMenuAccess(F.MainMenu1).GetChildren(Log.Child, F);
    CheckEqS('FileMenu,HelpItem,', Log.Text, 'TMenu.GetChildren: top-level items');
    Log.Text := '';
    TMenuItemAccess(F.FileMenu).GetChildren(Log.Child, F);
    CheckEqS('FileOpen,N1,FileRecent,FileExit,', Log.Text, 'TMenuItem.GetChildren: its items');
    CheckEqI(3, GetMenuItemCount(F.FileMenu.Handle), 'loaded tree builds its native menu');
  finally
    F.Free;
  end;

  P := TPanel.Create(nil);
  try
    Ok := False;
    Msg := '';
    try
      ReadComponentFromText('object P: TPanel'#10'  object Bad: TMenuItem'#10'  end'#10'end'#10, P, 'Bad.form');
    except
      on E: EReadError do
      begin
        Ok := True;
        Msg := E.Message;
      end;
    end;
    Check(Ok, 'a menu item under a control: EReadError');
    CheckEqS('Bad.form(2,10): error F0103: Control "Bad" cannot be placed on a TPanel', Msg, 'F0103');
  finally
    P.Free;
  end;
end;

begin
  Application.Initialize;
  Log := TLog.Create;
  try
    DefaultsAndTree;
    CommandIds;
    NativeMenus;
    Clicks;
    Streaming;
  finally
    Log.Free;
  end;
  Done;
end.
