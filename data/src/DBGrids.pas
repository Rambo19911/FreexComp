{ DBGrids - the data grid: the rows of a dataset as lines and its fields as columns, drawn by the control, with an
  in-place editor for the selected cell. The grid reads the rows through the row window of its data link; only
  the visible rows are ever read. }
unit DBGrids;

interface

uses
  Windows, Messages, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, DB, DBCtrls;

type
  TDBGridOption = (dgEditing, dgAlwaysShowEditor, dgTitles, dgIndicator, dgColLines, dgRowLines, dgTabs,
    dgRowSelect, dgAlwaysShowSelection, dgConfirmDelete, dgCancelOnExit);
  TDBGridOptions = set of TDBGridOption;

  { what the indicator column shows in a row: nothing, or the mark of the current row by the dataset state }
  TDBGridIndicator = (giNone, giBrowse, giEdit, giInsert);

  TDBGrid = class;

  { One column of the grid: a field, a title, a width and an alignment. }
  TColumn = class(TCollectionItem)
  private
    FFieldName: string;
    FTitle: string;
    FWidth: Integer;
    FAlignment: TAlignment;
    { Alignment was assigned: the field no longer decides it }
    FAlignmentSet: Boolean;
    FReadOnly: Boolean;
    FVisible: Boolean;
    function GetGrid: TDBGrid;
    function GetField: TField;
    function GetAlignment: TAlignment;
    procedure SetFieldName(const Value: string);
    procedure SetTitle(const Value: string);
    procedure SetWidth(Value: Integer);
    procedure SetAlignment(Value: TAlignment);
    procedure SetReadOnly(Value: Boolean);
    procedure SetVisible(Value: Boolean);
  public
    constructor Create(Collection: TCollection); override;
    { the shown title: Title, else the display label of the field, else the field name }
    function TitleText: string;
    { the field of the grid's dataset named by FieldName; nil while the dataset is closed or has no such field }
    property Field: TField read GetField;
    property Grid: TDBGrid read GetGrid;
  published
    property FieldName: string read FFieldName write SetFieldName;
    { '' shows the display label of the field }
    property Title: string read FTitle write SetTitle;
    { logical pixels; -1: computed from the display width of the field and the title }
    property Width: Integer read FWidth write SetWidth default -1;
    { follows the alignment of the field until it is assigned }
    property Alignment: TAlignment read GetAlignment write SetAlignment stored FAlignmentSet;
    property ReadOnly: Boolean read FReadOnly write SetReadOnly default False;
    property Visible: Boolean read FVisible write SetVisible default True;
  end;

  TDBGridColumns = class(TCollection)
  private
    FGrid: TDBGrid;
    function GetItem(Index: Integer): TColumn;
    procedure SetItem(Index: Integer; Value: TColumn);
  protected
    function GetOwner: TPersistent; override;
    procedure Update(Item: TCollectionItem); override;
  public
    constructor Create(AGrid: TDBGrid);
    function Add: TColumn;
    property Grid: TDBGrid read FGrid;
    property Items[Index: Integer]: TColumn read GetItem write SetItem; default;
  end;

  TColumnEvent = procedure(Column: TColumn) of object;

  { The data grid. Without persistent columns it shows one column per visible field. }
  TDBGrid = class(TCustomControl)
  private
    FDataLink: TDataLink;
    FColumns: TDBGridColumns;
    { the columns built from the fields while Columns is empty }
    FAutoColumns: TDBGridColumns;
    { the shown columns, in order: the visible persistent columns, else the automatic ones }
    FShown: TList;
    FBuilding: Boolean;
    FOptions: TDBGridOptions;
    FReadOnly: Boolean;
    FSelected: Integer;
    FLeftCol: Integer;
    FShowHorz: Boolean;
    FShowVert: Boolean;
    { the font metrics the geometry is built from }
    FMetricsValid: Boolean;
    FLineHeight: Integer;
    FZeroWidth: Integer;
    { the grid or its editor is the entered control of the form }
    FEntered: Boolean;
    { the focus is moving between the grid and its editor: not an exit }
    FFocusInside: Integer;
    FEditor: TEdit;
    FEditorOpen: Boolean;
    { the user changed the text of the editor since it was loaded }
    FEditorModified: Boolean;
    FOnCellClick: TColumnEvent;
    FOnTitleClick: TColumnEvent;
    FOnColEnter: TNotifyEvent;
    FOnColExit: TNotifyEvent;
    function GetDataSource: TDataSource;
    procedure SetDataSource(Value: TDataSource);
    procedure SetColumns(Value: TDBGridColumns);
    procedure SetOptions(Value: TDBGridOptions);
    procedure SetReadOnly(Value: Boolean);
    procedure SetSelectedIndex(Value: Integer);
    procedure SetLeftCol(Value: Integer);
    function GetColCount: Integer;
    function GetVisibleColumn(Index: Integer): TColumn;
    function GetSelectedField: TField;
    function GetVisibleRowCount: Integer;
    procedure SetEditorMode(Value: Boolean);
    { geometry }
    function GridDPI: Integer;
    function Scaled(Value: Integer): Integer;
    procedure EnsureMetrics;
    function RowHeight: Integer;
    function TitleHeight: Integer;
    function IndicatorWidth: Integer;
    function ColumnWidth(ACol: Integer): Integer;
    function ColumnsWidth: Integer;
    function ColumnLeft(ACol: Integer): Integer;
    function RowsFit(AHeight: Integer): Integer;
    function MaxLeftCol(AWidth: Integer): Integer;
    function FullColumns: Integer;
    function BarSize: Integer;
    procedure HitTest(X, Y: Integer; out ACol, ARow: Integer);
    { the scroll bars that are needed, the row window and the native bars }
    procedure UpdateLayout;
    procedure UpdateNativeBars;
    procedure ScrollSelectedIntoView;
    { columns }
    procedure RebuildColumns;
    procedure ColumnsChanged;
    function ColumnText(Column: TColumn): string;
    { what the link reports }
    procedure LinkActiveChanged;
    procedure LinkEditingChanged;
    procedure LinkDataSetChanged;
    procedure LinkRecordChanged(Field: TField);
    procedure LinkUpdateData;
    procedure LinkCheckBrowseMode;
    { editing }
    function CanModifyData: Boolean;
    function EditingPossible: Boolean;
    function EditorIsActive: Boolean;
    procedure FocusEditor;
    procedure LoadEditor;
    procedure UpdateEditorBounds;
    procedure UpdateEditorState;
    procedure CommitEditor;
    procedure EditorEnter;
    procedure EditorEscape;
    function EditorBeginModify: Boolean;
    procedure StartEditWith(Ch: Char);
    function StartModify: Boolean;
    { keys }
    procedure MoveDown;
    procedure TabMove(GoForward: Boolean);
    procedure DeleteRecord;
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure CreateWnd; override;
    { the window without its border and its scroll bars }
    function GetClientRect: TRect; override;
    procedure BoundsChanged(const OldBounds: TRect); override;
    procedure SetParent(AParent: TWinControl); override;
    procedure Loaded; override;
    procedure WndProc(var Message: TMessage); override;
    procedure WMHScroll(var Message: TWMScroll); override;
    procedure WMVScroll(var Message: TWMScroll); override;
    procedure Paint; override;
    { the three kinds of cells; Paint calls them for every shown cell }
    procedure DrawTitleCell(ACol: Integer; const ARect: TRect; const AText: string); virtual;
    procedure DrawDataCell(ACol, ARow: Integer; const ARect: TRect; const AText: string; AAlignment: TAlignment;
      AHighlight: Boolean); virtual;
    procedure DrawIndicator(ARow: Integer; const ARect: TRect; AMark: TDBGridIndicator); virtual;
    { everything, or one window row, has to be painted again }
    procedure InvalidateGrid; virtual;
    procedure InvalidateRow(ARow: Integer); virtual;
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    procedure KeyPress(var Key: Char); override;
    procedure MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer); override;
    function DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean; override;
    procedure DoEnter; override;
    { writes the text of the editor (when the field rejects it the editor keeps the focus) and cancels an
      untouched new record }
    procedure DoExit; override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { with an open editor the focus goes to the editor }
    procedure SetFocus; override;
    { the text of a cell: ACol counts the shown columns, ARow the rows of the row window }
    function CellText(ACol, ARow: Integer): string;
    { the rectangle of a cell, its grid lines included, in client coordinates; ACol = -1 is the indicator column,
      ARow = -1 the title row; empty for a cell that is not shown }
    function CellRect(ACol, ARow: Integer): TRect;
    { opens the in-place editor on the selected cell when editing is possible }
    procedure ShowEditor;
    { closes the editor without writing its text }
    procedure HideEditor;
    { the number of shown columns and the columns themselves }
    property ColCount: Integer read GetColCount;
    property VisibleColumns[Index: Integer]: TColumn read GetVisibleColumn;
    { index of the selected column among the shown columns }
    property SelectedIndex: Integer read FSelected write SetSelectedIndex;
    property SelectedField: TField read GetSelectedField;
    { True while the editor is open; False writes its text and closes it }
    property EditorMode: Boolean read FEditorOpen write SetEditorMode;
    { the in-place editor; nil until the first use }
    property InplaceEditor: TEdit read FEditor;
    { the whole data rows that fit below the title row; at least 1 }
    property VisibleRowCount: Integer read GetVisibleRowCount;
    { the first shown column after the indicator (horizontal scroll position) }
    property LeftCol: Integer read FLeftCol write SetLeftCol;
  published
    property DataSource: TDataSource read GetDataSource write SetDataSource;
    property Columns: TDBGridColumns read FColumns write SetColumns;
    property Options: TDBGridOptions read FOptions write SetOptions
      default [dgEditing, dgTitles, dgIndicator, dgColLines, dgRowLines, dgTabs, dgConfirmDelete, dgCancelOnExit];
    property ReadOnly: Boolean read FReadOnly write SetReadOnly default False;
    property Align;
    property Anchors;
    property Color default clWindow;
    property Font;
    property TabOrder;
    property TabStop default True;
    property Visible;
    property OnCellClick: TColumnEvent read FOnCellClick write FOnCellClick;
    property OnColEnter: TNotifyEvent read FOnColEnter write FOnColEnter;
    property OnColExit: TNotifyEvent read FOnColExit write FOnColExit;
    property OnTitleClick: TColumnEvent read FOnTitleClick write FOnTitleClick;
    property OnDblClick;
    property OnEnter;
    property OnExit;
    property OnKeyDown;
    property OnKeyPress;
  end;

