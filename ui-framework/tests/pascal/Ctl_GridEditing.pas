// covers: UIF-844, UIF-846, UIF-847, UIF-886
// needs: desktop
program Ctl_GridEditing;
{ U-092, controls-standard.md section 27: the in-place editor of UIF-846 (a border-less child TEdit with Owner and
  Parent = the grid and an empty Name, UIF-886, over CellRect(Col, Row)); starting it with F2, Enter and a typed
  character (UIF-844); its text from OnGetEditText (default the cell text); committing with Enter, focus leaving
  the editor, arrows at the text boundary, Up / Down, Tab (goTabs) and a click on another cell, each firing
  OnSetEditText (before OnSelectCell of the move) and storing the cell; Escape cancelling; CM_WANTSPECIALKEY of
  the editor and the grid; EditorMode in code; goRowSelect disabling editing; goAlwaysShowEditor keeping the editor
  open on the focused cell; the editor following scrolling; column / row sizing of UIF-847 by dragging a line in
  the fixed row / column (cursor crVSplit / crHSplit over the line, minimum 0, WM_CANCELMODE restoring the size);
  streaming of the editing events; 100 instances with an open editor leak no GUI objects. Keys and mouse input are
  window messages sent to the grid and to its editor (the hidden form takes no focus). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, Grids, TestKit;

type
  TLog = class
  public
    Text: string;
    GetValue: string;
    procedure GetEditText(Sender: TObject; ACol, ARow: Integer; var Value: string);
    procedure SetEditText(Sender: TObject; ACol, ARow: Integer; const Value: string);
    procedure SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
  end;

  TEditForm = class(TForm)
  published
    Grid: TStringGrid;
    procedure GridGetEditText(Sender: TObject; ACol, ARow: Integer; var Value: string);
    procedure GridSetEditText(Sender: TObject; ACol, ARow: Integer; const Value: string);
  end;

{$FORM Ctl_GridEditing.form}

var
  Log: TLog;

procedure TLog.GetEditText(Sender: TObject; ACol, ARow: Integer; var Value: string);
begin
  Text := Text + Format('get %d,%d=%s;', [ACol, ARow, Value]);
  if GetValue <> '' then
    Value := GetValue;
end;

procedure TLog.SetEditText(Sender: TObject; ACol, ARow: Integer; const Value: string);
begin
  Text := Text + Format('set %d,%d=%s;', [ACol, ARow, Value]);
  { the event comes before the cell is stored }
  Text := Text + 'cell=' + TStringGrid(Sender).Cells[ACol, ARow] + ';';
end;

procedure TLog.SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
begin
  Text := Text + Format('sel %d,%d;', [ACol, ARow]);
end;

procedure TEditForm.GridGetEditText(Sender: TObject; ACol, ARow: Integer; var Value: string);
begin
  Value := 'streamed';
end;

procedure TEditForm.GridSetEditText(Sender: TObject; ACol, ARow: Integer; const Value: string);
begin
  Log.Text := Log.Text + 'streamed set ' + Value + ';';
end;

function BoundsStr(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function CellBoundsStr(G: TStringGrid; ACol, ARow: Integer): string;
var
  R: TRect;
begin
  R := G.CellRect(ACol, ARow);
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top]);
end;

function FocusStr(G: TStringGrid): string;
begin
  Result := Format('%d,%d', [G.Col, G.Row]);
end;

procedure SetKeys(Shift: Boolean);
var
  State: TKeyboardState;
begin
  GetKeyboardState(State);
  if Shift then
    State[VK_SHIFT] := $80
  else
    State[VK_SHIFT] := 0;
  State[VK_CONTROL] := 0;
  SetKeyboardState(State);
end;

procedure Key(C: TWinControl; VK: Word);
begin
  SendMessageW(C.Handle, WM_KEYDOWN, VK, 1);
  SendMessageW(C.Handle, WM_KEYUP, VK, LPARAM($C0000001));
