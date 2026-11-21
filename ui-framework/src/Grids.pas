{ Grids - the framework-drawn grid controls (controls-standard.md section 27). }
unit Grids;

interface

uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls;

type
  EInvalidGridOperation = class(Exception);

  TGridOption = (goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect, goDrawFocusSelected,
    goRowSizing, goColSizing, goRowMoving, goColMoving, goEditing, goTabs, goRowSelect, goAlwaysShowEditor,
    goThumbTracking);
  TGridOptions = set of TGridOption;
  TGridDrawState = set of (gdSelected, gdFocused, gdFixed);

  TGridCoord = record
    X: Integer;
    Y: Integer;
  end;

  TGridRect = record
    Left: Integer;
    Top: Integer;
    Right: Integer;
    Bottom: Integer;
  end;

  TDrawCellEvent = procedure(Sender: TObject; ACol, ARow: Integer; Rect: TRect; State: TGridDrawState) of object;
  TSelectCellEvent = procedure(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean) of object;
  TGetEditEvent = procedure(Sender: TObject; ACol, ARow: Integer; var Value: string) of object;
  TSetEditEvent = procedure(Sender: TObject; ACol, ARow: Integer; const Value: string) of object;

  TCustomGrid = class;

  { controls-standard.md UIF-846: the border-less in-place editor of a grid (a helper child, UIF-886) }
  TInplaceEdit = class(TEdit)
  private
    FGrid: TCustomGrid;
    procedure ForwardKey(var Key: Word; Shift: TShiftState);
  protected
    { Enter commits, Escape cancels; Up / Down / PageUp / PageDown, Left / Right at the text boundary and Tab
      (goTabs) commit and move to another cell }
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    procedure KeyPress(var Key: Char); override;
    { CM_WANTSPECIALKEY for Enter, Escape and Tab (goTabs); focus leaving the editor commits }
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  { controls-standard.md section 27: geometry (UIF-841), painting (UIF-842), scrolling (UIF-845) }
  TCustomGrid = class(TCustomControl)
  private
    FBorderStyle: TBorderStyle;
    FColCount: Integer;
    FRowCount: Integer;
    FFixedCols: Integer;
    FFixedRows: Integer;
    FDefaultColWidth: Integer;
    FDefaultRowHeight: Integer;
    FColWidths: array of Integer;
    FRowHeights: array of Integer;
    FGridLineWidth: Integer;
    FFixedColor: TColor;
    FOptions: TGridOptions;
    FScrollBars: TScrollStyle;
    FLeftCol: Integer;
    FTopRow: Integer;
    FAnchor: TGridCoord;                                     { the focused cell (Col, Row) }
    FCurrent: TGridCoord;                                    { the moving corner of a range selection }
    FShowHorz: Boolean;                                      { the horizontal scroll bar is shown (UIF-845) }
    FShowVert: Boolean;
    FSelecting: Boolean;                                     { a mouse drag extends the selection }
    FEditor: TInplaceEdit;
    FEditing: Boolean;                                       { the editor is open on the focused cell }
    FSizing: Integer;                                        { UIF-847 drag in progress: 0 none, 1 column, 2 row }
    FSizingIndex: Integer;
    FSizingStart: Integer;                                   { left / top edge of the sized cell }
    FSizingOld: Integer;                                     { its size when the drag started }
    FHover: Integer;                                         { the line under the mouse: 0 none, 1 column, 2 row }
    FOnDrawCell: TDrawCellEvent;
    FOnSelectCell: TSelectCellEvent;
    FOnGetEditText: TGetEditEvent;
    FOnSetEditText: TSetEditEvent;
    FOnTopLeftChanged: TNotifyEvent;
    procedure SetBorderStyle(Value: TBorderStyle);
    procedure SetColCount(Value: Integer);
    procedure SetRowCount(Value: Integer);
    procedure SetFixedCols(Value: Integer);
    procedure SetFixedRows(Value: Integer);
    procedure SetDefaultColWidth(Value: Integer);
    procedure SetDefaultRowHeight(Value: Integer);
    function GetColWidths(Index: Integer): Integer;
    procedure SetColWidths(Index: Integer; Value: Integer);
    function GetRowHeights(Index: Integer): Integer;
    procedure SetRowHeights(Index: Integer; Value: Integer);
    procedure SetGridLineWidth(Value: Integer);
    procedure SetFixedColor(Value: TColor);
    procedure SetOptions(Value: TGridOptions);
    procedure SetScrollBars(Value: TScrollStyle);
    procedure SetLeftCol(Value: Integer);
    procedure SetTopRow(Value: Integer);
    function GetCol: Integer;
    procedure SetCol(Value: Integer);
    function GetRow: Integer;
    procedure SetRow(Value: Integer);
    function GetSelection: TGridRect;
    procedure SetSelection(const Value: TGridRect);
    function GetVisibleColCount: Integer;
    function GetVisibleRowCount: Integer;
    function GridDPI: Integer;
    function BarSize: Integer;
    function BorderInset: Integer;
    function FixedWidth: Integer;
    function FixedHeight: Integer;
    function MaxLeftCol(ClientW: Integer): Integer;
    function MaxTopRow(ClientH: Integer): Integer;
    function ColumnX(ACol: Integer): Integer;
    function RowY(ARow: Integer): Integer;
    function FirstShownCol: Integer;
    function NextShownCol(ACol: Integer): Integer;
    function FirstShownRow: Integer;
    function NextShownRow(ARow: Integer): Integer;
    function InSelection(ACol, ARow: Integer): Boolean;
    procedure CheckCol(Index: Integer);
    procedure CheckRow(Index: Integer);
    procedure UpdateScrollBars;
    procedure UpdateNativeBars;
    procedure ChangeTopLeft(ALeft, ATop: Integer);
    procedure ScrollIntoView(ACol, ARow: Integer);
    procedure MoveFocus(ACol, ARow: Integer; Extend: Boolean);
    function CanEditShow: Boolean;
    procedure ShowEditor;
    procedure ShowEditorChar(Ch: Char);
    procedure HideEditor;
    procedure UpdateText;
    procedure CommitEdit;
    procedure CancelEdit;
    procedure UpdateEditorBounds;
    function GetEditorMode: Boolean;
    procedure SetEditorMode(Value: Boolean);
    function GetInplaceEditor: TEdit;
    function SizingAt(X, Y: Integer; var Index: Integer): Integer;
    procedure ApplySizing(Pos: Integer);
    procedure PaintCell(ACol, ARow: Integer; const ARect: TRect);
  protected
    { WS_EX_CLIENTEDGE for bsSingle, WS_HSCROLL / WS_VSCROLL for the shown bars, WS_CLIPCHILDREN (editor) }
    procedure CreateParams(var Params: TCreateParams); override;
    { the native scroll bars take the ranges and positions }
    procedure CreateWnd; override;
    { layout.md UIF-215 as for TScrollBox: 2 px border per side for bsSingle, minus the shown bars }
    function GetClientRect: TRect; override;
    procedure BoundsChanged(const OldBounds: TRect); override;
    procedure SetParent(AParent: TWinControl); override;
    { layout.md UIF-280 step 4: DefaultColWidth, DefaultRowHeight, ColWidths, RowHeights }
    procedure ChangeScale(M, D: Integer); override;
    { the count checks of the stored values; Col, Row, LeftCol, TopRow at the first scrollable cell }
    procedure Loaded; override;
    procedure WndProc(var Message: TMessage); override;
    procedure WMHScroll(var Message: TWMScroll); override;
    procedure WMVScroll(var Message: TWMScroll); override;
    { UIF-842 }
    procedure Paint; override;
    { called after the default background of every shown cell: OnDrawCell, else the default text }
    procedure DrawCell(ACol, ARow: Integer; ARect: TRect; AState: TGridDrawState); virtual;
    { the default text of a cell (base: empty) }
    function GetCellText(ACol, ARow: Integer): string; virtual;
    { the cell counts changed (descendants resize their storage) }
    procedure SizeChanged(OldColCount, OldRowCount: Integer); virtual;
    { LeftCol or TopRow changed: OnTopLeftChanged }
    procedure TopLeftChanged; virtual;
    { UIF-843: asked before the focused cell moves to (ACol, ARow); base: OnSelectCell }
    function SelectCell(ACol, ARow: Integer): Boolean; virtual;
    { UIF-844 }
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    { UIF-843: a click focuses a cell, Shift+click and a drag extend the selection (goRangeSelect) }
    procedure MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    procedure MouseMove(Shift: TShiftState; X, Y: Integer); override;
    procedure MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    { UIF-844: a typed character starts editing, replacing the text }
    procedure KeyPress(var Key: Char); override;
    { UIF-847: crVSplit over a column line, crHSplit over a row line (while sizing: the sized kind) }
    function DefaultCursor: TCursor; override;
    { UIF-846: the editor's initial text: the cell text, then OnGetEditText }
    function GetEditText(ACol, ARow: Integer): string; virtual;
    { UIF-846: a commit: OnSetEditText (TStringGrid then stores the cell) }
    procedure SetEditText(ACol, ARow: Integer; const Value: string); virtual;
    property BorderStyle: TBorderStyle read FBorderStyle write SetBorderStyle default bsSingle;
    property ColCount: Integer read FColCount write SetColCount default 5;
    property DefaultColWidth: Integer read FDefaultColWidth write SetDefaultColWidth default 64;
    property DefaultRowHeight: Integer read FDefaultRowHeight write SetDefaultRowHeight default 24;
    property FixedColor: TColor read FFixedColor write SetFixedColor default clBtnFace;
    property FixedCols: Integer read FFixedCols write SetFixedCols default 1;
    property FixedRows: Integer read FFixedRows write SetFixedRows default 1;
    property GridLineWidth: Integer read FGridLineWidth write SetGridLineWidth default 1;
    property Options: TGridOptions read FOptions write SetOptions
      default [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect];
    property RowCount: Integer read FRowCount write SetRowCount default 5;
    property ScrollBars: TScrollStyle read FScrollBars write SetScrollBars default ssBoth;
    property OnDrawCell: TDrawCellEvent read FOnDrawCell write FOnDrawCell;
    property OnSelectCell: TSelectCellEvent read FOnSelectCell write FOnSelectCell;
    property OnGetEditText: TGetEditEvent read FOnGetEditText write FOnGetEditText;
    property OnSetEditText: TSetEditEvent read FOnSetEditText write FOnSetEditText;
    property OnTopLeftChanged: TNotifyEvent read FOnTopLeftChanged write FOnTopLeftChanged;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-841: the cell without its grid line; an empty rectangle for a cell that is not shown }
    function CellRect(ACol, ARow: Integer): TRect;
    { the cell under a client point (a grid line belongs to the cell before it); -1 outside the cells }
    procedure MouseToCell(X, Y: Integer; var ACol, ARow: Integer);
    property Canvas;
    property ColWidths[Index: Integer]: Integer read GetColWidths write SetColWidths;
    property RowHeights[Index: Integer]: Integer read GetRowHeights write SetRowHeights;
    property Col: Integer read GetCol write SetCol;
    property Row: Integer read GetRow write SetRow;
    property LeftCol: Integer read FLeftCol write SetLeftCol;
    property TopRow: Integer read FTopRow write SetTopRow;
    property Selection: TGridRect read GetSelection write SetSelection;
    property VisibleColCount: Integer read GetVisibleColCount;
    property VisibleRowCount: Integer read GetVisibleRowCount;
    { UIF-846: True while the in-place editor is open; False commits and closes it }
    property EditorMode: Boolean read GetEditorMode write SetEditorMode;
    { the in-place editor (nil until the first edit) }
    property InplaceEditor: TEdit read GetInplaceEditor;
  end;

  { controls-standard.md section 27: cells (UIF-840), Cols / Rows (UIF-848) }
  TStringGrid = class(TCustomGrid)
  private
    FData: array of TStringList;                             { rows, allocated on first write (nil = all empty) }
    FColViews: TList;
    FRowViews: TList;
    function DataRow(ARow: Integer): TStringList;
    function GetCells(ACol, ARow: Integer): string;
    procedure SetCells(ACol, ARow: Integer; const Value: string);
    function GetObjects(ACol, ARow: Integer): TObject;
    procedure SetObjects(ACol, ARow: Integer; Value: TObject);
    function GetCols(Index: Integer): TStrings;
    function GetRows(Index: Integer): TStrings;
    procedure InvalidateCell(ACol, ARow: Integer);
  protected
    function GetCellText(ACol, ARow: Integer): string; override;
    { OnSetEditText, then the cell takes the value }
    procedure SetEditText(ACol, ARow: Integer; const Value: string); override;
    { the removed cells are discarded, kept rows take the new column count }
    procedure SizeChanged(OldColCount, OldRowCount: Integer); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    property Cells[ACol, ARow: Integer]: string read GetCells write SetCells;
    property Objects[ACol, ARow: Integer]: TObject read GetObjects write SetObjects;
    property Cols[Index: Integer]: TStrings read GetCols;
    property Rows[Index: Integer]: TStrings read GetRows;
  published
    property BorderStyle;
    property ColCount;
    property Color default clWindow;
    property DefaultColWidth;
    property DefaultRowHeight;
    property FixedColor;
    property FixedCols;
    property FixedRows;
    property Font;
    property GridLineWidth;
    property Options;
    property ParentColor default False;
    property ParentFont;
    property RowCount;
    property ScrollBars;
    property TabOrder;
    property TabStop default True;
    property OnClick;
    property OnContextPopup;
    property OnDblClick;
    property OnDrawCell;
    property OnEnter;
    property OnExit;
    property OnGetEditText;
    property OnKeyDown;
    property OnKeyPress;
    property OnKeyUp;
    property OnMouseDown;
    property OnMouseEnter;
    property OnMouseLeave;
    property OnMouseMove;
    property OnMouseUp;
    property OnSelectCell;
    property OnSetEditText;
    property OnTopLeftChanged;
  end;