implementation

type
  TFormAccess = class(TCustomForm);

  TGridDataLink = class(TDataLink)
  private
    FGrid: TDBGrid;
  protected
    procedure ActiveChanged; override;
    procedure EditingChanged; override;
    procedure DataSetChanged; override;
    procedure LayoutChanged; override;
    procedure RecordChanged(Field: TField); override;
    procedure UpdateData; override;
    procedure CheckBrowseMode; override;
  end;

  { The in-place editor: a border-less edit without a name. Changes by the user start a modification of the
    record; the keys that end the editing or leave the cell go to the grid. }
  TGridEditor = class(TEdit)
  private
    FGrid: TDBGrid;
  protected
    procedure KeyDown(var Key: Word; Shift: TShiftState); override;
    procedure KeyPress(var Key: Char); override;
    procedure WndProc(var Message: TMessage); override;
    function EditCanModify: Boolean; override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

const
  SDeleteRecordQuestion = 'Delete record?';
  SConfirmCaption = 'Confirm';
  SBlobText = '(Blob)';
  DefaultOptions = [dgEditing, dgTitles, dgIndicator, dgColLines, dgRowLines, dgTabs, dgConfirmDelete,
    dgCancelOnExit];
  MaxMemoCellLength = 255;
  WheelRows = 3;

{ a field whose value is bytes, not text }
function IsBinaryField(Field: TField): Boolean;
begin
  Result := (Field is TBlobField) and not (Field is TMemoField);
end;

{ the text up to the first line break, at most MaxMemoCellLength characters }
function FirstLine(const S: string): string;
var
  I: Integer;
