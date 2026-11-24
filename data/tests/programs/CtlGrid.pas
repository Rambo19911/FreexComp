// covers: DAT-260, DAT-261, DAT-262, DAT-263, DAT-264, DAT-265, DAT-266, DAT-267, DAT-268, DAT-269, DAT-270, DAT-271, DAT-272, DAT-408
program CtlGrid;
{ TDBGrid on a shown form.
  Display: automatic and persistent columns, cell texts, the geometry as numbers derived from the font and the
  resolution, and what is painted (the draw calls of a paint into a memory bitmap are recorded).
  Navigation: the row window, the scroll bars, mouse and keyboard.
  Complete: when editing is possible, the in-place editor, commit and cancel, leaving the grid, row changes that
  post, what is repainted, lifetime. Nothing is compared by pixels; no expected number depends on the font or
  on the resolution of the machine. }
uses
  Windows, Messages, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, DB, DBCtrls, DBGrids, DataTestUtils,
  TestDataSet, CtlTestUtils;

const
  PRF_CLIENT = $4;
  AllOptions = [dgEditing, dgTitles, dgIndicator, dgColLines, dgRowLines, dgTabs, dgConfirmDelete, dgCancelOnExit];

type
  { records what the grid draws and what it invalidates }
  TTestGrid = class(TDBGrid)
  protected
    procedure DrawTitleCell(ACol: Integer; const ARect: TRect; const AText: string); override;
    procedure DrawDataCell(ACol, ARow: Integer; const ARect: TRect; const AText: string; AAlignment: TAlignment;
      AHighlight: Boolean); override;
    procedure DrawIndicator(ARow: Integer; const ARect: TRect; AMark: TDBGridIndicator); override;
    procedure InvalidateGrid; override;
    procedure InvalidateRow(ARow: Integer); override;
  public
    Draws: string;
    GridInvalidated: Boolean;
    RowsInvalidated: string;
    { a key with a shift state, as KeyDown receives it }
    procedure Key(AKey: Word; Shift: TShiftState);
    { 'grid' when everything was invalidated, else the invalidated rows ('row1;'), '' when nothing }
    function TakeInvals: string;
  end;

  THandlers = class
  public
    Log: string;
    procedure CellClick(Column: TColumn);
    procedure TitleClick(Column: TColumn);
    procedure ColEnter(Sender: TObject);
    procedure ColExit(Sender: TObject);
    procedure GridEnter(Sender: TObject);
    procedure GridExit(Sender: TObject);
    procedure AppError(Sender: TObject; E: Exception);
  end;

var
  H: THandlers;
  BoxTexts: string;
  BoxAnswer: Integer;
  { the line height and the width of '0' in the font of the measured grid; the resolution of the form }
  LineH, W0, DPI: Integer;

procedure TTestGrid.DrawTitleCell(ACol: Integer; const ARect: TRect; const AText: string);
begin
  inherited DrawTitleCell(ACol, ARect, AText);
  Draws := Draws + Format('T%d=%s;', [ACol, AText]);
end;

procedure TTestGrid.DrawDataCell(ACol, ARow: Integer; const ARect: TRect; const AText: string;
  AAlignment: TAlignment; AHighlight: Boolean);
var
  S: string;
begin
  inherited DrawDataCell(ACol, ARow, ARect, AText, AAlignment, AHighlight);
  S := Format('c%dr%d=%s', [ACol, ARow, AText]);
  if AAlignment = taRightJustify then
    S := S + '>'
  else if AAlignment = taCenter then
    S := S + '^';
  if AHighlight then
    S := S + '*';
  Draws := Draws + S + ';';
end;

procedure TTestGrid.DrawIndicator(ARow: Integer; const ARect: TRect; AMark: TDBGridIndicator);
begin
  inherited DrawIndicator(ARow, ARect, AMark);
  case AMark of
    giBrowse: Draws := Draws + Format('I%d=browse;', [ARow]);
    giEdit: Draws := Draws + Format('I%d=edit;', [ARow]);
    giInsert: Draws := Draws + Format('I%d=insert;', [ARow]);
  end;
end;

procedure TTestGrid.InvalidateGrid;
begin
  inherited InvalidateGrid;
  GridInvalidated := True;
end;

procedure TTestGrid.InvalidateRow(ARow: Integer);
var
  S: string;
begin
  inherited InvalidateRow(ARow);
  S := Format('row%d;', [ARow]);
  if Pos(S, RowsInvalidated) = 0 then
    RowsInvalidated := RowsInvalidated + S;
end;

procedure TTestGrid.Key(AKey: Word; Shift: TShiftState);
begin
  KeyDown(AKey, Shift);
end;

function TTestGrid.TakeInvals: string;
begin
  if GridInvalidated then
    Result := 'grid'
  else
    Result := RowsInvalidated;
  GridInvalidated := False;
  RowsInvalidated := '';
end;

procedure THandlers.CellClick(Column: TColumn);
begin
  Log := Log + 'cell:' + Column.FieldName + ';';
end;

procedure THandlers.TitleClick(Column: TColumn);
begin
  Log := Log + 'title:' + Column.FieldName + ';';
end;

procedure THandlers.ColEnter(Sender: TObject);
begin
  Log := Log + 'colenter;';
end;

procedure THandlers.ColExit(Sender: TObject);
begin
  Log := Log + 'colexit;';
end;

procedure THandlers.GridEnter(Sender: TObject);
begin
  Log := Log + 'enter;';
end;

procedure THandlers.GridExit(Sender: TObject);
begin
  Log := Log + 'exit;';
end;

procedure THandlers.AppError(Sender: TObject; E: Exception);
begin
  Log := Log + 'error:' + E.ClassName + ';';
end;

function AnswerBox(const Text: string): Integer;
begin
  BoxTexts := BoxTexts + Text + ';';
  Result := BoxAnswer;
end;

