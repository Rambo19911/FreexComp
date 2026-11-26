// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1426, UIF-1436, UIF-1440, UIF-1445, UIF-1446
// needs: desktop
// phase: 8
program Acc_Raisers;
{ U-163, accessibility.md sections 5, 7 and 8, in process: the raisers Accessibility exports for the providers of
  other units (a Boolean property change of an element that is not a control, children invalidated of a window),
  called from this program: without a provider of the control they create none and raise nothing (UIF-1440), with
  one they return without fault. A cell outside a shrunk grid, on which events were raised, still answers its runtime
  id and its fragment root (the system asks a cell it raised an event on for them when the grid disconnects it,
  UIF-1426), its other properties are not available (UIF-1436); when the grid is freed the cell is released: the
  object count is back and no provider object without its control is left. The output does not depend on whether a
  UI Automation client of another process listens. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, StdCtrls, ExtCtrls, Grids, ActiveX, UIAutomation,
  Accessibility, AccKit, TestKit;

var
  F: TForm;

procedure Build;
begin
  F := TForm.CreateNew(nil);
  F.Caption := 'Acc_Raisers';
  F.SetBounds(40, 40, 480, 300);
  F.Show;
end;

function GridOf(C: TCustomGrid): IGridProvider;
var
  U: IUnknown;
begin
  Result := nil;
  U := nil;
  if GetAutomationProvider(C).GetPatternProvider(UIA_GridPatternId, U) = S_OK then
    Result := U as IGridProvider;
end;

function CellOf(C: TCustomGrid; ACol, ARow: Integer): IRawElementProviderSimple;
begin
  Result := nil;
  GridOf(C).GetItem(ARow, ACol, Result);
end;

{ the runtime id of an element, its numbers separated by spaces; '<' + the result word + '>' when the call fails }
function RuntimeIdOf(const P: IRawElementProviderSimple): string;
var
  Ids: PSafeArray;
  R: HResult;
  Data: PInteger;
  I, N: Integer;
begin
  Ids := nil;
  R := (P as IRawElementProviderFragment).GetRuntimeId(Ids);
  if R <> S_OK then
  begin
    Result := '<' + ResultWord(R) + '>';
    Exit;
  end;
  Result := '';
  if Ids = nil then
    Exit;
  N := PCardinal(NativeUInt(Ids) + 16 + SizeOf(Pointer))^;  { rgsabound[0].cElements }
  Data := PPointer(NativeUInt(Ids) + 16)^;                   { pvData }
  for I := 0 to N - 1 do
  begin
    if Result <> '' then
      Result := Result + ' ';
    Result := Result + IntToStr(PInteger(NativeUInt(Data) + NativeUInt(I) * 4)^);
  end;
  SafeArrayDestroy(Ids);
end;

{ helpers that take the interface references of their expressions: the references are released when they return }
function HasProvider(C: TControl): Boolean;
begin
  Result := GetAutomationProvider(C) <> nil;
end;

{ the result word of get_FragmentRoot of P, and whether the root is the provider of G }
function RootOf(const P: IRawElementProviderSimple; G: TControl; out IsGrid: Boolean): string;
var
  Frag: IRawElementProviderFragment;
  Root: IRawElementProviderFragmentRoot;
  Simple: IRawElementProviderSimple;
begin
  Frag := P as IRawElementProviderFragment;
  Root := nil;
  Result := ResultWord(Frag.get_FragmentRoot(Root));
  IsGrid := False;
  if Root <> nil then
  begin
    Simple := Root as IRawElementProviderSimple;
    IsGrid := Pointer(Simple) = Pointer(GetAutomationProvider(G));
  end;
end;

procedure TestRaisers;
var
  Panel: TPanel;
  G: TStringGrid;
  Count: Integer;
  Cell: IRawElementProviderSimple;
begin
  Section('the exported raisers (UIF-1440, UIF-1445, UIF-1446)');
  Panel := TPanel.Create(F);
  Panel.Parent := F;
  Panel.SetBounds(8, 8, 200, 60);
  G := TStringGrid.Create(F);
  G.Parent := F;
  G.SetBounds(8, 80, 300, 140);
  Count := AutomationProviderCount;
  RaiseChildrenInvalidated(Panel);
  RaiseFragmentBoolPropertyChanged(Panel, nil, UIA_IsEnabledPropertyId, True, False);
  CheckEqI(Count, AutomationProviderCount, 'a control without a provider: no provider is created');
  Check(Panel.AutomationObject = nil, 'and the panel still has none');
  Check(HasProvider(Panel), 'the panel gets its provider');
  RaiseChildrenInvalidated(Panel);
  Check(True, 'children invalidated of a window with a provider: no fault');
  Cell := CellOf(G, 1, 1);
  Check(Cell <> nil, 'a cell of the grid');
  RaiseFragmentBoolPropertyChanged(G, Cell, UIA_IsEnabledPropertyId, True, False);
  RaiseFragmentBoolPropertyChanged(G, Cell, UIA_IsEnabledPropertyId, False, True);
  Check(True, 'a Boolean property change of the cell: no fault');
  RaiseFragmentBoolPropertyChanged(G, nil, UIA_IsEnabledPropertyId, True, False);
  Check(True, 'no element: nothing raised, no fault');
  Cell := nil;
  Panel.Free;
  G.Free;
  CheckEqI(Count, AutomationProviderCount, 'the controls freed: their provider objects are released');
  CheckEqI(0, AutomationDetachedProviderCount, 'no provider object without its control is left');
end;

procedure TestShrunkGrid;
var
  G: TStringGrid;
  Count: Integer;
  Cell: IRawElementProviderSimple;
  IsGrid: Boolean;
begin
  Section('a cell outside a shrunk grid (UIF-1426, UIF-1436)');
  G := TStringGrid.Create(F);
  G.Parent := F;
  G.SetBounds(8, 80, 300, 140);
  G.ColCount := 6;
  G.RowCount := 8;
  Count := AutomationProviderCount;
  Cell := CellOf(G, 3, 6);
  CheckEqS('3 3 6', RuntimeIdOf(Cell), 'the runtime id of cell (3, 6)');
  RaiseFragmentEvent(G, Cell, UIA_SelectionItem_ElementSelectedEventId);
  RaiseFragmentStrPropertyChanged(G, Cell, UIA_ValueValuePropertyId, '', 'x');
  RaiseFragmentBoolPropertyChanged(G, Cell, UIA_IsEnabledPropertyId, True, False);
  G.RowCount := 4;
  CheckEqS('<UIA_E_ELEMENTNOTAVAILABLE>', Prop(Cell, UIA_NamePropertyId), 'outside the shrunk grid: no name');
  CheckEqS('3 3 6', RuntimeIdOf(Cell), 'but its runtime id is answered while the grid exists');
  CheckEqS('S_OK', RootOf(Cell, G, IsGrid), 'and its fragment root');
  Check(IsGrid, 'the grid''s provider');
  Cell := nil;
  G.Free;
  CheckEqI(Count, AutomationProviderCount, 'the grid freed: its provider and the cell are released');
  CheckEqI(0, AutomationDetachedProviderCount, 'no provider object without its control is left');
end;

begin
  Application.Initialize;
  Build;
  try
    TestRaisers;
    TestShrunkGrid;
  finally
    F.Free;
  end;
  CheckEqI(0, AutomationProviderCount, 'every provider object is released (UIF-1425)');
  Done;
end.
