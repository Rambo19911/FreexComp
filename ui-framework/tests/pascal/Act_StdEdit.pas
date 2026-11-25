// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1368, UIF-1369, UIF-1370, UIF-1371, UIF-1373
// phase: 8
program Act_StdEdit;
{ U-152, standard-actions.md section 4: the edit actions of unit StdActns with Control set, for a TEdit and for a
  TMemo. Update gives the Enabled column of the table (empty text, text without a selection, a selection,
  ReadOnly, after an edit); Execute performs the operation, read back from the control; a disabled action does
  nothing. With Control set the action does not depend on the focus (no form is shown or active here); without
  Control and without an active control the action is disabled. Constructors set no texts. Clients that are
  updated on a hidden form (a tool button, a main-menu bar item) follow Enabled after UpdateActions; destroying
  the control clears Control.
  The clipboard: the program copies its own text first and asserts on that text only (read back by pasting);
  every operation that needs the clipboard goes through ClipKit, which repeats it while another program has the
  clipboard open.
  The form is never shown; the edit windows exist (the selection, undo and clipboard operations are theirs). }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, ComCtrls, Menus, ActnList, StdActns, TestKit,
  ClipKit;

type
  TEditAccess = class(TCustomEdit);
  TControlAccess = class(TControl);

var
  F: TForm;
  List: TActionList;
  Cut: TEditCut;
  Copy: TEditCopy;
  Paste: TEditPaste;
  SelectAll: TEditSelectAll;
  Undo: TEditUndo;
  Delete: TEditDelete;

procedure UpdateAll;
begin
  Cut.Update;
  Copy.Update;
  Paste.Update;
  SelectAll.Update;
  Undo.Update;
  Delete.Update;
end;

procedure Select(C: TCustomEdit; AStart, ALength: Integer);
begin
  C.SelStart := AStart;
  C.SelLength := ALength;
  UpdateAll;
end;

function Letter(B: Boolean): string;
begin
  if B then
    Result := 'T'
  else
    Result := 'F';
end;

{ the Enabled values of cut, copy, select all, undo, delete as five letters (T or F) }
function States: string;
begin
  Result := Letter(Cut.Enabled) + Letter(Copy.Enabled) + Letter(SelectAll.Enabled) + Letter(Undo.Enabled)
    + Letter(Delete.Enabled);
end;