begin
  I := 1;
  while (I <= Length(S)) and (I <= MaxMemoCellLength) and (S[I] <> #13) and (S[I] <> #10) do
    Inc(I);
  Result := Copy(S, 1, I - 1);
end;

{ TGridDataLink }

procedure TGridDataLink.ActiveChanged;
begin
  FGrid.LinkActiveChanged;
end;

procedure TGridDataLink.EditingChanged;
begin
  FGrid.LinkEditingChanged;
end;

procedure TGridDataLink.DataSetChanged;
begin
  FGrid.LinkDataSetChanged;
end;

procedure TGridDataLink.LayoutChanged;
begin
  FGrid.LinkActiveChanged;
end;

procedure TGridDataLink.RecordChanged(Field: TField);
begin
  FGrid.LinkRecordChanged(Field);
end;

procedure TGridDataLink.UpdateData;
begin
  FGrid.LinkUpdateData;
end;

procedure TGridDataLink.CheckBrowseMode;
begin
  FGrid.LinkCheckBrowseMode;
end;

{ TColumn }

constructor TColumn.Create(Collection: TCollection);
begin
  FWidth := -1;
  FVisible := True;
  inherited Create(Collection);
end;

function TColumn.GetGrid: TDBGrid;
begin
  if Collection is TDBGridColumns then
    Result := TDBGridColumns(Collection).FGrid
  else
    Result := nil;
end;

function TColumn.GetField: TField;
var
  G: TDBGrid;
begin
  Result := nil;
  G := GetGrid;
  if (G <> nil) and (G.FDataLink <> nil) and G.FDataLink.Active and (FFieldName <> '') then
    Result := G.FDataLink.DataSet.FindField(FFieldName);
end;

function TColumn.TitleText: string;
var
  F: TField;
begin
  Result := FTitle;
  if Result <> '' then
    Exit;
  F := GetField;
  if F <> nil then
    Result := F.DisplayLabel
  else
    Result := FFieldName;
end;

function TColumn.GetAlignment: TAlignment;
var
  F: TField;
begin
  Result := FAlignment;
  if FAlignmentSet then
    Exit;
  F := GetField;
  if F <> nil then
    Result := F.Alignment
  else
    Result := taLeftJustify;
end;

procedure TColumn.SetFieldName(const Value: string);
begin
  if FFieldName <> Value then
  begin
    FFieldName := Value;
    Changed(False);
  end;
end;

procedure TColumn.SetTitle(const Value: string);
begin
  if FTitle <> Value then
  begin
    FTitle := Value;
    Changed(False);
  end;
end;

procedure TColumn.SetWidth(Value: Integer);
begin
  if Value < -1 then
    Value := -1;
  if FWidth <> Value then
  begin
    FWidth := Value;
    Changed(False);
  end;
end;

procedure TColumn.SetAlignment(Value: TAlignment);
begin
  FAlignmentSet := True;
  FAlignment := Value;
  Changed(False);
end;

procedure TColumn.SetReadOnly(Value: Boolean);
begin
  if FReadOnly <> Value then
  begin
    FReadOnly := Value;
    Changed(False);
  end;
end;

procedure TColumn.SetVisible(Value: Boolean);
begin
  if FVisible <> Value then
  begin
    FVisible := Value;
    Changed(False);
  end;
end;

{ TDBGridColumns }

constructor TDBGridColumns.Create(AGrid: TDBGrid);
begin
  inherited Create(TColumn);
  FGrid := AGrid;
end;

function TDBGridColumns.GetOwner: TPersistent;
begin
  Result := FGrid;
end;

function TDBGridColumns.Add: TColumn;
begin
  Result := TColumn(inherited Add);
end;

function TDBGridColumns.GetItem(Index: Integer): TColumn;
begin
  Result := TColumn(inherited GetItem(Index));
end;

procedure TDBGridColumns.SetItem(Index: Integer; Value: TColumn);
begin
  inherited SetItem(Index, Value);
end;

procedure TDBGridColumns.Update(Item: TCollectionItem);
begin
  if FGrid <> nil then
    FGrid.ColumnsChanged;
end;

{ TGridEditor }

constructor TGridEditor.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  AutoSize := False;
  BorderStyle := bsNone;
  TabStop := False;
end;

procedure TGridEditor.KeyDown(var Key: Word; Shift: TShiftState);
var
  Forward: Boolean;
begin
  inherited KeyDown(Key, Shift);
  if (Key = 0) or (FGrid = nil) or not FGrid.FEditorOpen then
    Exit;
  Forward := False;
  case Key of
    VK_RETURN:
      begin
        Key := 0;
        FGrid.EditorEnter;
      end;
    VK_ESCAPE:
      begin
        Key := 0;
        FGrid.EditorEscape;
      end;
    VK_UP, VK_DOWN, VK_PRIOR, VK_NEXT:
      Forward := True;
    VK_TAB:
      Forward := dgTabs in FGrid.FOptions;
    VK_LEFT:
      Forward := (SelStart = 0) and (SelLength = 0);
    VK_RIGHT:
      Forward := (SelStart = Length(Text)) and (SelLength = 0);
  end;
  if Forward then
    try
      FGrid.KeyDown(Key, Shift);
    finally
      Key := 0;
    end;
end;

procedure TGridEditor.KeyPress(var Key: Char);
begin
  if (Key = #13) or (Key = #27) or (Key = #9) then
    Key := #0;
  inherited KeyPress(Key);
end;

function TGridEditor.EditCanModify: Boolean;
begin
  Result := (FGrid = nil) or FGrid.EditorBeginModify;
end;

procedure TGridEditor.WndProc(var Message: TMessage);
begin
  if FGrid = nil then
  begin
    inherited WndProc(Message);
    Exit;
  end;
  case Message.Msg of
    CM_WANTSPECIALKEY:
      if (Message.WParam = VK_RETURN) or (Message.WParam = VK_ESCAPE)
        or ((Message.WParam = VK_TAB) and (dgTabs in FGrid.FOptions)) then
        Message.Result := 1
      else
        inherited WndProc(Message);
    WM_GETDLGCODE:
      begin
        inherited WndProc(Message);
        if dgTabs in FGrid.FOptions then
          Message.Result := Message.Result or DLGC_WANTTAB;
      end;
    WM_SETFOCUS:
      begin
        Inc(FGrid.FFocusInside);
        try
          inherited WndProc(Message);
        finally
          Dec(FGrid.FFocusInside);
        end;
      end;
  else
    inherited WndProc(Message);
  end;
end;

{ TDBGrid }

constructor TDBGrid.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csOpaque] - [csAcceptsControls, csSetCaption];
  DoubleBuffered := True;
  FOptions := DefaultOptions;
  FShown := TList.Create;
  FColumns := TDBGridColumns.Create(Self);
  FAutoColumns := TDBGridColumns.Create(Self);
  FDataLink := TGridDataLink.Create;
  TGridDataLink(FDataLink).FGrid := Self;
  Color := clWindow;
  ParentColor := False;
  TabStop := True;
  SetBounds(Left, Top, 320, 120);
end;

destructor TDBGrid.Destroy;
begin
  FEditorOpen := False;
  FDataLink.Free;
  FDataLink := nil;
  inherited Destroy;
  FShown.Free;
  FShown := nil;
  FAutoColumns.Free;
  FColumns.Free;
end;

procedure TDBGrid.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := Params.Style or WS_CLIPCHILDREN;
  Params.ExStyle := Params.ExStyle or WS_EX_CLIENTEDGE;
  if FShowHorz then
    Params.Style := Params.Style or WS_HSCROLL;
  if FShowVert then
    Params.Style := Params.Style or WS_VSCROLL;
end;

procedure TDBGrid.CreateWnd;
begin
  inherited CreateWnd;
  UpdateNativeBars;
end;

function TDBGrid.GetClientRect: TRect;
var
  W, H: Integer;
begin
  W := Width - 4;
  H := Height - 4;
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

procedure TDBGrid.BoundsChanged(const OldBounds: TRect);
begin
  inherited BoundsChanged(OldBounds);
  if (OldBounds.Right - OldBounds.Left <> Width) or (OldBounds.Bottom - OldBounds.Top <> Height) then
  begin
    UpdateLayout;
    InvalidateGrid;
  end;
end;

procedure TDBGrid.SetParent(AParent: TWinControl);
begin
  inherited SetParent(AParent);
  if (FDataLink <> nil) and not (csDestroying in ComponentState) then
  begin
    FMetricsValid := False;
    UpdateLayout;
  end;
end;

procedure TDBGrid.Loaded;
begin
  inherited Loaded;
  RebuildColumns;
end;

procedure TDBGrid.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if (Operation = opRemove) and (FDataLink <> nil) and (AComponent = FDataLink.DataSource) then
    SetDataSource(nil);
end;

function TDBGrid.GetDataSource: TDataSource;
begin
  Result := FDataLink.DataSource;
end;

procedure TDBGrid.SetDataSource(Value: TDataSource);
begin
  if Value = FDataLink.DataSource then
    Exit;
  FDataLink.DataSource := Value;
  if Value <> nil then
    Value.FreeNotification(Self);
  LinkActiveChanged;
end;

procedure TDBGrid.SetColumns(Value: TDBGridColumns);
begin
  FColumns.Assign(Value);
end;

procedure TDBGrid.SetOptions(Value: TDBGridOptions);
begin
  if FOptions = Value then
    Exit;
  FOptions := Value;
  UpdateLayout;
  UpdateEditorState;
  InvalidateGrid;
end;

procedure TDBGrid.SetReadOnly(Value: Boolean);
begin
  if FReadOnly = Value then
    Exit;
  FReadOnly := Value;
  FDataLink.ReadOnly := Value;
  UpdateEditorState;
end;

{ ---------------------------------------------------------------- columns }

function TDBGrid.GetColCount: Integer;
begin
  Result := FShown.Count;
end;

function TDBGrid.GetVisibleColumn(Index: Integer): TColumn;
begin
  Result := TColumn(FShown[Index]);
end;

function TDBGrid.GetSelectedField: TField;
begin
  if (FSelected >= 0) and (FSelected < FShown.Count) then
    Result := TColumn(FShown[FSelected]).Field
  else
    Result := nil;
end;

{ collects the shown columns; the selection and the scroll position stay inside them }
procedure TDBGrid.RebuildColumns;
var
  I: Integer;
  DS: TDataSet;
  C: TColumn;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  FBuilding := True;
  try
    FShown.Clear;
    FAutoColumns.Clear;
    if FColumns.Count > 0 then
    begin
      for I := 0 to FColumns.Count - 1 do
        if FColumns[I].Visible then
          FShown.Add(Pointer(FColumns[I]));
    end
    else if FDataLink.Active then
    begin
      DS := FDataLink.DataSet;
      for I := 0 to DS.FieldCount - 1 do
        if DS.Fields[I].Visible then
        begin
          C := FAutoColumns.Add;
          C.FFieldName := DS.Fields[I].FieldName;
          FShown.Add(Pointer(C));
        end;
    end;
  finally
    FBuilding := False;
  end;
  if FSelected > FShown.Count - 1 then
    FSelected := FShown.Count - 1;
  if FSelected < 0 then
    FSelected := 0;
  UpdateLayout;
  UpdateEditorState;
  InvalidateGrid;
end;

procedure TDBGrid.ColumnsChanged;
begin
  if FBuilding or (csLoading in ComponentState) then
    Exit;
  if FEditorOpen then
    HideEditor;
  RebuildColumns;
end;

{ the text a column shows for the row the fields read from }
function TDBGrid.ColumnText(Column: TColumn): string;
var
  F: TField;
begin
  Result := '';
  F := Column.Field;
  if F = nil then
    Exit;
  if F is TMemoField then
  begin
    if not F.IsNull then
      Result := FirstLine(F.AsString);
  end
  else if F is TBlobField then
    Result := SBlobText
  else
    Result := F.DisplayText;
end;

function TDBGrid.CellText(ACol, ARow: Integer): string;
var
  Old: Integer;
begin
  Result := '';
  if not FDataLink.Active or (ACol < 0) or (ACol >= FShown.Count) or (ARow < 0)
    or (ARow >= FDataLink.RecordCount) then
    Exit;
  Old := FDataLink.ActiveRecord;
  try
    FDataLink.ActiveRecord := ARow;
    Result := ColumnText(TColumn(FShown[ACol]));
  finally
    FDataLink.ActiveRecord := Old;
  end;
end;

{ ---------------------------------------------------------------- geometry }

{ the resolution of the form the grid is on; 96 without a form }
function TDBGrid.GridDPI: Integer;
var
  F: TCustomForm;
begin
  Result := 96;
  F := GetParentForm(Self);
  if (F <> nil) and not LayoutDumpMode and (TFormAccess(F).PixelsPerInch > 0) then
    Result := TFormAccess(F).PixelsPerInch;
end;

function TDBGrid.Scaled(Value: Integer): Integer;
begin
  Result := LayoutMulDiv(Value, GridDPI, 96);
end;

function TDBGrid.BarSize: Integer;
begin
  Result := Scaled(17);
end;

procedure TDBGrid.EnsureMetrics;
var
  DC: HDC;
  Old: HGDIOBJ;
  TM: TTextMetricW;
  Size: TSize;
begin
  if FMetricsValid then
    Exit;
  DC := CreateCompatibleDC(0);
  try
    Old := SelectObject(DC, Font.Handle);
    FillChar(TM, SizeOf(TM), 0);
    GetTextMetricsW(DC, TM);
    FLineHeight := TM.tmHeight;
    Size.cx := 0;
    Size.cy := 0;
    GetTextExtentPoint32W(DC, '0', 1, Size);
    FZeroWidth := Size.cx;
    SelectObject(DC, Old);
  finally
    DeleteDC(DC);
  end;
  FMetricsValid := True;
end;

function TDBGrid.RowHeight: Integer;
begin
  EnsureMetrics;
  Result := FLineHeight + Scaled(4);
  if Result < 1 then
    Result := 1;
end;

function TDBGrid.TitleHeight: Integer;
begin
  if dgTitles in FOptions then
    Result := RowHeight
  else
    Result := 0;
end;

function TDBGrid.IndicatorWidth: Integer;
begin
  if dgIndicator in FOptions then
    Result := Scaled(12)
  else
    Result := 0;
end;

function TDBGrid.ColumnWidth(ACol: Integer): Integer;
var
  C: TColumn;
  F: TField;
  N: Integer;
begin
  C := TColumn(FShown[ACol]);
  if C.Width >= 0 then
  begin
    Result := Scaled(C.Width);
    Exit;
  end;
  N := Length(C.TitleText);
  F := C.Field;
  if (F <> nil) and (F.DisplayWidth > N) then
    N := F.DisplayWidth;
  EnsureMetrics;
  Result := N * FZeroWidth + Scaled(4);
end;

function TDBGrid.ColumnsWidth: Integer;
var
  I: Integer;
begin
  Result := IndicatorWidth;
  for I := 0 to FShown.Count - 1 do
    Result := Result + ColumnWidth(I);
end;

{ the left edge of a shown column; -1 when it is scrolled out to the left or starts beyond the client area }
function TDBGrid.ColumnLeft(ACol: Integer): Integer;
var
  I: Integer;
begin
  Result := -1;
  if (ACol < FLeftCol) or (ACol >= FShown.Count) then
    Exit;
  Result := IndicatorWidth;
  for I := FLeftCol to ACol - 1 do
    Result := Result + ColumnWidth(I);
  if Result >= ClientWidth then
    Result := -1;
end;

function TDBGrid.RowsFit(AHeight: Integer): Integer;
begin
  Result := (AHeight - TitleHeight) div RowHeight;
  if Result < 1 then
    Result := 1;
end;

function TDBGrid.GetVisibleRowCount: Integer;
begin
  Result := RowsFit(ClientHeight);
end;

{ the smallest scroll position from which the last column is shown whole }
function TDBGrid.MaxLeftCol(AWidth: Integer): Integer;
var
  Used: Integer;
begin
  Result := FShown.Count - 1;
  if Result < 0 then
  begin
    Result := 0;
    Exit;
  end;
  Used := IndicatorWidth + ColumnWidth(Result);
  while (Result > 0) and (Used + ColumnWidth(Result - 1) <= AWidth) do
  begin
    Dec(Result);
    Used := Used + ColumnWidth(Result);
  end;
end;

{ the columns shown whole from the scroll position; at least 1 }
function TDBGrid.FullColumns: Integer;
var
  I, X, W: Integer;
begin
  Result := 0;
  X := IndicatorWidth;
  W := ClientWidth;
  I := FLeftCol;
  while (I < FShown.Count) and (X + ColumnWidth(I) <= W) do
  begin
    X := X + ColumnWidth(I);
    Inc(Result);
    Inc(I);
  end;
  if Result < 1 then
    Result := 1;
end;

function TDBGrid.CellRect(ACol, ARow: Integer): TRect;
var
  L, T, W, RH: Integer;
begin
  Result := Rect(0, 0, 0, 0);
  RH := RowHeight;
  if ARow = -1 then
  begin
    if TitleHeight = 0 then
      Exit;
    T := 0;
  end
  else if (ARow >= 0) and (ARow < VisibleRowCount) then
    T := TitleHeight + ARow * RH
  else
    Exit;
  if ACol = -1 then
  begin
    W := IndicatorWidth;
    if W = 0 then
      Exit;
    L := 0;
  end
  else
  begin
    L := ColumnLeft(ACol);
    if L < 0 then
      Exit;
    W := ColumnWidth(ACol);
  end;
  Result := Rect(L, T, L + W, T + RH);
end;

{ ACol: the shown column, -1 the indicator, -2 none; ARow: the window row, -1 the title row, -2 none }
procedure TDBGrid.HitTest(X, Y: Integer; out ACol, ARow: Integer);
var
  I, L, W: Integer;
begin
  ACol := -2;
  ARow := -2;
  if (X < 0) or (Y < 0) or (X >= ClientWidth) or (Y >= ClientHeight) then
    Exit;
  if Y < TitleHeight then
    ARow := -1
  else
  begin
    ARow := (Y - TitleHeight) div RowHeight;
    if ARow >= VisibleRowCount then
      ARow := -2;
  end;
  if X < IndicatorWidth then
    ACol := -1
  else
  begin
    L := IndicatorWidth;
    for I := FLeftCol to FShown.Count - 1 do
    begin
      W := ColumnWidth(I);
      if X < L + W then
      begin
        ACol := I;
        Break;
      end;
      L := L + W;
    end;
  end;
end;

{ ---------------------------------------------------------------- scroll bars }

{ which bars are needed: the horizontal one when the columns exceed the client width, the vertical one when the
  dataset has more records than rows fit; a shown bar takes room from the other direction }
procedure TDBGrid.UpdateLayout;
var
  W0, H0, B, Total, Count, M: Integer;
  H, V: Boolean;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  W0 := Width - 4;
  H0 := Height - 4;
  B := BarSize;
  Total := ColumnsWidth;
  if FDataLink.Active then
    Count := FDataLink.DataSet.RecordCount
  else
    Count := 0;
  H := Total > W0;
  V := Count > RowsFit(H0);
  if H and not V then
    V := Count > RowsFit(H0 - B);
  if V and not H then
    H := Total > W0 - B;
  FShowHorz := H;
  FShowVert := V;
  if not H then
    M := 0
  else
    M := MaxLeftCol(ClientWidth);
  if FLeftCol > M then
  begin
    FLeftCol := M;
    InvalidateGrid;
  end;
  FDataLink.BufferCount := VisibleRowCount;
  UpdateNativeBars;
  UpdateEditorBounds;
end;

{ an empty range hides a bar; vertical: 1 .. RecordCount at RecNo; horizontal: one position per column }
procedure TDBGrid.UpdateNativeBars;
var
  Info: TScrollInfo;
  DS: TDataSet;
begin
  if not HandleAllocated or LayoutDumpMode then
    Exit;
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  if FShowHorz then
  begin
    Info.nMin := 0;
    Info.nMax := FShown.Count - 1;
    Info.nPage := UINT(FShown.Count - MaxLeftCol(ClientWidth));
    Info.nPos := FLeftCol;
  end;
  SetScrollInfo(Handle, SB_HORZ, Info, True);
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  if FShowVert and FDataLink.Active then
  begin
    DS := FDataLink.DataSet;
    Info.nMin := 1;
    Info.nMax := DS.RecordCount;
    Info.nPos := DS.RecNo;
  end;
  SetScrollInfo(Handle, SB_VERT, Info, True);
end;

procedure TDBGrid.SetLeftCol(Value: Integer);
var
  M: Integer;
begin
  if FShowHorz then
    M := MaxLeftCol(ClientWidth)
  else
    M := 0;
  if Value > M then
    Value := M;
  if Value < 0 then
    Value := 0;
  if Value = FLeftCol then
    Exit;
  FLeftCol := Value;
  UpdateNativeBars;
  UpdateEditorBounds;
  InvalidateGrid;
end;

procedure TDBGrid.ScrollSelectedIntoView;
var
  L, I, X: Integer;
begin
  if not FShowHorz or (FSelected >= FShown.Count) then
    Exit;
  L := FLeftCol;
  if FSelected < L then
    L := FSelected
  else
    while L < FSelected do
    begin
      X := IndicatorWidth;
      for I := L to FSelected do
        X := X + ColumnWidth(I);
      if X <= ClientWidth then
        Break;
      Inc(L);
    end;
  SetLeftCol(L);
end;

procedure TDBGrid.WMHScroll(var Message: TWMScroll);
begin
  case Message.ScrollCode of
    SB_LINEUP: SetLeftCol(FLeftCol - 1);
    SB_LINEDOWN: SetLeftCol(FLeftCol + 1);
    SB_PAGEUP: SetLeftCol(FLeftCol - FullColumns);
    SB_PAGEDOWN: SetLeftCol(FLeftCol + FullColumns);
    SB_THUMBPOSITION: SetLeftCol(Message.Pos);
    SB_TOP: SetLeftCol(0);
    SB_BOTTOM: SetLeftCol(FShown.Count);
  end;
  Message.Result := 0;
end;

procedure TDBGrid.WMVScroll(var Message: TWMScroll);
var
  DS: TDataSet;
  Info: TScrollInfo;
  P: Integer;
begin
  Message.Result := 0;
  if not FDataLink.Active then
    Exit;
  DS := FDataLink.DataSet;
  case Message.ScrollCode of
    SB_LINEUP: DS.Prior;
    SB_LINEDOWN: DS.Next;
    SB_PAGEUP: DS.MoveBy(-VisibleRowCount);
    SB_PAGEDOWN: DS.MoveBy(VisibleRowCount);
    SB_TOP: DS.First;
    SB_BOTTOM: DS.Last;
    SB_THUMBPOSITION:
      begin
        P := Message.Pos;
        if DS.RecordCount > High(SmallInt) then
        begin
          { the message carries 16 bits only }
          FillChar(Info, SizeOf(Info), 0);
          Info.cbSize := SizeOf(Info);
          Info.fMask := SIF_TRACKPOS;
          if GetScrollInfo(Handle, SB_VERT, Info) then
            P := Info.nTrackPos;
        end;
        if P > DS.RecordCount then
          P := DS.RecordCount;
        if (P >= 1) and (P <> DS.RecNo) then
          DS.RecNo := P;
      end;
  end;
end;

{ ---------------------------------------------------------------- the link }

{ the link became active or inactive, the layout of the dataset changed, or the data source was replaced }
procedure TDBGrid.LinkActiveChanged;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  if FEditorOpen then
    HideEditor;
  RebuildColumns;
end;

procedure TDBGrid.LinkEditingChanged;
begin
  if csDestroying in ComponentState then
    Exit;
  InvalidateRow(FDataLink.ActiveRecord);
end;

{ the rows changed or the dataset scrolled }
procedure TDBGrid.LinkDataSetChanged;
begin
  if csDestroying in ComponentState then
    Exit;
  UpdateLayout;
  if FEditorOpen then
  begin
    if (dgAlwaysShowEditor in FOptions) and FEntered and EditingPossible then
      LoadEditor
    else
      HideEditor;
  end
  else
    UpdateEditorState;
  InvalidateGrid;
end;

procedure TDBGrid.LinkRecordChanged(Field: TField);
begin
  if csDestroying in ComponentState then
    Exit;
  if FEditorOpen and ((Field = nil) or (Field = GetSelectedField)) then
    LoadEditor;
  InvalidateRow(FDataLink.ActiveRecord);
end;

procedure TDBGrid.LinkUpdateData;
begin
  CommitEditor;
end;

{ a text typed into the editor makes the record modified before the dataset decides between post and cancel }
procedure TDBGrid.LinkCheckBrowseMode;
begin
  if FEditorOpen and FEditorModified then
    FDataLink.UpdateRecord;
end;

{ ---------------------------------------------------------------- painting }

procedure TDBGrid.InvalidateGrid;
begin
  Invalidate;
end;

procedure TDBGrid.InvalidateRow(ARow: Integer);
var
  R: TRect;
begin
  if not HandleAllocated or (ARow < 0) or (ARow >= VisibleRowCount) then
    Exit;
  R := Rect(0, TitleHeight + ARow * RowHeight, ClientWidth, TitleHeight + (ARow + 1) * RowHeight);
  InvalidateRect(Handle, @R, False);
end;

procedure TDBGrid.DrawTitleCell(ACol: Integer; const ARect: TRect; const AText: string);
var
  Inner: TRect;
begin
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := clBtnShadow;
  Canvas.FillRect(ARect);
  Inner := Rect(ARect.Left, ARect.Top, ARect.Right - 1, ARect.Bottom - 1);
  if Inner.Right <= Inner.Left then
    Exit;
  Canvas.Brush.Color := clBtnFace;
  Canvas.FillRect(Inner);
  if AText = '' then
    Exit;
  Canvas.Font.Color := clBtnText;
  Canvas.TextRect(Inner, Inner.Left + Scaled(2), Inner.Top + Scaled(2), AText);
end;

procedure TDBGrid.DrawDataCell(ACol, ARow: Integer; const ARect: TRect; const AText: string;
  AAlignment: TAlignment; AHighlight: Boolean);
var
  Inner: TRect;
  X, Pad: Integer;
begin
  Inner := ARect;
  Canvas.Brush.Style := bsSolid;
  if (dgColLines in FOptions) or (dgRowLines in FOptions) then
  begin
    Canvas.Brush.Color := clSilver;
    Canvas.FillRect(ARect);
    if dgColLines in FOptions then
      Dec(Inner.Right);
    if dgRowLines in FOptions then
      Dec(Inner.Bottom);
  end;
  if Inner.Right <= Inner.Left then
    Exit;
  if AHighlight then
  begin
    Canvas.Brush.Color := clHighlight;
    Canvas.Font.Color := clHighlightText;
  end
  else
  begin
    Canvas.Brush.Color := Color;
    Canvas.Font.Color := Font.Color;
  end;
  Canvas.FillRect(Inner);
  if AText = '' then
    Exit;
  Pad := Scaled(2);
  case AAlignment of
    taRightJustify: X := Inner.Right - Pad - Canvas.TextWidth(AText);
    taCenter: X := (Inner.Left + Inner.Right - Canvas.TextWidth(AText)) div 2;
  else
    X := Inner.Left + Pad;
  end;
  Canvas.TextRect(Inner, X, Inner.Top + Pad, AText);
end;

{ a triangle for the current row, a pencil stroke while it is edited, an asterisk while it is inserted }
procedure TDBGrid.DrawIndicator(ARow: Integer; const ARect: TRect; AMark: TDBGridIndicator);
var
  Inner: TRect;
  CX, CY, H: Integer;
begin
  Canvas.Brush.Style := bsSolid;
  Canvas.Brush.Color := clBtnShadow;
  Canvas.FillRect(ARect);
  Inner := Rect(ARect.Left, ARect.Top, ARect.Right - 1, ARect.Bottom - 1);
  Canvas.Brush.Color := clBtnFace;
  Canvas.FillRect(Inner);
  if AMark = giNone then
    Exit;
  CX := (Inner.Left + Inner.Right) div 2;
  CY := (Inner.Top + Inner.Bottom) div 2;
  H := (Inner.Right - Inner.Left) div 2 - 2;
  if H < 2 then
    H := 2;
  Canvas.Pen.Color := clBtnText;
  Canvas.Pen.Width := 1;
  Canvas.Brush.Color := clBtnText;
  case AMark of
    giBrowse:
      Canvas.Polygon([Point(CX - H div 2, CY - H), Point(CX - H div 2, CY + H), Point(CX + H div 2 + 1, CY)]);
    giEdit:
      begin
        Canvas.MoveTo(CX + H, CY - H);
        Canvas.LineTo(CX - H, CY + H);
        Canvas.MoveTo(CX + H - 1, CY - H);
        Canvas.LineTo(CX - H - 1, CY + H);
        Canvas.MoveTo(CX - H - 1, CY + H);
        Canvas.LineTo(CX + H, CY + H);
      end;
    giInsert:
      begin
        Canvas.MoveTo(CX - H, CY);
        Canvas.LineTo(CX + H + 1, CY);
        Canvas.MoveTo(CX, CY - H);
        Canvas.LineTo(CX, CY + H + 1);
        Canvas.MoveTo(CX - H + 1, CY - H + 1);
        Canvas.LineTo(CX + H, CY + H);
        Canvas.MoveTo(CX + H - 1, CY - H + 1);
        Canvas.LineTo(CX - H, CY + H);
      end;
  end;
end;

procedure TDBGrid.Paint;
var
  CW, CH, RH, TH, IW, X, Y, W, C, R, N, Cur, Saved: Integer;
  Mark: TDBGridIndicator;
  Col: TColumn;
  Show: Boolean;
begin
  CW := ClientWidth;
  CH := ClientHeight;
  RH := RowHeight;
  TH := TitleHeight;
  IW := IndicatorWidth;
  Saved := SaveDC(Canvas.Handle);
  try
    IntersectClipRect(Canvas.Handle, 0, 0, CW, CH);
    Canvas.Font := Font;
    Canvas.Brush.Style := bsSolid;
    Canvas.Brush.Color := Color;
    Canvas.FillRect(Rect(0, 0, CW, CH));
    if TH > 0 then
    begin
      if IW > 0 then
        DrawIndicator(-1, Rect(0, 0, IW, TH), giNone);
      X := IW;
      C := FLeftCol;
      while (C < FShown.Count) and (X < CW) do
      begin
        W := ColumnWidth(C);
        Canvas.Font := Font;
        DrawTitleCell(C, Rect(X, 0, X + W, TH), TColumn(FShown[C]).TitleText);
        X := X + W;
        Inc(C);
      end;
    end;
    if not FDataLink.Active then
      Exit;
    N := FDataLink.RecordCount;
    Cur := FDataLink.ActiveRecord;
    Show := FEntered or (dgAlwaysShowSelection in FOptions);
    try
      for R := 0 to N - 1 do
      begin
        Y := TH + R * RH;
        if Y >= CH then
          Break;
        FDataLink.ActiveRecord := R;
        if IW > 0 then
        begin
          Mark := giNone;
          if R = Cur then
            case FDataLink.DataSet.State of
              dsEdit: Mark := giEdit;
              dsInsert: Mark := giInsert;
            else
              Mark := giBrowse;
            end;
          DrawIndicator(R, Rect(0, Y, IW, Y + RH), Mark);
        end;
        X := IW;
        C := FLeftCol;
        while (C < FShown.Count) and (X < CW) do
        begin
          W := ColumnWidth(C);
          Col := TColumn(FShown[C]);
          Canvas.Font := Font;
          DrawDataCell(C, R, Rect(X, Y, X + W, Y + RH), ColumnText(Col), Col.Alignment,
            Show and (R = Cur) and ((dgRowSelect in FOptions) or (C = FSelected)));
          X := X + W;
          Inc(C);
        end;
      end;
    finally
      FDataLink.ActiveRecord := Cur;
    end;
  finally
    RestoreDC(Canvas.Handle, Saved);
  end;
end;

{ ---------------------------------------------------------------- selection }

procedure TDBGrid.SetSelectedIndex(Value: Integer);
var
  WasOpen: Boolean;
begin
  if Value > FShown.Count - 1 then
    Value := FShown.Count - 1;
  if Value < 0 then
    Value := 0;
  if Value = FSelected then
    Exit;
  WasOpen := FEditorOpen;
  { the text of the editor belongs to the column that is left; a rejected text keeps the selection }
  CommitEditor;
  if Assigned(FOnColExit) then
    FOnColExit(Self);
  FSelected := Value;
  ScrollSelectedIntoView;
  if Assigned(FOnColEnter) then
    FOnColEnter(Self);
  if WasOpen then
    if (dgAlwaysShowEditor in FOptions) and FEntered and EditingPossible then
      ShowEditor
    else
      HideEditor
  else
    UpdateEditorState;
  if FDataLink.Active then
    InvalidateRow(FDataLink.ActiveRecord);
end;

{ ---------------------------------------------------------------- editing }

{ the grid may change the records of the dataset }
function TDBGrid.CanModifyData: Boolean;
begin
  Result := (dgEditing in FOptions) and not FReadOnly and FDataLink.Active and FDataLink.DataSet.CanModify;
end;

function TDBGrid.EditingPossible: Boolean;
var
  C: TColumn;
  F: TField;
begin
  Result := False;
  if not CanModifyData or (dgRowSelect in FOptions) or (FSelected < 0) or (FSelected >= FShown.Count) then
    Exit;
  C := TColumn(FShown[FSelected]);
  if C.ReadOnly then
    Exit;
  F := C.Field;
  Result := (F <> nil) and F.CanModify and not IsBinaryField(F);
end;

function TDBGrid.EditorIsActive: Boolean;
var
  F: TCustomForm;
begin
  Result := False;
  if FEditor = nil then
    Exit;
  F := GetParentForm(Self);
  Result := (F <> nil) and (TFormAccess(F).ActiveControl = FEditor);
end;

procedure TDBGrid.FocusEditor;
begin
  Inc(FFocusInside);
  try
    FEditor.SetFocus;
  finally
    Dec(FFocusInside);
  end;
end;

procedure TDBGrid.SetFocus;
begin
  if FEditorOpen and (FEditor <> nil) and FEditor.CanFocus then
  begin
    FocusEditor;
    Exit;
  end;
  Inc(FFocusInside);
  try
    inherited SetFocus;
  finally
    Dec(FFocusInside);
  end;
end;

{ the editor takes the edit text of the selected field; a pending change is dropped }
procedure TDBGrid.LoadEditor;
var
  F: TField;
  S: string;
begin
  F := GetSelectedField;
  if F <> nil then
    S := F.Text
  else
    S := '';
  FEditorModified := False;
  if FEditor.Text <> S then
    FEditor.Text := S;
  UpdateEditorBounds;
  FEditor.SelectAll;
end;

procedure TDBGrid.UpdateEditorBounds;
var
  R: TRect;
begin
  if not FEditorOpen or (FEditor = nil) then
    Exit;
  R := CellRect(FSelected, FDataLink.ActiveRecord);
  if R.Right > R.Left then
  begin
    if dgColLines in FOptions then
      Dec(R.Right);
    if dgRowLines in FOptions then
      Dec(R.Bottom);
  end;
  FEditor.SetBounds(R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top);
end;

procedure TDBGrid.ShowEditor;
begin
  if (csDestroying in ComponentState) or not EditingPossible then
    Exit;
  if FEditor = nil then
  begin
    FEditor := TGridEditor.Create(Self);
    TGridEditor(FEditor).FGrid := Self;
    FEditor.Visible := False;
    FEditor.Parent := Self;
  end;
  FEditorOpen := True;
  LoadEditor;
  FEditor.Visible := True;
  FEditor.SelectAll;
  if FEditor.CanFocus and not EditorIsActive then
    FocusEditor;
end;

procedure TDBGrid.HideEditor;
begin
  if not FEditorOpen then
    Exit;
  FEditorOpen := False;
  FEditorModified := False;
  if (FEditor = nil) or (csDestroying in ComponentState) then
    Exit;
  { the focus goes back to the grid before the editor disappears }
  if EditorIsActive and CanFocus then
    SetFocus;
  if not EditorIsActive then
    FEditor.Visible := False;
end;

procedure TDBGrid.SetEditorMode(Value: Boolean);
begin
  if Value then
  begin
    if not FEditorOpen then
      ShowEditor;
  end
  else if FEditorOpen then
  begin
    CommitEditor;
    HideEditor;
  end;
end;

{ after a change of what editing depends on: an editor that is no longer possible closes; with
  dgAlwaysShowEditor the focused grid opens it }
procedure TDBGrid.UpdateEditorState;
begin
  if (FDataLink = nil) or (csDestroying in ComponentState) then
    Exit;
  if FEditorOpen then
  begin
    if not EditingPossible then
      HideEditor;
  end
  else if (dgAlwaysShowEditor in FOptions) and FEntered and EditingPossible then
    ShowEditor;
end;

{ writes a changed editor text to the field; an exception of the field leaves everything as it is }
procedure TDBGrid.CommitEditor;
var
  F: TField;
begin
  if not FEditorOpen or not FEditorModified or (FEditor = nil) then
    Exit;
  F := GetSelectedField;
  if F = nil then
    Exit;
  F.Text := FEditor.Text;
  FEditorModified := False;
end;

procedure TDBGrid.EditorEnter;
begin
  CommitEditor;
  if dgAlwaysShowEditor in FOptions then
    FEditor.SelectAll
  else
    HideEditor;
end;

procedure TDBGrid.EditorEscape;
begin
  if dgAlwaysShowEditor in FOptions then
    LoadEditor
  else
    HideEditor;
end;

{ the dataset is, or can be put, in edit mode for a change by the user }
function TDBGrid.StartModify: Boolean;
begin
  Result := False;
  if not EditingPossible then
    Exit;
  Result := FDataLink.Editing;
  if not Result and FDataLink.DataSource.AutoEdit then
    Result := FDataLink.Edit;
end;

{ the user is about to change the text of the editor }
function TDBGrid.EditorBeginModify: Boolean;
begin
  Result := FEditorOpen and StartModify;
  if Result then
    FEditorModified := True;
end;

procedure TDBGrid.StartEditWith(Ch: Char);
begin
  if not EditingPossible then
    Exit;
  if not StartModify then
  begin
    RejectBeep;
    Exit;
  end;
  ShowEditor;
  if FEditorOpen then
  begin
    FEditor.Text := Ch;
    FEditor.SelStart := 1;
    FEditorModified := True;
  end;
end;

{ ---------------------------------------------------------------- keyboard }

{ Down: the next record; at the end a grid that can modify appends one, unless an untouched new one is there }
procedure TDBGrid.MoveDown;
var
  DS: TDataSet;
begin
  DS := FDataLink.DataSet;
  if (DS.State = dsInsert) and not DS.Modified and FDataLink.AtLastRow
    and not (FEditorOpen and FEditorModified) then
    Exit;
  DS.Next;
  if DS.Eof and CanModifyData and (DS.State = dsBrowse) then
    DS.Append;
end;

{ Tab: the next (previous) column, from the last (first) one into the next (previous) row, from the very last
  (first) cell out of the grid }
procedure TDBGrid.TabMove(GoForward: Boolean);
var
  DS: TDataSet;
  F: TCustomForm;
  Leave: Boolean;
begin
  DS := FDataLink.DataSet;
  Leave := False;
  if GoForward then
  begin
    if FSelected < FShown.Count - 1 then
      SetSelectedIndex(FSelected + 1)
    else if FDataLink.AtLastRow then
      Leave := True
    else
    begin
      DS.Next;
      SetSelectedIndex(0);
    end;
  end
  else if FSelected > 0 then
    SetSelectedIndex(FSelected - 1)
  else if FDataLink.AtFirstRow then
    Leave := True
  else
  begin
    DS.Prior;
    SetSelectedIndex(FShown.Count - 1);
  end;
  if Leave then
  begin
    F := GetParentForm(Self);
    if F <> nil then
      F.SelectNext(Self, GoForward, True);
  end;
end;

procedure TDBGrid.DeleteRecord;
var
  DS: TDataSet;
  Answer: Integer;
begin
  DS := FDataLink.DataSet;
  if not CanModifyData or DS.IsEmpty or (DS.State = dsInsert) then
    Exit;
  if dgConfirmDelete in FOptions then
  begin
    if Assigned(MessageBoxHook) then
      Answer := MessageBoxHook(SDeleteRecordQuestion)
    else
      Answer := MessageBoxW(Handle, PChar(SDeleteRecordQuestion), PChar(SConfirmCaption),
        MB_OKCANCEL or MB_ICONQUESTION);
    if Answer <> IDOK then
      Exit;
  end;
  DS.Delete;
end;

procedure TDBGrid.KeyDown(var Key: Word; Shift: TShiftState);
var
  DS: TDataSet;
begin
  inherited KeyDown(Key, Shift);
  if (Key = 0) or not FDataLink.Active then
    Exit;
  DS := FDataLink.DataSet;
  case Key of
    VK_UP: DS.Prior;
    VK_DOWN: MoveDown;
    VK_PRIOR: DS.MoveBy(-VisibleRowCount);
    VK_NEXT: DS.MoveBy(VisibleRowCount);
    VK_HOME:
      if ssCtrl in Shift then
        DS.First
      else
        SetSelectedIndex(0);
    VK_END:
      if ssCtrl in Shift then
        DS.Last
      else
        SetSelectedIndex(FShown.Count - 1);
    VK_LEFT: SetSelectedIndex(FSelected - 1);
    VK_RIGHT: SetSelectedIndex(FSelected + 1);
    VK_TAB:
      begin
        if not (dgTabs in FOptions) then
          Exit;
        TabMove(not (ssShift in Shift));
      end;
    VK_F2, VK_RETURN:
      begin
        if not EditingPossible then
          Exit;
        ShowEditor;
      end;
    VK_INSERT:
      begin
        if not CanModifyData then
          Exit;
        DS.Insert;
      end;
    VK_DELETE:
      begin
        if not (ssCtrl in Shift) then
          Exit;
        DeleteRecord;
      end;
    VK_ESCAPE:
      begin
        if not (DS.State in [dsEdit, dsInsert]) then
          Exit;
        DS.Cancel;
      end;
  else
    Exit;
  end;
  Key := 0;
end;

procedure TDBGrid.KeyPress(var Key: Char);
begin
  inherited KeyPress(Key);
  if (Key >= ' ') and not FEditorOpen and FDataLink.Active and EditingPossible then
  begin
    StartEditWith(Key);
    Key := #0;
  end;
end;

{ ---------------------------------------------------------------- mouse }

procedure TDBGrid.MouseDown(Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
var
  C, R, Delta: Integer;
  Column: TColumn;
begin
  if Button = mbLeft then
  begin
    { the click leaves the cell being edited }
    if FEditorOpen then
    begin
      CommitEditor;
      if not (dgAlwaysShowEditor in FOptions) then
        HideEditor;
    end;
    if not FEntered and CanFocus then
      SetFocus;
  end;
  inherited MouseDown(Button, Shift, X, Y);
  if Button <> mbLeft then
    Exit;
  HitTest(X, Y, C, R);
  if (C = -2) or (R = -2) then
    Exit;
  if R = -1 then
  begin
    if (C >= 0) and Assigned(FOnTitleClick) then
      FOnTitleClick(TColumn(FShown[C]));
    Exit;
  end;
  if not FDataLink.Active or (R >= FDataLink.RecordCount) then
    Exit;
  Delta := R - FDataLink.ActiveRecord;
  if Delta <> 0 then
    FDataLink.DataSet.MoveBy(Delta);
  if C < 0 then
    Exit;
  Column := TColumn(FShown[C]);
  SetSelectedIndex(C);
  if Assigned(FOnCellClick) then
    FOnCellClick(Column);
  if ssDouble in Shift then
    ShowEditor;
end;

function TDBGrid.DoMouseWheel(Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint): Boolean;
begin
  Result := inherited DoMouseWheel(Shift, WheelDelta, MousePos);
  if Result or not FDataLink.Active or (WheelDelta = 0) then
    Exit;
  if WheelDelta > 0 then
    FDataLink.DataSet.MoveBy(-WheelRows)
  else
    FDataLink.DataSet.MoveBy(WheelRows);
  Result := True;
end;

{ ---------------------------------------------------------------- focus }

procedure TDBGrid.DoEnter;
begin
  { the focus came back from the editor }
  if FEntered then
    Exit;
  FEntered := True;
  if FDataLink.Active then
    InvalidateRow(FDataLink.ActiveRecord);
  inherited DoEnter;
end;

procedure TDBGrid.DoExit;
var
  DS: TDataSet;
begin
  { the focus moves between the grid and its editor }
  if FFocusInside > 0 then
    Exit;
  try
    CommitEditor;
  except
    if FEditorOpen and FEditor.CanFocus then
      FocusEditor;
    raise;
  end;
  { the editor control is hidden when the focus has arrived at its new place (CM_FOCUSCHANGED) }
  FEditorOpen := False;
  FEditorModified := False;
  FEntered := False;
  if FDataLink.Active then
  begin
    DS := FDataLink.DataSet;
    if (dgCancelOnExit in FOptions) and (DS.State = dsInsert) and not DS.Modified then
      DS.Cancel;
    if FDataLink.Active then
      InvalidateRow(FDataLink.ActiveRecord);
  end;
  inherited DoExit;
end;

procedure TDBGrid.WndProc(var Message: TMessage);
var
  C: TWinControl;
begin
  case Message.Msg of
    CM_FOCUSCHANGED:
      begin
        inherited WndProc(Message);
        if (FDataLink = nil) or (csDestroying in ComponentState) then
          Exit;
        C := TWinControl(Pointer(Message.LParam));
        if (C = Self) or ((FEditor <> nil) and (C = FEditor)) then
          UpdateEditorState
        else if (FEditor <> nil) and not FEditorOpen and FEditor.Visible then
          FEditor.Visible := False;
      end;
    WM_SETFOCUS:
      begin
        Inc(FFocusInside);
        try
          inherited WndProc(Message);
        finally
          Dec(FFocusInside);
        end;
        { an open editor is where the keys go }
        if FEditorOpen and (FEditor <> nil) and FEditor.CanFocus and FEditor.HandleAllocated then
          FocusEditor;
      end;
    CM_WANTSPECIALKEY:
      if ((Message.WParam = VK_TAB) and (dgTabs in FOptions))
        or ((Message.WParam = VK_RETURN) and EditingPossible)
        or ((Message.WParam = VK_ESCAPE) and FDataLink.Active and (FDataLink.DataSet.State in [dsEdit, dsInsert])) then
        Message.Result := 1
      else
        inherited WndProc(Message);
    WM_GETDLGCODE:
      begin
        Message.Result := DLGC_WANTARROWS or DLGC_WANTCHARS;
        if dgTabs in FOptions then
          Message.Result := Message.Result or DLGC_WANTTAB;
      end;
    CM_FONTCHANGED, CM_DPICHANGED:
      begin
        inherited WndProc(Message);
        if (FDataLink <> nil) and not (csDestroying in ComponentState) then
        begin
          FMetricsValid := False;
          UpdateLayout;
          InvalidateGrid;
        end;
      end;
    CM_COLORCHANGED, CM_ENABLEDCHANGED:
      begin
        inherited WndProc(Message);
        Invalidate;
      end;
  else
    inherited WndProc(Message);
  end;
end;

initialization
  RegisterClasses([TDBGrid]);

end.