implementation

uses
  UIConsts;

type
  TFormAccess = class(TCustomForm);

procedure GridError(const Msg: string);
begin
  raise EInvalidGridOperation.Create(Msg);
end;

function Coord(X, Y: Integer): TGridCoord;
begin
  Result.X := X;
  Result.Y := Y;
end;

{ ---------------------------------------------------------------- TCustomGrid }

constructor TCustomGrid.Create(AOwner: TComponent);
var
  I: Integer;
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csOpaque] - [csAcceptsControls, csSetCaption];
  DoubleBuffered := True;
  FBorderStyle := bsSingle;
  FColCount := 5;
  FRowCount := 5;
  FFixedCols := 1;
  FFixedRows := 1;
  FDefaultColWidth := 64;
  FDefaultRowHeight := 24;
  SetLength(FColWidths, FColCount);
  for I := 0 to FColCount - 1 do
    FColWidths[I] := FDefaultColWidth;
  SetLength(FRowHeights, FRowCount);
  for I := 0 to FRowCount - 1 do
    FRowHeights[I] := FDefaultRowHeight;
  FGridLineWidth := 1;
  FFixedColor := clBtnFace;
  FOptions := [goFixedVertLine, goFixedHorzLine, goVertLine, goHorzLine, goRangeSelect];
  FScrollBars := ssBoth;
  FLeftCol := 1;
  FTopRow := 1;
  FAnchor := Coord(1, 1);
  FCurrent := FAnchor;
  Color := clWindow;
  ParentColor := False;
  TabStop := True;
  SetBounds(Left, Top, 320, 120);
  UpdateScrollBars;
end;

destructor TCustomGrid.Destroy;
begin
  FEditing := False;
  inherited Destroy;
end;

procedure TCustomGrid.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := Params.Style or WS_CLIPCHILDREN;
  if FBorderStyle = bsSingle then
    Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
  if FShowHorz then
    Params.Style := Params.Style or WS_HSCROLL;
  if FShowVert then
    Params.Style := Params.Style or WS_VSCROLL;
end;

procedure TCustomGrid.CreateWnd;
begin
  inherited CreateWnd;
  UpdateNativeBars;
end;