procedure Scenario(C: TCustomEdit; const Kind: string);
begin
  Section(Kind + ': Enabled after Update (UIF-1370, UIF-1371)');
  Cut.Control := C;
  Copy.Control := C;
  Paste.Control := C;
  SelectAll.Control := C;
  Undo.Control := C;
  Delete.Control := C;
  Check(Copy.Control = C, 'Control assigned');
  CheckEqB(False, Copy.Update, 'Update: no update handler ran');
  C.Text := '';
  UpdateAll;
  CheckEqS('FFFFF', States, 'empty text: cut, copy, select all, undo, delete');
  C.Text := 'hello';
  Select(C, 0, 0);
  CheckEqS('FFTFF', States, 'text without a selection: only select all');
  Select(C, 1, 3);
  CheckEqS('TTTFT', States, 'a selection: cut, copy, select all, delete');
  TEditAccess(C).ReadOnly := True;
  UpdateAll;
  CheckEqS('FTTFF', States, 'ReadOnly: copy and select all only');

  Section(Kind + ': copy and paste (UIF-1371)');
  CheckEqB(True, ExecuteUntilCopied(Copy, 'ell'), 'Copy.Execute (the selection "ell")');
  CheckEqS('ell', ClipboardText('ell'), 'the selection is on the clipboard');
  CheckEqS('hello', C.Text, 'copy leaves the text');
  Paste.Update;
  CheckEqB(False, Paste.Enabled, 'paste: ReadOnly, disabled');
  CheckEqB(False, Paste.Execute, 'paste: Execute False');
  CheckEqS('hello', C.Text, 'nothing pasted');
  TEditAccess(C).ReadOnly := False;
  Select(C, 5, 0);
  CheckEqB(True, Paste.Enabled, 'paste: text on the clipboard and not ReadOnly, enabled');
  CheckEqB(True, ExecuteUntilText(Paste, C, 'helloell'), 'Paste.Execute');
  CheckEqS('helloell', C.Text, 'the copied text pasted at the caret');

  Section(Kind + ': undo (UIF-1371)');
  Undo.Update;
  CheckEqB(True, Undo.Enabled, 'after the paste: CanUndo, enabled');
  CheckEqB(True, Undo.Execute, 'Undo.Execute');
  CheckEqS('hello', C.Text, 'the paste undone');
  C.Text := '';
  C.Text := 'hello';
  Undo.Update;
  CheckEqB(False, Undo.Enabled, 'text set by code: nothing to undo');
  CheckEqB(False, Undo.Execute, 'Execute False');
  Select(C, 5, 0);
  SendMessageW(C.Handle, WM_CHAR, Ord('!'), 1);
  CheckEqS('hello!', C.Text, 'a typed character');
  Undo.Update;
  CheckEqB(True, Undo.Enabled, 'after an edit: enabled');

  Section(Kind + ': cut, delete, select all (UIF-1371)');
  Select(C, 0, 2);
  CheckEqB(True, ExecuteUntilText(Cut, C, 'llo!'), 'Cut.Execute (the selection "he")');
  CheckEqS('llo!', C.Text, 'the selection removed');
  CheckEqI(0, C.SelLength, 'no selection left');
  Select(C, 4, 0);
  CheckEqB(True, ExecuteUntilText(Paste, C, 'llo!he'), 'paste again');
  CheckEqS('llo!he', C.Text, 'the cut text was on the clipboard');
  Select(C, 0, 3);
  CheckEqB(True, Delete.Execute, 'Delete.Execute (the selection "llo")');
  CheckEqS('!he', C.Text, 'the selection removed');
  CheckEqI(0, C.SelLength, 'no selection left');
  Select(C, 3, 0);
  CheckEqB(True, ExecuteUntilText(Paste, C, '!hehe'), 'paste once more');
  CheckEqS('!hehe', C.Text, 'delete left the clipboard alone');
  Select(C, 2, 0);
  CheckEqB(True, SelectAll.Execute, 'SelectAll.Execute');
  CheckEqI(0, C.SelStart, 'selection starts at 0');
  CheckEqI(5, C.SelLength, 'the whole text selected');

  Section(Kind + ': a disabled action does nothing (UIF-1371, UIF-902)');
  Select(C, 1, 0);
  CheckEqB(False, Copy.Execute, 'copy without a selection');
  CheckEqB(False, Cut.Execute, 'cut without a selection');
  CheckEqB(False, Delete.Execute, 'delete without a selection');
  CheckEqS('!hehe', C.Text, 'text unchanged');
  C.Text := '';
  CheckEqB(False, SelectAll.Execute, 'select all in an empty control');
  CheckEqB(False, SelectAll.Enabled, 'Execute updated the action first');
end;

procedure Run;
var
  E, Tmp: TEdit;
  M: TMemo;
  Bar: TToolBar;
  TB: TToolButton;
  MM: TMainMenu;
  Item: TMenuItem;
  Free1: TEditCopy;
  Start: DWORD;