function RectText(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

{ logical pixels at the resolution of the form }
function S(V: Integer): Integer;
begin
  Result := LayoutMulDiv(V, DPI, 96);
end;

function Larger(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

{ the font metrics the geometry of the grid is built from, measured independently of the grid }
procedure Measure(F: TForm; G: TDBGrid);
var
  Bmp: TBitmap;
begin
  Bmp := TBitmap.Create;
  try
    Bmp.SetSize(8, 8);
    Bmp.Canvas.Font := G.Font;
    LineH := Bmp.Canvas.TextHeight('0');
    W0 := Bmp.Canvas.TextWidth('0');
  finally
    Bmp.Free;
  end;
  DPI := F.PixelsPerInch;
end;

{ the height of a grid that shows Rows whole data rows below its title row, and half a row more }
function HeightFor(Rows: Integer): Integer;
begin
  Result := 4 + (Rows + 1) * (LineH + S(4)) + (LineH + S(4)) div 2;
end;

{ id (right-aligned), name (10); Count rows 1 n1, 2 n2, ...; not opened }
function NewDS(Count: Integer): TTestDataSet;
var
  I: Integer;
begin
  Result := TTestDataSet.Create(nil);
  Result.AddDef('id', ftInteger);
  Result.AddDef('name', ftString, 10);
  for I := 1 to Count do
    Result.AddRow([IntValue(I), StringValue('n' + IntToStr(I))]);
end;

function NewGrid(F: TForm): TTestGrid;
begin
  Result := TTestGrid.Create(F);
  Result.Parent := F;
  Result.SetBounds(8, 30, 380, 120);
end;

function NewEdit(F: TForm): TEdit;
begin
  Result := TEdit.Create(F);
  Result.Parent := F;
  Result.SetBounds(8, 4, 60, 23);
end;

{ the field names of the shown columns: 'id,name' }
function ColNames(G: TDBGrid): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to G.ColCount - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + G.VisibleColumns[I].FieldName;
  end;
end;

{ the names of the visible fields in field order }
function VisibleFieldNames(DS: TDataSet): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to DS.FieldCount - 1 do
    if DS.Fields[I].Visible then
    begin
      if Result <> '' then
        Result := Result + ',';
      Result := Result + DS.Fields[I].FieldName;
    end;
end;

{ the texts of column ACol in every row the grid can show: 'n1,n2,n3' }
function Rows(G: TDBGrid; ACol: Integer): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to G.VisibleRowCount - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + G.CellText(ACol, I);
  end;
end;

{ paints the client area into a memory bitmap; the recorded draw calls }
function Painted(G: TTestGrid): string;
var
  Bmp: TBitmap;
begin
  Bmp := TBitmap.Create;
  try
    Bmp.SetSize(G.Width, G.Height);
    G.Draws := '';
    SendMessageW(G.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT);
    Result := G.Draws;
  finally
    Bmp.Free;
  end;
end;

{ the native scroll bar: 'hidden' or range, page and position }
function BarText(G: TDBGrid; Bar: Integer): string;
var
  Info: TScrollInfo;
  Style, Bit: NativeInt;
begin
  Style := GetWindowLongPtrW(G.Handle, GWL_STYLE);
  if Bar = SB_VERT then
    Bit := WS_VSCROLL
  else
    Bit := WS_HSCROLL;
  if (Style and Bit) = 0 then
  begin
    Result := 'hidden';
    Exit;
  end;
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Info.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  GetScrollInfo(G.Handle, Bar, Info);
  Result := Format('%d..%d page %d pos %d', [Info.nMin, Info.nMax, Integer(Info.nPage), Info.nPos]);
end;

procedure Scroll(G: TDBGrid; Msg: Cardinal; Code, Position: Integer);
begin
  SendMessageW(G.Handle, Msg, MakeWParam(Word(Code), Word(Position)), 0);
end;

procedure MouseAt(G: TDBGrid; Msg: Cardinal; X, Y: Integer);
var
  P: LPARAM;
begin
  P := MakeLParam(Word(X), Word(Y));
  SendMessageW(G.Handle, Msg, MK_LBUTTON, P);
  SendMessageW(G.Handle, WM_LBUTTONUP, 0, P);
end;

{ a click in the middle of a cell (ACol -1: the indicator, ARow -1: the title) }
procedure ClickCell(G: TDBGrid; ACol, ARow: Integer);
var
  R: TRect;
begin
  R := G.CellRect(ACol, ARow);
  MouseAt(G, WM_LBUTTONDOWN, (R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
end;

procedure DblClickCell(G: TDBGrid; ACol, ARow: Integer);
var
  R: TRect;
begin
  R := G.CellRect(ACol, ARow);
  MouseAt(G, WM_LBUTTONDBLCLK, (R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
end;

procedure Wheel(G: TDBGrid; Delta: Integer);
begin
  SendMessageW(G.Handle, WM_MOUSEWHEEL, MakeWParam(0, Word(SmallInt(Delta))), 0);
end;

{ ---------------------------------------------------------------- display }

procedure TestDefaults(F: TForm);
var
  G: TTestGrid;
  C: TControl;
begin
  Section('defaults');
  G := TTestGrid.Create(F);
  G.Parent := F;
  C := G;
  Check(C is TCustomControl, 'a custom control');
  CheckEqInt(G.Width, 320, 'Width');
  CheckEqInt(G.Height, 120, 'Height');
  CheckEqBool(G.TabStop, True, 'TabStop');
  Check(G.Options = AllOptions, 'Options');
  CheckEqBool(G.ReadOnly, False, 'ReadOnly');
  Check(G.DataSource = nil, 'DataSource');
  CheckEqInt(G.Columns.Count, 0, 'no persistent columns');
  CheckEqInt(G.ColCount, 0, 'no columns without a dataset');
  CheckEqInt(G.SelectedIndex, 0, 'SelectedIndex');
  Check(G.SelectedField = nil, 'SelectedField');
  CheckEqBool(G.EditorMode, False, 'EditorMode');
  CheckEq(G.CellText(0, 0), '', 'no cell text');
  G.ShowEditor;
  CheckEqBool(G.EditorMode, False, 'no editor without data');
  CheckEqInt(G.ClientWidth, 316, 'the border takes two pixels on each side: client width');
  CheckEqInt(G.ClientHeight, 116, 'client height');
  CheckEq(Painted(G), '', 'paints without a data source');
  CheckEq(BarText(G, SB_VERT), 'hidden', 'no vertical scroll bar');
  CheckEq(BarText(G, SB_HORZ), 'hidden', 'no horizontal scroll bar');
  G.Free;
end;

procedure TestColumns(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  C1, C2, C3: TColumn;
  I: Integer;
begin
  Section('automatic columns');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10);
  DS.AddDef('secret', ftString, 5);
  for I := 1 to 3 do
    DS.AddRow([IntValue(I), StringValue('n' + IntToStr(I)), StringValue('s')]);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.DataSource := Src;
  CheckEqInt(G.ColCount, 0, 'a closed dataset: no columns');
  DS.Open;
  CheckEq(ColNames(G), 'id,name,secret', 'one column per field, in field order');
  CheckEqInt(G.Columns.Count, 0, 'the Columns collection stays empty');
  CheckEq(G.VisibleColumns[1].TitleText, 'name', 'the title is the display label of the field');
  Check(G.VisibleColumns[0].Field = DS.FieldByName('id'), 'the column knows its field');
  Check(G.SelectedField = DS.FieldByName('id'), 'SelectedField');
  CheckEqInt(G.VisibleColumns[0].Width, -1, 'the width is computed');
  DS.FieldByName('secret').Visible := False;
  DS.FieldByName('name').DisplayLabel := 'Who';
  DS.FieldByName('name').Index := 0;
  CheckEq(ColNames(G), 'name,id', 'a layout change rebuilds them: field order, invisible fields left out');
  CheckEq(G.VisibleColumns[0].TitleText, 'Who', 'a display label');
  DS.Close;
  CheckEqInt(G.ColCount, 0, 'closing removes them');
  DS.Open;
  CheckEq(ColNames(G), VisibleFieldNames(DS), 'opening builds them');

  Section('persistent columns');
  C1 := G.Columns.Add;
  CheckEqInt(C1.Width, -1, 'a new column: Width');
  CheckEqBool(C1.ReadOnly, False, 'ReadOnly');
  CheckEqBool(C1.Visible, True, 'Visible');
  CheckEq(C1.Title, '', 'Title');
  CheckEq(C1.FieldName, '', 'FieldName');
  C1.FieldName := 'name';
  C1.Title := 'Person';
  CheckEq(ColNames(G), 'name', 'a persistent column replaces the automatic ones');
  C2 := G.Columns.Add;
  C2.FieldName := 'missing';
  C3 := G.Columns.Add;
  C3.FieldName := 'id';
  C3.Visible := False;
  CheckEq(ColNames(G), 'name,missing', 'an invisible column is not shown');
  CheckEqInt(G.ColCount, 2, 'ColCount counts the shown columns');
  Check(G.Columns[2] = C3, 'Items');
  Check(C1.Field = DS.FieldByName('name'), 'the field of a column');
  Check(C2.Field = nil, 'a column whose field does not exist has none');
  CheckEq(G.CellText(1, 0), '', 'and shows empty cells');
  CheckEq(C2.TitleText, 'missing', 'its title is the field name');
  CheckEq(C1.TitleText, 'Person', 'an own title');
  CheckEq(C3.TitleText, 'id', 'no own title: the display label');
  C3.Visible := True;
  CheckEq(ColNames(G), 'name,missing,id', 'shown again');
  DS.FieldByName('id').Alignment := taCenter;
  Check(C3.Alignment = taCenter, 'the alignment is the field''s');
  C3.Alignment := taLeftJustify;
  DS.FieldByName('id').Alignment := taRightJustify;
  Check(C3.Alignment = taLeftJustify, 'an assigned alignment stays');
  Check(C2.Alignment = taLeftJustify, 'without a field: left');
  G.SelectedIndex := 99;
  CheckEqInt(G.SelectedIndex, 2, 'SelectedIndex is kept inside the columns');
  G.SelectedIndex := -5;
  CheckEqInt(G.SelectedIndex, 0, 'at both ends');
  G.SelectedIndex := 2;
  DS.Close;
  CheckEq(ColNames(G), 'name,missing,id', 'persistent columns stay when the dataset closes');
  Check(C1.Field = nil, 'without fields');
  CheckEq(C1.TitleText, 'Person', 'the own title stays');
  DS.Open;
  G.Columns.Delete(2);
  CheckEq(ColNames(G), 'name,missing', 'a deleted column');
  CheckEqInt(G.SelectedIndex, 1, 'the selection moves inside');
  G.Columns.Clear;
  CheckEq(ColNames(G), VisibleFieldNames(DS), 'no persistent column: automatic columns again');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestCellText(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  B: TBytes;
begin
  Section('cell text');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10);
  DS.AddDef('note', ftMemo);
  DS.AddDef('data', ftBlob);
  SetLength(B, 3);
  DS.AddRow([IntValue(1), StringValue('n1'), StringValue('line1'#13#10'line2'), BytesValue(B)]);
  DS.AddRow([IntValue(2), StringValue('n2'), StringValue('a'#10'b'), BytesValue(B)]);
  DS.AddRow([IntValue(3), StringValue('n3'), StringValue(StringOfChar('x', 300)), BytesValue(B)]);
  DS.AddRow([IntValue(4), NullValue, NullValue, BytesValue(B)]);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.Height := 300;
  G.DataSource := Src;
  DS.Open;
  CheckEq(G.CellText(1, 0), 'n1', 'window row 0');
  CheckEq(G.CellText(1, 1), 'n2', 'window row 1');
  CheckEq(G.CellText(1, 2), 'n3', 'window row 2');
  CheckEq(G.CellText(1, 3), '', 'a null value');
  CheckEq(G.CellText(1, 4), '', 'a row beyond the data');
  CheckEq(G.CellText(1, -1), '', 'a negative row');
  CheckEq(G.CellText(9, 0), '', 'a column beyond the columns');
  CheckEq(G.CellText(-1, 0), '', 'a negative column');
  CheckEq(G.CellText(2, 0), 'line1', 'a memo: the first line');
  CheckEq(G.CellText(2, 1), 'a', 'a lone line feed ends the first line');
  CheckEqInt(Length(G.CellText(2, 2)), 255, 'a memo: at most 255 characters');
  CheckEq(G.CellText(2, 3), '', 'a null memo');
  CheckEq(G.CellText(3, 0), '(Blob)', 'a blob');
  (DS.FieldByName('id') as TIntegerField).DisplayFormat := '000';
  CheckEq(G.CellText(0, 1), '002', 'the display text of the field');
  DS.Next;
  CheckEq(G.CellText(1, 0), 'n1', 'rows are window rows, not relative to the current row');
  CheckEq(DS.FieldByName('name').AsString, 'n2', 'reading cells leaves the fields on the current row');
  CheckEqInt(DS.RecNo, 2, 'and does not move the dataset');
  DS.Edit;
  DS.FieldByName('name').AsString := 'changed';
  CheckEq(G.CellText(1, 1), 'changed', 'the row being edited shows its new values');
  CheckEq(G.CellText(1, 0), 'n1', 'the other rows their own');
  DS.Cancel;
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestGeometry(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  C: TColumn;
  RH, IW, WId, WName: Integer;
  R: TRect;
begin
  Section('geometry');
  DS := NewDS(3);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.Height := 200;
  G.DataSource := Src;
  DS.Open;
  Measure(F, G);
  RH := LineH + S(4);
  IW := S(12);
  WId := Larger(DS.FieldByName('id').DisplayWidth, 2) * W0 + S(4);
  WName := Larger(DS.FieldByName('name').DisplayWidth, 4) * W0 + S(4);
  CheckEqInt(DS.FieldByName('name').DisplayWidth, 10, 'the display width of a string field is its size');
  CheckEq(RectText(G.CellRect(-1, -1)), RectText(Rect(0, 0, IW, RH)), 'the corner: indicator width, row height');
  CheckEq(RectText(G.CellRect(0, -1)), RectText(Rect(IW, 0, IW + WId, RH)), 'the first title cell');
  CheckEq(RectText(G.CellRect(1, -1)), RectText(Rect(IW + WId, 0, IW + WId + WName, RH)), 'the second title cell');
  CheckEq(RectText(G.CellRect(0, 0)), RectText(Rect(IW, RH, IW + WId, 2 * RH)), 'the first data cell');
  CheckEq(RectText(G.CellRect(1, 2)), RectText(Rect(IW + WId, 3 * RH, IW + WId + WName, 4 * RH)),
    'a cell of the third row');
  CheckEq(RectText(G.CellRect(-1, 1)), RectText(Rect(0, 2 * RH, IW, 3 * RH)), 'an indicator cell');
  CheckEq(RectText(G.CellRect(5, 0)), '0,0,0,0', 'no such column: empty');
  CheckEq(RectText(G.CellRect(0, G.VisibleRowCount)), '0,0,0,0', 'no such row: empty');
  CheckEqInt(G.VisibleRowCount, (G.ClientHeight - RH) div RH, 'the whole rows below the title row');
  DS.FieldByName('name').DisplayLabel := 'A title longer than ten';
  R := G.CellRect(1, 0);
  CheckEqInt(R.Right - R.Left, 23 * W0 + S(4), 'a longer title widens the column');
  DS.FieldByName('name').DisplayLabel := '';
  G.Options := AllOptions - [dgTitles];
  CheckEq(RectText(G.CellRect(0, 0)), RectText(Rect(IW, 0, IW + WId, RH)), 'without titles the rows start at the top');
  CheckEq(RectText(G.CellRect(0, -1)), '0,0,0,0', 'and there is no title cell');
  CheckEqInt(G.VisibleRowCount, G.ClientHeight div RH, 'one more row fits');
  G.Options := AllOptions - [dgIndicator];
  CheckEq(RectText(G.CellRect(0, 0)), RectText(Rect(0, RH, WId, 2 * RH)), 'without indicator the cells start at the left');
  CheckEq(RectText(G.CellRect(-1, 0)), '0,0,0,0', 'and there is no indicator cell');
  G.Options := AllOptions;

  Section('column width');
  C := G.Columns.Add;
  C.FieldName := 'name';
  R := G.CellRect(0, 0);
  CheckEqInt(R.Right - R.Left, WName, 'a persistent column: computed like an automatic one');
  C.Width := 50;
  R := G.CellRect(0, 0);
  CheckEqInt(R.Right - R.Left, S(50), 'an own width');
  C.Width := 0;
  R := G.CellRect(0, 0);
  CheckEqInt(R.Right - R.Left, 0, 'zero is a width');
  C.Width := -1;
  C.Title := 'A title longer than ten';
  R := G.CellRect(0, 0);
  CheckEqInt(R.Right - R.Left, 23 * W0 + S(4), 'computed again, from the own title');
  C.FieldName := 'missing';
  C.Title := 'abc';
  R := G.CellRect(0, 0);
  CheckEqInt(R.Right - R.Left, 3 * W0 + S(4), 'without a field: from the title alone');
  G.Columns.Clear;

  Section('font');
  G.Font.Size := G.Font.Size + 6;
  Measure(F, G);
  R := G.CellRect(0, 0);
  CheckEqInt(R.Bottom - R.Top, LineH + S(4), 'the row height follows the font');
  CheckEqInt(R.Right - R.Left, Larger(DS.FieldByName('id').DisplayWidth, 2) * W0 + S(4), 'and the column width');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestPainting(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  Other: TEdit;
  C: TColumn;
  P: string;
begin
  Section('painting');
  DS := NewDS(2);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  Other := NewEdit(F);
  G := NewGrid(F);
  G.DataSource := Src;
  DS.Open;
  DS.FieldByName('id').Alignment := taRightJustify;
  Other.SetFocus;
  CheckEq(Painted(G), 'T0=id;T1=name;I0=browse;c0r0=1>;c1r0=n1;c0r1=2>;c1r1=n2;',
    'titles, the indicator of the current row, the cells with their alignment; no highlight without focus');
  G.SetFocus;
  CheckEq(Painted(G), 'T0=id;T1=name;I0=browse;c0r0=1>*;c1r0=n1;c0r1=2>;c1r1=n2;',
    'focused: the selected cell is highlighted');
  G.SelectedIndex := 1;
  DS.Next;
  CheckEq(Painted(G), 'T0=id;T1=name;c0r0=1>;c1r0=n1;I1=browse;c0r1=2>;c1r1=n2*;',
    'the selection follows the column and the row');
  Other.SetFocus;
  CheckEq(Painted(G), 'T0=id;T1=name;c0r0=1>;c1r0=n1;I1=browse;c0r1=2>;c1r1=n2;', 'the focus left: no highlight');
  G.Options := AllOptions + [dgAlwaysShowSelection];
  CheckEq(Painted(G), 'T0=id;T1=name;c0r0=1>;c1r0=n1;I1=browse;c0r1=2>;c1r1=n2*;',
    'dgAlwaysShowSelection: highlighted without focus');
  G.Options := AllOptions + [dgRowSelect];
  CheckEq(Painted(G), 'T0=id;T1=name;c0r0=1>;c1r0=n1;I1=browse;c0r1=2>;c1r1=n2;',
    'dgRowSelect without focus: no highlight');
  G.SetFocus;
  CheckEq(Painted(G), 'T0=id;T1=name;c0r0=1>;c1r0=n1;I1=browse;c0r1=2>*;c1r1=n2*;',
    'dgRowSelect: the whole current row');
  G.Options := AllOptions;
  DS.Edit;
  P := Painted(G);
  Check(Pos('I1=edit;', P) > 0, 'the row being edited has the edit mark');
  DS.Cancel;
  DS.Insert;
  P := Painted(G);
  Check(Pos('I1=insert;', P) > 0, 'the row being inserted has the insert mark');
  Check(Pos('c1r2=n2;', P) > 0, 'and occupies its insert position');
  DS.Cancel;
  G.Options := AllOptions - [dgTitles, dgIndicator];
  CheckEq(Painted(G), 'c0r0=1>;c1r0=n1;c0r1=2>;c1r1=n2*;', 'no title row, no indicator column');
  G.Options := AllOptions;
  C := G.Columns.Add;
  C.FieldName := 'name';
  C.Alignment := taCenter;
  C.Title := 'Who';
  C := G.Columns.Add;
  C.FieldName := 'missing';
  CheckEq(Painted(G), 'T0=Who;T1=missing;c0r0=n1^;c1r0=;I1=browse;c0r1=n2^*;c1r1=;',
    'persistent columns: own title and alignment, empty cells for a missing field');
  DS.Close;
  CheckEq(Painted(G), 'T0=Who;T1=missing;', 'a closed dataset: the titles only');
  G.Update;
  Check(True, 'paints to the screen');
  G.Free;
  Other.Free;
  Src.Free;
  DS.Free;
end;

{ ---------------------------------------------------------------- navigation }

procedure TestRows(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
begin
  Section('row window');
  DS := NewDS(10);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  Measure(F, G);
  G.Height := HeightFor(3);
  G.DataSource := Src;
  DS.Open;
  CheckEqInt(G.VisibleRowCount, 3, 'three whole rows fit');
  CheckEq(Rows(G, 1), 'n1,n2,n3', 'the first rows');
  DS.MoveBy(4);
  CheckEq(Rows(G, 1), 'n3,n4,n5', 'the window follows the current row by the minimum');
  DS.Prior;
  CheckEq(Rows(G, 1), 'n3,n4,n5', 'a move inside the window keeps it');
  DS.Last;
  CheckEq(Rows(G, 1), 'n8,n9,n10', 'the last rows');
  DS.First;
  CheckEq(Rows(G, 1), 'n1,n2,n3', 'the first rows again');
  G.Height := HeightFor(5);
  CheckEqInt(G.VisibleRowCount, 5, 'a taller grid');
  CheckEq(Rows(G, 1), 'n1,n2,n3,n4,n5', 'shows more rows');
  G.Height := 10;
  CheckEqInt(G.VisibleRowCount, 1, 'at least one row');
  CheckEq(Rows(G, 1), 'n1', 'the current one');
  G.Height := HeightFor(3);
  G.Options := AllOptions - [dgTitles];
  CheckEqInt(G.VisibleRowCount, 4, 'without the title row one more');
  CheckEq(Rows(G, 1), 'n1,n2,n3,n4', 'is shown');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestScrollBars(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  C: TColumn;
  I: Integer;
  R: TRect;
begin
  Section('vertical scroll bar');
  DS := NewDS(10);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  Measure(F, G);
  G.Height := HeightFor(3);
  G.DataSource := Src;
  CheckEq(BarText(G, SB_VERT), 'hidden', 'a closed dataset: hidden');
  DS.Open;
  CheckEq(BarText(G, SB_VERT), '1..10 page 0 pos 1', 'more records than rows: range 1..RecordCount, position RecNo');
  CheckEq(BarText(G, SB_HORZ), 'hidden', 'the columns fit: no horizontal bar');
  CheckEqInt(G.ClientWidth, 380 - 4 - S(17), 'the bar takes its width from the client area');
  Scroll(G, WM_VSCROLL, SB_LINEDOWN, 0);
  CheckEqInt(DS.RecNo, 2, 'line down: Next');
  CheckEq(BarText(G, SB_VERT), '1..10 page 0 pos 2', 'the position follows');
  Scroll(G, WM_VSCROLL, SB_LINEUP, 0);
  CheckEqInt(DS.RecNo, 1, 'line up: Prior');
  Scroll(G, WM_VSCROLL, SB_PAGEDOWN, 0);
  CheckEqInt(DS.RecNo, 4, 'page down: by the visible rows');
  Scroll(G, WM_VSCROLL, SB_PAGEUP, 0);
  CheckEqInt(DS.RecNo, 1, 'page up');
  Scroll(G, WM_VSCROLL, SB_THUMBTRACK, 6);
  CheckEqInt(DS.RecNo, 1, 'dragging the thumb does not move yet');
  Scroll(G, WM_VSCROLL, SB_THUMBPOSITION, 6);
  CheckEqInt(DS.RecNo, 6, 'releasing it: RecNo := position');
  Scroll(G, WM_VSCROLL, SB_BOTTOM, 0);
  CheckEqInt(DS.RecNo, 10, 'bottom: Last');
  CheckEq(BarText(G, SB_VERT), '1..10 page 0 pos 10', 'the position at the end');
  Scroll(G, WM_VSCROLL, SB_LINEDOWN, 0);
  CheckEq(StateName(DS.State), 'dsBrowse', 'line down at the last record does not append');
  Scroll(G, WM_VSCROLL, SB_TOP, 0);
  CheckEqInt(DS.RecNo, 1, 'top: First');
  for I := 1 to 7 do
    DS.Delete;
  CheckEq(BarText(G, SB_VERT), 'hidden', 'as many records as rows: hidden');
  CheckEqInt(G.ClientWidth, 376, 'the client area has its full width');
  DS.Append;
  DS.FieldByName('id').AsInteger := 99;
  DS.Post;
  CheckEq(BarText(G, SB_VERT), '1..4 page 0 pos 4', 'one record more: shown');
  Src.Enabled := False;
  CheckEq(BarText(G, SB_VERT), 'hidden', 'an inactive link: hidden');
  Src.Enabled := True;
  CheckEq(BarText(G, SB_VERT), '1..4 page 0 pos 4', 'active again: shown');

  Section('horizontal scroll bar');
  for I := 0 to 4 do
  begin
    C := G.Columns.Add;
    if Odd(I) then
      C.FieldName := 'name'
    else
      C.FieldName := 'id';
    C.Width := 100;
  end;
  G.SetBounds(G.Left, G.Top, 4 + S(12) + 2 * S(100) + S(50), HeightFor(5));
  CheckEq(BarText(G, SB_VERT), 'hidden', 'four records in five rows: no vertical bar');
  CheckEq(BarText(G, SB_HORZ), '0..4 page 2 pos 0', 'the columns exceed the width: one position per column');
  CheckEqInt(G.ClientHeight, HeightFor(5) - 4 - S(17), 'the bar takes its height from the client area');
  CheckEqInt(G.LeftCol, 0, 'LeftCol');
  R := G.CellRect(2, 0);
  CheckEqInt(R.Left, S(12) + 2 * S(100), 'the partly visible column starts inside the client area');
  CheckEqInt(R.Right - R.Left, S(100), 'and keeps its width');
  Scroll(G, WM_HSCROLL, SB_LINEDOWN, 0);
  CheckEqInt(G.LeftCol, 1, 'line right: one whole column');
  CheckEq(BarText(G, SB_HORZ), '0..4 page 2 pos 1', 'the position follows');
  CheckEq(RectText(G.CellRect(0, 0)), '0,0,0,0', 'the column scrolled out has no rectangle');
  R := G.CellRect(1, 0);
  CheckEqInt(R.Left, S(12), 'the next one starts after the indicator');
  Scroll(G, WM_HSCROLL, SB_PAGEDOWN, 0);
  CheckEqInt(G.LeftCol, 3, 'page right: by the whole columns shown');
  Scroll(G, WM_HSCROLL, SB_LINEDOWN, 0);
  CheckEqInt(G.LeftCol, 3, 'not beyond the position that shows the last column whole');
  Scroll(G, WM_HSCROLL, SB_THUMBPOSITION, 2);
  CheckEqInt(G.LeftCol, 2, 'the thumb');
  Scroll(G, WM_HSCROLL, SB_PAGEUP, 0);
  CheckEqInt(G.LeftCol, 0, 'page left');
  Scroll(G, WM_HSCROLL, SB_BOTTOM, 0);
  CheckEqInt(G.LeftCol, 3, 'right end');
  Scroll(G, WM_HSCROLL, SB_TOP, 0);
  CheckEqInt(G.LeftCol, 0, 'left end');
  Scroll(G, WM_HSCROLL, SB_LINEUP, 0);
  CheckEqInt(G.LeftCol, 0, 'not before the first column');
  G.SelectedIndex := 4;
  CheckEqInt(G.LeftCol, 3, 'selecting a column scrolls it into view');
  G.SelectedIndex := 1;
  CheckEqInt(G.LeftCol, 1, 'to the left as well');
  G.SelectedIndex := 2;
  CheckEqInt(G.LeftCol, 1, 'a column that is whole in view does not scroll');
  G.Width := 4 + S(12) + 5 * S(100);
  CheckEq(BarText(G, SB_HORZ), 'hidden', 'wide enough: hidden');
  CheckEqInt(G.LeftCol, 0, 'and scrolled back');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestMouse(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  Other: TEdit;
  R: TRect;
begin
  Section('mouse');
  DS := NewDS(5);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  Other := NewEdit(F);
  G := NewGrid(F);
  Measure(F, G);
  G.Height := HeightFor(7);
  G.DataSource := Src;
  G.OnCellClick := H.CellClick;
  G.OnTitleClick := H.TitleClick;
  G.OnColEnter := H.ColEnter;
  G.OnColExit := H.ColExit;
  DS.Open;
  Other.SetFocus;
  H.Log := '';
  ClickCell(G, 1, 2);
  CheckEqInt(DS.RecNo, 3, 'a click on a cell moves to its row');
  CheckEqInt(G.SelectedIndex, 1, 'selects its column');
  CheckEq(H.Log, 'colexit;colenter;cell:name;', 'OnColExit, OnColEnter, OnCellClick');
  Check(F.ActiveControl = G, 'and focuses the grid');
  H.Log := '';
  ClickCell(G, 1, 0);
  CheckEqInt(DS.RecNo, 1, 'a click in another row');
  CheckEq(H.Log, 'cell:name;', 'the same column: only OnCellClick');
  H.Log := '';
  ClickCell(G, 0, -1);
  CheckEq(H.Log, 'title:id;', 'a click on a title: OnTitleClick');
  CheckEqInt(DS.RecNo, 1, 'no move');
  CheckEqInt(G.SelectedIndex, 1, 'no column change');
  H.Log := '';
  ClickCell(G, -1, 3);
  CheckEqInt(DS.RecNo, 4, 'a click on the indicator moves to the row');
  CheckEq(H.Log, '', 'without a cell click');
  R := G.CellRect(0, 4);
  MouseAt(G, WM_LBUTTONDOWN, R.Left + 2, R.Bottom + (R.Bottom - R.Top) div 2);
  CheckEqInt(DS.RecNo, 4, 'a click below the last record does nothing');
  R := G.CellRect(1, 0);
  MouseAt(G, WM_LBUTTONDOWN, R.Right + 5, R.Top + 2);
  CheckEqInt(DS.RecNo, 4, 'a click beside the last column does nothing');
  CheckEq(H.Log, '', 'no event');
  DS.Edit;
  DS.FieldByName('name').AsString := 'x4';
  ClickCell(G, 0, 1);
  CheckEq(StateName(DS.State), 'dsBrowse', 'a click in another row posts the edited one');
  CheckEq(DS.StoredText, '[1|n1] [2|n2] [3|n3] [4|x4] [5|n5]', 'with its values');
  CheckEqInt(DS.RecNo, 2, 'and moves');
  H.Log := '';
  DblClickCell(G, 1, 2);
  CheckEqInt(DS.RecNo, 3, 'a double click moves like a click');
  CheckEq(H.Log, 'colexit;colenter;cell:name;', 'fires the click events');
  CheckEqBool(G.EditorMode, True, 'and shows the editor');
  CheckEq(G.InplaceEditor.Text, 'n3', 'on the clicked cell');
  G.HideEditor;
  G.ReadOnly := True;
  DblClickCell(G, 0, 0);
  CheckEqInt(DS.RecNo, 1, 'read-only: the double click moves');
  CheckEqBool(G.EditorMode, False, 'but shows no editor');
  G.ReadOnly := False;
  Wheel(G, -120);
  CheckEqInt(DS.RecNo, 4, 'wheel down: three records forward');
  Wheel(G, 120);
  CheckEqInt(DS.RecNo, 1, 'wheel up: three records back');
  G.Free;
  Other.Free;
  Src.Free;
  DS.Free;
end;

procedure TestKeyboard(F: TForm);
var
  DS, Empty: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  Before, After: TEdit;
  N: Integer;
begin
  Section('keyboard: rows');
  DS := NewDS(5);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  Before := NewEdit(F);
  G := NewGrid(F);
  After := NewEdit(F);
  Measure(F, G);
  G.Height := HeightFor(3);
  G.DataSource := Src;
  DS.Open;
  G.SetFocus;
  PressKey(G.Handle, VK_DOWN);
  CheckEqInt(DS.RecNo, 2, 'Down: Next');
  PressKey(G.Handle, VK_UP);
  CheckEqInt(DS.RecNo, 1, 'Up: Prior');
  PressKey(G.Handle, VK_NEXT);
  CheckEqInt(DS.RecNo, 4, 'PageDown: by the visible rows');
  PressKey(G.Handle, VK_NEXT);
  CheckEqInt(DS.RecNo, 5, 'PageDown near the end: the last record');
  PressKey(G.Handle, VK_PRIOR);
  CheckEqInt(DS.RecNo, 2, 'PageUp');
  G.Key(VK_END, [ssCtrl]);
  CheckEqInt(DS.RecNo, 5, 'Ctrl+End: Last');
  G.Key(VK_HOME, [ssCtrl]);
  CheckEqInt(DS.RecNo, 1, 'Ctrl+Home: First');

  Section('keyboard: columns');
  PressKey(G.Handle, VK_RIGHT);
  CheckEqInt(G.SelectedIndex, 1, 'Right: the next column');
  PressKey(G.Handle, VK_RIGHT);
  CheckEqInt(G.SelectedIndex, 1, 'Right in the last column: no wrap');
  CheckEqInt(DS.RecNo, 1, 'and no row change');
  PressKey(G.Handle, VK_LEFT);
  CheckEqInt(G.SelectedIndex, 0, 'Left: the previous column');
  PressKey(G.Handle, VK_LEFT);
  CheckEqInt(G.SelectedIndex, 0, 'Left in the first column: no wrap');
  PressKey(G.Handle, VK_END);
  CheckEqInt(G.SelectedIndex, 1, 'End: the last column');
  PressKey(G.Handle, VK_HOME);
  CheckEqInt(G.SelectedIndex, 0, 'Home: the first column');
  CheckEqInt(DS.RecNo, 1, 'Home and End do not move the dataset');

  Section('keyboard: Tab');
  G.Key(VK_TAB, []);
  CheckEqInt(G.SelectedIndex, 1, 'Tab: the next column');
  G.Key(VK_TAB, []);
  CheckEqInt(G.SelectedIndex, 0, 'Tab in the last column: the first column');
  CheckEqInt(DS.RecNo, 2, 'of the next row');
  G.Key(VK_TAB, [ssShift]);
  CheckEqInt(G.SelectedIndex, 1, 'Shift+Tab in the first column: the last column');
  CheckEqInt(DS.RecNo, 1, 'of the previous row');
  G.Key(VK_TAB, [ssShift]);
  CheckEqInt(G.SelectedIndex, 0, 'Shift+Tab: the previous column');
  Check(F.ActiveControl = G, 'still in the grid');
  G.Key(VK_TAB, [ssShift]);
  Check(F.ActiveControl = Before, 'Shift+Tab in the very first cell leaves the grid backwards');
  G.SetFocus;
  DS.Last;
  G.SelectedIndex := 1;
  G.Key(VK_TAB, []);
  Check(F.ActiveControl = After, 'Tab in the very last cell leaves the grid forwards');
  CheckEq(StateName(DS.State), 'dsBrowse', 'without appending');
  G.SetFocus;
  Check((SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTTAB) <> 0, 'dgTabs: the grid wants Tab');
  Check((SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTARROWS) <> 0, 'and the arrow keys');
  G.Options := AllOptions - [dgTabs];
  G.SelectedIndex := 0;
  G.Key(VK_TAB, []);
  CheckEqInt(G.SelectedIndex, 0, 'without dgTabs the grid does not handle Tab');
  Check((SendMessageW(G.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTTAB) = 0, 'and does not want it');
  G.Options := AllOptions;

  Section('keyboard: editor, insert, cancel');
  DS.First;
  G.SelectedIndex := 1;
  PressKey(G.Handle, VK_F2);
  CheckEqBool(G.EditorMode, True, 'F2 shows the editor');
  PressKey(G.InplaceEditor.Handle, VK_ESCAPE);
  CheckEqBool(G.EditorMode, False, 'Escape hides it');
  Check(F.ActiveControl = G, 'the grid has the focus again');
  PressKey(G.Handle, VK_RETURN);
  CheckEqBool(G.EditorMode, True, 'Enter shows the editor');
  CheckEq(G.InplaceEditor.Text, 'n1', 'with the text of the cell');
  PressKey(G.InplaceEditor.Handle, VK_ESCAPE);
  TypeText(G.Handle, 'q');
  CheckEqBool(G.EditorMode, True, 'a character shows the editor');
  CheckEq(G.InplaceEditor.Text, 'q', 'and replaces the text');
  CheckEq(StateName(DS.State), 'dsEdit', 'the dataset is in edit mode');
  PressKey(G.InplaceEditor.Handle, VK_RETURN);
  CheckEq(DS.FieldByName('name').AsString, 'q', 'Enter writes the text to the field');
  Check(G.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0) <> 0, 'while editing the grid wants Escape');
  PressKey(G.Handle, VK_ESCAPE);
  CheckEq(StateName(DS.State), 'dsBrowse', 'Escape in the grid cancels the record');
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'the value is back');
  Check(G.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0) = 0, 'in browse mode the grid does not want Escape');
  PressKey(G.Handle, VK_INSERT);
  CheckEq(StateName(DS.State), 'dsInsert', 'Insert inserts a record');
  PressKey(G.Handle, VK_ESCAPE);
  CheckEq(StateName(DS.State), 'dsBrowse', 'Escape cancels it');
  CheckEqInt(DS.RecordCount, 5, 'nothing inserted');

  Section('keyboard: delete');
  MessageBoxHook := AnswerBox;
  BoxTexts := '';
  BoxAnswer := IDCANCEL;
  PressKey(G.Handle, VK_DELETE);
  CheckEqInt(DS.RecordCount, 5, 'Delete alone does nothing');
  CheckEq(BoxTexts, '', 'and asks nothing');
  G.Key(VK_DELETE, [ssCtrl]);
  CheckEq(BoxTexts, 'Delete record?;', 'Ctrl+Delete asks');
  CheckEqInt(DS.RecordCount, 5, 'declined: nothing deleted');
  BoxAnswer := IDOK;
  BoxTexts := '';
  G.Key(VK_DELETE, [ssCtrl]);
  CheckEq(BoxTexts, 'Delete record?;', 'asks again');
  CheckEq(DS.StoredText, '[2|n2] [3|n3] [4|n4] [5|n5]', 'confirmed: the current record is deleted');
  G.Options := AllOptions - [dgConfirmDelete];
  BoxTexts := '';
  G.Key(VK_DELETE, [ssCtrl]);
  CheckEq(BoxTexts, '', 'without dgConfirmDelete: not asked');
  CheckEq(DS.StoredText, '[3|n3] [4|n4] [5|n5]', 'deleted');
  G.ReadOnly := True;
  G.Key(VK_DELETE, [ssCtrl]);
  CheckEqInt(DS.RecordCount, 3, 'a read-only grid does not delete');
  PressKey(G.Handle, VK_INSERT);
  CheckEq(StateName(DS.State), 'dsBrowse', 'and does not insert');
  G.ReadOnly := False;
  G.Options := AllOptions;
  MessageBoxHook := nil;

  Section('keyboard: append');
  DS.Last;
  PressKey(G.Handle, VK_DOWN);
  CheckEq(StateName(DS.State), 'dsInsert', 'Down on the last record appends');
  CheckEq(G.CellText(1, G.VisibleRowCount - 1), '', 'the new row is the last row of the grid');
  CheckEq(G.CellText(1, G.VisibleRowCount - 2), 'n5', 'after the last record');
  DS.HookEvents;
  ClearLog;
  PressKey(G.Handle, VK_DOWN);
  CheckEq(TakeLog, '', 'Down in the untouched new row does nothing');
  CheckEq(StateName(DS.State), 'dsInsert', 'the row stays');
  PressKey(G.Handle, VK_UP);
  CheckEq(StateName(DS.State), 'dsBrowse', 'Up leaves and cancels it');
  CheckEqInt(DS.RecordCount, 3, 'nothing appended');
  DS.Last;
  PressKey(G.Handle, VK_DOWN);
  DS.FieldByName('id').AsInteger := 77;
  PressKey(G.Handle, VK_DOWN);
  CheckEq(DS.StoredText, '[3|n3] [4|n4] [5|n5] [77|NULL]', 'Down in a filled new row posts it');
  CheckEq(StateName(DS.State), 'dsInsert', 'and appends the next one');
  DS.Cancel;
  DS.Last;
  DS.Edit;
  DS.FieldByName('name').AsString := 'e77';
  PressKey(G.Handle, VK_DOWN);
  CheckEq(DS.StoredText, '[3|n3] [4|n4] [5|n5] [77|e77]', 'Down in the edited last record posts it');
  CheckEq(StateName(DS.State), 'dsInsert', 'and appends');
  DS.Cancel;
  G.ReadOnly := True;
  DS.Last;
  PressKey(G.Handle, VK_DOWN);
  CheckEq(StateName(DS.State), 'dsBrowse', 'a read-only grid does not append');
  G.ReadOnly := False;
  G.Options := AllOptions - [dgEditing];
  PressKey(G.Handle, VK_DOWN);
  CheckEq(StateName(DS.State), 'dsBrowse', 'nor a grid without dgEditing');
  G.Options := AllOptions;
  DS.Close;
  DS.DriverReadOnly := True;
  DS.Open;
  DS.Last;
  N := DS.RecordCount;
  PressKey(G.Handle, VK_DOWN);
  CheckEq(StateName(DS.State), 'dsBrowse', 'nor a grid on a dataset that cannot be modified');
  CheckEqInt(DS.RecordCount, N, 'the records are unchanged');
  DS.Close;
  DS.DriverReadOnly := False;
  Empty := NewDS(0);
  Src.DataSet := Empty;
  Empty.Open;
  PressKey(G.Handle, VK_UP);
  CheckEq(StateName(Empty.State), 'dsBrowse', 'Up in an empty dataset does nothing');
  PressKey(G.Handle, VK_DOWN);
  CheckEq(StateName(Empty.State), 'dsInsert', 'Down in an empty dataset appends');
  Empty.Cancel;
  G.Free;
  Before.Free;
  After.Free;
  Src.Free;
  Empty.Free;
  DS.Free;
end;

{ ---------------------------------------------------------------- complete }

function CanEdit(G: TDBGrid): Boolean;
begin
  G.ShowEditor;
  Result := G.EditorMode;
  G.HideEditor;
end;

procedure TestEditingPossible(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  C: TColumn;
  B: TBytes;
begin
  Section('editing possible');
  DS := TTestDataSet.Create(nil);
  DS.AddDef('id', ftInteger);
  DS.AddDef('name', ftString, 10);
  DS.AddDef('data', ftBlob);
  SetLength(B, 2);
  DS.AddRow([IntValue(1), StringValue('n1'), BytesValue(B)]);
  DS.AddRow([IntValue(2), StringValue('n2'), BytesValue(B)]);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.DataSource := Src;
  CheckEqBool(CanEdit(G), False, 'a closed dataset: no');
  DS.Open;
  CheckEqBool(CanEdit(G), True, 'the default: yes');
  G.Options := AllOptions - [dgEditing];
  CheckEqBool(CanEdit(G), False, 'without dgEditing: no');
  G.Options := AllOptions + [dgRowSelect];
  CheckEqBool(CanEdit(G), False, 'with dgRowSelect: no');
  G.Options := AllOptions;
  G.ReadOnly := True;
  CheckEqBool(CanEdit(G), False, 'a read-only grid: no');
  G.ReadOnly := False;
  G.SelectedIndex := 2;
  CheckEqBool(CanEdit(G), False, 'a blob column: no');
  G.SelectedIndex := 0;
  Src.AutoEdit := False;
  CheckEqBool(CanEdit(G), True, 'AutoEdit off: the editor shows (changes are rejected when they start)');
  Src.AutoEdit := True;
  G.EditorMode := True;
  CheckEqBool(G.EditorMode, True, 'EditorMode := True shows the editor');
  G.EditorMode := False;
  CheckEqBool(G.EditorMode, False, 'EditorMode := False hides it');
  C := G.Columns.Add;
  C.FieldName := 'name';
  C := G.Columns.Add;
  C.FieldName := 'id';
  C.ReadOnly := True;
  C := G.Columns.Add;
  C.FieldName := 'missing';
  G.SelectedIndex := 0;
  CheckEqBool(CanEdit(G), True, 'a persistent column: yes');
  G.SelectedIndex := 1;
  CheckEqBool(CanEdit(G), False, 'a read-only column: no');
  G.SelectedIndex := 2;
  CheckEqBool(CanEdit(G), False, 'a column without a field: no');
  G.Columns.Clear;
  DS.Close;
  DS.FixedField := 'id';
  DS.Open;
  G.SelectedIndex := 0;
  CheckEqBool(CanEdit(G), False, 'a field that cannot be modified: no');
  G.SelectedIndex := 1;
  CheckEqBool(CanEdit(G), True, 'the field beside it: yes');
  DS.Close;
  DS.FixedField := '';
  DS.DriverReadOnly := True;
  DS.Open;
  CheckEqBool(CanEdit(G), False, 'a dataset that cannot be modified: no');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestEditor(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  Before: TEdit;
  Ed: TEdit;
  R: TRect;
begin
  Section('editor');
  DS := NewDS(3);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  Before := NewEdit(F);
  G := NewGrid(F);
  G.DataSource := Src;
  DS.Open;
  G.SetFocus;
  G.SelectedIndex := 1;
  Check(G.InplaceEditor = nil, 'no editor control before the first use');
  G.ShowEditor;
  Ed := G.InplaceEditor;
  Check(Ed <> nil, 'ShowEditor creates the editor');
  Check(Ed.Parent = G, 'a child of the grid');
  CheckEq(Ed.Name, '', 'without a name');
  CheckEq(Ed.Text, 'n1', 'with the edit text of the field');
  CheckEqInt(Ed.SelLength, 2, 'all selected');
  Check(F.ActiveControl = Ed, 'focused');
  R := G.CellRect(1, 0);
  Dec(R.Right);
  Dec(R.Bottom);
  CheckEq(RectText(Ed.BoundsRect), RectText(R), 'over the selected cell, inside its grid lines');
  CheckEq(StateName(DS.State), 'dsBrowse', 'showing the editor does not start editing');
  TypeText(Ed.Handle, 'ab');
  CheckEq(StateName(DS.State), 'dsEdit', 'the first change puts the dataset into edit mode');
  CheckEq(Ed.Text, 'ab', 'the typed text');
  CheckEq(DS.FieldByName('name').AsString, 'n1', 'the field is not written yet');
  CheckEqBool(DS.Modified, False, 'the record is not modified yet');
  PressKey(Ed.Handle, VK_RETURN);
  CheckEq(DS.FieldByName('name').AsString, 'ab', 'Enter writes the text to the field');
  CheckEqBool(G.EditorMode, False, 'and hides the editor');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record is not posted');
  Check(F.ActiveControl = G, 'the grid has the focus');
  G.ShowEditor;
  TypeText(Ed.Handle, 'zz');
  PressKey(Ed.Handle, VK_ESCAPE);
  CheckEqBool(G.EditorMode, False, 'Escape hides the editor');
  CheckEq(DS.FieldByName('name').AsString, 'ab', 'without writing');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record stays in edit mode');
  DS.Cancel;
  G.ShowEditor;
  PressKey(Ed.Handle, VK_RETURN);
  CheckEq(StateName(DS.State), 'dsBrowse', 'Enter in an unchanged editor writes nothing');
  CheckEqBool(G.EditorMode, False, 'and hides it');

  Section('editor: moving commits');
  G.SelectedIndex := 0;
  G.ShowEditor;
  TypeText(Ed.Handle, '7');
  PressKey(Ed.Handle, VK_TAB);
  CheckEqInt(DS.FieldByName('id').AsInteger, 7, 'Tab writes the text');
  CheckEqInt(G.SelectedIndex, 1, 'and moves to the next column');
  CheckEqBool(G.EditorMode, False, 'the editor is hidden');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record is not posted');
  DS.Cancel;
  G.ShowEditor;
  TypeText(Ed.Handle, 'dn');
  PressKey(Ed.Handle, VK_DOWN);
  CheckEq(DS.StoredText, '[1|dn] [2|n2] [3|n3]', 'Down writes the text and the row change posts the record');
  CheckEqInt(DS.RecNo, 2, 'the next row');
  CheckEqBool(G.EditorMode, False, 'the editor is hidden');
  G.ShowEditor;
  CheckEq(Ed.Text, 'n2', 'the editor shows the cell it is opened on');
  TypeText(Ed.Handle, 'up');
  PressKey(Ed.Handle, VK_UP);
  CheckEq(DS.StoredText, '[1|dn] [2|up] [3|n3]', 'Up writes and posts');
  CheckEqInt(DS.RecNo, 1, 'the previous row');
  G.ShowEditor;
  TypeText(Ed.Handle, 'sc');
  G.SelectedIndex := 0;
  CheckEq(DS.FieldByName('name').AsString, 'sc', 'selecting another column writes the text');
  CheckEq(StateName(DS.State), 'dsEdit', 'without posting');
  DS.Cancel;
  G.SelectedIndex := 1;
  G.ShowEditor;
  TypeText(Ed.Handle, 'pp');
  DS.Post;
  CheckEq(DS.StoredText, '[1|pp] [2|up] [3|n3]', 'Post by the application writes the text first');
  CheckEqBool(G.EditorMode, False, 'and the editor is hidden');
  G.ShowEditor;
  TypeText(Ed.Handle, 'nx');
  DS.Next;
  CheckEq(DS.StoredText, '[1|nx] [2|up] [3|n3]', 'a move by the application writes the text and posts');
  DS.First;
  G.ShowEditor;
  TypeText(Ed.Handle, 'cc');
  DS.Cancel;
  CheckEqBool(G.EditorMode, False, 'Cancel by the application hides the editor');
  CheckEq(DS.StoredText, '[1|nx] [2|up] [3|n3]', 'nothing written');

  Section('editor: deleting keys');
  G.ShowEditor;
  TypeText(Ed.Handle, #8);
  CheckEq(StateName(DS.State), 'dsEdit', 'Backspace is a change');
  CheckEq(Ed.Text, '', 'the selected text is gone');
  PressKey(Ed.Handle, VK_RETURN);
  CheckEq(DS.FieldByName('name').AsString, '', 'an emptied editor writes an empty text');
  DS.Cancel;
  G.ShowEditor;
  PressKey(Ed.Handle, VK_DELETE);
  CheckEq(StateName(DS.State), 'dsEdit', 'Delete is a change');
  CheckEq(Ed.Text, '', 'the selected text is gone');
  PressKey(Ed.Handle, VK_ESCAPE);
  DS.Cancel;

  Section('editor: rejected changes');
  Src.AutoEdit := False;
  G.ShowEditor;
  TakeBeeps;
  TypeText(Ed.Handle, 'x');
  CheckEqInt(TakeBeeps, 1, 'AutoEdit off: a typed character beeps');
  CheckEq(Ed.Text, 'nx', 'and is swallowed');
  CheckEq(StateName(DS.State), 'dsBrowse', 'the dataset stays in browse mode');
  PressKey(Ed.Handle, VK_DELETE);
  CheckEqInt(TakeBeeps, 1, 'Delete beeps');
  CheckEq(Ed.Text, 'nx', 'and deletes nothing');
  DS.Edit;
  TypeText(Ed.Handle, 'x');
  CheckEqInt(TakeBeeps, 0, 'after Edit by the application the character is accepted');
  CheckEq(Ed.Text, 'x', 'and typed');
  PressKey(Ed.Handle, VK_ESCAPE);
  DS.Cancel;
  TypeText(G.Handle, 'k');
  CheckEqInt(TakeBeeps, 1, 'a character typed into the grid beeps as well');
  CheckEqBool(G.EditorMode, False, 'and shows no editor');
  Src.AutoEdit := True;

  Section('editor: a text the field rejects');
  G.SelectedIndex := 0;
  G.ShowEditor;
  TypeText(Ed.Handle, 'abc');
  H.Log := '';
  PressKey(Ed.Handle, VK_RETURN);
  Check(Pos('error:', H.Log) = 1, 'Enter: the exception reaches the application');
  CheckEqBool(G.EditorMode, True, 'the editor stays');
  CheckEq(Ed.Text, 'abc', 'with its text');
  Check(F.ActiveControl = Ed, 'and the focus');
  H.Log := '';
  PressKey(Ed.Handle, VK_DOWN);
  Check(Pos('error:', H.Log) = 1, 'Down: the exception reaches the application');
  CheckEqInt(DS.RecNo, 1, 'the row does not change');
  CheckEqBool(G.EditorMode, True, 'the editor stays');
  H.Log := '';
  PressKey(Ed.Handle, VK_TAB);
  Check(Pos('error:', H.Log) = 1, 'Tab: the exception reaches the application');
  CheckEqInt(G.SelectedIndex, 0, 'the column does not change');
  H.Log := '';
  PressKey(Ed.Handle, VK_ESCAPE);
  CheckEqBool(G.EditorMode, False, 'Escape gives the text up');
  CheckEq(H.Log, '', 'without an error');
  DS.Cancel;

  Section('dgAlwaysShowEditor');
  Before.SetFocus;
  G.SelectedIndex := 1;
  G.Options := AllOptions + [dgAlwaysShowEditor];
  CheckEqBool(G.EditorMode, False, 'no editor while the grid is not focused');
  G.SetFocus;
  CheckEqBool(G.EditorMode, True, 'focusing the grid shows the editor');
  CheckEq(Ed.Text, 'nx', 'on the selected cell');
  Check(F.ActiveControl = Ed, 'the editor has the focus');
  PressKey(Ed.Handle, VK_DOWN);
  CheckEqInt(DS.RecNo, 2, 'Down moves');
  CheckEqBool(G.EditorMode, True, 'the editor stays shown');
  CheckEq(Ed.Text, 'up', 'with the text of the new row');
  R := G.CellRect(1, 1);
  Dec(R.Right);
  Dec(R.Bottom);
  CheckEq(RectText(Ed.BoundsRect), RectText(R), 'over the new cell');
  TypeText(Ed.Handle, 'aa');
  PressKey(Ed.Handle, VK_RETURN);
  CheckEq(DS.FieldByName('name').AsString, 'aa', 'Enter writes');
  CheckEqBool(G.EditorMode, True, 'and the editor stays');
  TypeText(Ed.Handle, 'bb');
  PressKey(Ed.Handle, VK_ESCAPE);
  CheckEqBool(G.EditorMode, True, 'Escape keeps the editor');
  CheckEq(Ed.Text, 'aa', 'with the text of the field again');
  G.Key(VK_TAB, [ssShift]);
  CheckEqInt(G.SelectedIndex, 0, 'a column change');
  CheckEqBool(G.EditorMode, True, 'keeps the editor');
  CheckEq(Ed.Text, '2', 'on the new cell');
  Before.SetFocus;
  CheckEqBool(G.EditorMode, False, 'leaving the grid hides the editor');
  CheckEqBool(Ed.Visible, False, 'the editor control is hidden');
  DS.Cancel;
  G.Free;
  Before.Free;
  Src.Free;
  DS.Free;
end;

procedure TestExit(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
  Before, After: TEdit;
  Ed: TEdit;
  Err: string;
begin
  Section('leaving the grid');
  DS := NewDS(3);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  Before := NewEdit(F);
  G := NewGrid(F);
  After := NewEdit(F);
  G.DataSource := Src;
  G.OnEnter := H.GridEnter;
  G.OnExit := H.GridExit;
  DS.Open;
  Before.SetFocus;
  H.Log := '';
  G.SetFocus;
  G.SelectedIndex := 1;
  G.ShowEditor;
  Ed := G.InplaceEditor;
  PressKey(Ed.Handle, VK_RETURN);
  G.ShowEditor;
  TypeText(Ed.Handle, 'ex');
  After.SetFocus;
  CheckEq(H.Log, 'enter;exit;', 'the editor is part of the grid: one OnEnter, one OnExit');
  CheckEq(DS.FieldByName('name').AsString, 'ex', 'leaving writes the text of the editor');
  CheckEq(StateName(DS.State), 'dsEdit', 'without posting');
  CheckEqBool(G.EditorMode, False, 'the editor is closed');
  CheckEqBool(Ed.Visible, False, 'and hidden');
  Check(F.ActiveControl = After, 'the focus moved');
  DS.Cancel;

  Section('leaving with a text the field rejects');
  G.SetFocus;
  G.SelectedIndex := 0;
  G.ShowEditor;
  TypeText(Ed.Handle, 'abc');
  Err := FocusError(After);
  Check(Err <> '', 'the exception leaves SetFocus');
  Check(F.ActiveControl = Ed, 'the editor keeps the focus');
  CheckEqBool(G.EditorMode, True, 'and stays open');
  CheckEq(Ed.Text, 'abc', 'with its text');
  PressKey(Ed.Handle, VK_ESCAPE);
  CheckEq(FocusError(After), '', 'after Escape the focus can leave');
  DS.Cancel;

  Section('dgCancelOnExit');
  G.SetFocus;
  PressKey(G.Handle, VK_INSERT);
  CheckEq(StateName(DS.State), 'dsInsert', 'a new record');
  After.SetFocus;
  CheckEq(StateName(DS.State), 'dsBrowse', 'leaving cancels the untouched new record');
  CheckEqInt(DS.RecordCount, 3, 'nothing inserted');
  G.SetFocus;
  PressKey(G.Handle, VK_INSERT);
  DS.FieldByName('id').AsInteger := 50;
  After.SetFocus;
  CheckEq(StateName(DS.State), 'dsInsert', 'a modified new record stays');
  DS.Cancel;
  G.SetFocus;
  PressKey(G.Handle, VK_INSERT);
  G.SelectedIndex := 1;
  G.ShowEditor;
  TypeText(Ed.Handle, 'new');
  After.SetFocus;
  CheckEq(StateName(DS.State), 'dsInsert', 'a new record with a text in the editor stays');
  CheckEq(DS.FieldByName('name').AsString, 'new', 'the text is written');
  DS.Cancel;
  G.SetFocus;
  DS.Edit;
  After.SetFocus;
  CheckEq(StateName(DS.State), 'dsEdit', 'an untouched record in edit mode is not cancelled');
  DS.Cancel;
  G.Options := AllOptions - [dgCancelOnExit];
  G.SetFocus;
  PressKey(G.Handle, VK_INSERT);
  After.SetFocus;
  CheckEq(StateName(DS.State), 'dsInsert', 'without dgCancelOnExit the new record stays');
  DS.Cancel;
  G.Free;
  Before.Free;
  After.Free;
  Src.Free;
  DS.Free;
end;

procedure TestRowChange(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
begin
  Section('row changes post');
  DS := NewDS(3);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.DataSource := Src;
  DS.Open;
  G.SetFocus;
  DS.Edit;
  DS.FieldByName('name').AsString := 'm1';
  PressKey(G.Handle, VK_DOWN);
  CheckEq(DS.StoredText, '[1|m1] [2|n2] [3|n3]', 'Down posts the modified record');
  CheckEqInt(DS.RecNo, 2, 'and moves');
  DS.Edit;
  DS.FieldByName('name').AsString := 'bad';
  DS.FailApply := 1;
  H.Log := '';
  PressKey(G.Handle, VK_DOWN);
  CheckEq(H.Log, 'error:EDatabaseError;', 'a post error reaches the application');
  CheckEqInt(DS.RecNo, 2, 'the cursor stays on the row');
  CheckEq(StateName(DS.State), 'dsEdit', 'in edit mode');
  DS.FailApply := 1;
  H.Log := '';
  ClickCell(G, 1, 0);
  CheckEq(H.Log, 'error:EDatabaseError;', 'the same for a click on another row');
  CheckEqInt(DS.RecNo, 2, 'the cursor stays');
  PressKey(G.Handle, VK_UP);
  CheckEq(DS.StoredText, '[1|m1] [2|bad] [3|n3]', 'without the error the record is posted');
  CheckEqInt(DS.RecNo, 1, 'and the cursor moves');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestRepaint(F: TForm);
var
  DS: TTestDataSet;
  Src: TDataSource;
  G: TTestGrid;
begin
  Section('repainting');
  DS := NewDS(5);
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.DataSource := Src;
  G.TakeInvals;
  DS.Open;
  CheckEq(G.TakeInvals, 'grid', 'opening: everything');
  DS.Next;
  CheckEq(G.TakeInvals, 'grid', 'a scroll: everything');
  DS.Edit;
  CheckEq(G.TakeInvals, 'row1;', 'entering edit mode: the current row (its indicator)');
  DS.FieldByName('name').AsString := 'r';
  CheckEq(G.TakeInvals, 'row1;', 'a field change: the current row only');
  DS.Post;
  CheckEq(G.TakeInvals, 'grid', 'a post: everything');
  DS.FieldByName('name').Index := 0;
  CheckEq(G.TakeInvals, 'grid', 'a layout change: everything');
  G.SelectedIndex := 1;
  CheckEq(G.TakeInvals, 'row1;', 'a column change: the current row');
  DS.Close;
  CheckEq(G.TakeInvals, 'grid', 'closing: everything');
  G.Free;
  Src.Free;
  DS.Free;
end;

procedure TestLifetime(F: TForm);
var
  DS: TTestDataSet;
  Box: TComponent;
  Src: TDataSource;
  G: TTestGrid;
begin
  Section('lifetime');
  DS := NewDS(2);
  Box := TComponent.Create(nil);
  Src := TDataSource.Create(Box);
  Src.DataSet := DS;
  G := NewGrid(F);
  G.DataSource := Src;
  DS.Open;
  G.SetFocus;
  G.ShowEditor;
  CheckEqBool(G.EditorMode, True, 'linked, the editor open');
  Box.Free;
  Check(G.DataSource = nil, 'destroying the source clears DataSource');
  CheckEqInt(G.ColCount, 0, 'no columns');
  CheckEqBool(G.EditorMode, False, 'no editor');
  CheckEq(Painted(G), '', 'paints empty');
  Src := TDataSource.Create(nil);
  Src.DataSet := DS;
  G.DataSource := Src;
  CheckEqInt(G.ColCount, 2, 'a new source at run time');
  CheckEq(G.CellText(1, 1), 'n2', 'shows its data');
  G.ShowEditor;
  G.Free;
  Check(True, 'a grid with an open editor can be destroyed');
  G := NewGrid(F);
  G.DataSource := Src;
  G.Columns.Add.FieldName := 'name';
  DS.Free;
  CheckEqInt(G.ColCount, 1, 'destroying the dataset leaves the persistent columns');
  CheckEq(G.CellText(0, 0), '', 'without data');
  CheckEq(Painted(G), 'T0=name;', 'paints the titles');
  G.Free;
  Src.Free;
end;

var
  F: TForm;
begin
  Application.Initialize;
  HookBeeps;
  H := THandlers.Create;
  Application.OnException := H.AppError;
  F := NewForm;
  try
    TestDefaults(F);
    TestColumns(F);
    TestCellText(F);
    TestGeometry(F);
    TestPainting(F);
    TestRows(F);
    TestScrollBars(F);
    TestMouse(F);
    TestKeyboard(F);
    TestEditingPossible(F);
    TestEditor(F);
    TestExit(F);
    TestRowChange(F);
    TestRepaint(F);
    TestLifetime(F);
  finally
    F.Free;
  end;
  H.Free;
  WriteLn('failed checks: ', FailedChecks);
end.
