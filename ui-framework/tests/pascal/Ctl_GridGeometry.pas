// covers: UIF-841, UIF-842, UIF-845
// needs: desktop
program Ctl_GridGeometry;
{ U-090, controls-standard.md section 27: TCustomGrid / TStringGrid defaults (320 x 120, 5 x 5 cells, one fixed
  column and row, 64 x 24 cells, line width 1, the default Options, bsSingle, ssBoth); the count / fixed count /
  index checks (EInvalidGridOperation with the UIConsts texts, checked after loading); ColWidths / RowHeights and
  their reset by DefaultColWidth / DefaultRowHeight; the geometry of UIF-841 without a window (fixed cells first,
  then from LeftCol / TopRow, each cell followed by its grid line, CellRect empty for cells not shown, MouseToCell,
  VisibleColCount / VisibleRowCount, LeftCol / TopRow clamped, OnTopLeftChanged); the client formula (border 2 px
  per side, bar thickness MulDiv(17, DPI, 96)) equal to Win32's; the scroll bars of UIF-845 (shown only when the
  scrollable cells do not fit and ScrollBars allows; native range in cell units; line, page, thumb with and
  without goThumbTracking, top, bottom); the window (own class, WS_EX_CLIENTEDGE, BorderStyle recreates keeping
  the state); painting of UIF-842 captured with WM_PRINTCLIENT (fixed / normal / highlighted backgrounds, focused
  cell, goDrawFocusSelected, goRowSelect, line colours and the line options, OnDrawCell after the background with
  the cell state); scaling; streaming; 100 instances with handles leak no GUI objects. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, Grids, UIConsts, TestKit;