{ the DPI of the grid: its form's current PixelsPerInch, 96 without a form }
function TCustomGrid.GridDPI: Integer;
var
  F: TCustomForm;
begin
  Result := 96;
  F := GetParentForm(Self);
  if (F <> nil) and (TFormAccess(F).PixelsPerInch > 0) then
    Result := TFormAccess(F).PixelsPerInch;
end;

function TCustomGrid.BarSize: Integer;
begin
  Result := LayoutMulDiv(17, GridDPI, 96);
end;

function TCustomGrid.BorderInset: Integer;
begin
  if FBorderStyle = bsSingle then
    Result := 2
  else
    Result := 0;
end;

function TCustomGrid.GetClientRect: TRect;
var
  W, H: Integer;
begin
  W := Width - 2 * BorderInset;
  H := Height - 2 * BorderInset;
  if FShowVert then
    W := W - BarSize;
  if FShowHorz then
    H := H - BarSize;
  if W < 0 then
    W := 0;
  if H < 0 then
    H := 0;
  Result := Rect(0, 0, W, H);
end;

procedure TCustomGrid.BoundsChanged(const OldBounds: TRect);
begin
  inherited BoundsChanged(OldBounds);
  if (OldBounds.Right - OldBounds.Left <> Width) or (OldBounds.Bottom - OldBounds.Top <> Height) then
  begin
    UpdateScrollBars;
    Invalidate;
  end;
end;

procedure TCustomGrid.SetParent(AParent: TWinControl);
begin
  inherited SetParent(AParent);
  UpdateScrollBars;
end;

procedure TCustomGrid.ChangeScale(M, D: Integer);
var
  I: Integer;
begin
  if (M <> D) and (M > 0) and (D > 0) then
  begin
    FDefaultColWidth := LayoutMulDiv(FDefaultColWidth, M, D);
    FDefaultRowHeight := LayoutMulDiv(FDefaultRowHeight, M, D);
    for I := 0 to Length(FColWidths) - 1 do
      FColWidths[I] := LayoutMulDiv(FColWidths[I], M, D);
    for I := 0 to Length(FRowHeights) - 1 do
      FRowHeights[I] := LayoutMulDiv(FRowHeights[I], M, D);
  end;
  inherited ChangeScale(M, D);
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.Loaded;
begin
  inherited Loaded;
  if FFixedCols >= FColCount then
    GridError(SFixedColTooBig);
  if FFixedRows >= FRowCount then
    GridError(SFixedRowTooBig);
  FAnchor := Coord(FFixedCols, FFixedRows);
  FCurrent := FAnchor;
  FLeftCol := FFixedCols;
  FTopRow := FFixedRows;
  UpdateScrollBars;
  Invalidate;
end;

{ ---------------------------------------------------------------- geometry (UIF-841) }

function TCustomGrid.FixedWidth: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to FFixedCols - 1 do
    Result := Result + FColWidths[I] + FGridLineWidth;
end;

function TCustomGrid.FixedHeight: Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to FFixedRows - 1 do
    Result := Result + FRowHeights[I] + FGridLineWidth;
end;

{ the largest LeftCol: the last columns that fit into ClientW after the fixed ones (at least the last column) }
function TCustomGrid.MaxLeftCol(ClientW: Integer): Integer;
var
  X: Integer;
begin
  Result := FColCount - 1;
  if Result < FFixedCols then
  begin
    Result := FFixedCols;
    Exit;
  end;
  X := FixedWidth + FColWidths[Result];
  while (Result > FFixedCols) and (X + FColWidths[Result - 1] + FGridLineWidth <= ClientW) do
  begin
    X := X + FColWidths[Result - 1] + FGridLineWidth;
    Dec(Result);
  end;
end;

function TCustomGrid.MaxTopRow(ClientH: Integer): Integer;
var
  Y: Integer;
begin
  Result := FRowCount - 1;
  if Result < FFixedRows then
  begin
    Result := FFixedRows;
    Exit;
  end;
  Y := FixedHeight + FRowHeights[Result];
  while (Result > FFixedRows) and (Y + FRowHeights[Result - 1] + FGridLineWidth <= ClientH) do
  begin
    Y := Y + FRowHeights[Result - 1] + FGridLineWidth;
    Dec(Result);
  end;
end;

{ the left edge of a column, -1 when it is scrolled out (left of LeftCol) }
function TCustomGrid.ColumnX(ACol: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  if ACol < FFixedCols then
  begin
    for I := 0 to ACol - 1 do
      Result := Result + FColWidths[I] + FGridLineWidth;
    Exit;
  end;
  if ACol < FLeftCol then
  begin
    Result := -1;
    Exit;
  end;
  Result := FixedWidth;
  for I := FLeftCol to ACol - 1 do
    Result := Result + FColWidths[I] + FGridLineWidth;
end;

function TCustomGrid.RowY(ARow: Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  if ARow < FFixedRows then
  begin
    for I := 0 to ARow - 1 do
      Result := Result + FRowHeights[I] + FGridLineWidth;
    Exit;
  end;
  if ARow < FTopRow then
  begin
    Result := -1;
    Exit;
  end;
  Result := FixedHeight;
  for I := FTopRow to ARow - 1 do
    Result := Result + FRowHeights[I] + FGridLineWidth;
end;

{ the columns in screen order: fixed ones, then LeftCol ..; -1 = no more }
function TCustomGrid.FirstShownCol: Integer;
begin
  if FFixedCols > 0 then
    Result := 0
  else if FLeftCol < FColCount then
    Result := FLeftCol
  else
    Result := -1;
end;

function TCustomGrid.NextShownCol(ACol: Integer): Integer;
begin
  Result := ACol + 1;
  if Result = FFixedCols then
    Result := FLeftCol;
  if Result >= FColCount then
    Result := -1;
end;

function TCustomGrid.FirstShownRow: Integer;
begin
  if FFixedRows > 0 then
    Result := 0
  else if FTopRow < FRowCount then
    Result := FTopRow
  else
    Result := -1;
end;

function TCustomGrid.NextShownRow(ARow: Integer): Integer;
begin
  Result := ARow + 1;
  if Result = FFixedRows then
    Result := FTopRow;
  if Result >= FRowCount then
    Result := -1;
end;

function TCustomGrid.CellRect(ACol, ARow: Integer): TRect;
var
  X, Y: Integer;
  R: TRect;
begin
  Result := Rect(0, 0, 0, 0);
  if (ACol < 0) or (ACol >= FColCount) or (ARow < 0) or (ARow >= FRowCount) then
    Exit;
  X := ColumnX(ACol);
  Y := RowY(ARow);
  R := ClientRect;
  if (X < 0) or (Y < 0) or (X >= R.Right) or (Y >= R.Bottom) then
    Exit;
  Result := Rect(X, Y, X + FColWidths[ACol], Y + FRowHeights[ARow]);
end;

procedure TCustomGrid.MouseToCell(X, Y: Integer; var ACol, ARow: Integer);
var
  C, P: Integer;
  R: TRect;
begin
  ACol := -1;
  ARow := -1;
  R := ClientRect;
  if (X >= 0) and (X < R.Right) then
  begin
    P := 0;
    C := FirstShownCol;
    while C >= 0 do
    begin
      if X < P + FColWidths[C] + FGridLineWidth then
      begin
        ACol := C;
        Break;
      end;
      P := P + FColWidths[C] + FGridLineWidth;
      C := NextShownCol(C);
    end;
  end;
  if (Y >= 0) and (Y < R.Bottom) then
  begin
    P := 0;
    C := FirstShownRow;
    while C >= 0 do
    begin
      if Y < P + FRowHeights[C] + FGridLineWidth then
      begin
        ARow := C;
        Break;
      end;
      P := P + FRowHeights[C] + FGridLineWidth;
      C := NextShownRow(C);
    end;
  end;
end;

{ the scrollable columns from LeftCol whose cell (without its line) lies inside the client area }
function TCustomGrid.GetVisibleColCount: Integer;
var
  X, C, W: Integer;
begin
  Result := 0;
  W := ClientWidth;
  X := FixedWidth;
  C := FLeftCol;
  while (C < FColCount) and (X + FColWidths[C] <= W) do
  begin
    Inc(Result);
    X := X + FColWidths[C] + FGridLineWidth;
    Inc(C);
  end;
end;

function TCustomGrid.GetVisibleRowCount: Integer;
var
  Y, R, H: Integer;
begin
  Result := 0;
  H := ClientHeight;
  Y := FixedHeight;
  R := FTopRow;
  while (R < FRowCount) and (Y + FRowHeights[R] <= H) do
  begin
    Inc(Result);
    Y := Y + FRowHeights[R] + FGridLineWidth;
    Inc(R);
  end;
end;

{ ---------------------------------------------------------------- scroll bars (UIF-845) }

{ UIF-845 with the bar procedure of layout.md UIF-291: a bar is needed when scrolling can bring another cell into
  view; LeftCol / TopRow are then clamped to the new client size }
procedure TCustomGrid.UpdateScrollBars;
var
  W0, H0, B: Integer;
  AllowH, AllowV, H, V: Boolean;
begin
  W0 := Width - 2 * BorderInset;
  H0 := Height - 2 * BorderInset;
  B := BarSize;
  AllowH := FScrollBars in [ssHorizontal, ssBoth];
  AllowV := FScrollBars in [ssVertical, ssBoth];
  H := AllowH and (MaxLeftCol(W0) > FFixedCols);
  V := AllowV and (MaxTopRow(H0) > FFixedRows);
  if H and not V then
    V := AllowV and (MaxTopRow(H0 - B) > FFixedRows);
  if V and not H then
    H := AllowH and (MaxLeftCol(W0 - B) > FFixedCols);
  FShowHorz := H;
  FShowVert := V;
  ChangeTopLeft(FLeftCol, FTopRow);
  UpdateNativeBars;
  UpdateEditorBounds;
end;

{ SetScrollInfo without SIF_DISABLENOSCROLL: an empty range hides the bar; the range is in cells }
procedure TCustomGrid.UpdateNativeBars;
var
  Info: TScrollInfo;
begin
  if not HandleAllocated or LayoutDumpMode then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  if FShowHorz then
  begin
    Info.nMin := FFixedCols;
    Info.nMax := FColCount - 1;
    Info.nPage := UINT(FColCount - MaxLeftCol(ClientWidth));
    Info.nPos := FLeftCol;
  end;
  SetScrollInfo(Handle, SB_HORZ, Info, True);
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  if FShowVert then
  begin
    Info.nMin := FFixedRows;
    Info.nMax := FRowCount - 1;
    Info.nPage := UINT(FRowCount - MaxTopRow(ClientHeight));
    Info.nPos := FTopRow;
  end;
  SetScrollInfo(Handle, SB_VERT, Info, True);
end;

{ LeftCol / TopRow clamped to FixedCols .. MaxLeftCol (rows alike); a change repaints and fires OnTopLeftChanged }
procedure TCustomGrid.ChangeTopLeft(ALeft, ATop: Integer);
var
  M: Integer;
begin
  M := MaxLeftCol(ClientWidth);
  if ALeft > M then
    ALeft := M;
  if ALeft < FFixedCols then
    ALeft := FFixedCols;
  M := MaxTopRow(ClientHeight);
  if ATop > M then
    ATop := M;
  if ATop < FFixedRows then
    ATop := FFixedRows;
  if (ALeft = FLeftCol) and (ATop = FTopRow) then
    Exit;
  FLeftCol := ALeft;
  FTopRow := ATop;
  UpdateNativeBars;
  UpdateEditorBounds;
  Invalidate;
  if not (csLoading in ComponentState) then
    TopLeftChanged;
end;

procedure TCustomGrid.TopLeftChanged;
begin
  if Assigned(FOnTopLeftChanged) then
    FOnTopLeftChanged(Self);
end;

procedure TCustomGrid.SetLeftCol(Value: Integer);
begin
  ChangeTopLeft(Value, FTopRow);
end;

procedure TCustomGrid.SetTopRow(Value: Integer);
begin
  ChangeTopLeft(FLeftCol, Value);
end;

procedure TCustomGrid.WMHScroll(var Message: TWMScroll);
var
  Page: Integer;
begin
  Page := VisibleColCount;
  if Page < 1 then
    Page := 1;
  case Message.ScrollCode of
    SB_LINEUP:
      SetLeftCol(FLeftCol - 1);
    SB_LINEDOWN:
      SetLeftCol(FLeftCol + 1);
    SB_PAGEUP:
      SetLeftCol(FLeftCol - Page);
    SB_PAGEDOWN:
      SetLeftCol(FLeftCol + Page);
    SB_THUMBPOSITION:
      SetLeftCol(Message.Pos);
    SB_THUMBTRACK:
      if goThumbTracking in FOptions then
        SetLeftCol(Message.Pos);
    SB_TOP:
      SetLeftCol(FFixedCols);
    SB_BOTTOM:
      SetLeftCol(FColCount);
  end;
  Message.Result := 0;
end;

procedure TCustomGrid.WMVScroll(var Message: TWMScroll);
var
  Page: Integer;
begin
  Page := VisibleRowCount;
  if Page < 1 then
    Page := 1;
  case Message.ScrollCode of
    SB_LINEUP:
      SetTopRow(FTopRow - 1);
    SB_LINEDOWN:
      SetTopRow(FTopRow + 1);
    SB_PAGEUP:
      SetTopRow(FTopRow - Page);
    SB_PAGEDOWN:
      SetTopRow(FTopRow + Page);
    SB_THUMBPOSITION:
      SetTopRow(Message.Pos);
    SB_THUMBTRACK:
      if goThumbTracking in FOptions then
        SetTopRow(Message.Pos);
    SB_TOP:
      SetTopRow(FFixedRows);
    SB_BOTTOM:
      SetTopRow(FRowCount);
  end;
  Message.Result := 0;
end;

procedure TCustomGrid.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_DPICHANGED:
      begin
        inherited WndProc(Message);
        UpdateScrollBars;
      end;
    { UIF-844: Tab stays in the grid with goTabs }
    CM_WANTSPECIALKEY:
      if ((Message.WParam = VK_TAB) and (goTabs in FOptions)) or ((Message.WParam = VK_RETURN) and CanEditShow) then
        Message.Result := 1
      else
        inherited WndProc(Message);
    WM_GETDLGCODE:
      begin
        Message.Result := DLGC_WANTARROWS;
        if goTabs in FOptions then
          Message.Result := Message.Result or DLGC_WANTTAB;
        if goEditing in FOptions then
          Message.Result := Message.Result or DLGC_WANTCHARS;
      end;
    { a cancelled drag: the sized cell gets its old size back }
    WM_CANCELMODE:
      begin
        FSelecting := False;
        if FSizing = 1 then
          SetColWidths(FSizingIndex, FSizingOld)
        else if FSizing = 2 then
          SetRowHeights(FSizingIndex, FSizingOld);
        FSizing := 0;
        inherited WndProc(Message);
      end;
    CM_FONTCHANGED, CM_COLORCHANGED, CM_ENABLEDCHANGED, WM_SETFOCUS, WM_KILLFOCUS:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ ---------------------------------------------------------------- properties }

procedure TCustomGrid.CheckCol(Index: Integer);
begin
  if (Index < 0) or (Index >= FColCount) then
    GridError(SGridIndexOutOfRange);
end;

procedure TCustomGrid.CheckRow(Index: Integer);
begin
  if (Index < 0) or (Index >= FRowCount) then
    GridError(SGridIndexOutOfRange);
end;

procedure TCustomGrid.SetBorderStyle(Value: TBorderStyle);
begin
  if FBorderStyle <> Value then
  begin
    FBorderStyle := Value;
    UpdateScrollBars;
    RecreateWnd;
  end;
end;

procedure TCustomGrid.SizeChanged(OldColCount, OldRowCount: Integer);
begin
end;

procedure TCustomGrid.SetColCount(Value: Integer);
var
  I, Old: Integer;
begin
  if Value = FColCount then
    Exit;
  if not (csLoading in ComponentState) and (Value <= FFixedCols) then
    GridError(SFixedColTooBig);
  if Value < 1 then
    Value := 1;
  Old := FColCount;
  SetLength(FColWidths, Value);
  for I := Old to Value - 1 do
    FColWidths[I] := FDefaultColWidth;
  FColCount := Value;
  if FAnchor.X >= Value then
    FAnchor.X := Value - 1;
  if FCurrent.X >= Value then
    FCurrent.X := Value - 1;
  SizeChanged(Old, FRowCount);
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.SetRowCount(Value: Integer);
var
  I, Old: Integer;
begin
  if Value = FRowCount then
    Exit;
  if not (csLoading in ComponentState) and (Value <= FFixedRows) then
    GridError(SFixedRowTooBig);
  if Value < 1 then
    Value := 1;
  Old := FRowCount;
  SetLength(FRowHeights, Value);
  for I := Old to Value - 1 do
    FRowHeights[I] := FDefaultRowHeight;
  FRowCount := Value;
  if FAnchor.Y >= Value then
    FAnchor.Y := Value - 1;
  if FCurrent.Y >= Value then
    FCurrent.Y := Value - 1;
  SizeChanged(FColCount, Old);
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.SetFixedCols(Value: Integer);
begin
  if Value = FFixedCols then
    Exit;
  if Value < 0 then
    GridError(SGridIndexOutOfRange);
  if not (csLoading in ComponentState) and (Value >= FColCount) then
    GridError(SFixedColTooBig);
  FFixedCols := Value;
  if FAnchor.X < Value then
    FAnchor.X := Value;
  if FCurrent.X < Value then
    FCurrent.X := Value;
  if FLeftCol < Value then
    FLeftCol := Value;
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.SetFixedRows(Value: Integer);
begin
  if Value = FFixedRows then
    Exit;
  if Value < 0 then
    GridError(SGridIndexOutOfRange);
  if not (csLoading in ComponentState) and (Value >= FRowCount) then
    GridError(SFixedRowTooBig);
  FFixedRows := Value;
  if FAnchor.Y < Value then
    FAnchor.Y := Value;
  if FCurrent.Y < Value then
    FCurrent.Y := Value;
  if FTopRow < Value then
    FTopRow := Value;
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.SetDefaultColWidth(Value: Integer);
var
  I: Integer;
begin
  if Value < 0 then
    Value := 0;
  FDefaultColWidth := Value;
  for I := 0 to Length(FColWidths) - 1 do
    FColWidths[I] := Value;
  UpdateScrollBars;
  Invalidate;
end;

procedure TCustomGrid.SetDefaultRowHeight(Value: Integer);
var
  I: Integer;
begin
  if Value < 0 then
    Value := 0;
  FDefaultRowHeight := Value;
  for I := 0 to Length(FRowHeights) - 1 do
    FRowHeights[I] := Value;
  UpdateScrollBars;
  Invalidate;
end;

function TCustomGrid.GetColWidths(Index: Integer): Integer;
begin
  CheckCol(Index);
  Result := FColWidths[Index];
end;

procedure TCustomGrid.SetColWidths(Index: Integer; Value: Integer);
begin
  CheckCol(Index);
  if Value < 0 then
    Value := 0;
  if FColWidths[Index] <> Value then
  begin
    FColWidths[Index] := Value;
    UpdateScrollBars;
    Invalidate;
  end;
end;

function TCustomGrid.GetRowHeights(Index: Integer): Integer;
begin
  CheckRow(Index);
  Result := FRowHeights[Index];
end;

procedure TCustomGrid.SetRowHeights(Index: Integer; Value: Integer);
begin
  CheckRow(Index);
  if Value < 0 then
    Value := 0;
  if FRowHeights[Index] <> Value then
  begin
    FRowHeights[Index] := Value;
    UpdateScrollBars;
    Invalidate;
  end;
end;

procedure TCustomGrid.SetGridLineWidth(Value: Integer);
begin
  if Value < 0 then
    Value := 0;
  if FGridLineWidth <> Value then
  begin
    FGridLineWidth := Value;
    UpdateScrollBars;
    Invalidate;
  end;
end;

procedure TCustomGrid.SetFixedColor(Value: TColor);
begin
  if FFixedColor <> Value then
  begin
    FFixedColor := Value;
    Invalidate;
  end;
end;

procedure TCustomGrid.SetOptions(Value: TGridOptions);
var
  Old: TGridOptions;
begin
  if FOptions = Value then
    Exit;
  Old := FOptions;
  FOptions := Value;
  if not (goRangeSelect in FOptions) then
    FCurrent := FAnchor;
  if FEditing and (not CanEditShow or ((goAlwaysShowEditor in Old) and not (goAlwaysShowEditor in FOptions))) then
  begin
    UpdateText;
    HideEditor;
  end;
  if (goAlwaysShowEditor in FOptions) and not FEditing and not (csLoading in ComponentState) then
    ShowEditor;
  Invalidate;
end;

procedure TCustomGrid.SetScrollBars(Value: TScrollStyle);
begin
  if FScrollBars <> Value then
  begin
    FScrollBars := Value;
    UpdateScrollBars;
  end;
end;

function TCustomGrid.GetCol: Integer;
begin
  Result := FAnchor.X;
end;

function TCustomGrid.GetRow: Integer;
begin
  Result := FAnchor.Y;
end;

procedure TCustomGrid.SetCol(Value: Integer);
begin
  if (Value < FFixedCols) or (Value >= FColCount) then
    GridError(SGridIndexOutOfRange);
  MoveFocus(Value, FAnchor.Y, False);
end;

procedure TCustomGrid.SetRow(Value: Integer);
begin
  if (Value < FFixedRows) or (Value >= FRowCount) then
    GridError(SGridIndexOutOfRange);
  MoveFocus(FAnchor.X, Value, False);
end;

{ goRowSelect: whole rows; goRangeSelect: the rectangle of the focused cell and the moving corner; else the focused
  cell }
function TCustomGrid.GetSelection: TGridRect;
var
  A, C: TGridCoord;
begin
  A := FAnchor;
  C := FCurrent;
  if not (goRangeSelect in FOptions) then
    C := A;
  if A.X <= C.X then
  begin
    Result.Left := A.X;
    Result.Right := C.X;
  end
  else
  begin
    Result.Left := C.X;
    Result.Right := A.X;
  end;
  if A.Y <= C.Y then
  begin
    Result.Top := A.Y;
    Result.Bottom := C.Y;
  end
  else
  begin
    Result.Top := C.Y;
    Result.Bottom := A.Y;
  end;
  if goRowSelect in FOptions then
  begin
    Result.Left := FFixedCols;
    Result.Right := FColCount - 1;
  end;
end;

{ the focused cell goes to the top-left corner, the moving corner to the bottom-right (without goRangeSelect the
  selection is that one cell) }
procedure TCustomGrid.SetSelection(const Value: TGridRect);
var
  L, T, R, B: Integer;
begin
  L := Value.Left;
  R := Value.Right;
  T := Value.Top;
  B := Value.Bottom;
  if L > R then
  begin
    L := Value.Right;
    R := Value.Left;
  end;
  if T > B then
  begin
    T := Value.Bottom;
    B := Value.Top;
  end;
  if (L < FFixedCols) or (R >= FColCount) or (T < FFixedRows) or (B >= FRowCount) then
    GridError(SGridIndexOutOfRange);
  FAnchor := Coord(L, T);
  if goRangeSelect in FOptions then
    FCurrent := Coord(R, B)
  else
    FCurrent := FAnchor;
  Invalidate;
end;

function TCustomGrid.InSelection(ACol, ARow: Integer): Boolean;
var
  S: TGridRect;
begin
  S := GetSelection;
  Result := (ACol >= S.Left) and (ACol <= S.Right) and (ARow >= S.Top) and (ARow <= S.Bottom);
end;

{ ---------------------------------------------------------------- selection, mouse and keys (UIF-843, UIF-844) }

function TCustomGrid.SelectCell(ACol, ARow: Integer): Boolean;
begin
  Result := True;
  if Assigned(FOnSelectCell) then
    FOnSelectCell(Self, ACol, ARow, Result);
end;

{ UIF-845: the smallest scrolling that shows the whole cell (a cell wider than the client area: at LeftCol) }
procedure TCustomGrid.ScrollIntoView(ACol, ARow: Integer);
var
  L, T, Ext: Integer;
begin
  L := FLeftCol;
  if ACol >= FFixedCols then
  begin
    if ACol < FLeftCol then
      L := ACol
    else
    begin
      L := ACol;
      Ext := FixedWidth + FColWidths[ACol];
      while (L > FFixedCols) and (Ext + FColWidths[L - 1] + FGridLineWidth <= ClientWidth) do
      begin
        Ext := Ext + FColWidths[L - 1] + FGridLineWidth;
        Dec(L);
      end;
      if L < FLeftCol then
        L := FLeftCol;
    end;
  end;
  T := FTopRow;
  if ARow >= FFixedRows then
  begin
    if ARow < FTopRow then
      T := ARow
    else
    begin
      T := ARow;
      Ext := FixedHeight + FRowHeights[ARow];
      while (T > FFixedRows) and (Ext + FRowHeights[T - 1] + FGridLineWidth <= ClientHeight) do
      begin
        Ext := Ext + FRowHeights[T - 1] + FGridLineWidth;
        Dec(T);
      end;
      if T < FTopRow then
        T := FTopRow;
    end;
  end;
  ChangeTopLeft(L, T);
end;

{ Extend (goRangeSelect): the moving corner goes to the cell, the focused cell stays; else the focused cell moves
  there after OnSelectCell allowed it (no event when it is already there) and the range collapses }
procedure TCustomGrid.MoveFocus(ACol, ARow: Integer; Extend: Boolean);
begin
  if Extend and (goRangeSelect in FOptions) then
  begin
    if (FCurrent.X = ACol) and (FCurrent.Y = ARow) then
      Exit;
    FCurrent := Coord(ACol, ARow);
  end
  else
  begin
    if (ACol <> FAnchor.X) or (ARow <> FAnchor.Y) then
    begin
      { UIF-846: moving to another cell commits the editor first }
      if FEditing then
      begin
        UpdateText;
        HideEditor;
      end;
      if not SelectCell(ACol, ARow) then
      begin
        if goAlwaysShowEditor in FOptions then
          ShowEditor;
        Exit;
      end;
      FAnchor := Coord(ACol, ARow);
    end;
    FCurrent := FAnchor;
  end;
  ScrollIntoView(ACol, ARow);
  Invalidate;
  if (goAlwaysShowEditor in FOptions) and not FEditing then
    ShowEditor;
end;

procedure TCustomGrid.KeyDown(var Key: Word; Shift: TShiftState);
var
  Base: TGridCoord;
  C, R, Page: Integer;
  Extend: Boolean;
begin
  inherited KeyDown(Key, Shift);
  if Key = 0 then
    Exit;
  Extend := (ssShift in Shift) and (goRangeSelect in FOptions);
  if Extend then
    Base := FCurrent
  else
    Base := FAnchor;
  C := Base.X;
  R := Base.Y;
  Page := VisibleRowCount;
  if Page < 1 then
    Page := 1;
  if (Key = VK_RETURN) or (Key = VK_F2) then
  begin
    if CanEditShow then
      if not FEditing then
        ShowEditor
      else if Key = VK_RETURN then
        CommitEdit;
    Exit;
  end;
  case Key of
    VK_LEFT:
      Dec(C);
    VK_RIGHT:
      Inc(C);
    VK_UP:
      Dec(R);
    VK_DOWN:
      Inc(R);
    VK_PRIOR:
      R := R - Page;
    VK_NEXT:
      R := R + Page;
    VK_HOME:
      begin
        C := FFixedCols;
        if ssCtrl in Shift then
          R := FFixedRows;
      end;
    VK_END:
      begin
        C := FColCount - 1;
        if ssCtrl in Shift then
          R := FRowCount - 1;
      end;
    VK_TAB:
      begin
        if not (goTabs in FOptions) then
          Exit;
        Extend := False;
        C := FAnchor.X;
        R := FAnchor.Y;
        if ssShift in Shift then
        begin
          Dec(C);
          if C < FFixedCols then
          begin
            if R <= FFixedRows then
              Exit;
            C := FColCount - 1;
            Dec(R);
          end;
        end
        else
        begin
          Inc(C);
          if C >= FColCount then
          begin
            if R >= FRowCount - 1 then
              Exit;
            C := FFixedCols;
            Inc(R);
          end;
        end;
      end;
  else
    Exit;
  end;
  if C < FFixedCols then
    C := FFixedCols;
  if C >= FColCount then
    C := FColCount - 1;
  if R < FFixedRows then
    R := FFixedRows;
  if R >= FRowCount then
    R := FRowCount - 1;
  MoveFocus(C, R, Extend);
end;

procedure TCustomGrid.MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
var
  C, R, Index: Integer;
begin
  if (Button = mbLeft) and CanFocus and not Focused then
    SetFocus;
  inherited MouseDown(Button, Shift, X, Y);
  if Button <> mbLeft then
    Exit;
  Index := 0;
  FSizing := SizingAt(X, Y, Index);
  if FSizing <> 0 then
  begin
    FSizingIndex := Index;
    if FSizing = 1 then
    begin
      FSizingStart := ColumnX(Index);
      FSizingOld := FColWidths[Index];
    end
    else
    begin
      FSizingStart := RowY(Index);
      FSizingOld := FRowHeights[Index];
    end;
    Exit;
  end;
  C := -1;
  R := -1;
  MouseToCell(X, Y, C, R);
  if (C < FFixedCols) or (R < FFixedRows) then
    Exit;
  MoveFocus(C, R, ssShift in Shift);
  FSelecting := goRangeSelect in FOptions;
end;

procedure TCustomGrid.MouseMove(Shift: TShiftState; X, Y: Integer);
var
  C, R: Integer;
begin
  inherited MouseMove(Shift, X, Y);
  if FSizing = 1 then
  begin
    ApplySizing(X);
    Exit;
  end;
  if FSizing = 2 then
  begin
    ApplySizing(Y);
    Exit;
  end;
  C := 0;
  FHover := SizingAt(X, Y, C);
  if not FSelecting or not (ssLeft in Shift) then
    Exit;
  C := -1;
  R := -1;
  MouseToCell(X, Y, C, R);
  if (C < FFixedCols) or (R < FFixedRows) then
    Exit;
  MoveFocus(C, R, True);
end;

procedure TCustomGrid.MouseUp(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  if Button = mbLeft then
  begin
    FSelecting := False;
    if FSizing = 1 then
      ApplySizing(X)
    else if FSizing = 2 then
      ApplySizing(Y);
    FSizing := 0;
  end;
  inherited MouseUp(Button, Shift, X, Y);
end;

procedure TCustomGrid.KeyPress(var Key: Char);
begin
  inherited KeyPress(Key);
  if (Key >= ' ') and CanEditShow and not FEditing then
  begin
    ShowEditorChar(Key);
    Key := #0;
  end;
end;

{ ---------------------------------------------------------------- column and row sizing (UIF-847) }

{ the kind of line under a client point: 1 = a column line inside the fixed rows (goColSizing), 2 = a row line
  inside the fixed columns (goRowSizing), 0 = none; the zone is the line and 3 px on each side; Index = the cell
  before the line (the last match wins) }
function TCustomGrid.SizingAt(X, Y: Integer; var Index: Integer): Integer;
const
  Zone = 3;
var
  C, P, E: Integer;
  R: TRect;
begin
  Result := 0;
  R := ClientRect;
  if (X < 0) or (Y < 0) or (X >= R.Right) or (Y >= R.Bottom) then
    Exit;
  if (goColSizing in FOptions) and (Y < FixedHeight) then
  begin
    P := 0;
    C := FirstShownCol;
    while (C >= 0) and (P < R.Right) do
    begin
      E := P + FColWidths[C];
      if (X >= E - Zone) and (X < E + FGridLineWidth + Zone) then
      begin
        Index := C;
        Result := 1;
      end;
      P := E + FGridLineWidth;
      C := NextShownCol(C);
    end;
  end;
  if (Result = 0) and (goRowSizing in FOptions) and (X < FixedWidth) then
  begin
    P := 0;
    C := FirstShownRow;
    while (C >= 0) and (P < R.Bottom) do
    begin
      E := P + FRowHeights[C];
      if (Y >= E - Zone) and (Y < E + FGridLineWidth + Zone) then
      begin
        Index := C;
        Result := 2;
      end;
      P := E + FGridLineWidth;
      C := NextShownRow(C);
    end;
  end;
end;

procedure TCustomGrid.ApplySizing(Pos: Integer);
var
  V: Integer;
begin
  V := Pos - FSizingStart;
  if V < 0 then
    V := 0;
  if FSizing = 1 then
    SetColWidths(FSizingIndex, V)
  else if FSizing = 2 then
    SetRowHeights(FSizingIndex, V);
end;

function TCustomGrid.DefaultCursor: TCursor;
var
  K: Integer;
begin
  K := FSizing;
  if K = 0 then
    K := FHover;
  case K of
    1:
      Result := crVSplit;
    2:
      Result := crHSplit;
  else
    Result := inherited DefaultCursor;
  end;
end;

{ ---------------------------------------------------------------- in-place editor (UIF-846) }

function TCustomGrid.CanEditShow: Boolean;
begin
  Result := (goEditing in FOptions) and not (goRowSelect in FOptions);
end;

function TCustomGrid.GetEditText(ACol, ARow: Integer): string;
begin
  Result := GetCellText(ACol, ARow);
  if Assigned(FOnGetEditText) then
    FOnGetEditText(Self, ACol, ARow, Result);
end;

procedure TCustomGrid.SetEditText(ACol, ARow: Integer; const Value: string);
begin
  if Assigned(FOnSetEditText) then
    FOnSetEditText(Self, ACol, ARow, Value);
end;

function TCustomGrid.GetEditorMode: Boolean;
begin
  Result := FEditing;
end;

procedure TCustomGrid.SetEditorMode(Value: Boolean);
begin
  if Value then
  begin
    if not FEditing then
      ShowEditor;
  end
  else if FEditing then
  begin
    UpdateText;
    HideEditor;
  end;
end;

function TCustomGrid.GetInplaceEditor: TEdit;
begin
  Result := FEditor;
end;

{ opens the editor on the focused cell with GetEditText, all text selected, and focuses it when the grid can }
procedure TCustomGrid.ShowEditor;
var
  Text: string;
begin
  if not CanEditShow or (csDestroying in ComponentState) then
    Exit;
  if FEditor = nil then
  begin
    FEditor := TInplaceEdit.Create(Self);
    FEditor.FGrid := Self;
    FEditor.Visible := False;
    FEditor.Parent := Self;
  end;
  Text := GetEditText(FAnchor.X, FAnchor.Y);
  FEditor.Text := Text;
  FEditing := True;
  UpdateEditorBounds;
  FEditor.Visible := True;
  FEditor.SelectAll;
  if FEditor.CanFocus then
    FEditor.SetFocus;
end;

procedure TCustomGrid.ShowEditorChar(Ch: Char);
begin
  ShowEditor;
  if FEditing then
  begin
    FEditor.Text := Ch;
    FEditor.SelStart := 1;
  end;
end;

{ closes the editor without a commit; the focus goes back to the grid when the editor had it }
procedure TCustomGrid.HideEditor;
var
  HadFocus: Boolean;
begin
  if not FEditing then
    Exit;
  FEditing := False;
  if FEditor = nil then
    Exit;
  HadFocus := FEditor.HandleAllocated and (GetFocus = FEditor.Handle);
  FEditor.Visible := False;
  if HadFocus and HandleAllocated then
    Windows.SetFocus(Handle);
end;

{ the commit of UIF-846: SetEditText with the editor's text }
procedure TCustomGrid.UpdateText;
begin
  if FEditing and (FEditor <> nil) then
    SetEditText(FAnchor.X, FAnchor.Y, FEditor.Text);
end;

{ Enter: commit; the editor closes unless goAlwaysShowEditor }
procedure TCustomGrid.CommitEdit;
begin
  UpdateText;
  if goAlwaysShowEditor in FOptions then
    FEditor.SelectAll
  else
    HideEditor;
end;

{ Escape: no commit; with goAlwaysShowEditor the editor takes the text again }
procedure TCustomGrid.CancelEdit;
begin
  if not FEditing then
    Exit;
  if goAlwaysShowEditor in FOptions then
  begin
    FEditor.Text := GetEditText(FAnchor.X, FAnchor.Y);
    FEditor.SelectAll;
  end
  else
    HideEditor;
end;

procedure TCustomGrid.UpdateEditorBounds;
var
  R: TRect;
begin
  if not FEditing or (FEditor = nil) then
    Exit;
  R := CellRect(FAnchor.X, FAnchor.Y);
  FEditor.SetBounds(R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top);
end;

{ ---------------------------------------------------------------- painting (UIF-842) }

function TCustomGrid.GetCellText(ACol, ARow: Integer): string;
begin
  Result := '';
end;

procedure TCustomGrid.DrawCell(ACol, ARow: Integer; ARect: TRect; AState: TGridDrawState);
var
  S: string;
begin
  if Assigned(FOnDrawCell) then
  begin
    FOnDrawCell(Self, ACol, ARow, ARect, AState);
    Exit;
  end;
  S := GetCellText(ACol, ARow);
  if S = '' then
    Exit;
  Canvas.Brush.Style := bsClear;
  Canvas.TextRect(ARect, ARect.Left + 2, ARect.Top + 2, S);
end;

{ background, the cell's right and bottom grid lines (in the cell colour when the line option is off), then
  DrawCell with the font colour of the background }
procedure TCustomGrid.PaintCell(ACol, ARow: Integer; const ARect: TRect);
var
  Fixed, Highlight, VertOn, HorzOn: Boolean;
  State: TGridDrawState;
  Back, Line: TColor;
begin
  Fixed := (ACol < FFixedCols) or (ARow < FFixedRows);
  State := [];
  if Fixed then
    Include(State, gdFixed)
  else if InSelection(ACol, ARow) then
    Include(State, gdSelected);
  if (ACol = FAnchor.X) and (ARow = FAnchor.Y) then
    Include(State, gdFocused);
  Highlight := (gdSelected in State) and (not (gdFocused in State) or (goDrawFocusSelected in FOptions)
    or (goRowSelect in FOptions));
  if Fixed then
    Back := FFixedColor
  else if Highlight then
    Back := clHighlight
  else
    Back := Color;
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := Back;
  Canvas.FillRect(ARect);
  if FGridLineWidth > 0 then
  begin
    if Fixed then
    begin
      VertOn := goFixedVertLine in FOptions;
      HorzOn := goFixedHorzLine in FOptions;
      Line := clBtnShadow;
    end
    else
    begin
      VertOn := goVertLine in FOptions;
      HorzOn := goHorzLine in FOptions;
      Line := clSilver;
    end;
    if VertOn then
      Canvas.Brush.Color := Line
    else
      Canvas.Brush.Color := Back;
    Canvas.FillRect(Rect(ARect.Right, ARect.Top, ARect.Right + FGridLineWidth, ARect.Bottom + FGridLineWidth));
    if HorzOn then
      Canvas.Brush.Color := Line
    else
      Canvas.Brush.Color := Back;
    Canvas.FillRect(Rect(ARect.Left, ARect.Bottom, ARect.Right, ARect.Bottom + FGridLineWidth));
  end;
  Canvas.Brush.Color := Back;
  Canvas.Font := Font;
  if Highlight then
    Canvas.Font.Color := clHighlightText;
  DrawCell(ACol, ARow, ARect, State);
end;

procedure TCustomGrid.Paint;
var
  C, R, X, Y, CW, CH, Saved: Integer;
  Focus: TRect;
begin
  CW := ClientWidth;
  CH := ClientHeight;
  Saved := SaveDC(Canvas.Handle);
  try
    IntersectClipRect(Canvas.Handle, 0, 0, CW, CH);
    Y := 0;
    R := FirstShownRow;
    while (R >= 0) and (Y < CH) do
    begin
      X := 0;
      C := FirstShownCol;
      while (C >= 0) and (X < CW) do
      begin
        PaintCell(C, R, Rect(X, Y, X + FColWidths[C], Y + FRowHeights[R]));
        X := X + FColWidths[C] + FGridLineWidth;
        C := NextShownCol(C);
      end;
      if X < CW then
      begin
        Canvas.Brush.Style := bsSolid;
        Canvas.Brush.Color := Color;
        Canvas.FillRect(Rect(X, Y, CW, Y + FRowHeights[R] + FGridLineWidth));
      end;
      Y := Y + FRowHeights[R] + FGridLineWidth;
      R := NextShownRow(R);
    end;
    if Y < CH then
    begin
      Canvas.Brush.Style := bsSolid;
      Canvas.Brush.Color := Color;
      Canvas.FillRect(Rect(0, Y, CW, CH));
    end;
    if HandleAllocated and (GetFocus = Handle) then
    begin
      Focus := CellRect(FAnchor.X, FAnchor.Y);
      if goRowSelect in FOptions then
      begin
        Focus.Left := FixedWidth;
        Focus.Right := CW;
      end;
      if Focus.Right > Focus.Left then
        Canvas.DrawFocusRect(Focus);
    end;
  finally
    RestoreDC(Canvas.Handle, Saved);
  end;
end;

{ ---------------------------------------------------------------- TInplaceEdit }

constructor TInplaceEdit.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  AutoSize := False;
  BorderStyle := bsNone;
  TabStop := False;
end;

procedure TInplaceEdit.ForwardKey(var Key: Word; Shift: TShiftState);
begin
  FGrid.KeyDown(Key, Shift);
  Key := 0;
end;

procedure TInplaceEdit.KeyDown(var Key: Word; Shift: TShiftState);
begin
  inherited KeyDown(Key, Shift);
  if (Key = 0) or (FGrid = nil) or not FGrid.FEditing then
    Exit;
  case Key of
    VK_RETURN:
      begin
        FGrid.CommitEdit;
        Key := 0;
      end;
    VK_ESCAPE:
      begin
        FGrid.CancelEdit;
        Key := 0;
      end;
    VK_UP, VK_DOWN, VK_PRIOR, VK_NEXT:
      ForwardKey(Key, Shift);
    VK_LEFT:
      if (SelStart = 0) and (SelLength = 0) then
        ForwardKey(Key, Shift);
    VK_RIGHT:
      if (SelStart = Length(Text)) and (SelLength = 0) then
        ForwardKey(Key, Shift);
    VK_TAB:
      if goTabs in FGrid.FOptions then
        ForwardKey(Key, Shift);
  end;
end;

procedure TInplaceEdit.KeyPress(var Key: Char);
begin
  if (Key = #13) or (Key = #27) or ((Key = #9) and (FGrid <> nil) and (goTabs in FGrid.FOptions)) then
    Key := #0;
  inherited KeyPress(Key);
end;

procedure TInplaceEdit.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_WANTSPECIALKEY:
      if (Message.WParam = VK_RETURN) or (Message.WParam = VK_ESCAPE)
        or ((Message.WParam = VK_TAB) and (FGrid <> nil) and (goTabs in FGrid.FOptions)) then
        Message.Result := 1
      else
        inherited WndProc(Message);
    WM_GETDLGCODE:
      begin
        inherited WndProc(Message);
        if (FGrid <> nil) and (goTabs in FGrid.FOptions) then
          Message.Result := Message.Result or DLGC_WANTTAB;
      end;
    WM_KILLFOCUS:
      begin
        inherited WndProc(Message);
        if (FGrid <> nil) and FGrid.FEditing then
        begin
          FGrid.UpdateText;
          if not (goAlwaysShowEditor in FGrid.FOptions) then
            FGrid.HideEditor;
        end;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ ---------------------------------------------------------------- TStringGridStrings }

type
  { UIF-848: a live view of one column or one row of a string grid }
  TStringGridStrings = class(TStrings)
  private
    FGrid: TStringGrid;
    FIndex: Integer;
    FIsRow: Boolean;
  protected
    function Get(Index: Integer): string; override;
    function GetCount: Integer; override;
    procedure Put(Index: Integer; const S: string); override;
    function GetObject(Index: Integer): TObject; override;
    procedure PutObject(Index: Integer; AObject: TObject); override;
  public
    constructor Create(AGrid: TStringGrid; AIndex: Integer; AIsRow: Boolean);
    { the cells take the source's strings and objects, the rest are emptied (the count stays) }
    procedure Assign(Source: TPersistent); override;
    { empties every cell of the view }
    procedure Clear; override;
    procedure Delete(Index: Integer); override;
    procedure Insert(Index: Integer; const S: string); override;
  end;

constructor TStringGridStrings.Create(AGrid: TStringGrid; AIndex: Integer; AIsRow: Boolean);
begin
  inherited Create;
  FGrid := AGrid;
  FIndex := AIndex;
  FIsRow := AIsRow;
end;

function TStringGridStrings.GetCount: Integer;
begin
  if FIsRow then
    Result := FGrid.ColCount
  else
    Result := FGrid.RowCount;
end;

function TStringGridStrings.Get(Index: Integer): string;
begin
  if FIsRow then
    Result := FGrid.Cells[Index, FIndex]
  else
    Result := FGrid.Cells[FIndex, Index];
end;

procedure TStringGridStrings.Put(Index: Integer; const S: string);
begin
  if FIsRow then
    FGrid.Cells[Index, FIndex] := S
  else
    FGrid.Cells[FIndex, Index] := S;
end;

function TStringGridStrings.GetObject(Index: Integer): TObject;
begin
  if FIsRow then
    Result := FGrid.Objects[Index, FIndex]
  else
    Result := FGrid.Objects[FIndex, Index];
end;

procedure TStringGridStrings.PutObject(Index: Integer; AObject: TObject);
begin
  if FIsRow then
    FGrid.Objects[Index, FIndex] := AObject
  else
    FGrid.Objects[FIndex, Index] := AObject;
end;

procedure TStringGridStrings.Assign(Source: TPersistent);
var
  I: Integer;
  Src: TStrings;
begin
  if not (Source is TStrings) then
  begin
    inherited Assign(Source);
    Exit;
  end;
  Src := TStrings(Source);
  for I := 0 to GetCount - 1 do
    if I < Src.Count then
    begin
      Put(I, Src[I]);
      PutObject(I, Src.Objects[I]);
    end
    else
    begin
      Put(I, '');
      PutObject(I, nil);
    end;
end;

procedure TStringGridStrings.Clear;
var
  I: Integer;
begin
  for I := 0 to GetCount - 1 do
  begin
    Put(I, '');
    PutObject(I, nil);
  end;
end;

procedure TStringGridStrings.Delete(Index: Integer);
begin
  GridError(SGridRowsFixed);
end;

procedure TStringGridStrings.Insert(Index: Integer; const S: string);
begin
  GridError(SGridRowsFixed);
end;

{ ---------------------------------------------------------------- TStringGrid }

constructor TStringGrid.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FColViews := TList.Create;
  FRowViews := TList.Create;
  SetLength(FData, RowCount);
end;

destructor TStringGrid.Destroy;
var
  I: Integer;
begin
  inherited Destroy;
  for I := 0 to Length(FData) - 1 do
    FData[I].Free;
  for I := 0 to FColViews.Count - 1 do
    TObject(FColViews[I]).Free;
  for I := 0 to FRowViews.Count - 1 do
    TObject(FRowViews[I]).Free;
  FColViews.Free;
  FRowViews.Free;
end;

{ the storage of a row, created on first write with ColCount empty cells }
function TStringGrid.DataRow(ARow: Integer): TStringList;
var
  I: Integer;
begin
  Result := FData[ARow];
  if Result = nil then
  begin
    Result := TStringList.Create;
    for I := 0 to ColCount - 1 do
      Result.Add('');
    FData[ARow] := Result;
  end;
end;

function TStringGrid.GetCells(ACol, ARow: Integer): string;
begin
  CheckCol(ACol);
  CheckRow(ARow);
  if FData[ARow] = nil then
    Result := ''
  else
    Result := FData[ARow][ACol];
end;

procedure TStringGrid.SetCells(ACol, ARow: Integer; const Value: string);
begin
  CheckCol(ACol);
  CheckRow(ARow);
  DataRow(ARow)[ACol] := Value;
  InvalidateCell(ACol, ARow);
end;

function TStringGrid.GetObjects(ACol, ARow: Integer): TObject;
begin
  CheckCol(ACol);
  CheckRow(ARow);
  if FData[ARow] = nil then
    Result := nil
  else
    Result := FData[ARow].Objects[ACol];
end;

procedure TStringGrid.SetObjects(ACol, ARow: Integer; Value: TObject);
begin
  CheckCol(ACol);
  CheckRow(ARow);
  DataRow(ARow).Objects[ACol] := Value;
end;

function TStringGrid.GetCols(Index: Integer): TStrings;
begin
  CheckCol(Index);
  while FColViews.Count <= Index do
    FColViews.Add(nil);
  if FColViews[Index] = nil then
    FColViews[Index] := TStringGridStrings.Create(Self, Index, False);
  Result := TStrings(FColViews[Index]);
end;

function TStringGrid.GetRows(Index: Integer): TStrings;
begin
  CheckRow(Index);
  while FRowViews.Count <= Index do
    FRowViews.Add(nil);
  if FRowViews[Index] = nil then
    FRowViews[Index] := TStringGridStrings.Create(Self, Index, True);
  Result := TStrings(FRowViews[Index]);
end;

procedure TStringGrid.InvalidateCell(ACol, ARow: Integer);
var
  R: TRect;
begin
  if not HandleAllocated then
    Exit;
  R := CellRect(ACol, ARow);
  if R.Right > R.Left then
    InvalidateRect(Handle, @R, False);
end;

function TStringGrid.GetCellText(ACol, ARow: Integer): string;
begin
  if FData[ARow] = nil then
    Result := ''
  else
    Result := FData[ARow][ACol];
end;

procedure TStringGrid.SetEditText(ACol, ARow: Integer; const Value: string);
begin
  inherited SetEditText(ACol, ARow, Value);
  SetCells(ACol, ARow, Value);
end;

procedure TStringGrid.SizeChanged(OldColCount, OldRowCount: Integer);
var
  I: Integer;
  L: TStringList;
begin
  for I := RowCount to Length(FData) - 1 do
  begin
    FData[I].Free;
    FData[I] := nil;
  end;
  SetLength(FData, RowCount);
  for I := 0 to Length(FData) - 1 do
  begin
    L := FData[I];
    if L <> nil then
    begin
      while L.Count > ColCount do
        L.Delete(L.Count - 1);
      while L.Count < ColCount do
        L.Add('');
    end;
  end;
end;

initialization
  RegisterClasses([TStringGrid]);
end.
