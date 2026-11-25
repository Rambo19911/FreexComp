// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1365, UIF-1366, UIF-1367
// phase: 8
program Act_SecondaryShortCuts;
{ U-151, standard-actions.md section 3: TCustomAction.SecondaryShortCuts (a TShortCutList, published by TAction).
  The list is read from the form as a string list directly under the property name; an action without the property
  in the form has an empty list, created on first read. Each line is a shortcut text; a line that is not one has
  the value 0 and never matches. The primary key (through the linked menu item) and every secondary key (through
  the action list of the form) execute the action in TCustomForm.IsShortCut; a disabled action and a suspended
  list do not match; assignment copies the lines; the menu item shows the primary shortcut only. The Shift / Ctrl
  state is set with KeyStateKit, Alt is bit 29 of the key data (no real input). Needs no desktop: the form is
  never shown, only the menu handle is created. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, Menus, ActnList, TestKit, KeyStateKit;

type
  TSecForm = class(TForm)
  published
    MainMenu1: TMainMenu;
    Item1: TMenuItem;
    ActionList1: TActionList;
    ActSave: TAction;
    ActPlain: TAction;
    procedure ActSaveExecute(Sender: TObject);
    procedure ActPlainExecute(Sender: TObject);
  end;

{$FORM Act_SecondaryShortCuts.form}

var
  Log: string;

procedure TSecForm.ActSaveExecute(Sender: TObject);
begin
  Log := Log + 'save;';
end;

procedure TSecForm.ActPlainExecute(Sender: TObject);
begin
  Log := Log + 'plain;';
end;

{ the key through the shortcut search of F; Alt as bit 29 of the key data }
function Key(F: TCustomForm; VK: Word; Shift, Ctrl, Alt: Boolean): Boolean;
var
  Msg: TWMKey;
begin
  Log := '';
  SetKeyState(Shift, Ctrl);
  FillChar(Msg, SizeOf(Msg), 0);
  Msg.Msg := WM_KEYDOWN;
  Msg.CharCode := VK;
  if Alt then
  begin
    Msg.Msg := WM_SYSKEYDOWN;
    Msg.KeyData := $20000000;
  end;
  Result := F.IsShortCut(Msg);
  SetKeyState(False, False);
end;

function NativeText(Menu: HMENU; Command: Word): string;
var
  Info: TMenuItemInfoW;
  Buffer: array[0..127] of WideChar;
begin
  Result := '';
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := MIIM_STRING;
  Info.dwTypeData := @Buffer[0];
  Info.cch := Length(Buffer);
  if GetMenuItemInfoW(Menu, Command, False, Info) then
    SetString(Result, PWideChar(@Buffer[0]), Info.cch);
end;

procedure Run;
var
  F: TSecForm;
  L, Other: TShortCutList;
  Text: string;
begin
  F := TSecForm.Create(nil);
  try
    Section('read from the form (UIF-1365, UIF-1366)');
    L := F.ActSave.SecondaryShortCuts;
    CheckEqS('TShortCutList', L.ClassName, 'the class of the list');
    CheckEqI(4, L.Count, 'four lines');
    CheckEqS('Ctrl+Ins', L[0], 'line 0 as written');
    CheckEqS('shift + f2', L[1], 'line 1 as written');
    CheckEqS('Alt+F7', L[2], 'line 2 as written');
    CheckEqS('Ctrl+Nothing', L[3], 'line 3 as written');
    CheckEqI(scCtrl or VK_INSERT, L.ShortCuts[0], 'value of line 0');
    CheckEqI(scShift or VK_F2, L.ShortCuts[1], 'value of line 1: any case, spaces around +');
    CheckEqI(scAlt or VK_F7, L.ShortCuts[2], 'value of line 2');
    CheckEqI(0, L.ShortCuts[3], 'a line that is not a shortcut text has the value 0');
    CheckEqI(1, L.IndexOfShortCut(scShift or VK_F2), 'IndexOfShortCut');
    CheckEqI(-1, L.IndexOfShortCut(scCtrl or VK_F2), 'IndexOfShortCut: not in the list');
    CheckEqI(-1, L.IndexOfShortCut(0), 'IndexOfShortCut(0): the unknown line never matches');
    Check(L = F.ActSave.SecondaryShortCuts, 'every read gives the same list');
    CheckEqI(16467, F.ActSave.ShortCut, 'the primary shortcut');
    Other := F.ActPlain.SecondaryShortCuts;
    Check(Other <> nil, 'an action without the property in the form: the list is created on first read');
    CheckEqI(0, Other.Count, 'and is empty');

    Section('keys (UIF-1367)');
    CheckEqB(True, Key(F, Ord('S'), False, True, False), 'Ctrl+S, the primary shortcut');
    CheckEqS('save;', Log, 'executed once (through the linked menu item)');
    CheckEqB(True, Key(F, VK_INSERT, False, True, False), 'Ctrl+Ins, a secondary shortcut');
    CheckEqS('save;', Log, 'executed once');
    CheckEqB(True, Key(F, VK_F2, True, False, False), 'Shift+F2, a secondary shortcut');
    CheckEqS('save;', Log, 'executed once');
    CheckEqB(True, Key(F, VK_F7, False, False, True), 'Alt+F7, a secondary shortcut');
    CheckEqS('save;', Log, 'executed once');
    CheckEqB(False, Key(F, VK_F2, False, False, False), 'F2 without Shift is not in the list');
    CheckEqB(False, Key(F, VK_INSERT, True, True, False), 'Ctrl+Shift+Ins is not in the list');
    CheckEqB(False, Key(F, Ord('N'), False, True, False), 'Ctrl+N: the unknown line matches no key');
    CheckEqS('', Log, 'nothing ran');

    Section('the action must be able to run (UIF-1367, UIF-923)');
    F.ActSave.Enabled := False;
    CheckEqB(False, Key(F, VK_INSERT, False, True, False), 'disabled: the secondary key is not consumed');
    CheckEqS('', Log, 'nothing ran');
    F.ActSave.Enabled := True;
    F.ActionList1.State := asSuspended;
    CheckEqB(False, Key(F, VK_INSERT, False, True, False), 'suspended list: not consumed');
    F.ActionList1.State := asNormal;
    CheckEqB(True, Key(F, VK_INSERT, False, True, False), 'normal again');

    Section('assignment and order (UIF-1365, UIF-925)');
    F.ActPlain.SecondaryShortCuts := L;
    Check(F.ActPlain.SecondaryShortCuts = Other, 'assignment keeps the action''s own list');
    CheckEqS(L.Text, Other.Text, 'and copies the lines');
    CheckEqB(True, Key(F, VK_INSERT, False, True, False), 'both actions have Ctrl+Ins');
    CheckEqS('save;', Log, 'the first action in index order runs');
    L.Clear;
    CheckEqI(4, Other.Count, 'clearing one list leaves the other');
    CheckEqB(True, Key(F, VK_INSERT, False, True, False), 'Ctrl+Ins again');
    CheckEqS('plain;', Log, 'now the second action runs');
    Other.Clear;
    Other.Add('Ctrl+S');
    CheckEqB(True, Key(F, Ord('S'), False, True, False), 'a secondary key equal to another action''s primary key');
    CheckEqS('save;', Log, 'the menu item with the primary shortcut comes first');
    F.ActSave.ShortCut := 0;
    CheckEqB(True, Key(F, Ord('S'), False, True, False), 'the primary shortcut removed');
    CheckEqS('plain;', Log, 'the secondary key of the other action runs it');
    F.ActSave.ShortCut := 16467;

    Section('clients show the primary shortcut only (UIF-1367, UIF-934)');
    L.Add('Ctrl+Ins');
    CheckEqI(16467, F.Item1.ShortCut, 'the menu item has the primary shortcut');
    Text := NativeText(F.MainMenu1.Handle, F.Item1.Command);
    CheckEqS('Save'#9'Ctrl+S', Text, 'the native item text: caption, tab, primary shortcut text');
  finally
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Run;
  Done;
end.