end;

{ a typed letter or digit as the keyboard delivers it: key down, the translated character, key up }
procedure Char(C: TWinControl; Ch: WideChar);
var
  VK: Word;
begin
  VK := Ord(UpCase(Ch));
  SendMessageW(C.Handle, WM_KEYDOWN, VK, 1);
  SendMessageW(C.Handle, WM_CHAR, Ord(Ch), 1);
  SendMessageW(C.Handle, WM_KEYUP, VK, LPARAM($C0000001));
end;

function Editor(G: TStringGrid): TEdit;
begin
  Result := G.InplaceEditor;
end;

function EditorShown(G: TStringGrid): Boolean;
begin
  Result := (G.InplaceEditor <> nil) and G.InplaceEditor.HandleAllocated
    and ((GetWindowLongPtrW(G.InplaceEditor.Handle, GWL_STYLE) and WS_VISIBLE) <> 0);
end;

procedure Down(G: TWinControl; X, Y: Integer);
begin
  SendMessageW(G.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(X, Y));
end;

procedure Drag(G: TWinControl; X, Y: Integer);
begin
  SendMessageW(G.Handle, WM_MOUSEMOVE, MK_LBUTTON, MakeLParam(X, Y));
end;

procedure Hover(G: TWinControl; X, Y: Integer);
begin
  SendMessageW(G.Handle, WM_MOUSEMOVE, 0, MakeLParam(X, Y));
end;

