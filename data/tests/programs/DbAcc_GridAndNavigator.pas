// needs: desktop
program DbAcc_GridAndNavigator;
{ A-083 (follow-up of ui-framework accessibility.md section 12), in process: the UI Automation providers of the
  data grid and of the navigator, read through the provider interfaces the way a client reads them.
  Grid: the control type, the Grid, Table and Selection patterns; the items are the cells of the rows of the row
  window and of the shown columns (names and values from the text the cells show), the title cells are the column
  headers; navigation over the shown elements (title row first), runtime ids, bounding rectangles, hit testing;
  SelectionItem (Select moves the dataset and the selected column as a click), Value (read, written through the
  field as typed by the user, refused for what the user could not change), focus; lifetime (an element the row
  window no longer has, a destroyed grid). Navigator: the control type, its children (the visible buttons named
  by their hints, automation ids, Button, Invoke), IsEnabled of each button from the dataset's state, bounding
  rectangles, hit testing, Invoke (refused for a disabled button, else posted and clicked when the message is
  handled), lifetime. Every provider object is released at the end. Geometry is compared with CellRect and
  ButtonRect, so no expected text depends on the font or the resolution of the screen. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, ActiveX, UIAutomation, Accessibility, DB, DBCtrls, DBGrids,
  DataTestUtils, TestDataSet, CtlTestUtils;

type
  PProvider = ^IRawElementProviderSimple;

  THandlers = class
  public
    Log: string;
    procedure NavClick(Sender: TObject; Button: TNavigateBtn);
  end;

const
  BtnLetters = 'FPNLIDEOCR';

var
  F: TForm;
  H: THandlers;
  BoxTexts: string;

procedure THandlers.NavClick(Sender: TObject; Button: TNavigateBtn);
begin
  Log := Log + 'click ' + IntToStr(Ord(Button)) + ';';
end;

function AnswerCancel(const Text: string): Integer;
begin
  BoxTexts := BoxTexts + Text + ';';
  Result := IDCANCEL;
end;

{ ---------------------------------------------------------------- reading providers }

function ResultWord(R: HResult): string;
begin
  if R = S_OK then
    Result := 'S_OK'
  else if R = UIA_E_ELEMENTNOTAVAILABLE then
    Result := 'UIA_E_ELEMENTNOTAVAILABLE'
  else if R = UIA_E_INVALIDOPERATION then
    Result := 'UIA_E_INVALIDOPERATION'
  else if R = UIA_E_ELEMENTNOTENABLED then
    Result := 'UIA_E_ELEMENTNOTENABLED'
  else if R = E_INVALIDARG then
    Result := 'E_INVALIDARG'
  else
    Result := 'other ' + IntToHex(Cardinal(R), 8);
end;

{ a property as text: the string, True / False, the number, '<empty>', '<error>' }
function Prop(const P: IRawElementProviderSimple; Id: Integer): string;
var
  V: TVariantArg;
  R: HResult;
begin
  if P = nil then
  begin
    Result := '<no element>';
    Exit;
  end;
  FillChar(V, SizeOf(V), 0);
  R := P.GetPropertyValue(Id, V);
  if R <> S_OK then
    Result := '<' + ResultWord(R) + '>'
  else if V.vt = VT_EMPTY then
    Result := '<empty>'
  else if V.vt = VT_BSTR then
    Result := string(PWideChar(V.bstrVal))
  else if V.vt = VT_BOOL then
  begin
    if V.boolVal <> 0 then
      Result := 'True'
    else
      Result := 'False';
  end
  else if V.vt = VT_I4 then
    Result := IntToStr(V.lVal)
  else
    Result := '<vt ' + IntToStr(V.vt) + '>';
  VariantClear(V);
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
  if S = IntToStr(UIA_DataGridControlTypeId) then
    Result := 'DataGrid'
  else if S = IntToStr(UIA_DataItemControlTypeId) then
    Result := 'DataItem'
  else if S = IntToStr(UIA_HeaderItemControlTypeId) then
    Result := 'HeaderItem'
  else if S = IntToStr(UIA_ToolBarControlTypeId) then
    Result := 'ToolBar'
  else if S = IntToStr(UIA_ButtonControlTypeId) then
    Result := 'Button'
  else
    Result := S;
end;

function Pat(const P: IRawElementProviderSimple; Id: Integer): IUnknown;
begin
  Result := nil;
  if (P = nil) or (P.GetPatternProvider(Id, Result) <> S_OK) then
    Result := nil;
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

{ the names of the elements of an array, '|'-separated; the array is destroyed }
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

function RuntimeIdText(const P: IRawElementProviderSimple): string;
var
  Ids: PSafeArray;
  Data: NativeUInt;
  I: Integer;
begin
  Ids := nil;
  (P as IRawElementProviderFragment).GetRuntimeId(Ids);
  Result := '';
  if Ids = nil then
    Exit;
  Data := NativeUInt(PPointer(NativeUInt(Ids) + 16)^);
  for I := 0 to ArrayCount(Ids) - 1 do
    Result := Result + IntToStr(PInteger(Data + NativeUInt(I) * 4)^) + ' ';
  SafeArrayDestroy(Ids);
end;

function BoundsOf(const P: IRawElementProviderSimple): TUiaRect;
begin
  FillChar(Result, SizeOf(Result), 0);
  (P as IRawElementProviderFragment).get_BoundingRectangle(Result);
end;

{ a client rectangle of a window in screen coordinates as 'left,top,width,height' }
function ScreenText(Wnd: HWND; const R: TRect): string;
var
  P: TPoint;
begin
  P := Point(R.Left, R.Top);
  ClientToScreen(Wnd, P);
  Result := Format('%d,%d,%d,%d', [P.X, P.Y, R.Right - R.Left, R.Bottom - R.Top]);
end;

function UiaRectText(const R: TUiaRect): string;
begin
  Result := Format('%d,%d,%d,%d', [Round(R.left), Round(R.top), Round(R.width), Round(R.height)]);
end;

function FromPoint(C: TWinControl; X, Y: Integer): IRawElementProviderSimple;
var
  P: TPoint;
  Frag: IRawElementProviderFragment;
begin
  Result := nil;
  P := Point(X, Y);
  ClientToScreen(C.Handle, P);
  Frag := nil;
  (GetAutomationProvider(C) as IRawElementProviderFragmentRoot).ElementProviderFromPoint(P.X, P.Y, Frag);
  if Frag <> nil then
    Result := Frag as IRawElementProviderSimple;
end;

{ ---------------------------------------------------------------- the grid }

function GridOf(G: TDBGrid): IGridProvider;
begin
  Result := Pat(GetAutomationProvider(G), UIA_GridPatternId) as IGridProvider;
end;

function TableOf(G: TDBGrid): ITableProvider;
begin
  Result := Pat(GetAutomationProvider(G), UIA_TablePatternId) as ITableProvider;
end;

function CellOf(G: TDBGrid; ACol, ARow: Integer): IRawElementProviderSimple;
begin
  Result := nil;
  GridOf(G).GetItem(ARow, ACol, Result);
end;

function TitleOf(G: TDBGrid; ACol: Integer): IRawElementProviderSimple;
var
  A: PSafeArray;
begin
  Result := nil;
  A := nil;
  TableOf(G).GetColumnHeaders(A);
  if A = nil then
    Exit;
  Result := ArrayItem(A, ACol);
  SafeArrayDestroy(A);
end;

{ 'col,row' of a data cell from its GridItem pattern, 'title' for a header item, 'nil' for no element }
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
    Result := 'title ' + NameOf(P);
    Exit;
  end;
  C := -1;
  R := -1;
  Gi.get_Column(C);
  Gi.get_Row(R);
  Result := IntToStr(C) + ',' + IntToStr(R);
end;

function WalkForward(C: TWinControl): string;
var
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Result := '';
  P := Nav(GetAutomationProvider(C), NavigateDirection_FirstChild);
  N := 0;
  while (P <> nil) and (N < 500) do
  begin
    Result := Result + NameOf(P) + '|';
    P := Nav(P, NavigateDirection_NextSibling);
    Inc(N);
  end;
end;

function WalkBack(C: TWinControl): string;
var
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Result := '';
  P := Nav(GetAutomationProvider(C), NavigateDirection_LastChild);
  N := 0;
  while (P <> nil) and (N < 500) do
  begin
    Result := NameOf(P) + '|' + Result;
    P := Nav(P, NavigateDirection_PreviousSibling);
    Inc(N);
  end;
end;

{ what the walk should give: the title cells and the data cells whose CellRect is not empty, row-major }
function ExpectedWalk(G: TDBGrid; Rows: Integer): string;
var
  C, R: Integer;
  Rc: TRect;
begin
  Result := '';
  for R := -1 to Rows - 1 do
    for C := 0 to G.ColCount - 1 do
    begin
      Rc := G.CellRect(C, R);
      if Rc.Right > Rc.Left then
      begin
        if R = -1 then
          Result := Result + G.VisibleColumns[C].TitleText + '|'
        else
          Result := Result + G.CellText(C, R) + '|';
      end;
    end;
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

function ValueText(const P: IRawElementProviderSimple): string;
var
  S: TBStr;
begin
  S := nil;
  if ValueOf(P).get_Value(S) <> S_OK then
  begin
    Result := '<error>';
    Exit;
  end;
  Result := string(PWideChar(S));
  SysFreeString(S);
end;

function ReadOnlyOf(const P: IRawElementProviderSimple): Boolean;
var
  B: BOOL;
begin
  B := False;
  ValueOf(P).get_IsReadOnly(B);
  Result := B;
end;

{ id, name (10); Count rows 1 n1, 2 n2, ... }
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

var
  DS: TTestDataSet;
  S: TDataSource;
  G: TDBGrid;

procedure BuildGrid;
begin
  DS := NewDS(3);
  S := TDataSource.Create(nil);
  S.DataSet := DS;
  G := TDBGrid.Create(F);
  G.Name := 'Grid';
  G.Parent := F;
  G.SetBounds(8, 8, 560, 260);
  G.DataSource := S;
  DS.Open;
  Application.ProcessMessages;
end;

procedure TestGridPatterns;
var
  Root, P, Q: IRawElementProviderSimple;
  Gp: IGridProvider;
  Tp: ITableProvider;
  Sp: ISelectionProvider;
  A: PSafeArray;
  N: Integer;
  B: BOOL;
begin
  Section('the grid: Grid, Table and Selection');
  Root := GetAutomationProvider(G);
  CheckEq(TypeOf(Root), 'DataGrid', 'control type DataGrid');
  CheckEq(Prop(Root, UIA_AutomationIdPropertyId), 'Grid', 'AutomationId: the component name');
  Gp := GridOf(G);
  Check(Gp <> nil, 'Grid pattern');
  Tp := TableOf(G);
  Check(Tp <> nil, 'Table pattern');
  Sp := Pat(Root, UIA_SelectionPatternId) as ISelectionProvider;
  Check(Sp <> nil, 'Selection pattern');
  Check((Pat(Root, UIA_ValuePatternId) = nil) and (Pat(Root, UIA_GridItemPatternId) = nil), 'no other pattern');
  N := -1;
  CheckEq(ResultWord(Gp.get_RowCount(N)), 'S_OK', 'get_RowCount');
  CheckEqInt(N, 3, 'RowCount: the rows of the row window');
  CheckEq(ResultWord(Gp.get_ColumnCount(N)), 'S_OK', 'get_ColumnCount');
  CheckEqInt(N, 2, 'ColumnCount: the shown columns');
  P := nil;
  CheckEq(ResultWord(Gp.GetItem(1, 1, P)), 'S_OK', 'GetItem(1, 1)');
  CheckEq(Where(P), '1,1', 'GetItem(row 1, column 1) is cell (1, 1)');
  CheckEq(NameOf(P), 'n2', 'its name is the text the cell shows');
  Q := nil;
  Gp.GetItem(1, 1, Q);
  Check(Same(P, Q), 'the same cell twice: the same provider');
  Q := nil;
  CheckEq(ResultWord(Gp.GetItem(3, 0, Q)), 'E_INVALIDARG', 'row RowCount: E_INVALIDARG');
  Check(Q = nil, 'and no element');
  CheckEq(ResultWord(Gp.GetItem(-1, 0, Q)), 'E_INVALIDARG', 'row -1 (the title row): E_INVALIDARG');
  CheckEq(ResultWord(Gp.GetItem(0, 2, Q)), 'E_INVALIDARG', 'column ColumnCount: E_INVALIDARG');
  CheckEq(ResultWord(Gp.GetItem(0, -1, Q)), 'E_INVALIDARG', 'column -1 (the indicator): E_INVALIDARG');
  N := -1;
  Tp.get_RowOrColumnMajor(N);
  CheckEqInt(N, RowOrColumnMajor_RowMajor, 'row-major');
  A := nil;
  CheckEq(ResultWord(Tp.GetColumnHeaders(A)), 'S_OK', 'GetColumnHeaders');
  CheckEqInt(ArrayCount(A), 2, 'column headers: one title cell per shown column');
  CheckEq(ArrayNames(A), 'id|name|', 'named by the column titles');
  A := nil;
  CheckEq(ResultWord(Tp.GetRowHeaders(A)), 'S_OK', 'GetRowHeaders');
  Check(A = nil, 'no row headers (the indicator has no text)');
  B := True;
  Sp.get_CanSelectMultiple(B);
  CheckEqBool(B, False, 'CanSelectMultiple: one current cell');
  B := False;
  Sp.get_IsSelectionRequired(B);
  CheckEqBool(B, True, 'IsSelectionRequired');
  A := nil;
  CheckEq(ResultWord(Sp.GetSelection(A)), 'S_OK', 'GetSelection');
  CheckEq(ArrayNames(A), '1|', 'the selection: the current cell');
end;

procedure TestGridCells;
var
  Root, P, Q, T: IRawElementProviderSimple;
  FR: IRawElementProviderFragmentRoot;
  A: PSafeArray;
  N: Integer;
begin
  Section('cells and title cells');
  Root := GetAutomationProvider(G);
  P := CellOf(G, 1, 1);
  CheckEq(TypeOf(P), 'DataItem', 'a cell: DataItem');
  CheckEq(Prop(P, UIA_AutomationIdPropertyId), '', 'AutomationId empty');
  CheckEq(Prop(P, UIA_FrameworkIdPropertyId), 'FreexComp', 'FrameworkId');
  CheckEq(Prop(P, UIA_IsContentElementPropertyId), 'True', 'a content element');
  CheckEq(Prop(P, UIA_IsEnabledPropertyId), 'True', 'enabled');
  CheckEq(Prop(P, UIA_IsKeyboardFocusablePropertyId), 'True', 'keyboard focusable');
  CheckEq(Prop(P, UIA_IsOffscreenPropertyId), 'False', 'on screen');
  Check((Pat(P, UIA_GridItemPatternId) <> nil) and (Pat(P, UIA_TableItemPatternId) <> nil)
    and (SelItem(P) <> nil) and (ValueOf(P) <> nil), 'GridItem, TableItem, SelectionItem and Value');
  T := TitleOf(G, 1);
  CheckEq(TypeOf(T), 'HeaderItem', 'a title cell: HeaderItem');
  CheckEq(NameOf(T), 'name', 'named by the title');
  Check((Pat(T, UIA_GridItemPatternId) = nil) and (Pat(T, UIA_ValuePatternId) = nil)
    and (Pat(T, UIA_SelectionItemPatternId) = nil), 'a title cell has no pattern');
  CheckEq(Prop(T, UIA_IsKeyboardFocusablePropertyId), 'False', 'and is not keyboard focusable');
  A := nil;
  (Pat(P, UIA_TableItemPatternId) as ITableItemProvider).GetColumnHeaderItems(A);
  CheckEqInt(ArrayCount(A), 1, 'the column header items of a cell: one');
  Check(Same(ArrayItem(A, 0), T), 'the title cell of its column');
  SafeArrayDestroy(A);
  A := nil;
  (Pat(P, UIA_TableItemPatternId) as ITableItemProvider).GetRowHeaderItems(A);
  Check(A = nil, 'no row header items');
  Q := nil;
  (Pat(P, UIA_GridItemPatternId) as IGridItemProvider).get_ContainingGrid(Q);
  Check(Same(Q, Root), 'the containing grid');
  N := 0;
  (Pat(P, UIA_GridItemPatternId) as IGridItemProvider).get_RowSpan(N);
  CheckEqInt(N, 1, 'row span 1');
  DS.FieldByName('name').DisplayLabel := 'Name';
  CheckEq(NameOf(T), 'Name', 'a changed display label: the new name');
  CheckEq(RuntimeIdText(P), IntToStr(UiaAppendRuntimeId) + ' 1 2 ', 'runtime id [UiaAppendRuntimeId, column, row + 1]');
  CheckEq(RuntimeIdText(T), IntToStr(UiaAppendRuntimeId) + ' 1 0 ', 'a title cell: row 0');
  FR := nil;
  (P as IRawElementProviderFragment).get_FragmentRoot(FR);
  Check(Pointer(FR) = Pointer(Root as IRawElementProviderFragmentRoot), 'the fragment root is the grid');
  FR := nil;
  Check(Same(Nav(P, NavigateDirection_Parent), Root), 'the parent is the grid');
  Check(Nav(P, NavigateDirection_FirstChild) = nil, 'a cell has no children');
  Q := nil;
  P.get_HostRawElementProvider(Q);
  Check(Q = nil, 'no host provider');
end;

procedure TestGridNavigation;
var
  Root, T: IRawElementProviderSimple;
  A: PSafeArray;
  Rc: TRect;
begin
  Section('navigation, geometry, hit testing');
  Root := GetAutomationProvider(G);
  CheckEq(WalkForward(G), ExpectedWalk(G, 3), 'FirstChild, NextSibling: title cells, then the cells, row-major');
  CheckEq(WalkForward(G), 'id|Name|1|n1|2|n2|3|n3|', 'every cell of this grid is shown');
  CheckEq(WalkBack(G), WalkForward(G), 'LastChild, PreviousSibling: the same backwards');
  CheckEq(UiaRectText(BoundsOf(CellOf(G, 1, 1))), ScreenText(G.Handle, G.CellRect(1, 1)),
    'a cell: CellRect in screen coordinates');
  CheckEq(UiaRectText(BoundsOf(TitleOf(G, 1))), ScreenText(G.Handle, G.CellRect(1, -1)), 'a title cell: its rectangle');
  Rc := G.CellRect(1, 2);
  Check(Same(FromPoint(G, (Rc.Left + Rc.Right) div 2, (Rc.Top + Rc.Bottom) div 2), CellOf(G, 1, 2)),
    'ElementProviderFromPoint: the cell under the point');
  Rc := G.CellRect(0, -1);
  Check(Same(FromPoint(G, (Rc.Left + Rc.Right) div 2, (Rc.Top + Rc.Bottom) div 2), TitleOf(G, 0)),
    'a title cell under the point');
  Rc := G.CellRect(-1, 1);
  Check(Same(FromPoint(G, (Rc.Left + Rc.Right) div 2, (Rc.Top + Rc.Bottom) div 2), Root),
    'the indicator: the grid');
  T := TitleOf(G, 1);
  G.Options := G.Options - [dgTitles];
  A := nil;
  TableOf(G).GetColumnHeaders(A);
  Check(A = nil, 'without titles: no column headers');
  CheckEq(NameOf(T), '<UIA_E_ELEMENTNOTAVAILABLE>', 'and a title cell is not available');
  CheckEq(Where(Nav(Root, NavigateDirection_FirstChild)), '0,0', 'the first child is cell (0, 0)');
  G.Options := G.Options + [dgTitles];
  CheckEq(NameOf(T), 'Name', 'titles back: the title cell answers again');
end;

procedure TestGridRowWindow;
var
  Many: TTestDataSet;
  SMany: TDataSource;
  Small: TDBGrid;
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Section('the row window');
  Many := NewDS(40);
  SMany := TDataSource.Create(nil);
  SMany.DataSet := Many;
  Small := TDBGrid.Create(F);
  Small.Parent := F;
  Small.SetBounds(8, 280, 300, 120);
  Small.DataSource := SMany;
  Many.Open;
  Application.ProcessMessages;
  N := -1;
  GridOf(Small).get_RowCount(N);
  CheckEqInt(N, Small.VisibleRowCount, 'more records than rows: RowCount = VisibleRowCount');
  Check(N < 40, 'fewer than the records');
  P := CellOf(Small, 1, 0);
  CheckEq(NameOf(P), 'n1', 'row 0: the first row shown');
  Many.Last;
  CheckEq(NameOf(P), Small.CellText(1, 0), 'scrolled to the end: row 0 names the record shown there');
  Check(NameOf(P) <> 'n1', 'another record');
  CheckEq(NameOf(CellOf(Small, 1, N - 1)), 'n40', 'the last row: the last record');
  CheckEq(WalkForward(Small), ExpectedWalk(Small, N), 'navigation: the shown elements');
  Small.Free;
  CheckEq(NameOf(P), '<UIA_E_ELEMENTNOTAVAILABLE>', 'a cell of a destroyed grid: not available');
  Check(Nav(P, NavigateDirection_Parent) = nil, 'and no parent');
  SMany.Free;
  Many.Free;
end;

procedure TestGridSelection;
var
  Root, P, Q: IRawElementProviderSimple;
begin
  Section('SelectionItem');
  Root := GetAutomationProvider(G);
  P := CellOf(G, 1, 2);
  CheckEqBool(IsSel(CellOf(G, 0, 0)), True, 'the current cell is selected');
  CheckEqBool(IsSel(P), False, 'another is not');
  CheckEq(ResultWord(SelItem(P).Select), 'S_OK', 'Select');
  CheckEqInt(DS.RecNo, 3, 'the dataset moved to its row');
  CheckEqInt(G.SelectedIndex, 1, 'its column is selected');
  CheckEqBool(IsSel(P), True, 'now it is selected');
  CheckEqBool(IsSel(CellOf(G, 0, 0)), False, 'the former current cell is not');
  CheckEq(ResultWord(SelItem(P).AddToSelection), 'S_OK', 'AddToSelection of the selected cell: nothing changes');
  CheckEq(ResultWord(SelItem(CellOf(G, 0, 0)).AddToSelection), 'UIA_E_INVALIDOPERATION', 'of another: refused');
  CheckEq(ResultWord(SelItem(P).RemoveFromSelection), 'UIA_E_INVALIDOPERATION',
    'RemoveFromSelection of the selected cell: refused (a selection is required)');
  CheckEq(ResultWord(SelItem(CellOf(G, 0, 0)).RemoveFromSelection), 'S_OK', 'of another: nothing changes');
  Q := nil;
  SelItem(P).get_SelectionContainer(Q);
  Check(Same(Q, Root), 'the selection container is the grid');
  G.Enabled := False;
  CheckEq(Prop(P, UIA_IsEnabledPropertyId), 'False', 'a disabled grid: its cells are not enabled');
  CheckEq(ResultWord(SelItem(CellOf(G, 0, 0)).Select), 'UIA_E_ELEMENTNOTENABLED', 'and Select is refused');
  G.Enabled := True;
end;

procedure TestGridValue;
begin
  Section('Value');
  CheckEq(ValueText(CellOf(G, 1, 2)), 'n3', 'Value: the text the cell shows');
  CheckEqBool(ReadOnlyOf(CellOf(G, 1, 2)), False, 'an editable column: not read-only');
  CheckEq(ResultWord(ValueOf(CellOf(G, 1, 0)).SetValue('x')), 'S_OK', 'SetValue');
  CheckEq(StateName(DS.State), 'dsEdit', 'the record is in edit mode');
  CheckEqInt(DS.RecNo, 1, 'of the cell''s row');
  CheckEq(DS.FieldByName('name').AsString, 'x', 'the field has the new text');
  CheckEq(ValueText(CellOf(G, 1, 0)), 'x', 'and the cell shows it');
  DS.Cancel;
  CheckEq(ValueText(CellOf(G, 1, 0)), 'n1', 'cancelled: the old text');
  G.Columns.Add.FieldName := 'id';
  G.Columns.Add.FieldName := 'name';
  G.Columns[0].ReadOnly := True;
  CheckEqBool(ReadOnlyOf(CellOf(G, 0, 0)), True, 'a read-only column: read-only');
  CheckEq(ResultWord(ValueOf(CellOf(G, 0, 0)).SetValue('9')), 'UIA_E_ELEMENTNOTENABLED', 'SetValue refused');
  CheckEqBool(ReadOnlyOf(CellOf(G, 1, 0)), False, 'the other column is not');
  G.ReadOnly := True;
  CheckEqBool(ReadOnlyOf(CellOf(G, 1, 0)), True, 'a read-only grid: read-only');
  CheckEq(ResultWord(ValueOf(CellOf(G, 1, 0)).SetValue('y')), 'UIA_E_ELEMENTNOTENABLED', 'SetValue refused');
  CheckEq(StateName(DS.State), 'dsBrowse', 'and the dataset is untouched');
  G.ReadOnly := False;
  G.Columns.Clear;
end;

procedure TestGridFocus;
var
  Frag: IRawElementProviderFragment;
begin
  Section('focus');
  SetActiveWindow(F.Handle);                                 { the form is the active window of this thread }
  G.SetFocus;
  Check(Windows.GetFocus = G.Handle, 'the grid has the focus');
  DS.First;
  CheckEq(Prop(CellOf(G, G.SelectedIndex, 0), UIA_HasKeyboardFocusPropertyId), 'True',
    'the current cell has the keyboard focus');
  CheckEq(Prop(CellOf(G, 0, 1), UIA_HasKeyboardFocusPropertyId), 'False', 'another has not');
  Frag := nil;
  (GetAutomationProvider(G) as IRawElementProviderFragmentRoot).GetFocus(Frag);
  Check(Same(Frag as IRawElementProviderSimple, CellOf(G, G.SelectedIndex, 0)), 'GetFocus: the current cell');
  Frag := nil;
  CheckEq(ResultWord((CellOf(G, 0, 2) as IRawElementProviderFragment).SetFocus), 'S_OK', 'SetFocus of a cell');
  CheckEqInt(DS.RecNo, 3, 'it becomes the current cell');
  CheckEqInt(G.SelectedIndex, 0, 'in its column');
  CheckEq(ResultWord((TitleOf(G, 0) as IRawElementProviderFragment).SetFocus), 'UIA_E_INVALIDOPERATION',
    'a title cell cannot take the focus');
end;

procedure TestGridLifetime;
var
  P: IRawElementProviderSimple;
  N: Integer;
begin
  Section('lifetime of the grid''s elements');
  P := CellOf(G, 1, 2);
  DS.Close;
  CheckEq(NameOf(P), '<UIA_E_ELEMENTNOTAVAILABLE>', 'the dataset closed: the cell is not available');
  N := -1;
  GridOf(G).get_RowCount(N);
  CheckEqInt(N, 0, 'and the grid has no rows');
  DS.Open;
  CheckEq(NameOf(P), 'n3', 'opened again: the cell answers');
  G.Free;
  CheckEq(NameOf(P), '<UIA_E_ELEMENTNOTAVAILABLE>', 'the grid destroyed: the cell is not available');
  S.Free;
  DS.Free;
end;

procedure TestGrid;
var
  Count: Integer;
begin
  Count := AutomationProviderCount;
  BuildGrid;
  TestGridPatterns;
  TestGridCells;
  TestGridNavigation;
  TestGridRowWindow;
  TestGridSelection;
  TestGridValue;
  TestGridFocus;
  TestGridLifetime;
  CheckEqInt(AutomationProviderCount, Count, 'every provider object of the grids is released');
end;

{ ---------------------------------------------------------------- the navigator }

{ one letter per button whose element is enabled, '-' when not (the order of TNavigateBtn) }
function ElementEnabling(N: TDBNavigator): string;
var
  P: IRawElementProviderSimple;
  B: TNavigateBtn;
begin
  Result := '';
  P := Nav(GetAutomationProvider(N), NavigateDirection_FirstChild);
  for B := nbFirst to nbRefresh do
  begin
    if Prop(P, UIA_IsEnabledPropertyId) = 'True' then
      Result := Result + BtnLetters[Ord(B) + 1]
    else
      Result := Result + '-';
    P := Nav(P, NavigateDirection_NextSibling);
  end;
end;

function ButtonOf(N: TDBNavigator; B: TNavigateBtn): IRawElementProviderSimple;
var
  R: TRect;
begin
  R := N.ButtonRect(B);
  Result := FromPoint(N, (R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
end;

function InvokeOf(const P: IRawElementProviderSimple): IInvokeProvider;
begin
  Result := Pat(P, UIA_InvokePatternId) as IInvokeProvider;
end;

function Ids(C: TWinControl): string;
var
  P: IRawElementProviderSimple;
begin
  Result := '';
  P := Nav(GetAutomationProvider(C), NavigateDirection_FirstChild);
  while P <> nil do
  begin
    Result := Result + Prop(P, UIA_AutomationIdPropertyId) + ' ';
    P := Nav(P, NavigateDirection_NextSibling);
  end;
end;

var
  NDS: TTestDataSet;
  NS: TDataSource;
  N: TDBNavigator;

procedure BuildNavigator;
begin
  NDS := NewDS(3);
  NS := TDataSource.Create(nil);
  NS.DataSet := NDS;
  N := TDBNavigator.Create(F);
  N.Name := 'Nav';
  N.Parent := F;
  N.SetBounds(8, 420, 400, 30);
  N.DataSource := NS;
  N.OnClick := H.NavClick;
  NDS.Open;
  Application.ProcessMessages;
end;

procedure TestNavButtons;
var
  Root, P: IRawElementProviderSimple;
  FR: IRawElementProviderFragmentRoot;
begin
  Section('the navigator and its buttons');
  Root := GetAutomationProvider(N);
  CheckEq(TypeOf(Root), 'ToolBar', 'control type ToolBar');
  CheckEq(Prop(Root, UIA_AutomationIdPropertyId), 'Nav', 'AutomationId: the component name');
  CheckEq(WalkForward(N), 'First record|Prior record|Next record|Last record|Insert record|Delete record|' +
    'Edit record|Post edit|Cancel edit|Refresh data|', 'children: the visible buttons, named by their hints');
  CheckEq(WalkBack(N), WalkForward(N), 'LastChild, PreviousSibling: the same backwards');
  CheckEq(Ids(N), 'nbFirst nbPrior nbNext nbLast nbInsert nbDelete nbEdit nbPost nbCancel nbRefresh ',
    'automation ids: the button identifiers');
  P := ButtonOf(N, nbNext);
  CheckEq(NameOf(P), 'Next record', 'ElementProviderFromPoint: the button under the point');
  Check(Same(Nav(Nav(Root, NavigateDirection_FirstChild), NavigateDirection_NextSibling),
    ButtonOf(N, nbPrior)), 'one provider per button');
  CheckEq(TypeOf(P), 'Button', 'a button: Button');
  CheckEq(Prop(P, UIA_FrameworkIdPropertyId), 'FreexComp', 'FrameworkId');
  CheckEq(Prop(P, UIA_IsKeyboardFocusablePropertyId), 'False', 'not keyboard focusable');
  CheckEq(Prop(P, UIA_IsContentElementPropertyId), 'True', 'a content element');
  Check(InvokeOf(P) <> nil, 'the Invoke pattern');
  Check(Pat(P, UIA_TogglePatternId) = nil, 'no other pattern');
  CheckEq(UiaRectText(BoundsOf(P)), ScreenText(N.Handle, N.ButtonRect(nbNext)),
    'bounding rectangle: ButtonRect in screen coordinates');
  CheckEq(RuntimeIdText(P), IntToStr(UiaAppendRuntimeId) + ' 2 ', 'runtime id [UiaAppendRuntimeId, Ord of the button]');
  Check(Same(Nav(P, NavigateDirection_Parent), Root), 'the parent is the navigator');
  FR := nil;
  (P as IRawElementProviderFragment).get_FragmentRoot(FR);
  Check(Pointer(FR) = Pointer(Root as IRawElementProviderFragmentRoot), 'the fragment root is the navigator');
  FR := nil;
  Check(Same(FromPoint(N, -5, -5), Root), 'outside the buttons: the navigator');
  N.Hints.Text := 'Go to start|Moves to the first record';
  CheckEq(NameOf(ButtonOf(N, nbFirst)), 'Go to start', 'a line of Hints: its short part names the button');
  CheckEq(NameOf(ButtonOf(N, nbPrior)), 'Prior record', 'no line: the default hint');
  N.Hints.Clear;
end;

procedure TestNavEnabling;
var
  Empty: TTestDataSet;
begin
  Section('enabled from the dataset''s state');
  CheckEq(ElementEnabling(N), '--NLIDE--R', 'the first record');
  NDS.Last;
  CheckEq(ElementEnabling(N), 'FP--IDE--R', 'the last record');
  NDS.First;
  NDS.Next;
  CheckEq(ElementEnabling(N), 'FPNLIDE--R', 'a middle record');
  NDS.Edit;
  CheckEq(ElementEnabling(N), 'FPNLID-OCR', 'editing');
  NDS.Cancel;
  NDS.First;
  N.Enabled := False;
  CheckEq(ElementEnabling(N), '----------', 'a disabled navigator');
  N.Enabled := True;
  F.Enabled := False;
  CheckEq(ElementEnabling(N), '----------', 'a disabled form');
  F.Enabled := True;
  NDS.Close;
  CheckEq(ElementEnabling(N), '----------', 'a closed dataset');
  Empty := NewDS(0);
  NS.DataSet := Empty;
  Empty.Open;
  CheckEq(ElementEnabling(N), '----I-E--R', 'an empty dataset');
  NS.DataSet := NDS;
  Empty.Free;
  NDS.Open;
end;

procedure TestNavInvoke;
begin
  Section('Invoke');
  CheckEq(ResultWord(InvokeOf(ButtonOf(N, nbPrior)).Invoke), 'UIA_E_ELEMENTNOTENABLED', 'a disabled button: refused');
  CheckEq(ResultWord(InvokeOf(ButtonOf(N, nbNext)).Invoke), 'S_OK', 'Invoke');
  CheckEqInt(NDS.RecNo, 1, 'the click is posted, not made at once');
  Application.ProcessMessages;
  CheckEqInt(NDS.RecNo, 2, 'handled: the button was clicked');
  CheckEq(H.Log, 'click 2;', 'OnClick as for a mouse click');
  MessageBoxHook := AnswerCancel;
  H.Log := '';
  CheckEq(ResultWord(InvokeOf(ButtonOf(N, nbDelete)).Invoke), 'S_OK', 'Invoke of Delete');
  CheckEq(BoxTexts, '', 'the confirmation is not asked inside the call');
  Application.ProcessMessages;
  CheckEq(BoxTexts, 'Delete record?;', 'it is asked when the click is handled');
  CheckEqInt(NDS.RecordCount, 3, 'cancelled: nothing deleted');
  MessageBoxHook := nil;
  H.Log := '';
  CheckEq(ResultWord(InvokeOf(ButtonOf(N, nbLast)).Invoke), 'S_OK', 'Invoke of Last');
  NDS.Last;
  Application.ProcessMessages;
  CheckEq(H.Log, '', 'the button became disabled before the click was handled: not clicked');
end;

procedure TestNavLifetime;
var
  P, Q: IRawElementProviderSimple;
begin
  Section('lifetime of the buttons');
  P := ButtonOf(N, nbEdit);
  N.VisibleButtons := [nbFirst, nbNext];
  CheckEq(WalkForward(N), 'First record|Next record|', 'hidden buttons are not children');
  CheckEq(NameOf(P), '<UIA_E_ELEMENTNOTAVAILABLE>', 'a hidden button is not available');
  CheckEq(NameOf(ButtonOf(N, nbFirst)), 'First record', 'a visible one still answers');
  N.VisibleButtons := [nbFirst..nbRefresh];
  CheckEq(NameOf(P), 'Edit record', 'shown again: it answers again');
  Q := ButtonOf(N, nbFirst);
  N.Free;
  CheckEq(NameOf(Q), '<UIA_E_ELEMENTNOTAVAILABLE>', 'a button of a destroyed navigator: not available');
  Check(Nav(Q, NavigateDirection_Parent) = nil, 'and no parent');
  Check(InvokeOf(P) = nil, 'and no pattern');
  NS.Free;
  NDS.Free;
end;

procedure TestNavigator;
var
  Count: Integer;
begin
  Count := AutomationProviderCount;
  BuildNavigator;
  TestNavButtons;
  TestNavEnabling;
  TestNavInvoke;
  TestNavLifetime;
  CheckEqInt(AutomationProviderCount, Count, 'every provider object of the navigator is released');
end;

begin
  Application.Initialize;
  HookBeeps;
  H := THandlers.Create;
  F := NewForm;
  F.SetBounds(40, 40, 640, 520);
  try
    TestGrid;
    TestNavigator;
  finally
    F.Free;
    H.Free;
  end;
  CheckEqInt(AutomationProviderCount, 0, 'every provider object is released');
  WriteLn('failed checks: ', FailedChecks);
end.
