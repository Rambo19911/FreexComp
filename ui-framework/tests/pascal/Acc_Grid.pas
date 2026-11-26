// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1435, UIF-1436, UIF-1437, UIF-1438, UIF-1439
// needs: desktop
// phase: 8
program Acc_Grid;
{ U-157, accessibility.md section 7 (UIF-1435...1439), in process: the grid provider and its cells. Grid, Table and
  Selection on the grid (counts, GetItem also outside the visible range and out of range, header arrays of fixed
  row 0 and fixed column 0, row-major order, the selection); cells as fragments of the grid (one object per cell
  while one is alive, runtime ids, navigation over the visible cells in row-major order, also after scrolling,
  bounding rectangles in screen coordinates, offscreen cells, hit testing, control types, names from
  GetAccessibleCellText of the grid class); GridItem and TableItem of every cell, SelectionItem and Value of cells
  that are not fixed (Select as a click, OnSelectCell refusing, a disabled grid, the range rules of AddToSelection /
  RemoveFromSelection, Value read and written through SetEditText, a read-only grid refused); focus (HasKeyboardFocus,
  the root's GetFocus, SetFocus of a cell); lifetime (a cell outside a shrunk grid, a cell of a destroyed grid or of
  a destroyed window answers UIA_E_ELEMENTNOTAVAILABLE; every provider object is released at the end). Visible cells
  are computed by the test from CellRect, so the output does not depend on the screen's DPI. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, Grids, ActiveX, UIAutomation,
  Accessibility, AccKit, TestKit;

type
  PProvider = ^IRawElementProviderSimple;

  { UIF-1438: a grid class with cell texts of its own }
  TTextGrid = class(TCustomGrid)
  public
    function GetAccessibleCellText(ACol, ARow: Integer): string; override;
  end;

  { UIF-1438: the base class has no cell texts }
  TPlainGrid = class(TCustomGrid)
  end;

  TEvents = class
  public
    Edits: string;
    Refuse: Boolean;
    procedure SetEdit(Sender: TObject; ACol, ARow: Integer; const Value: string);
    procedure SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
  end;

function TTextGrid.GetAccessibleCellText(ACol, ARow: Integer): string;
begin
  Result := 'c' + IntToStr(ACol) + 'r' + IntToStr(ARow);
end;

procedure TEvents.SetEdit(Sender: TObject; ACol, ARow: Integer; const Value: string);
begin
  Edits := Edits + IntToStr(ACol) + ',' + IntToStr(ARow) + '=' + Value + ' ';
end;

procedure TEvents.SelectCell(Sender: TObject; ACol, ARow: Integer; var CanSelect: Boolean);
begin
  CanSelect := not Refuse;
end;

var
  F: TForm;
  G: TStringGrid;                                            { 12 x 30, fixed row and column, goEditing }
  T: TTextGrid;                                              { no fixed cells, no editing, no range selection }
  Plain: TPlainGrid;
  Other: TEdit;
  Ev: TEvents;

procedure Build;
var
  I: Integer;
begin
  Ev := TEvents.Create;
  F := TForm.CreateNew(nil);
  F.Caption := 'Acc_Grid';
  F.SetBounds(40, 40, 640, 420);
  G := TStringGrid.Create(F);
  G.Name := 'Data';
  G.Parent := F;
  G.SetBounds(8, 8, 300, 140);
  G.ColCount := 12;
  G.RowCount := 30;
  G.Options := G.Options + [goEditing];
  G.OnSetEditText := Ev.SetEdit;
  G.OnSelectCell := Ev.SelectCell;
  G.Cells[1, 0] := 'Name';
  G.Cells[2, 0] := 'Size';
  for I := 1 to 29 do
    G.Cells[0, I] := 'Row ' + IntToStr(I);
  G.Cells[1, 1] := 'Alpha';
  G.Cells[1, 2] := 'Beta';
  G.Cells[2, 2] := 'Two';
  T := TTextGrid.Create(F);
  T.Name := 'Texts';
  T.Parent := F;
  T.SetBounds(320, 8, 300, 140);
  T.FixedRows := 0;
  T.FixedCols := 0;
  T.Options := T.Options - [goRangeSelect];
  Plain := TPlainGrid.Create(F);
  Plain.Parent := F;
  Plain.SetBounds(8, 160, 200, 100);
  Other := TEdit.Create(F);
  Other.Parent := F;
  Other.SetBounds(320, 160, 100, 23);
  F.Show;
end;

function Pat(const P: IRawElementProviderSimple; Id: Integer): IUnknown;
begin
  Result := nil;
  if (P = nil) or (P.GetPatternProvider(Id, Result) <> S_OK) then
    Result := nil;
end;

function GridOf(C: TCustomGrid): IGridProvider;
begin
  Result := Pat(GetAutomationProvider(C), UIA_GridPatternId) as IGridProvider;
end;

function CellOf(C: TCustomGrid; ACol, ARow: Integer): IRawElementProviderSimple;
begin
  Result := nil;
  GridOf(C).GetItem(ARow, ACol, Result);
end;

function NameOf(const P: IRawElementProviderSimple): string;
begin
  Result := Prop(P, UIA_NamePropertyId);
end;

function TypeOf(const P: IRawElementProviderSimple): string;
var
  S: string;
begin
  S := Prop(P, UIA_ControlTypePropertyId);
  if (S <> '') and (S[1] in ['0'..'9']) then
    Result := TypeWord(StrToInt(S))
  else
    Result := S;
end;

{ 'col,row' of a cell from its GridItem pattern; 'nil' for no element }
function Where(const P: IRawElementProviderSimple): string;
var
  Gi: IGridItemProvider;
  C, R: Integer;
begin
  if P = nil then
  begin
    Result := 'nil';
    Exit;
  end;
  Gi := Pat(P, UIA_GridItemPatternId) as IGridItemProvider;
  if Gi = nil then
  begin
    Result := 'no GridItem';
    Exit;
  end;
  C := -1;
  R := -1;
  Gi.get_Column(C);
  Gi.get_Row(R);
  Result := IntToStr(C) + ',' + IntToStr(R);
end;

function Nav(const P: IRawElementProviderSimple; Dir: Integer): IRawElementProviderSimple;
var
  Frag, Next: IRawElementProviderFragment;
begin
  Result := nil;
  Frag := P as IRawElementProviderFragment;
  Next := nil;
  Frag.Navigate(Dir, Next);
  if Next <> nil then
    Result := Next as IRawElementProviderSimple;
end;

function Same(const A, B: IRawElementProviderSimple): Boolean;
begin
  Result := Pointer(A) = Pointer(B);
end;

function ArrayCount(A: PSafeArray): Integer;
begin
  if A = nil then
    Result := 0
  else
    Result := PCardinal(NativeUInt(A) + 16 + SizeOf(Pointer))^;   { rgsabound[0].cElements }
end;

function ArrayItem(A: PSafeArray; I: Integer): IRawElementProviderSimple;
var
  Data: NativeUInt;
begin
  Data := NativeUInt(PPointer(NativeUInt(A) + 16)^);                { pvData }
  Result := PProvider(Data + NativeUInt(I) * SizeOf(Pointer))^;
end;

{ the names of the elements of an array, space-separated; the array is destroyed }
function ArrayNames(A: PSafeArray): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to ArrayCount(A) - 1 do
    Result := Result + NameOf(ArrayItem(A, I)) + '|';
  if A <> nil then
    SafeArrayDestroy(A);
end;

function ArrayCells(A: PSafeArray): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to ArrayCount(A) - 1 do
    Result := Result + Where(ArrayItem(A, I)) + ' ';
  if A <> nil then
    SafeArrayDestroy(A);
end;

{ the cells CellRect reports as shown, row-major (screen order = index order while the scrolled-away columns and
  rows have empty rectangles) }
function ExpectedVisible(C: TStringGrid): string;
var
  X, Y: Integer;
  R: TRect;
begin
  Result := '';
  for Y := 0 to C.RowCount - 1 do
    for X := 0 to C.ColCount - 1 do
    begin
      R := C.CellRect(X, Y);
      if R.Right > R.Left then
        Result := Result + IntToStr(X) + ',' + IntToStr(Y) + ' ';
    end;
end;

function WalkForward(C: TCustomGrid): string;
var
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Result := '';
  P := Nav(GetAutomationProvider(C), NavigateDirection_FirstChild);
  N := 0;
  while (P <> nil) and (N < 2000) do
  begin
    Result := Result + Where(P) + ' ';
    P := Nav(P, NavigateDirection_NextSibling);
    Inc(N);
  end;
end;

function WalkBack(C: TCustomGrid): string;
var
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Result := '';
  P := Nav(GetAutomationProvider(C), NavigateDirection_LastChild);
  N := 0;
  while (P <> nil) and (N < 2000) do
  begin
    Result := Where(P) + ' ' + Result;
    P := Nav(P, NavigateDirection_PreviousSibling);
    Inc(N);
  end;
end;

function RuntimeIdText(const P: IRawElementProviderSimple): string;
var
  Ids: PSafeArray;
  Data: PInteger;
  I, N: Integer;
begin
  Ids := nil;
  (P as IRawElementProviderFragment).GetRuntimeId(Ids);
  Result := '';
  if Ids = nil then
    Exit;
  N := ArrayCount(Ids);
  Data := PPointer(NativeUInt(Ids) + 16)^;
  for I := 0 to N - 1 do
    Result := Result + IntToStr(PInteger(NativeUInt(Data) + NativeUInt(I) * 4)^) + ' ';
  SafeArrayDestroy(Ids);
end;

function BoundsOf(const P: IRawElementProviderSimple): TUiaRect;
begin
  FillChar(Result, SizeOf(Result), 0);
  (P as IRawElementProviderFragment).get_BoundingRectangle(Result);
end;

function SelItem(const P: IRawElementProviderSimple): ISelectionItemProvider;
begin
  Result := Pat(P, UIA_SelectionItemPatternId) as ISelectionItemProvider;
end;

function ValueOf(const P: IRawElementProviderSimple): IValueProvider;
begin
  Result := Pat(P, UIA_ValuePatternId) as IValueProvider;
end;

function IsSel(const P: IRawElementProviderSimple): Boolean;
var
  B: BOOL;
begin
  B := False;
  SelItem(P).get_IsSelected(B);
  Result := B;
end;

function SelText(C: TCustomGrid): string;
var
  S: TGridRect;
begin
  S := C.Selection;
  Result := IntToStr(S.Left) + ',' + IntToStr(S.Top) + '-' + IntToStr(S.Right) + ',' + IntToStr(S.Bottom);
end;

procedure TestGrid;
var
  Gp: IGridProvider;
  Tp: ITableProvider;
  Sp: ISelectionProvider;
  P, Q: IRawElementProviderSimple;
  N: Integer;
  B: BOOL;
  A: PSafeArray;
begin
  Section('the grid: Grid, Table and Selection (UIF-1435)');
  CheckEqS('DataGrid', ControlTypeOf(G), 'control type DataGrid');
  Gp := GridOf(G);
  Check(Gp <> nil, 'Grid pattern');
  Tp := Pat(GetAutomationProvider(G), UIA_TablePatternId) as ITableProvider;
  Check(Tp <> nil, 'Table pattern');
  Sp := Pat(GetAutomationProvider(G), UIA_SelectionPatternId) as ISelectionProvider;
  Check(Sp <> nil, 'Selection pattern');
  Check(Pat(GetAutomationProvider(G), UIA_ValuePatternId) = nil, 'no other pattern');
  N := 0;
  CheckEqS('S_OK', ResultWord(Gp.get_RowCount(N)), 'get_RowCount');
  CheckEqI(30, N, 'RowCount = RowCount');
  CheckEqS('S_OK', ResultWord(Gp.get_ColumnCount(N)), 'get_ColumnCount');
  CheckEqI(12, N, 'ColumnCount = ColCount');
  P := nil;
  CheckEqS('S_OK', ResultWord(Gp.GetItem(2, 1, P)), 'GetItem(2, 1)');
  CheckEqS('1,2', Where(P), 'GetItem(row 2, column 1) is cell (1, 2)');
  CheckEqS('Beta', NameOf(P), 'its name is the cell text');
  Q := nil;
  Gp.GetItem(2, 1, Q);
  Check(Same(P, Q), 'the same cell twice: the same provider');
  Q := nil;
  CheckEqS('S_OK', ResultWord(Gp.GetItem(29, 11, Q)), 'GetItem of a cell outside the visible range');
  CheckEqS('11,29', Where(Q), 'it is cell (11, 29)');
  Check(Gp.GetItem(-1, 0, Q) = E_INVALIDARG, 'row -1: E_INVALIDARG');
  Check(Q = nil, 'and no element');
  Check(Gp.GetItem(30, 0, Q) = E_INVALIDARG, 'row RowCount: E_INVALIDARG');
  Check(Gp.GetItem(0, 12, Q) = E_INVALIDARG, 'column ColCount: E_INVALIDARG');
  Check(Gp.GetItem(0, -1, Q) = E_INVALIDARG, 'column -1: E_INVALIDARG');
  N := -1;
  CheckEqS('S_OK', ResultWord(Tp.get_RowOrColumnMajor(N)), 'get_RowOrColumnMajor');
  CheckEqI(RowOrColumnMajor_RowMajor, N, 'row-major');
  A := nil;
  CheckEqS('S_OK', ResultWord(Tp.GetColumnHeaders(A)), 'GetColumnHeaders');
  CheckEqI(12, ArrayCount(A), 'column headers: every cell of fixed row 0');
  CheckEqS('|Name|Size||||||||||', ArrayNames(A), 'their names, column by column');
  A := nil;
  CheckEqS('S_OK', ResultWord(Tp.GetRowHeaders(A)), 'GetRowHeaders');
  CheckEqI(30, ArrayCount(A), 'row headers: every cell of fixed column 0');
  CheckEqS('0,0 0,1 0,2 ', Copy(ArrayCells(A), 1, 12), 'row by row');
  A := nil;
  CheckEqS('S_OK', ResultWord((Pat(GetAutomationProvider(T), UIA_TablePatternId) as ITableProvider).GetColumnHeaders(A)),
    'no fixed row: GetColumnHeaders');
  Check(A = nil, 'no column headers');
  (Pat(GetAutomationProvider(T), UIA_TablePatternId) as ITableProvider).GetRowHeaders(A);
  Check(A = nil, 'no fixed column: no row headers');
  B := False;
  Sp.get_CanSelectMultiple(B);
  CheckEqB(True, B, 'goRangeSelect: CanSelectMultiple');
  (Pat(GetAutomationProvider(T), UIA_SelectionPatternId) as ISelectionProvider).get_CanSelectMultiple(B);
  CheckEqB(False, B, 'without goRangeSelect: not');
  B := False;
  Sp.get_IsSelectionRequired(B);
  CheckEqB(True, B, 'IsSelectionRequired');
  A := nil;
  CheckEqS('S_OK', ResultWord(Sp.GetSelection(A)), 'GetSelection');
  CheckEqS('1,1 ', ArrayCells(A), 'the selection: the current cell');
end;

procedure TestCells;
var
  P, Q, Root: IRawElementProviderSimple;
  FR: IRawElementProviderFragmentRoot;
  Frag: IRawElementProviderFragment;
  R: TUiaRect;
  Cr: TRect;
  Pt: TPoint;
  Forward: string;
begin
  Section('cells: fragments of the grid (UIF-1436, UIF-1438)');
  Root := GetAutomationProvider(G);
  P := CellOf(G, 1, 2);
  CheckEqS('DataItem', TypeOf(P), 'a cell: DataItem');
  CheckEqS('HeaderItem', TypeOf(CellOf(G, 0, 2)), 'a cell of the fixed column: HeaderItem');
  CheckEqS('HeaderItem', TypeOf(CellOf(G, 1, 0)), 'a cell of the fixed row: HeaderItem');
  CheckEqS('', Prop(P, UIA_AutomationIdPropertyId), 'AutomationId empty');
  CheckEqS('FreexComp', Prop(P, UIA_FrameworkIdPropertyId), 'FrameworkId');
  CheckEqS('True', Prop(P, UIA_IsContentElementPropertyId), 'a content element');
  CheckEqS('True', Prop(P, UIA_IsEnabledPropertyId), 'enabled');
  CheckEqS('Beta', G.GetAccessibleCellText(1, 2), 'TStringGrid: GetAccessibleCellText = Cells');
  CheckEqS('c3r4', NameOf(CellOf(T, 3, 4)), 'a grid class overriding GetAccessibleCellText names its cells');
  CheckEqS('', NameOf(CellOf(Plain, 1, 1)), 'TCustomGrid: an empty name');
  CheckEqS(IntToStr(UiaAppendRuntimeId) + ' 1 2 ', RuntimeIdText(P), 'runtime id [UiaAppendRuntimeId, ACol, ARow]');
  Check(RuntimeIdText(CellOf(G, 2, 1)) <> RuntimeIdText(P), 'another cell: another runtime id');
  Frag := P as IRawElementProviderFragment;
  FR := nil;
  CheckEqS('S_OK', ResultWord(Frag.get_FragmentRoot(FR)), 'get_FragmentRoot');
  Check(Pointer(FR) = Pointer(Root as IRawElementProviderFragmentRoot), 'the fragment root is the grid');
  Check(Same(Nav(P, NavigateDirection_Parent), Root), 'the parent is the grid');
  Check(Nav(P, NavigateDirection_FirstChild) = nil, 'a cell has no children');
  Q := nil;
  CheckEqS('S_OK', ResultWord(P.get_HostRawElementProvider(Q)), 'get_HostRawElementProvider');
  Check(Q = nil, 'no host provider');

  Forward := WalkForward(G);
  Check(Length(Forward) > 0, 'FirstChild: a cell');
  CheckEqS(ExpectedVisible(G), Forward, 'FirstChild, NextSibling: the visible cells, row-major, fixed ones included');
  CheckEqS(Forward, WalkBack(G), 'LastChild, PreviousSibling: the same cells backwards');
  CheckEqS('0,0', Where(Nav(Root, NavigateDirection_FirstChild)), 'the first child is cell (0, 0)');
  Check(Nav(CellOf(G, 11, 29), NavigateDirection_NextSibling) = nil, 'a cell outside the visible range: no next');
  Check(Nav(CellOf(G, 11, 29), NavigateDirection_PreviousSibling) = nil, 'and no previous sibling');

  CheckEqS('False', Prop(P, UIA_IsOffscreenPropertyId), 'a visible cell is not offscreen');
  R := BoundsOf(P);
  Cr := G.CellRect(1, 2);
  Pt := Point(Cr.Left, Cr.Top);
  ClientToScreen(G.Handle, Pt);
  Check((Round(R.left) = Pt.X) and (Round(R.top) = Pt.Y) and (Round(R.width) = Cr.Right - Cr.Left)
    and (Round(R.height) = Cr.Bottom - Cr.Top), 'its bounding rectangle: CellRect in screen coordinates');
  CheckEqS('True', Prop(CellOf(G, 11, 29), UIA_IsOffscreenPropertyId), 'a cell outside the visible range: offscreen');
  R := BoundsOf(CellOf(G, 11, 29));
  Check((R.left = 0) and (R.top = 0) and (R.width = 0) and (R.height = 0), 'and an empty rectangle');

  Frag := nil;
  Cr := G.CellRect(2, 1);
  Pt := Point((Cr.Left + Cr.Right) div 2, (Cr.Top + Cr.Bottom) div 2);
  ClientToScreen(G.Handle, Pt);
  (Root as IRawElementProviderFragmentRoot).ElementProviderFromPoint(Pt.X, Pt.Y, Frag);
  CheckEqS('2,1', Where(Frag as IRawElementProviderSimple), 'ElementProviderFromPoint: the cell under the point');

  G.LeftCol := 3;
  G.TopRow := 5;
  CheckEqS(ExpectedVisible(G), WalkForward(G), 'scrolled: the visible cells, the fixed ones first in each row');
  CheckEqS('3,0', Where(Nav(CellOf(G, 0, 0), NavigateDirection_NextSibling)), 'after fixed (0, 0) comes (LeftCol, 0)');
  CheckEqS('0,0', Where(Nav(CellOf(G, 3, 0), NavigateDirection_PreviousSibling)), 'and back');
  CheckEqS('True', Prop(CellOf(G, 1, 2), UIA_IsOffscreenPropertyId), 'a scrolled-away cell is offscreen');
  Check(Nav(CellOf(G, 1, 2), NavigateDirection_NextSibling) = nil, 'and has no siblings');
  G.LeftCol := 1;
  G.TopRow := 1;
end;

procedure TestItems;
var
  P, Q: IRawElementProviderSimple;
  Gi: IGridItemProvider;
  Ti: ITableItemProvider;
  N: Integer;
  A: PSafeArray;
begin
  Section('GridItem and TableItem (UIF-1437)');
  P := CellOf(G, 1, 2);
  Gi := Pat(P, UIA_GridItemPatternId) as IGridItemProvider;
  Check(Gi <> nil, 'GridItem');
  N := -1;
  CheckEqS('S_OK', ResultWord(Gi.get_Row(N)), 'get_Row');
  CheckEqI(2, N, 'Row');
  Gi.get_Column(N);
  CheckEqI(1, N, 'Column');
  Gi.get_RowSpan(N);
  CheckEqI(1, N, 'RowSpan 1');
  Gi.get_ColumnSpan(N);
  CheckEqI(1, N, 'ColumnSpan 1');
  Q := nil;
  CheckEqS('S_OK', ResultWord(Gi.get_ContainingGrid(Q)), 'get_ContainingGrid');
  Check(Same(Q, GetAutomationProvider(G)), 'the containing grid is the grid');
  Ti := Pat(P, UIA_TableItemPatternId) as ITableItemProvider;
  Check(Ti <> nil, 'TableItem');
  A := nil;
  CheckEqS('S_OK', ResultWord(Ti.GetRowHeaderItems(A)), 'GetRowHeaderItems');
  CheckEqS('Row 2|', ArrayNames(A), 'the row header: the fixed cell of the row');
  A := nil;
  CheckEqS('S_OK', ResultWord(Ti.GetColumnHeaderItems(A)), 'GetColumnHeaderItems');
  CheckEqS('Name|', ArrayNames(A), 'the column header: the fixed cell of the column');
  Check(Pat(CellOf(G, 0, 2), UIA_GridItemPatternId) <> nil, 'a fixed cell: GridItem');
  Check(Pat(CellOf(G, 0, 2), UIA_TableItemPatternId) <> nil, 'and TableItem');
  A := nil;
  (Pat(CellOf(G, 0, 2), UIA_TableItemPatternId) as ITableItemProvider).GetRowHeaderItems(A);
  Check(A = nil, 'a cell of the fixed column has no row header');
  (Pat(CellOf(G, 0, 2), UIA_TableItemPatternId) as ITableItemProvider).GetColumnHeaderItems(A);
  CheckEqS('|', ArrayNames(A), 'but its column header (the corner cell)');
  A := nil;
  (Pat(CellOf(T, 2, 2), UIA_TableItemPatternId) as ITableItemProvider).GetColumnHeaderItems(A);
  Check(A = nil, 'a grid without fixed cells: no header items');
end;

procedure TestSelectionAndValue;
var
  P: IRawElementProviderSimple;
  V: IValueProvider;
  S: TBStr;
  B: BOOL;
  Q: IRawElementProviderSimple;
  A: PSafeArray;
begin
  Section('SelectionItem and Value (UIF-1437)');
  Check(SelItem(CellOf(G, 0, 2)) = nil, 'a fixed cell: no SelectionItem');
  Check(ValueOf(CellOf(G, 1, 0)) = nil, 'and no Value');
  P := CellOf(G, 1, 2);
  V := ValueOf(P);
  Check(V <> nil, 'a cell: Value');
  S := nil;
  CheckEqS('S_OK', ResultWord(V.get_Value(S)), 'get_Value');
  CheckEqS('Beta', string(S), 'Value = the cell text');
  SysFreeString(S);
  B := True;
  V.get_IsReadOnly(B);
  CheckEqB(False, B, 'goEditing: not read-only');
  Ev.Edits := '';
  CheckEqS('S_OK', ResultWord(V.SetValue('Gamma')), 'SetValue');
  CheckEqS('Gamma', G.Cells[1, 2], 'the cell has the new text');
  CheckEqS('1,2=Gamma ', Ev.Edits, 'through SetEditText (OnSetEditText ran once)');
  CheckEqS('Gamma', NameOf(P), 'the name follows');
  V.SetValue('Beta');
  V := ValueOf(CellOf(T, 1, 1));
  V.get_IsReadOnly(B);
  CheckEqB(True, B, 'without goEditing: read-only');
  CheckEqS('UIA_E_ELEMENTNOTENABLED', ResultWord(V.SetValue('x')), 'SetValue on a read-only grid: refused');

  Check(SelItem(P) <> nil, 'a cell: SelectionItem');
  CheckEqB(False, IsSel(P), 'not the current cell: not selected');
  CheckEqS('S_OK', ResultWord(SelItem(P).Select), 'Select');
  CheckEqI(1, G.Col, 'Col follows');
  CheckEqI(2, G.Row, 'Row follows');
  CheckEqB(True, IsSel(P), 'now selected');
  CheckEqB(False, IsSel(CellOf(G, 1, 1)), 'the previous cell is not');
  Q := nil;
  SelItem(P).get_SelectionContainer(Q);
  Check(Same(Q, GetAutomationProvider(G)), 'SelectionContainer is the grid');
  CheckEqS('True', Prop(CellOf(G, 9, 25), UIA_IsOffscreenPropertyId), 'cell (9, 25) is offscreen');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 9, 25)).Select), 'Select it');
  CheckEqS('False', Prop(CellOf(G, 9, 25), UIA_IsOffscreenPropertyId), 'the grid scrolled it into view');
  SelItem(P).Select;
  Ev.Refuse := True;
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(G, 2, 2)).Select), 'OnSelectCell refuses: refused');
  CheckEqI(1, G.Col, 'the current cell stays');
  Ev.Refuse := False;
  G.Enabled := False;
  CheckEqS('UIA_E_ELEMENTNOTENABLED', ResultWord(SelItem(CellOf(G, 2, 2)).Select), 'a disabled grid: refused');
  G.Enabled := True;

  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 2, 2)).AddToSelection), 'AddToSelection of the right neighbour');
  CheckEqS('1,2-2,2', SelText(G), 'the selection grows to a rectangle');
  CheckEqI(1, G.Col, 'the current cell stays');
  A := nil;
  (Pat(GetAutomationProvider(G), UIA_SelectionPatternId) as ISelectionProvider).GetSelection(A);
  CheckEqS('1,2 2,2 ', ArrayCells(A), 'GetSelection: both cells');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 2, 2)).AddToSelection), 'AddToSelection of a selected cell: S_OK');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(G, 3, 3)).AddToSelection),
    'a cell whose addition is no rectangle: refused');
  CheckEqS('1,2-2,2', SelText(G), 'unchanged');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 2, 2)).RemoveFromSelection), 'RemoveFromSelection of the end cell');
  CheckEqS('1,2-1,2', SelText(G), 'the selection shrinks');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(G, 1, 2)).RemoveFromSelection),
    'removing the only cell: refused');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 3, 3)).RemoveFromSelection), 'removing a cell not selected: S_OK');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(G, 1, 3)).AddToSelection), 'AddToSelection below');
  CheckEqS('1,2-1,3', SelText(G), 'a column of two');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(G, 1, 2)).RemoveFromSelection),
    'removing the focused cell: refused');
  SelItem(CellOf(G, 2, 2)).Select;
  SelItem(CellOf(G, 3, 2)).AddToSelection;
  CheckEqS('2,2-3,2', SelText(G), 'a new row of two from (2, 2)');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(G, 1, 2)).AddToSelection),
    'extending on the side of the focused cell: refused (it would not be a corner)');
  CheckEqS('S_OK', ResultWord(SelItem(CellOf(T, 1, 1)).AddToSelection), 'without goRangeSelect: the selected cell');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord(SelItem(CellOf(T, 2, 1)).AddToSelection),
    'without goRangeSelect: a neighbour is refused');
  SelItem(P).Select;