procedure Up(G: TWinControl; X, Y: Integer);
begin
  SendMessageW(G.Handle, WM_LBUTTONUP, 0, MakeLParam(X, Y));
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  G, S: TStringGrid;
  SF: TEditForm;
  E: TEdit;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 600;
    F.ClientHeight := 400;
    G := TStringGrid.Create(F);
    G.BorderStyle := bsNone;
    G.SetBounds(0, 0, 330, 130);
    G.ColCount := 6;
    G.RowCount := 6;
    G.DefaultColWidth := 40;
    G.DefaultRowHeight := 20;
    G.Parent := F;
    G.OnGetEditText := Log.GetEditText;
    G.OnSetEditText := Log.SetEditText;
    G.OnSelectCell := Log.SelectCell;
    G.Cells[2, 2] := 'abc';
    SetKeys(False);

    Section('defaults');
    CheckEqB(False, G.EditorMode, 'EditorMode False');
    Check(G.InplaceEditor = nil, 'no editor before the first edit');
    CheckEqI(0, G.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'without goEditing Enter is the form''s');
    G.Col := 2;
    G.Row := 2;
    Key(G, VK_F2);
    CheckEqB(False, G.EditorMode, 'without goEditing F2 does nothing');
    G.Options := G.Options + [goEditing];
    Check(G.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0) <> 0, 'goEditing: the grid wants Enter');
    CheckEqB(True, (SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTCHARS) <> 0,
      'goEditing: WM_GETDLGCODE wants characters');

    Section('the editor (UIF-846, UIF-886)');
    Log.Text := '';
    Key(G, VK_F2);
    CheckEqB(True, G.EditorMode, 'F2 starts editing');
    CheckEqS('get 2,2=abc;', Log.Text, 'OnGetEditText with the cell text');
    E := Editor(G);
    Check(E <> nil, 'editor created');
    Check(E is TEdit, 'a TEdit');
    Check(E.Owner = G, 'owned by the grid');
    Check(E.Parent = G, 'child of the grid');
    CheckEqS('', E.Name, 'no Name');
    CheckEqB(True, E.BorderStyle = bsNone, 'border-less');
    CheckEqB(False, E.TabStop, 'not a tab stop');
    CheckEqB(True, EditorShown(G), 'editor window shown');
    CheckEqS(CellBoundsStr(G, 2, 2), BoundsStr(E), 'over CellRect(Col, Row)');
    CheckEqS('82,42,40,20', BoundsStr(E), 'cell (2, 2)');
    CheckEqS('abc', E.Text, 'editor text');
    CheckEqI(3, E.SelLength, 'text selected');
    Check(E.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0) <> 0, 'editor wants Enter');
    Check(E.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0) <> 0, 'editor wants Escape');
    CheckEqI(0, E.Perform(CM_WANTSPECIALKEY, VK_TAB, 0), 'without goTabs the editor leaves Tab');
    Char(E, 'x');
    CheckEqS('x', E.Text, 'typing replaces the selected text');
    Log.Text := '';
    Key(E, VK_RETURN);
    CheckEqS('set 2,2=x;cell=abc;', Log.Text, 'Enter commits: OnSetEditText before the cell is stored');
    CheckEqS('x', G.Cells[2, 2], 'cell stored');
    CheckEqB(False, G.EditorMode, 'Enter ends editing');
    CheckEqB(False, EditorShown(G), 'editor hidden');

    Section('Enter, Escape, typing (UIF-844)');
    Key(G, VK_RETURN);
    CheckEqB(True, G.EditorMode, 'Enter on the grid starts editing');
    CheckEqS('x', E.Text, 'the cell text');
    Char(E, 'q');
    Log.Text := '';
    Key(E, VK_ESCAPE);
    CheckEqB(False, G.EditorMode, 'Escape ends editing');
    CheckEqS('', Log.Text, 'Escape: no OnSetEditText');
    CheckEqS('x', G.Cells[2, 2], 'Escape: cell unchanged');
    Log.Text := '';
    Char(G, 'k');
    CheckEqB(True, G.EditorMode, 'a typed character starts editing');
    CheckEqS('k', E.Text, 'replacing the text');
    CheckEqI(1, E.SelStart, 'caret after the character');
    CheckEqI(0, E.SelLength, 'nothing selected');
    Key(E, VK_RETURN);
    CheckEqS('k', G.Cells[2, 2], 'typed text committed');
    Log.GetValue := 'from event';
    Key(G, VK_F2);
    CheckEqS('from event', E.Text, 'OnGetEditText can change the text');
    Key(E, VK_ESCAPE);
    Log.GetValue := '';

    Section('arrows and focus loss commit (UIF-846)');
    Key(G, VK_F2);
    Key(E, VK_LEFT);
    CheckEqB(True, G.EditorMode, 'Left with selected text: stays in the editor');
    CheckEqI(0, E.SelStart, 'caret moved to the start');
    Log.Text := '';
    Key(E, VK_LEFT);
    CheckEqS('set 2,2=k;cell=k;sel 1,2;', Log.Text, 'Left at the start: commit, then the move');
    CheckEqS('1,2', FocusStr(G), 'moved left');
    CheckEqB(False, G.EditorMode, 'editor closed');
    Key(G, VK_F2);
    Log.Text := '';
    Key(E, VK_RIGHT);
    CheckEqS('set 1,2=;cell=;sel 2,2;', Log.Text, 'Right at the end (empty text): commit and move');
    Key(G, VK_F2);
    E.SelStart := 0;
    Key(E, VK_RIGHT);
    CheckEqB(True, G.EditorMode, 'Right inside the text: stays');
    Log.Text := '';
    Key(E, VK_DOWN);
    CheckEqS('set 2,2=k;cell=k;sel 2,3;', Log.Text, 'Down commits and moves');
    Key(G, VK_F2);
    Key(E, VK_UP);
    CheckEqS('2,2', FocusStr(G), 'Up commits and moves');
    Key(G, VK_F2);
    Char(E, 'z');
    Log.Text := '';
    SendMessageW(E.Handle, WM_KILLFOCUS, 0, 0);
    CheckEqS('set 2,2=z;cell=k;', Log.Text, 'focus leaving the editor commits');
    CheckEqB(False, G.EditorMode, 'and closes it');
    CheckEqS('z', G.Cells[2, 2], 'stored');
    Key(G, VK_F2);
    Char(E, 'm');
    Log.Text := '';
    SendMessageW(G.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(41 * 4 + 20, 21 * 4 + 10));
    SendMessageW(G.Handle, WM_LBUTTONUP, 0, MakeLParam(41 * 4 + 20, 21 * 4 + 10));
    CheckEqS('set 2,2=m;cell=z;sel 4,4;', Log.Text, 'a click on another cell commits first');
    CheckEqB(False, G.EditorMode, 'click: editor closed');
    G.Options := G.Options + [goTabs];
    Key(G, VK_F2);
    Check(E.Perform(CM_WANTSPECIALKEY, VK_TAB, 0) <> 0, 'goTabs: the editor wants Tab');
    Char(E, 'w');
    Log.Text := '';
    Key(E, VK_TAB);
    CheckEqS('set 4,4=w;cell=;sel 5,4;', Log.Text, 'Tab commits and moves to the next cell');
    G.Options := G.Options - [goTabs];

    Section('EditorMode, goRowSelect');
    G.EditorMode := True;
    CheckEqB(True, EditorShown(G), 'EditorMode True shows the editor');
    Char(E, 'n');
    Log.Text := '';
    G.EditorMode := False;
    CheckEqS('set 5,4=n;cell=;', Log.Text, 'EditorMode False commits');
    CheckEqB(False, EditorShown(G), 'and hides');
    G.Options := G.Options + [goRowSelect];
    Key(G, VK_F2);
    CheckEqB(False, G.EditorMode, 'goRowSelect: no editing');
    G.EditorMode := True;
    CheckEqB(False, G.EditorMode, 'goRowSelect: EditorMode stays False');
    CheckEqI(0, G.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'goRowSelect: Enter is the form''s');
    G.Options := G.Options - [goRowSelect];

    Section('goAlwaysShowEditor (UIF-846)');
    G.Col := 1;
    G.Row := 1;
    G.Cells[1, 1] := 'one';
    G.Cells[1, 2] := 'two';
    G.Options := G.Options + [goAlwaysShowEditor];
    CheckEqB(True, G.EditorMode, 'the editor opens at once');
    CheckEqS('one', E.Text, 'on the focused cell');
    Char(E, '1');
    Log.Text := '';
    Key(E, VK_RETURN);
    CheckEqS('set 1,1=1;cell=one;', Log.Text, 'Enter commits');
    CheckEqB(True, G.EditorMode, 'and the editor stays');
    Log.Text := '';
    Key(E, VK_DOWN);
    CheckEqS('set 1,1=1;cell=1;sel 1,2;get 1,2=two;', Log.Text, 'Down: commit, move, reopen on the new cell');
    CheckEqB(True, EditorShown(G), 'shown on the new cell');
    CheckEqS(CellBoundsStr(G, 1, 2), BoundsStr(E), 'over the new cell');
    CheckEqS('two', E.Text, 'new text');
    Char(E, 'Q');
    Key(E, VK_ESCAPE);
    CheckEqS('two', E.Text, 'Escape restores the text');
    CheckEqB(True, G.EditorMode, 'Escape keeps the editor open');
    G.Options := G.Options - [goAlwaysShowEditor];
    CheckEqB(False, G.EditorMode, 'option off: editor closed');

    Section('scrolling moves the editor');
    G.ColCount := 20;
    G.Col := 3;
    G.Row := 2;
    G.EditorMode := True;
    CheckEqS('123,42,40,20', BoundsStr(E), 'cell (3, 2)');
    G.LeftCol := 2;
    CheckEqS('82,42,40,20', BoundsStr(E), 'follows LeftCol');
    G.ColWidths[2] := 10;
    CheckEqS('52,42,40,20', BoundsStr(E), 'follows a width change');
    G.ColWidths[3] := 60;
    CheckEqS('52,42,60,20', BoundsStr(E), 'takes its own column width');
    G.EditorMode := False;
    G.Free;

    Section('column and row sizing (UIF-847)');
    S := TStringGrid.Create(F);
    S.BorderStyle := bsNone;
    S.SetBounds(0, 0, 330, 130);
    S.ColCount := 6;
    S.RowCount := 6;
    S.DefaultColWidth := 40;
    S.DefaultRowHeight := 20;
    S.Parent := F;
    S.OnSelectCell := Log.SelectCell;
    Hover(S, 81, 10);
    CheckEqI(crDefault, EffectiveCursor(S), 'without goColSizing: no sizing cursor');
    S.Options := S.Options + [goColSizing, goRowSizing];
    Hover(S, 81, 10);
    CheckEqI(crVSplit, EffectiveCursor(S), 'over a column line in the fixed row: crVSplit');
    Hover(S, 79, 10);
    CheckEqI(crVSplit, EffectiveCursor(S), 'near the line');
    Hover(S, 60, 10);
    CheckEqI(crDefault, EffectiveCursor(S), 'inside a fixed cell: default');
    Hover(S, 81, 50);
    CheckEqI(crDefault, EffectiveCursor(S), 'column line outside the fixed row: default');
    Hover(S, 20, 41);
    CheckEqI(crHSplit, EffectiveCursor(S), 'over a row line in the fixed column: crHSplit');
    Log.Text := '';
    Down(S, 81, 10);
    Drag(S, 100, 10);
    CheckEqI(59, S.ColWidths[1], 'dragging resizes the column');
    Drag(S, 20, 10);
    CheckEqI(0, S.ColWidths[1], 'minimum 0');
    Up(S, 61, 10);
    CheckEqI(20, S.ColWidths[1], 'released: the final width');
    CheckEqS('', Log.Text, 'sizing selects no cell');
    CheckEqS('1,1', FocusStr(S), 'focused cell unchanged');
    Drag(S, 150, 10);
    CheckEqI(20, S.ColWidths[1], 'after the release: no sizing');
    Down(S, 20, 41);
    Drag(S, 20, 60);
    Up(S, 20, 60);
    CheckEqI(39, S.RowHeights[1], 'dragging resizes the row');
    Down(S, 102, 10);
    Drag(S, 150, 10);
    CheckEqI(88, S.ColWidths[2], 'second column follows its new left edge');
    SendMessageW(S.Handle, WM_CANCELMODE, 0, 0);
    CheckEqI(40, S.ColWidths[2], 'WM_CANCELMODE restores the width');
    Drag(S, 200, 10);
    CheckEqI(40, S.ColWidths[2], 'cancelled: no more sizing');
    Up(S, 200, 10);
    S.Options := S.Options - [goColSizing];
    Down(S, 81 - 40 + 20 + 1, 10);
    Drag(S, 150, 10);
    Up(S, 150, 10);
    CheckEqI(20, S.ColWidths[1], 'without goColSizing: no sizing');
    S.Free;

    Section('streaming');
    Log.Text := '';
    SF := TEditForm.Create(nil);
    try
      CheckEqB(True, SF.Grid.Options = [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goColSizing,
        goEditing], 'Options');
      Check(Assigned(SF.Grid.OnGetEditText), 'OnGetEditText bound');
      Check(Assigned(SF.Grid.OnSetEditText), 'OnSetEditText bound');
      SF.Grid.EditorMode := True;
      CheckEqS('streamed', SF.Grid.InplaceEditor.Text, 'the form''s OnGetEditText');
      SF.Grid.EditorMode := False;
      CheckEqS('streamed set streamed;', Log.Text, 'the form''s OnSetEditText');
      CheckEqS('streamed', SF.Grid.Cells[1, 1], 'stored');
      CheckEqI(1, SF.ComponentCount, 'the editor is not a component of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      G := TStringGrid.Create(nil);
      G.Options := G.Options + [goEditing];
      G.Parent := F;
      G.Cells[1, 1] := 'leak';
      G.EditorMode := True;
      G.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with an open editor');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
