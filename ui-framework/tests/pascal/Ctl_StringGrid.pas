// covers: UIF-840, UIF-842, UIF-843, UIF-844, UIF-845, UIF-848
// needs: desktop
program Ctl_StringGrid;
{ U-091, controls-standard.md section 27: TStringGrid cells (sparse, '' / nil when unset, index checks, shrinking
  discards, UIF-840); the live Cols / Rows views (counts, read / write of strings and objects, Assign, Clear, Add /
  Insert / Delete raise, UIF-848); the default cell text of UIF-842 (2 px inset, clipped to the cell, Font.Color or
  clHighlightText, replaced by OnDrawCell); the focused cell and the selection of UIF-843 (Col / Row in code with
  OnSelectCell and its veto, mouse clicks, drags and Shift+clicks with and without goRangeSelect, goRowSelect); the
  keys of UIF-844 (arrows, Home / End, Ctrl+Home / Ctrl+End, PageUp / PageDown, Tab / Shift+Tab with goTabs and
  CM_WANTSPECIALKEY, Shift+arrows extending a range, OnKeyDown swallowing a key, WM_GETDLGCODE); scrolling that
  keeps the focused cell visible (UIF-845); streaming of OnSelectCell; 100 instances with cells and handles leak no
  GUI objects. The Shift / Ctrl state of keys is set with KeyStateKit (whole thread key state; no real input).
  Mouse input is sent as window messages to the grid of a hidden form (the grid does not take the focus then). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, Grids,
  UIConsts, TestKit, KeyStateKit;

type
  TLog = class
  public
    Text: string;
    Allow: Boolean;
    Swallow: Boolean;
    procedure SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
    procedure TopLeft(Sender: TObject);
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure DrawNothing(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
  end;

  TCellForm = class(TForm)
  published
    Grid: TStringGrid;
    procedure GridSelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
  end;

{$FORM Ctl_StringGrid.form}

var
  Log: TLog;
  Bmp: TBitmap;

procedure TLog.SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
begin
  Text := Text + Format('sel %d,%d;', [ACol, ARow]);
  CanSelect := Allow;
end;

procedure TLog.TopLeft(Sender: TObject);
begin
  Text := Text + 'topleft;';
end;

procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  if Swallow then
    Key := 0;
end;

procedure TLog.DrawNothing(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
begin
end;

procedure TCellForm.GridSelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
begin
  Log.Text := Log.Text + 'streamed select;';
end;

function SelStr(G: TStringGrid): string;
begin
  Result := Format('%d,%d,%d,%d', [G.Selection.Left, G.Selection.Top, G.Selection.Right, G.Selection.Bottom]);
end;

function FocusStr(G: TStringGrid): string;
begin
  Result := Format('%d,%d', [G.Col, G.Row]);
end;

procedure Key(G: TWinControl; VK: Word; Shift, Ctrl: Boolean);
begin
  SetKeyState(Shift, Ctrl);
  try
    SendKeyMessage(G.Handle, WM_KEYDOWN, VK, 1);
    SendKeyMessage(G.Handle, WM_KEYUP, VK, LPARAM($C0000001));
  finally
    SetKeyState(False, False);
  end;
end;

{ the centre of cell (C, R) of the interaction grid (41 x 21 px per cell, nothing scrolled) }
function CellPos(C, R: Integer): LPARAM;
begin
  Result := MakeLParam(41 * C + 20, 21 * R + 10);
end;

procedure Down(G: TWinControl; Pos: LPARAM; Keys: WPARAM);
begin
  SendMessageW(G.Handle, WM_LBUTTONDOWN, MK_LBUTTON or Keys, Pos);
end;

procedure MoveTo(G: TWinControl; Pos: LPARAM);
begin
  SendMessageW(G.Handle, WM_MOUSEMOVE, MK_LBUTTON, Pos);
end;

procedure Up(G: TWinControl; Pos: LPARAM);
begin
  SendMessageW(G.Handle, WM_LBUTTONUP, 0, Pos);
end;

procedure Click(G: TWinControl; Pos: LPARAM);
begin
  Down(G, Pos, 0);
  Up(G, Pos);
end;

procedure Capture(C: TWinControl);
begin
  Bmp.Canvas.Brush.Color := clWhite;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(C.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

function Count(C: TColor; X1, Y1, X2, Y2: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
        Inc(Result);
end;

function GR(L, T, R, B: Integer): TGridRect;
begin
  Result.Left := L;
  Result.Top := T;
  Result.Right := R;
  Result.Bottom := B;
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  G, G3: TStringGrid;
  SF: TCellForm;
  L: TStringList;
  O1, O2: TObject;
  S: string;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  Log.Allow := True;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  O1 := TObject.Create;
  O2 := TObject.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 600;
    F.ClientHeight := 400;
    G := TStringGrid.Create(F);

    Section('cells (UIF-840)');
    CheckEqS('', G.Cells[2, 3], 'unset cell: empty');
    Check(G.Objects[2, 3] = nil, 'unset object: nil');
    G.Cells[2, 3] := 'two three';
    G.Objects[4, 4] := O1;
    CheckEqS('two three', G.Cells[2, 3], 'cell written and read');
    CheckEqS('', G.Cells[3, 3], 'neighbour in the same row: empty');
    Check(G.Objects[2, 3] = nil, 'object of a written cell: nil');
    Check(G.Objects[4, 4] = O1, 'object written and read');
    CheckEqS('', G.Cells[4, 4], 'text of an object-only cell: empty');
    Expect('EInvalidGridOperation', SGridIndexOutOfRange);
    try
      S := G.Cells[5, 0];
      Check(False, 'Cells[ColCount, 0] raises');
    except
      on E: Exception do
        CheckRaises('Cells[ColCount, 0] raises', E.ClassName, E.Message);
    end;
    try
      G.Cells[0, -1] := 'x';
      Check(False, 'Cells[0, -1] raises');
    except
      on E: Exception do
        CheckRaises('Cells[0, -1] raises', E.ClassName, E.Message);
    end;
    try
      G.Objects[0, 5] := O1;
      Check(False, 'Objects[0, RowCount] raises');
    except
      on E: Exception do
        CheckRaises('Objects[0, RowCount] raises', E.ClassName, E.Message);
    end;
    G.Cells[4, 1] := 'last column';
    G.ColCount := 3;
    G.ColCount := 5;
    CheckEqS('', G.Cells[4, 1], 'shrinking ColCount discards the cells');
    CheckEqS('two three', G.Cells[2, 3], 'other cells kept');
    G.RowCount := 4;
    G.RowCount := 5;
    Check(G.Objects[4, 4] = nil, 'shrinking RowCount discards the cells');

    Section('Cols and Rows (UIF-848)');
    CheckEqI(5, G.Cols[2].Count, 'Cols: count = RowCount');
    CheckEqI(5, G.Rows[3].Count, 'Rows: count = ColCount');
    CheckEqS('two three', G.Cols[2][3], 'Cols[2][3] = Cells[2, 3]');
    CheckEqS('two three', G.Rows[3][2], 'Rows[3][2] = Cells[2, 3]');
    G.Cols[1][4] := 'via column';
    CheckEqS('via column', G.Cells[1, 4], 'writing through Cols');
    G.Rows[2].Objects[3] := O2;
    Check(G.Objects[3, 2] = O2, 'object through Rows');
    Check(G.Cols[3].Objects[2] = O2, 'the same object through Cols');
    G.RowCount := 7;
    CheckEqI(7, G.Cols[2].Count, 'the view is live: new RowCount');
    G.RowCount := 5;
    L := TStringList.Create;
    try
      L.Add('a');
      L.Add('b');
      G.Rows[4].Assign(L);
      CheckEqS('a', G.Cells[0, 4], 'Assign: first cell');
      CheckEqS('b', G.Cells[1, 4], 'Assign: second cell');
      CheckEqS('', G.Cells[2, 4], 'Assign: the rest cleared');
      G.Cols[2].Clear;
      CheckEqS('', G.Cells[2, 3], 'Clear empties the cells');
      CheckEqI(5, G.Cols[2].Count, 'Clear keeps the count');
    finally
      L.Free;
    end;
    Expect('EInvalidGridOperation', SGridRowsFixed);
    try
      G.Rows[1].Add('x');
      Check(False, 'Add raises');
    except
      on E: Exception do
        CheckRaises('Add raises', E.ClassName, E.Message);
    end;
    try
      G.Cols[1].Delete(0);
      Check(False, 'Delete raises');
    except
      on E: Exception do
        CheckRaises('Delete raises', E.ClassName, E.Message);
    end;
    try
      G.Cols[1].Insert(0, 'x');
      Check(False, 'Insert raises');
    except
      on E: Exception do
        CheckRaises('Insert raises', E.ClassName, E.Message);
    end;
    CheckEqI(5, G.Rows[1].Count, 'count unchanged');
    Expect('EInvalidGridOperation', SGridIndexOutOfRange);
    try
      I := G.Cols[5].Count;
      Check(False, 'Cols[ColCount] raises');
    except
      on E: Exception do
        CheckRaises('Cols[ColCount] raises', E.ClassName, E.Message);
    end;
    G.Free;

    Section('cell text (UIF-842)');
    G3 := TStringGrid.Create(F);
    G3.BorderStyle := bsNone;
    G3.SetBounds(0, 0, 200, 100);
    G3.ColCount := 3;
    G3.RowCount := 3;
    G3.DefaultColWidth := 40;
    G3.DefaultRowHeight := 20;
    G3.Color := clWhite;
    G3.ParentFont := False;
    G3.Font.Height := -11;
    G3.Font.Quality := fqNonAntialiased;                   { exact colours: no smoothing }
    G3.Font.Color := clBlack;
    G3.Parent := F;
    G3.Cells[2, 1] := 'MM';
    Capture(G3);
    Check(Count(clBlack, 82, 21, 122, 41) > 0, 'text drawn in Font.Color');
    CheckEqI(0, Count(clBlack, 82, 21, 84, 41), 'left inset 2 px');
    CheckEqI(0, Count(clBlack, 82, 21, 122, 23), 'top inset 2 px');
    G3.Cells[1, 2] := 'MMMMMMMMMMMM';
    Capture(G3);
    Check(Count(clBlack, 41, 42, 81, 62) > 0, 'long text drawn');
    CheckEqI(0, Count(clBlack, 81, 42, 200, 62), 'clipped to its cell');
    G3.Cells[0, 1] := 'MM';
    Capture(G3);
    Check(Count(clBlack, 0, 21, 40, 41) > 0, 'fixed cell text');
    G3.Selection := GR(1, 1, 2, 2);
    Capture(G3);
    CheckEqI(0, Count(clBlack, 82, 21, 122, 41), 'highlighted cell: not in Font.Color');
    Check(Count(clHighlightText, 82, 21, 122, 41) > 0, 'highlighted cell: clHighlightText');
    G3.OnDrawCell := Log.DrawNothing;
    G3.Selection := GR(1, 1, 1, 1);
    Capture(G3);
    CheckEqI(0, Count(clBlack, 0, 0, 200, 100), 'OnDrawCell replaces the default text');
    G3.Free;

    Section('focused cell in code (UIF-843)');
    G := TStringGrid.Create(F);
    G.BorderStyle := bsNone;
    G.SetBounds(0, 0, 330, 130);
    G.ColCount := 6;
    G.RowCount := 6;
    G.DefaultColWidth := 40;
    G.DefaultRowHeight := 20;
    G.Parent := F;
    G.OnSelectCell := Log.SelectCell;
    G.OnTopLeftChanged := Log.TopLeft;
    Log.Text := '';
    G.Col := 3;
    CheckEqS('sel 3,1;', Log.Text, 'Col: OnSelectCell first');
    CheckEqS('3,1', FocusStr(G), 'Col 3');
    CheckEqS('3,1,3,1', SelStr(G), 'selection: the focused cell');
    Log.Text := '';
    Log.Allow := False;
    G.Row := 4;
    CheckEqS('sel 3,4;', Log.Text, 'Row: OnSelectCell');
    CheckEqS('3,1', FocusStr(G), 'CanSelect False cancels');
    Log.Allow := True;
    Expect('EInvalidGridOperation', SGridIndexOutOfRange);
    try
      G.Col := 0;
      Check(False, 'Col in the fixed columns raises');
    except
      on E: Exception do
        CheckRaises('Col in the fixed columns raises', E.ClassName, E.Message);
    end;
    try
      G.Row := 6;
      Check(False, 'Row = RowCount raises');
    except
      on E: Exception do
        CheckRaises('Row = RowCount raises', E.ClassName, E.Message);
    end;

    Section('mouse (UIF-843)');
    Log.Text := '';
    Click(G, CellPos(2, 3));
    CheckEqS('sel 2,3;', Log.Text, 'click: OnSelectCell');
    CheckEqS('2,3', FocusStr(G), 'click: focused cell');
    Log.Text := '';
    Click(G, CellPos(2, 3));
    CheckEqS('', Log.Text, 'click on the focused cell: no event');
    Click(G, CellPos(0, 2));
    Click(G, CellPos(3, 0));
    CheckEqS('', Log.Text, 'click on fixed cells: nothing');
    CheckEqS('2,3', FocusStr(G), 'fixed click: focused cell unchanged');
    Click(G, MakeLParam(300, 10));
    CheckEqS('2,3', FocusStr(G), 'click beside the cells: nothing');
    Log.Allow := False;
    Click(G, CellPos(4, 4));
    CheckEqS('sel 4,4;', Log.Text, 'vetoed click: the event');
    CheckEqS('2,3', FocusStr(G), 'vetoed click: no change');
    Log.Allow := True;
    Log.Text := '';
    Down(G, CellPos(1, 1), 0);
    MoveTo(G, CellPos(2, 2));
    MoveTo(G, CellPos(3, 3));
    Up(G, CellPos(3, 3));
    CheckEqS('sel 1,1;', Log.Text, 'drag: one OnSelectCell for the start cell');
    CheckEqS('1,1', FocusStr(G), 'drag: focused cell = start cell');
    CheckEqS('1,1,3,3', SelStr(G), 'drag with goRangeSelect: the range');
    MoveTo(G, CellPos(4, 4));
    CheckEqS('1,1,3,3', SelStr(G), 'after the button is up: no change');
    Log.Text := '';
    Down(G, CellPos(2, 4), MK_SHIFT);
    Up(G, CellPos(2, 4));
    CheckEqS('1,1,2,4', SelStr(G), 'Shift+click extends from the focused cell');
    CheckEqS('1,1', FocusStr(G), 'Shift+click: focused cell kept');
    CheckEqS('', Log.Text, 'Shift+click: no OnSelectCell');
    Click(G, CellPos(4, 2));
    CheckEqS('4,2,4,2', SelStr(G), 'a plain click collapses the range');
    G.Options := G.Options - [goRangeSelect];
    Down(G, CellPos(1, 1), 0);
    MoveTo(G, CellPos(3, 3));
    Up(G, CellPos(3, 3));
    CheckEqS('1,1,1,1', SelStr(G), 'drag without goRangeSelect: one cell');
    G.Options := G.Options + [goRowSelect, goRangeSelect];
    Click(G, CellPos(2, 3));
    CheckEqS('1,3,5,3', SelStr(G), 'goRowSelect: the row');
    Down(G, CellPos(2, 3), 0);
    MoveTo(G, CellPos(2, 4));
    Up(G, CellPos(2, 4));
    CheckEqS('1,3,5,4', SelStr(G), 'goRowSelect drag: rows');
    G.Options := [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect];

    Section('keys (UIF-844)');
    CheckEqB(True, (SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTARROWS) <> 0, 'WM_GETDLGCODE: arrows');
    G.Col := 1;
    G.Row := 1;
    Log.Text := '';
    Key(G, VK_RIGHT, False, False);
    CheckEqS('2,1', FocusStr(G), 'Right');
    CheckEqS('sel 2,1;', Log.Text, 'keys fire OnSelectCell');
    Key(G, VK_DOWN, False, False);
    CheckEqS('2,2', FocusStr(G), 'Down');
    Key(G, VK_LEFT, False, False);
    CheckEqS('1,2', FocusStr(G), 'Left');
    Key(G, VK_LEFT, False, False);
    CheckEqS('1,2', FocusStr(G), 'Left stops at the fixed column');
    Key(G, VK_UP, False, False);
    Key(G, VK_UP, False, False);
    CheckEqS('1,1', FocusStr(G), 'Up stops at the fixed row');
    Key(G, VK_END, False, False);
    CheckEqS('5,1', FocusStr(G), 'End: last column of the row');
    Key(G, VK_RIGHT, False, False);
    CheckEqS('5,1', FocusStr(G), 'Right stops at the last column');
    Key(G, VK_HOME, False, False);
    CheckEqS('1,1', FocusStr(G), 'Home: first column of the row');
    Key(G, VK_END, False, True);
    CheckEqS('5,5', FocusStr(G), 'Ctrl+End: last cell');
    Key(G, VK_HOME, False, True);
    CheckEqS('1,1', FocusStr(G), 'Ctrl+Home: first cell');
    Log.Text := '';
    Log.Allow := False;
    Key(G, VK_RIGHT, False, False);
    CheckEqS('sel 2,1;', Log.Text, 'vetoed key: the event');
    CheckEqS('1,1', FocusStr(G), 'vetoed key: no move');
    Log.Allow := True;
    Log.Swallow := True;
    G.OnKeyDown := Log.KeyDown;
    Key(G, VK_RIGHT, False, False);
    CheckEqS('1,1', FocusStr(G), 'OnKeyDown Key := 0 swallows the key');
    Log.Swallow := False;
    G.OnKeyDown := nil;

    Section('Shift+arrows (UIF-843)');
    G.Col := 2;
    G.Row := 2;
    Log.Text := '';
    Key(G, VK_RIGHT, True, False);
    CheckEqS('2,2,3,2', SelStr(G), 'Shift+Right extends');
    CheckEqS('2,2', FocusStr(G), 'focused cell is the anchor');
    Key(G, VK_DOWN, True, False);
    CheckEqS('2,2,3,3', SelStr(G), 'Shift+Down extends');
    CheckEqS('', Log.Text, 'extending fires no OnSelectCell');
    Key(G, VK_LEFT, True, False);
    Key(G, VK_LEFT, True, False);
    CheckEqS('1,2,2,3', SelStr(G), 'Shift+Left past the anchor');
    Key(G, VK_RIGHT, False, False);
    CheckEqS('3,2', FocusStr(G), 'plain arrow moves from the focused cell');
    CheckEqS('3,2,3,2', SelStr(G), 'and collapses the range');
    G.Options := G.Options - [goRangeSelect];
    Key(G, VK_RIGHT, True, False);
    CheckEqS('4,2', FocusStr(G), 'without goRangeSelect Shift+Right moves');
    CheckEqS('4,2,4,2', SelStr(G), 'one cell');
    G.Options := G.Options + [goRangeSelect];

    Section('Tab (UIF-844)');
    CheckEqI(0, G.Perform(CM_WANTSPECIALKEY, VK_TAB, 0), 'without goTabs: Tab is the form''s');
    CheckEqB(False, (SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTTAB) <> 0, 'no DLGC_WANTTAB');
    Key(G, VK_TAB, False, False);
    CheckEqS('4,2', FocusStr(G), 'without goTabs: Tab does not move');
    G.Options := G.Options + [goTabs];
    Check(G.Perform(CM_WANTSPECIALKEY, VK_TAB, 0) <> 0, 'goTabs: the grid wants Tab');
    CheckEqB(True, (SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTTAB) <> 0, 'goTabs: DLGC_WANTTAB');
    Key(G, VK_TAB, False, False);
    CheckEqS('5,2', FocusStr(G), 'Tab: next cell');
    Key(G, VK_TAB, False, False);
    CheckEqS('1,3', FocusStr(G), 'Tab wraps to the next row');
    Key(G, VK_TAB, True, False);
    CheckEqS('5,2', FocusStr(G), 'Shift+Tab wraps back');
    G.Col := 5;
    G.Row := 5;
    Key(G, VK_TAB, False, False);
    CheckEqS('5,5', FocusStr(G), 'Tab on the last cell stays');
    G.Col := 1;
    G.Row := 1;
    Key(G, VK_TAB, True, False);
    CheckEqS('1,1', FocusStr(G), 'Shift+Tab on the first cell stays');
    G.Options := G.Options - [goTabs];

    Section('scrolling to the focused cell (UIF-845)');
    G.RowCount := 30;
    CheckEqI(5, G.VisibleRowCount, 'five rows shown');
    Log.Text := '';
    Key(G, VK_END, False, True);
    CheckEqS('5,29', FocusStr(G), 'Ctrl+End');
    CheckEqI(25, G.TopRow, 'last row scrolled into view');
    Check(Pos('topleft;', Log.Text) > 0, 'OnTopLeftChanged');
    Key(G, VK_HOME, False, True);
    CheckEqI(1, G.TopRow, 'Ctrl+Home: back to the top');
    for I := 1 to 5 do
      Key(G, VK_DOWN, False, False);
    CheckEqS('1,6', FocusStr(G), 'five times Down');
    CheckEqI(2, G.TopRow, 'one row scrolled');
    Key(G, VK_HOME, False, True);
    Key(G, VK_NEXT, False, False);
    CheckEqS('1,6', FocusStr(G), 'PageDown: VisibleRowCount rows');
    CheckEqI(2, G.TopRow, 'PageDown: scrolled into view');
    Key(G, VK_PRIOR, False, False);
    CheckEqS('1,1', FocusStr(G), 'PageUp');
    CheckEqI(1, G.TopRow, 'PageUp: scrolled back');
    G.Row := 20;
    CheckEqI(16, G.TopRow, 'Row in code scrolls too');
    G.TopRow := 1;
    CheckEqS('1,20', FocusStr(G), 'TopRow alone does not move the focused cell');
    G.Free;

    Section('streaming');
    Log.Text := '';
    SF := TCellForm.Create(nil);
    try
      CheckEqB(True, SF.Grid.Options = [goFixedVertLine, goFixedHorzLine, goTabs, goRowSelect], 'Options');
      Check(Assigned(SF.Grid.OnSelectCell), 'OnSelectCell bound');
      CheckEqS('1,1,4,1', SelStr(SF.Grid), 'goRowSelect: the first row selected');
      CheckEqS('', Log.Text, 'no OnSelectCell while loading');
      SF.Grid.Col := 2;
      CheckEqS('streamed select;', Log.Text, 'the form''s handler');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      G := TStringGrid.Create(nil);
      G.Parent := F;
      G.Cells[1, 1] := 'leak';
      G.Cols[2][3] := 'view';
      G.Rows[4].Objects[1] := O1;
      G.HandleNeeded;
      Capture(G);
      G.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Bmp.Free;
    O1.Free;
    O2.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
