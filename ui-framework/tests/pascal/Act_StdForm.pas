// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1365, UIF-1368, UIF-1374, UIF-1380
// phase: 8
program Act_StdForm;
{ U-153, standard-actions.md sections 5 and 6: a form as the designer stores it - an action list with one action
  of every standard class, each with the texts the design metadata gives a new action (caption, category, hint,
  shortcut; the constructors set none), a main menu and a tool bar whose items are linked to the actions. The
  form loads; every action has its class and its stored values; the captions, hints and shortcuts arrive at the
  menu items and the tool buttons; a component reference (Control), a string list (SecondaryShortCuts) and an
  event of a file action (OnAccept) are read; the dialog of a file action is not in the form and exists. Keys go
  through TCustomForm.IsShortCut: Ctrl+O runs the open dialog (replaced by Dialogs.CommonDialogHook), Ctrl+A
  selects the text of the memo named by Control. Needs no desktop: the form is never shown.
  That the form written back equals the file is proved by the designer (the run-time library has no form
  writer). }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls, Menus, Dialogs, CommDlg, ActnList,
  StdActns, TestKit, KeyStateKit;

type
  TStdForm = class(TForm)
  published
    ToolBar1: TToolBar;
    ToolButton1: TToolButton;
    ToolButton2: TToolButton;
    ToolButton3: TToolButton;
    ToolButton4: TToolButton;
    ToolButton5: TToolButton;
    ToolButton6: TToolButton;
    ToolButton7: TToolButton;
    ToolButton8: TToolButton;
    ToolButton9: TToolButton;
    Memo1: TMemo;
    MainMenu1: TMainMenu;
    FileMenu: TMenuItem;
    FileItem1: TMenuItem;
    FileItem2: TMenuItem;
    FileItem3: TMenuItem;
    EditMenu: TMenuItem;
    EditItem1: TMenuItem;
    EditItem2: TMenuItem;
    EditItem3: TMenuItem;
    EditItem4: TMenuItem;
    EditItem5: TMenuItem;
    EditItem6: TMenuItem;
    ActionList1: TActionList;
    EditCut1: TEditCut;
    EditCopy1: TEditCopy;
    EditPaste1: TEditPaste;
    EditSelectAll1: TEditSelectAll;
    EditUndo1: TEditUndo;
    EditDelete1: TEditDelete;
    FileOpen1: TFileOpen;
    FileSaveAs1: TFileSaveAs;
    FileExit1: TFileExit;
    procedure FileOpen1Accept(Sender: TObject);
  end;

{$FORM Act_StdForm.form}

var
  Log: string;

procedure TStdForm.FileOpen1Accept(Sender: TObject);
begin
  Log := Log + 'accept:' + FileOpen1.Dialog.FileName + ';';
end;

function TestHook(Dialog: TCommonDialog; ParentWnd: HWND; Data: Pointer; var ExtendedError: DWORD): Boolean;
const
  Picked = 'C:\data\a.txt';
var
  O: POpenFilenameW;
  I: Integer;
begin
  Log := Log + Dialog.ClassName + ';';
  O := POpenFilenameW(Data);
  for I := 1 to Length(Picked) do
    O^.lpstrFile[I - 1] := Picked[I];
  O^.lpstrFile[Length(Picked)] := #0;
  O^.lpstrFile[Length(Picked) + 1] := #0;
  ExtendedError := 0;
  Result := True;
end;

{ Ctrl+VK through the shortcut search of F }
function CtrlKey(F: TCustomForm; VK: Word): Boolean;
var
  Msg: TWMKey;
begin
  Log := '';
  SetKeyState(False, True);
  FillChar(Msg, SizeOf(Msg), 0);
  Msg.Msg := WM_KEYDOWN;
  Msg.CharCode := VK;
  Result := F.IsShortCut(Msg);
  SetKeyState(False, False);
end;

{ one action with its two clients: the stored values, and what the links gave the clients }
procedure CheckAction(A: TAction; const AClass, ACaption, ACategory, AHint, AShortCutText: string;
  AShortCut: TShortCut; Item: TMenuItem; Button: TToolButton);
begin
  Section(A.Name);
  CheckEqS(AClass, A.ClassName, 'class');
  CheckEqS(ACaption, A.Caption, 'Caption');
  CheckEqS(ACategory, A.Category, 'Category');
  CheckEqS(AHint, A.Hint, 'Hint');
  CheckEqI(AShortCut, A.ShortCut, 'ShortCut');
  CheckEqS(AShortCutText, ShortCutToText(A.ShortCut), 'the text of the shortcut');
  CheckEqB(True, A.DisableIfNoHandler, 'DisableIfNoHandler');
  Check(Item.Action = A, 'the menu item is linked');
  CheckEqS(ACaption, Item.Caption, 'menu item: Caption');
  CheckEqI(AShortCut, Item.ShortCut, 'menu item: ShortCut');
  CheckEqS(AHint, Item.Hint, 'menu item: Hint');
  Check(Button.Action = A, 'the tool button is linked');
  CheckEqS(ACaption, Button.Caption, 'tool button: Caption');
  CheckEqS(AHint, Button.Hint, 'tool button: Hint');