begin
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(100, 100, 400, 300);
    E := TEdit.Create(F);
    E.SetBounds(8, 40, 200, 24);
    E.Parent := F;
    M := TMemo.Create(F);
    M.SetBounds(8, 80, 200, 100);
    M.Parent := F;
    E.HandleNeeded;
    M.HandleNeeded;
    List := TActionList.Create(F);
    Cut := TEditCut.Create(F);
    Cut.ActionList := List;
    Copy := TEditCopy.Create(F);
    Copy.ActionList := List;
    Paste := TEditPaste.Create(F);
    Paste.ActionList := List;
    SelectAll := TEditSelectAll.Create(F);
    SelectAll.ActionList := List;
    Undo := TEditUndo.Create(F);
    Undo.ActionList := List;
    Delete := TEditDelete.Create(F);
    Delete.ActionList := List;

    Section('after the constructor (UIF-1371, UIF-1361)');
    Check(Copy is TEditAction, 'TEditCopy is a TEditAction');
    Check(Copy is TAction, 'and a TAction');
    Check(Copy.Control = nil, 'Control is nil');
    CheckEqS('', Copy.Caption, 'no caption');
    CheckEqS('', Copy.Hint, 'no hint');
    CheckEqI(0, Copy.ShortCut, 'no shortcut');
    CheckEqI(-1, Copy.ImageIndex, 'no image');
    CheckEqS('', Copy.Category, 'no category');
    CheckEqB(True, Copy.Enabled, 'enabled until the first update');
    CheckEqB(True, Cut.DisableIfNoHandler and Copy.DisableIfNoHandler and Paste.DisableIfNoHandler
      and SelectAll.DisableIfNoHandler and Undo.DisableIfNoHandler and Delete.DisableIfNoHandler,
      'DisableIfNoHandler set by every class');
    Check(GetClass('TEditPaste') = TEditPaste, 'the classes are registered');

    Section('HandlesTarget (UIF-1369)');
    CheckEqB(True, Copy.HandlesTarget(E), 'no Control: an edit');
    CheckEqB(True, Copy.HandlesTarget(M), 'no Control: a memo');
    CheckEqB(False, Copy.HandlesTarget(F), 'no Control: a form is not a target');
    CheckEqB(False, Copy.HandlesTarget(List), 'no Control: another component is not a target');
    Copy.Control := E;
    CheckEqB(True, Copy.HandlesTarget(E), 'Control set: that control');
    CheckEqB(False, Copy.HandlesTarget(M), 'Control set: no other edit');
    Copy.Control := nil;

    Section('no Control and no active control (UIF-1361)');
    E.Text := 'abc';
    E.SelStart := 0;
    E.SelLength := 3;
    Check(Screen.ActiveCustomForm = nil, 'no form is active');
    CheckEqB(False, Copy.Update, 'Update');
    CheckEqB(False, Copy.Enabled, 'disabled: no target');
    CheckEqB(False, Copy.Execute, 'Execute False');
    Copy.Control := E;
    Copy.Update;
    CheckEqB(True, Copy.Enabled, 'Control set: enabled without the focus (UIF-1370)');

    Scenario(E, 'TEdit');
    Scenario(M, 'TMemo');

    Section('linked clients (UIF-1373)');
    Bar := TToolBar.Create(F);
    Bar.Parent := F;
    TB := TToolButton.Create(F);
    TB.Parent := Bar;
    TB.Action := Copy;
    MM := TMainMenu.Create(F);
    Item := TMenuItem.Create(F);
    MM.Items.Add(Item);
    Item.Action := Cut;
    F.Menu := MM;
    Copy.Control := E;
    Cut.Control := E;
    E.Text := 'linked';
    E.SelStart := 0;
    E.SelLength := 0;
    F.UpdateActions;
    CheckEqB(False, TB.Enabled, 'no selection: the tool button linked to copy is disabled');
    CheckEqB(False, Item.Enabled, 'and the menu item linked to cut');
    E.SelLength := 4;
    CheckEqB(False, TB.Enabled, 'a selection: nothing changes before the update');
    F.UpdateActions;
    CheckEqB(True, TB.Enabled, 'after UpdateActions: the tool button is enabled');
    CheckEqB(True, Item.Enabled, 'and the menu item');
    TEditAccess(E).ReadOnly := True;
    F.UpdateActions;
    CheckEqB(True, TB.Enabled, 'ReadOnly: copy stays enabled');
    CheckEqB(False, Item.Enabled, 'cut is disabled');
    TEditAccess(E).ReadOnly := False;
    Start := GetTickCount;
    repeat
      TControlAccess(TB).Click;
    until (ClipboardTextNow = 'link') or ClipWaitOver(Start);
    CheckEqS('link', ClipboardText('link'), 'a click on the tool button copied the selection');

    Section('destroying the control (UIF-1368)');
    Tmp := TEdit.Create(nil);
    Free1 := TEditCopy.Create(F);
    Free1.Control := Tmp;
    Cut.Control := Tmp;
    Check((Free1.Control = Tmp) and (Cut.Control = Tmp), 'two actions use the control');
    Tmp.Free;
    Check(Free1.Control = nil, 'Control cleared');
    Check(Cut.Control = nil, 'in every action');
    Check(Copy.Control = E, 'another action keeps its control');
    CheckEqB(True, Free1.HandlesTarget(M), 'and the action takes any edit again');
    Tmp := TEdit.Create(nil);
    Free1.Control := Tmp;
    Free1.Free;
    Tmp.Free;
    Check(True, 'an action freed before its control');
  finally
    F.Free;
  end;
end;

begin
  Application.Initialize;
  Run;
  Done;
end.