end;

procedure TestFocus;
var
  P, Cur: IRawElementProviderSimple;
  Frag: IRawElementProviderFragment;
begin
  Section('focus (UIF-1439)');
  SetActiveWindow(F.Handle);                                 { the form is the active window of this thread }
  G.SetFocus;
  Check(Windows.GetFocus = G.Handle, 'the grid has the focus');
  Cur := CellOf(G, G.Col, G.Row);
  CheckEqS('True', Prop(Cur, UIA_HasKeyboardFocusPropertyId), 'the current cell has the keyboard focus');
  CheckEqS('True', Prop(Cur, UIA_IsKeyboardFocusablePropertyId), 'a cell is focusable');
  CheckEqS('False', Prop(CellOf(G, 0, 1), UIA_IsKeyboardFocusablePropertyId), 'a fixed cell is not');
  CheckEqS('False', Prop(CellOf(G, 2, 3), UIA_HasKeyboardFocusPropertyId), 'another cell has not');
  Frag := nil;
  CheckEqS('S_OK', ResultWord((GetAutomationProvider(G) as IRawElementProviderFragmentRoot).GetFocus(Frag)),
    'GetFocus');
  Check(Same(Frag as IRawElementProviderSimple, Cur), 'GetFocus returns the current cell');
  G.Row := 4;                                                { the current cell changes while the grid has the focus }
  CheckEqS('True', Prop(CellOf(G, 1, 4), UIA_HasKeyboardFocusPropertyId), 'the new current cell has the focus');
  P := CellOf(G, 2, 3);
  CheckEqS('S_OK', ResultWord((P as IRawElementProviderFragment).SetFocus), 'SetFocus of a cell');
  CheckEqI(2, G.Col, 'it is the current cell (Col)');
  CheckEqI(3, G.Row, 'it is the current cell (Row)');
  CheckEqS('True', Prop(P, UIA_HasKeyboardFocusPropertyId), 'and has the keyboard focus');
  CheckEqS('UIA_E_INVALIDOPERATION', ResultWord((CellOf(G, 0, 3) as IRawElementProviderFragment).SetFocus),
    'SetFocus of a fixed cell: refused');
  Other.SetFocus;
  CheckEqS('False', Prop(P, UIA_HasKeyboardFocusPropertyId), 'the focus elsewhere: the cell has not');
  Frag := nil;
  (GetAutomationProvider(G) as IRawElementProviderFragmentRoot).GetFocus(Frag);
  Check(Frag = nil, 'GetFocus returns nil');
  CheckEqS('S_OK', ResultWord((P as IRawElementProviderFragment).SetFocus), 'SetFocus of the cell again');
  Check(Windows.GetFocus = G.Handle, 'focuses the grid');