type
  TGridAccess = class(TStringGrid);

  TLog = class
  public
    Text: string;
    Highlighted: string;
    procedure TopLeft(Sender: TObject);
    procedure DrawCell(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
  end;

  TGeoForm = class(TForm)
  published
    Full: TStringGrid;
    One: TStringGrid;
    procedure FullDrawCell(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
    procedure FullTopLeftChanged(Sender: TObject);
  end;

{$FORM Ctl_GridGeometry.form}

var
  Log: TLog;
  Bmp: TBitmap;

procedure TLog.TopLeft(Sender: TObject);
begin
  Text := Text + 'topleft;';
end;

procedure TLog.DrawCell(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
var
  G: TStringGrid;
  R: TRect;
begin
  G := TStringGrid(Sender);
  Text := Text + Format('%d,%d', [ACol, ARow]);
  if gdFixed in State then
    Text := Text + 'F';
  if gdSelected in State then
    Text := Text + 'S';
  if gdFocused in State then
    Text := Text + 'X';
  R := G.CellRect(ACol, ARow);
  if (R.Left <> Rect.Left) or (R.Top <> Rect.Top) or (R.Right <> Rect.Right) or (R.Bottom <> Rect.Bottom) then
    Text := Text + '!rect';
  Text := Text + ';';
  { the default background is already there }
  if G.Canvas.Pixels[(Rect.Left + Rect.Right) div 2, (Rect.Top + Rect.Bottom) div 2] = ColorToRGB(clHighlight) then
    Highlighted := Highlighted + Format('%d,%d;', [ACol, ARow]);
end;

procedure TGeoForm.FullDrawCell(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState);
begin
end;

procedure TGeoForm.FullTopLeftChanged(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed topleft;';
end;

function ClassOf(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := GetClassNameW(H, @Buf[0], 128);
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function Styled(C: TWinControl; Bit: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bit) <> 0;
end;

function ExStyled(C: TWinControl; Bit: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_EXSTYLE) and Bit) <> 0;
end;

function RectStr(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function ClientStr(C: TControl): string;
begin
  Result := Format('%dx%d', [C.ClientWidth, C.ClientHeight]);
end;

function Win32ClientStr(C: TWinControl): string;
var
  R: TRect;
begin
  Windows.GetClientRect(C.Handle, R);
  Result := Format('%dx%d', [R.Right, R.Bottom]);
end;

function CellAt(G: TStringGrid; X, Y: Integer): string;
var
  C, R: Integer;
begin
  C := 0;
  R := 0;
  G.MouseToCell(X, Y, C, R);
  Result := Format('%d,%d', [C, R]);
end;

function GR(L, T, R, B: Integer): TGridRect;
begin
  Result.Left := L;
  Result.Top := T;
  Result.Right := R;
  Result.Bottom := B;
end;

function SelStr(G: TStringGrid): string;
begin
  Result := Format('%d,%d,%d,%d', [G.Selection.Left, G.Selection.Top, G.Selection.Right, G.Selection.Bottom]);
end;

function NativeInfo(C: TWinControl; Bar: Integer): string;
var
  Info: TScrollInfo;
begin
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_ALL;
  GetScrollInfo(C.Handle, Bar, Info);
  Result := Format('%d..%d page %d pos %d', [Info.nMin, Info.nMax, Info.nPage, Info.nPos]);
end;

procedure Scroll(C: TWinControl; Msg: Cardinal; Code, Pos: Integer);
begin
  SendMessageW(C.Handle, Msg, MakeWParam(Word(Code), Word(Pos)), 0);
end;

procedure Capture(C: TWinControl);
begin
  Bmp.Canvas.Brush.Color := clWhite;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(C.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

function At(X, Y: Integer; C: TColor): Boolean;
begin
  Result := Bmp.Canvas.Pixels[X, Y] = ColorToRGB(C);
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  G, G2, G3: TStringGrid;
  SF: TGeoForm;
  H: HWND;
  I, B, Top, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 600;
    F.ClientHeight := 400;
    B := LayoutMulDiv(17, F.PixelsPerInch, 96);
    G := TStringGrid.Create(F);

    Section('defaults');
    CheckEqS('320,120', Format('%d,%d', [G.Width, G.Height]), 'size 320 x 120');
    CheckEqB(True, G.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqI(5, G.ColCount, 'ColCount 5');
    CheckEqB(True, G.Color = clWindow, 'Color clWindow');
    CheckEqI(64, G.DefaultColWidth, 'DefaultColWidth 64');
    CheckEqI(24, G.DefaultRowHeight, 'DefaultRowHeight 24');
    CheckEqB(True, G.FixedColor = clBtnFace, 'FixedColor clBtnFace');
    CheckEqI(1, G.FixedCols, 'FixedCols 1');
    CheckEqI(1, G.FixedRows, 'FixedRows 1');
    CheckEqI(1, G.GridLineWidth, 'GridLineWidth 1');
    CheckEqB(True, G.Options = [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect],
      'Options default');
    CheckEqB(False, G.ParentColor, 'ParentColor False');
    CheckEqB(True, G.ParentFont, 'ParentFont True');
    CheckEqI(5, G.RowCount, 'RowCount 5');
    CheckEqB(True, G.ScrollBars = ssBoth, 'ScrollBars ssBoth');
    CheckEqI(-1, G.TabOrder, 'TabOrder -1');
    CheckEqB(True, G.TabStop, 'TabStop True');
    CheckEqB(True, TGridAccess(G).DoubleBuffered, 'DoubleBuffered True');
    CheckEqB(False, csAcceptsControls in G.ControlStyle, 'no csAcceptsControls');
    CheckEqI(1, G.Col, 'Col: first scrollable column');
    CheckEqI(1, G.Row, 'Row: first scrollable row');
    CheckEqI(1, G.LeftCol, 'LeftCol 1');
    CheckEqI(1, G.TopRow, 'TopRow 1');
    CheckEqS('1,1,1,1', SelStr(G), 'Selection: the focused cell');
    CheckEqI(64, G.ColWidths[4], 'ColWidths: DefaultColWidth');
    CheckEqI(24, G.RowHeights[4], 'RowHeights: DefaultRowHeight');
    Check(GetClass('TStringGrid') <> nil, 'registered');

    Section('counts and indexes');
    Expect('EInvalidGridOperation', SFixedColTooBig);
    try
      G.ColCount := 1;
      Check(False, 'ColCount = FixedCols raises');
    except
      on E: Exception do
        CheckRaises('ColCount = FixedCols raises', E.ClassName, E.Message);
    end;
    CheckEqI(5, G.ColCount, 'ColCount unchanged');
    try
      G.FixedCols := 5;
      Check(False, 'FixedCols = ColCount raises');
    except
      on E: Exception do
        CheckRaises('FixedCols = ColCount raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidGridOperation', SFixedRowTooBig);
    try
      G.RowCount := 0;
      Check(False, 'RowCount 0 raises');
    except
      on E: Exception do
        CheckRaises('RowCount 0 raises', E.ClassName, E.Message);
    end;
    try
      G.FixedRows := 7;
      Check(False, 'FixedRows > RowCount raises');
    except
      on E: Exception do
        CheckRaises('FixedRows > RowCount raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidGridOperation', SGridIndexOutOfRange);
    try
      I := G.ColWidths[5];
      Check(False, 'ColWidths[ColCount] raises');
    except
      on E: Exception do
        CheckRaises('ColWidths[ColCount] raises', E.ClassName, E.Message);
    end;
    try
      G.RowHeights[-1] := 5;
      Check(False, 'RowHeights[-1] raises');
    except
      on E: Exception do
        CheckRaises('RowHeights[-1] raises', E.ClassName, E.Message);
    end;
    G.ColWidths[3] := 10;
    G.RowHeights[2] := 7;
    CheckEqI(10, G.ColWidths[3], 'ColWidths set');
    G.ColCount := 8;
    CheckEqI(64, G.ColWidths[7], 'new column: DefaultColWidth');
    CheckEqI(10, G.ColWidths[3], 'old columns keep their width');
    G.ColCount := 5;
    G.DefaultColWidth := 64;
    CheckEqI(64, G.ColWidths[3], 'DefaultColWidth resets every column');
    G.DefaultRowHeight := 24;
    CheckEqI(24, G.RowHeights[2], 'DefaultRowHeight resets every row');
    G.ColWidths[1] := -3;
    CheckEqI(0, G.ColWidths[1], 'a negative width is 0');
    G.ColWidths[1] := 64;

    Section('geometry without a window (UIF-841)');
    G2 := TStringGrid.Create(nil);
    try
      G2.OnTopLeftChanged := Log.TopLeft;
      G2.BorderStyle := bsNone;
      G2.SetBounds(0, 0, 200, 100);
      G2.ColCount := 6;
      G2.RowCount := 6;
      G2.DefaultColWidth := 40;
      G2.DefaultRowHeight := 20;
      CheckEqS('183x83', ClientStr(G2), 'both bars (96 DPI without a form): client 200 - 17 x 100 - 17');
      CheckEqS('0,0,40,20', RectStr(G2.CellRect(0, 0)), 'fixed cell (0, 0)');
      CheckEqS('82,21,122,41', RectStr(G2.CellRect(2, 1)), 'cell (2, 1): after two cells and their lines');
      CheckEqS('164,21,204,41', RectStr(G2.CellRect(4, 1)), 'partly shown cell: its whole rectangle');
      CheckEqS('0,0,0,0', RectStr(G2.CellRect(5, 1)), 'cell right of the client area: empty');
      CheckEqS('0,0,0,0', RectStr(G2.CellRect(1, 4)), 'cell below the client area: empty');
      CheckEqS('41,63,81,83', RectStr(G2.CellRect(1, 3)), 'cell (1, 3)');
      CheckEqI(3, G2.VisibleColCount, 'VisibleColCount: fully shown scrollable columns');
      CheckEqI(3, G2.VisibleRowCount, 'VisibleRowCount: fully shown scrollable rows');
      CheckEqS('0,0', CellAt(G2, 40, 5), 'MouseToCell: the line belongs to the cell before it');
      CheckEqS('1,1', CellAt(G2, 41, 21), 'MouseToCell (41, 21)');
      CheckEqS('4,0', CellAt(G2, 170, 5), 'MouseToCell on a partly shown cell');
      CheckEqS('4,-1', CellAt(G2, 170, 90), 'MouseToCell below the client area: row -1');
      CheckEqS('-1,0', CellAt(G2, 300, 5), 'MouseToCell right of the client area: column -1');
      Log.Text := '';
      G2.LeftCol := 2;
      CheckEqI(2, G2.LeftCol, 'LeftCol 2');
      CheckEqS('topleft;', Log.Text, 'OnTopLeftChanged once');
      CheckEqS('0,0,0,0', RectStr(G2.CellRect(1, 1)), 'scrolled out: empty');
      CheckEqS('41,21,81,41', RectStr(G2.CellRect(2, 1)), 'first scrollable column after the fixed one');
      CheckEqS('0,21,40,41', RectStr(G2.CellRect(0, 1)), 'fixed column stays');
      G2.LeftCol := 10;
      CheckEqI(3, G2.LeftCol, 'LeftCol clamped: the last columns fill the client area');
      CheckEqS('123,21,163,41', RectStr(G2.CellRect(5, 1)), 'last column shown');
      CheckEqI(3, G2.VisibleColCount, 'VisibleColCount from LeftCol');
      G2.LeftCol := 0;
      CheckEqI(1, G2.LeftCol, 'LeftCol not below FixedCols');
      Log.Text := '';
      G2.LeftCol := 1;
      CheckEqS('', Log.Text, 'no change: no event');
      G2.TopRow := 3;
      CheckEqS('41,21,81,41', RectStr(G2.CellRect(1, 3)), 'TopRow 3');
      G2.TopRow := 9;
      CheckEqI(3, G2.TopRow, 'TopRow clamped');
      CheckEqS('topleft;', Log.Text, 'one event for the real change');
      G2.TopRow := 1;
      G2.LeftCol := 3;
      Log.Text := '';
      G2.Width := 300;
      CheckEqS('283x100', ClientStr(G2), 'wider: no horizontal bar, vertical bar stays');
      CheckEqI(1, G2.LeftCol, 'everything fits: LeftCol back to the first column');
      CheckEqS('topleft;', Log.Text, 'pulled back: OnTopLeftChanged');
      CheckEqI(3, G2.VisibleRowCount, 'rows: 100 px client');
      G2.Width := 200;
      G2.GridLineWidth := 0;
      CheckEqS('80,20,120,40', RectStr(G2.CellRect(2, 1)), 'GridLineWidth 0: cells touch');
      G2.GridLineWidth := 1;
      G2.FixedCols := 2;
      CheckEqI(2, G2.Col, 'Col moves out of the fixed columns');
      CheckEqI(2, G2.LeftCol, 'LeftCol moves out of the fixed columns');
      CheckEqS('2,1,2,1', SelStr(G2), 'selection follows');
      CheckEqS('41,21,81,41', RectStr(G2.CellRect(1, 1)), 'second fixed column');
      CheckEqS('82,21,122,41', RectStr(G2.CellRect(2, 1)), 'first scrollable column');
      G2.FixedCols := 1;
      CheckEqI(2, G2.LeftCol, 'fewer fixed columns: LeftCol kept');

      Section('scaling');
      G2.ColWidths[2] := 30;
      G2.ScaleBy(2, 1);
      CheckEqI(80, G2.DefaultColWidth, 'DefaultColWidth scaled');
      CheckEqI(40, G2.DefaultRowHeight, 'DefaultRowHeight scaled');
      CheckEqI(60, G2.ColWidths[2], 'ColWidths scaled');
      CheckEqI(80, G2.ColWidths[1], 'other ColWidths scaled');
      CheckEqI(40, G2.RowHeights[3], 'RowHeights scaled');
      CheckEqI(1, G2.GridLineWidth, 'GridLineWidth not scaled');
    finally
      G2.Free;
    end;

    Section('window and client formula');
    G.Parent := F;
    H := G.Handle;
    CheckEqS('PN.TStringGrid', ClassOf(H), 'own window class');
    CheckEqB(True, ExStyled(G, WS_EX_CLIENTEDGE), 'bsSingle: WS_EX_CLIENTEDGE');
    CheckEqB(True, Styled(G, WS_TABSTOP), 'WS_TABSTOP');
    CheckEqB(True, Styled(G, WS_CLIPCHILDREN), 'WS_CLIPCHILDREN (in-place editor)');
    CheckEqB(True, Styled(G, WS_HSCROLL), 'horizontal bar needed');
    CheckEqB(True, Styled(G, WS_VSCROLL), 'vertical bar needed');
    CheckEqS(Format('%dx%d', [316 - B, 116 - B]), ClientStr(G), 'client: 2 px border per side, both bars');
    CheckEqS(ClientStr(G), Win32ClientStr(G), 'formula = Win32');
    CheckEqS('1..4 page 3 pos 1', NativeInfo(G, SB_HORZ), 'horizontal range in columns: FixedCols .. ColCount - 1');
    G.TopRow := 100;
    Top := G.TopRow;
    CheckEqS(Format('1..4 page %d pos %d', [5 - Top, Top]), NativeInfo(G, SB_VERT),
      'vertical: page = rows of the last screen, position = TopRow');
    G.TopRow := 1;

    Section('scrolling (UIF-845)');
    G.ColCount := 20;
    G.RowCount := 20;
    CheckEqI(3, G.VisibleColCount, 'three columns fully shown');
    Scroll(G, WM_HSCROLL, SB_LINEDOWN, 0);
    CheckEqI(2, G.LeftCol, 'line down');
    Scroll(G, WM_HSCROLL, SB_LINEUP, 0);
    CheckEqI(1, G.LeftCol, 'line up');
    Scroll(G, WM_HSCROLL, SB_PAGEDOWN, 0);
    CheckEqI(4, G.LeftCol, 'page down: VisibleColCount');
    Scroll(G, WM_HSCROLL, SB_PAGEUP, 0);
    CheckEqI(1, G.LeftCol, 'page up');
    Scroll(G, WM_HSCROLL, SB_PAGEUP, 0);
    CheckEqI(1, G.LeftCol, 'page up clamped');
    Scroll(G, WM_HSCROLL, SB_THUMBTRACK, 10);
    CheckEqI(1, G.LeftCol, 'thumb track without goThumbTracking: no change');
    Scroll(G, WM_HSCROLL, SB_THUMBPOSITION, 10);
    CheckEqI(10, G.LeftCol, 'thumb position');
    G.Options := G.Options + [goThumbTracking];
    Scroll(G, WM_HSCROLL, SB_THUMBTRACK, 12);
    CheckEqI(12, G.LeftCol, 'thumb track with goThumbTracking');
    Scroll(G, WM_HSCROLL, SB_BOTTOM, 0);
    CheckEqI(17, G.LeftCol, 'bottom: the last three columns');
    CheckEqS('1..19 page 3 pos 17', NativeInfo(G, SB_HORZ), 'native position');
    Scroll(G, WM_HSCROLL, SB_LINEDOWN, 0);
    CheckEqI(17, G.LeftCol, 'line down clamped');
    Scroll(G, WM_HSCROLL, SB_TOP, 0);
    CheckEqI(1, G.LeftCol, 'top');
    Scroll(G, WM_VSCROLL, SB_LINEDOWN, 0);
    CheckEqI(2, G.TopRow, 'vertical line down');
    Scroll(G, WM_VSCROLL, SB_TOP, 0);
    CheckEqI(1, G.TopRow, 'vertical top');
    G.ScrollBars := ssNone;
    CheckEqB(False, Styled(G, WS_HSCROLL), 'ssNone: no horizontal bar');
    CheckEqB(False, Styled(G, WS_VSCROLL), 'ssNone: no vertical bar');
    CheckEqS('316x116', ClientStr(G), 'ssNone: client without bars');
    CheckEqS(ClientStr(G), Win32ClientStr(G), 'ssNone: formula = Win32');
    G.LeftCol := 5;
    CheckEqI(5, G.LeftCol, 'ssNone: LeftCol still set in code');
    G.ScrollBars := ssHorizontal;
    CheckEqB(True, Styled(G, WS_HSCROLL), 'ssHorizontal: horizontal bar');
    CheckEqB(False, Styled(G, WS_VSCROLL), 'ssHorizontal: no vertical bar');
    CheckEqS(Format('316x%d', [116 - B]), ClientStr(G), 'ssHorizontal: client');
    CheckEqS(ClientStr(G), Win32ClientStr(G), 'ssHorizontal: formula = Win32');
    G.ScrollBars := ssBoth;
    G.ColCount := 3;
    G.RowCount := 3;
    CheckEqB(False, Styled(G, WS_HSCROLL), 'all cells fit: no horizontal bar');
    CheckEqB(False, Styled(G, WS_VSCROLL), 'all cells fit: no vertical bar');
    CheckEqS('316x116', ClientStr(G), 'no bars: client');
    CheckEqI(1, G.LeftCol, 'all fit: LeftCol 1');
    G.ColCount := 20;
    G.LeftCol := 5;
    G.BorderStyle := bsNone;
    Check(G.Handle <> H, 'BorderStyle recreates the window');
    CheckEqB(False, ExStyled(G, WS_EX_CLIENTEDGE), 'bsNone: no client edge');
    CheckEqI(5, G.LeftCol, 'LeftCol kept');
    CheckEqS(Format('320x%d', [120 - B]), ClientStr(G), 'bsNone, rows fit, columns scroll: client');
    CheckEqB(True, Styled(G, WS_HSCROLL), 'horizontal bar');
    CheckEqS(ClientStr(G), Win32ClientStr(G), 'bsNone: formula = Win32');

    Section('painting (UIF-842)');
    G3 := TStringGrid.Create(F);
    G3.BorderStyle := bsNone;
    G3.SetBounds(0, 150, 200, 100);
    G3.ColCount := 3;
    G3.RowCount := 3;
    G3.DefaultColWidth := 40;
    G3.DefaultRowHeight := 20;
    G3.Color := clYellow;
    G3.FixedColor := clAqua;
    G3.Parent := F;
    Capture(G3);
    CheckEqB(True, At(20, 10, clAqua), 'fixed cell: FixedColor');
    CheckEqB(True, At(60, 30, clYellow), 'focused cell, not highlighted: Color');
    CheckEqB(True, At(100, 50, clYellow), 'normal cell: Color');
    CheckEqB(True, At(40, 30, clBtnShadow), 'line of a fixed cell: clBtnShadow');
    CheckEqB(True, At(60, 20, clBtnShadow), 'horizontal line of the fixed row: clBtnShadow');
    CheckEqB(True, At(81, 30, clSilver), 'vertical line of a normal cell: clSilver');
    CheckEqB(True, At(60, 41, clSilver), 'horizontal line of a normal cell: clSilver');
    CheckEqB(True, At(122, 50, clSilver), 'line after the last column');
    CheckEqB(True, At(150, 80, clYellow), 'area after the cells: Color');
    G3.Options := G3.Options - [goVertLine];
    Capture(G3);
    CheckEqB(True, At(81, 30, clYellow), 'goVertLine off: the line in the cell colour');
    CheckEqB(True, At(40, 30, clBtnShadow), 'fixed line still drawn');
    G3.Options := G3.Options - [goFixedVertLine];
    Capture(G3);
    CheckEqB(True, At(40, 30, clAqua), 'goFixedVertLine off: FixedColor');
    G3.Options := G3.Options - [goHorzLine, goFixedHorzLine];
    Capture(G3);
    CheckEqB(True, At(60, 41, clYellow), 'goHorzLine off: cell colour');
    CheckEqB(True, At(60, 20, clAqua), 'goFixedHorzLine off: FixedColor');
    G3.Options := [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect];
    G3.Selection := GR(1, 1, 2, 2);
    CheckEqS('1,1,2,2', SelStr(G3), 'Selection set');
    CheckEqI(1, G3.Col, 'Selection: Col at its left');
    CheckEqI(1, G3.Row, 'Selection: Row at its top');
    Capture(G3);
    CheckEqB(True, At(100, 50, clHighlight), 'selected cell: clHighlight');
    CheckEqB(True, At(60, 50, clHighlight), 'selected cell (1, 2)');
    CheckEqB(True, At(60, 30, clYellow), 'focused cell not highlighted');
    CheckEqB(True, At(81, 50, clSilver), 'line between selected cells');
    G3.Options := G3.Options - [goVertLine];
    Capture(G3);
    CheckEqB(True, At(81, 50, clHighlight), 'line off between selected cells: highlight');
    G3.Options := G3.Options + [goVertLine, goDrawFocusSelected];
    Capture(G3);
    CheckEqB(True, At(60, 30, clHighlight), 'goDrawFocusSelected: focused cell highlighted');
    G3.Options := G3.Options - [goDrawFocusSelected, goRangeSelect];
    G3.Selection := GR(1, 1, 2, 2);
    CheckEqS('1,1,1,1', SelStr(G3), 'without goRangeSelect: a single cell');
    Capture(G3);
    CheckEqB(True, At(100, 50, clYellow), 'single cell: nothing highlighted');
    G3.Options := G3.Options + [goRowSelect];
    CheckEqS('1,1,2,1', SelStr(G3), 'goRowSelect: the whole row');
    Capture(G3);
    CheckEqB(True, At(60, 30, clHighlight), 'goRowSelect: focused cell highlighted');
    CheckEqB(True, At(100, 30, clHighlight), 'goRowSelect: the row');
    CheckEqB(True, At(100, 50, clYellow), 'goRowSelect: other rows not');
    G3.Options := [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect];
    G3.Selection := GR(1, 1, 2, 2);
    G3.OnDrawCell := Log.DrawCell;
    Log.Text := '';
    Log.Highlighted := '';
    Capture(G3);
    CheckEqS('0,0F;1,0F;2,0F;0,1F;1,1SX;2,1S;0,2F;1,2S;2,2S;', Log.Text,
      'OnDrawCell per cell, row by row, with Rect = CellRect and the state');
    CheckEqS('2,1;1,2;2,2;', Log.Highlighted, 'OnDrawCell after the default background');
    G3.OnDrawCell := nil;

    Section('streaming');
    Log.Text := '';
    SF := TGeoForm.Create(nil);
    try
      CheckEqB(True, SF.Full.BorderStyle = bsNone, 'BorderStyle');
      CheckEqI(3, SF.Full.ColCount, 'ColCount');
      CheckEqB(True, SF.Full.Color = clLime, 'Color');
      CheckEqI(30, SF.Full.DefaultColWidth, 'DefaultColWidth');
      CheckEqI(18, SF.Full.DefaultRowHeight, 'DefaultRowHeight');
      CheckEqI(30, SF.Full.ColWidths[2], 'ColWidths from DefaultColWidth');
      CheckEqB(True, SF.Full.FixedColor = clRed, 'FixedColor');
      CheckEqI(0, SF.Full.FixedCols, 'FixedCols');
      CheckEqI(2, SF.Full.FixedRows, 'FixedRows');
      CheckEqI(2, SF.Full.GridLineWidth, 'GridLineWidth');
      CheckEqB(True, SF.Full.Options = [goVertLine, goEditing, goTabs], 'Options');
      CheckEqI(10, SF.Full.RowCount, 'RowCount');
      CheckEqB(True, SF.Full.ScrollBars = ssVertical, 'ScrollBars');
      CheckEqB(False, SF.Full.TabStop, 'TabStop');
      Check(Assigned(SF.Full.OnDrawCell), 'OnDrawCell bound');
      Check(Assigned(SF.Full.OnTopLeftChanged), 'OnTopLeftChanged bound');
      CheckEqS('', Log.Text, 'no OnTopLeftChanged while loading');
      CheckEqI(0, SF.Full.Col, 'Col: first column (no fixed columns)');
      CheckEqI(2, SF.Full.Row, 'Row: after the fixed rows');
      CheckEqI(2, SF.Full.TopRow, 'TopRow');
      CheckEqS('32,40,62,58', RectStr(SF.Full.CellRect(1, 2)), 'loaded geometry (line width 2)');
      CheckEqS(Format('%dx150', [200 - LayoutMulDiv(17, SF.PixelsPerInch, 96)]), ClientStr(SF.Full),
        'ssVertical: vertical bar only');
      CheckEqI(1, SF.One.ColCount, 'ColCount 1 before FixedCols 0 (checked after loading)');
      CheckEqI(0, SF.One.FixedCols, 'FixedCols 0');
      CheckEqI(1, SF.One.RowCount, 'RowCount 1');
      CheckEqI(0, SF.One.FixedRows, 'FixedRows 0');
      CheckEqS('0,0', Format('%d,%d', [SF.One.Col, SF.One.Row]), 'the only cell is focused');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      G2 := TStringGrid.Create(nil);
      G2.Parent := F;
      G2.HandleNeeded;
      Capture(G2);
      G2.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Bmp.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