end;

procedure Run;
var
  F: TStdForm;
begin
  F := TStdForm.Create(nil);
  try
    Section('the form');
    CheckEqS('Standard actions', F.Caption, 'loaded');
    CheckEqI(9, F.ActionList1.ActionCount, 'nine actions');
    Check(F.ActionList1.Actions[0] = F.EditCut1, 'the first action');
    Check(F.ActionList1.Actions[8] = F.FileExit1, 'the last action');
    Check(F.Menu = F.MainMenu1, 'the main menu of the form');
    CheckEqI(9, F.ToolBar1.ControlCount, 'nine tool buttons');

    CheckAction(F.EditCut1, 'TEditCut', 'Cu&t', 'Edit', 'Cut', 'Ctrl+X', 16472, F.EditItem1, F.ToolButton1);
    CheckAction(F.EditCopy1, 'TEditCopy', '&Copy', 'Edit', 'Copy', 'Ctrl+C', 16451, F.EditItem2, F.ToolButton2);
    CheckAction(F.EditPaste1, 'TEditPaste', '&Paste', 'Edit', 'Paste', 'Ctrl+V', 16470, F.EditItem3, F.ToolButton3);
    CheckAction(F.EditSelectAll1, 'TEditSelectAll', 'Select &All', 'Edit', 'Select all', 'Ctrl+A', 16449,
      F.EditItem4, F.ToolButton4);
    CheckAction(F.EditUndo1, 'TEditUndo', '&Undo', 'Edit', 'Undo', 'Ctrl+Z', 16474, F.EditItem5, F.ToolButton5);
    CheckAction(F.EditDelete1, 'TEditDelete', '&Delete', 'Edit', 'Delete', 'Del', 46, F.EditItem6, F.ToolButton6);
    CheckAction(F.FileOpen1, 'TFileOpen', '&Open'#$2026, 'File', 'Open a file', 'Ctrl+O', 16463, F.FileItem1,
      F.ToolButton7);
    CheckAction(F.FileSaveAs1, 'TFileSaveAs', 'Save &As'#$2026, 'File', 'Save under another name', '', 0,
      F.FileItem2, F.ToolButton8);
    CheckAction(F.FileExit1, 'TFileExit', 'E&xit', 'File', 'Close the application', '', 0, F.FileItem3,
      F.ToolButton9);

    Section('other stored members (UIF-1365, UIF-1368, UIF-1374)');
    Check(F.EditSelectAll1.Control = F.Memo1, 'Control: the component reference');
    Check(F.EditCut1.Control = nil, 'Control not in the form: nil');
    CheckEqI(1, F.EditCopy1.SecondaryShortCuts.Count, 'SecondaryShortCuts of a standard action');
    CheckEqI(scCtrl or VK_INSERT, F.EditCopy1.SecondaryShortCuts.ShortCuts[0], 'its value');
    CheckEqB(True, Assigned(F.FileOpen1.OnAccept), 'OnAccept: the method of the form');
    CheckEqB(False, Assigned(F.FileOpen1.BeforeExecute), 'BeforeExecute not in the form');
    CheckEqB(False, Assigned(F.FileSaveAs1.OnAccept), 'no event of the save action');
    CheckEqS('TOpenDialog', F.FileOpen1.Dialog.ClassName, 'the dialog of the open action exists');
    CheckEqS('', F.FileOpen1.Dialog.Filter, 'with its defaults: it is not in the form');
    CheckEqS('TSaveDialog', F.FileSaveAs1.Dialog.ClassName, 'the dialog of the save action exists');

    Section('keys (UIF-1375, UIF-1370)');
    CommonDialogHook := TestHook;
    CheckEqB(True, CtrlKey(F, Ord('O')), 'Ctrl+O is consumed');
    CheckEqS('TOpenDialog;accept:C:\data\a.txt;', Log, 'the open dialog, then the handler of the form');
    CommonDialogHook := nil;
    F.Memo1.Text := 'some text';
    F.Memo1.SelStart := 0;
    F.Memo1.SelLength := 0;
    CheckEqB(True, CtrlKey(F, Ord('A')), 'Ctrl+A is consumed');
    CheckEqI(9, F.Memo1.SelLength, 'the text of the memo is selected');
    CheckEqB(False, CtrlKey(F, Ord('X')), 'Ctrl+X: no edit has the focus, not consumed');
    CheckEqB(False, F.EditCut1.Enabled, 'the cut action is disabled');
    CheckEqB(False, F.EditItem1.Enabled, 'and its menu item');
    CheckEqB(False, F.ToolButton1.Enabled, 'and its tool button');
    CheckEqB(False, CtrlKey(F, Ord('Q')), 'Ctrl+Q belongs to no action');
  finally
    CommonDialogHook := nil;
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Run;
  Done;
end.