end;

procedure TestLifetime;
var
  P: IRawElementProviderSimple;
  Count: Integer;
  Doomed: TStringGrid;
begin
  Section('lifetime (UIF-1425, UIF-1426)');
  Count := AutomationProviderCount;
  P := CellOf(G, 7, 20);
  CheckEqI(Count + 1, AutomationProviderCount, 'a new cell: one provider object more');
  P := nil;
  CheckEqI(Count, AutomationProviderCount, 'released: one less');
  P := CellOf(G, 1, 25);
  G.RowCount := 20;
  CheckEqS('<UIA_E_ELEMENTNOTAVAILABLE>', NameOf(P), 'a cell outside the shrunk grid: not available');
  CheckEqS('Beta', NameOf(CellOf(G, 1, 2)), 'a cell inside it still answers');
  G.RowCount := 30;
  CheckEqS('', NameOf(P), 'back inside the grid: available again');
  P := CellOf(G, 1, 1);
  G.BorderStyle := bsNone;                                   { the window is recreated }
  CheckEqS('<UIA_E_ELEMENTNOTAVAILABLE>', NameOf(P), 'a cell of a destroyed window: not available');
  CheckEqS('Alpha', NameOf(CellOf(G, 1, 1)), 'the new window has new cells');
  Doomed := TStringGrid.Create(F);
  Doomed.Parent := F;
  Doomed.SetBounds(8, 270, 200, 100);
  Doomed.Cells[1, 1] := 'doomed';
  P := CellOf(Doomed, 1, 1);
  CheckEqS('doomed', NameOf(P), 'a cell of another grid');
  Doomed.Free;
  CheckEqS('<UIA_E_ELEMENTNOTAVAILABLE>', NameOf(P), 'its grid destroyed: not available');
  Check(Nav(P, NavigateDirection_Parent) = nil, 'and no parent');
  P := nil;
end;

begin
  Application.Initialize;
  Build;
  try
    TestGrid;
    TestCells;
    TestItems;
    TestSelectionAndValue;
    TestFocus;
    TestLifetime;
  finally
    F.Free;
    Ev.Free;
  end;
  CheckEqI(0, AutomationProviderCount, 'every provider object is released (UIF-1425)');
  Done;
end.
