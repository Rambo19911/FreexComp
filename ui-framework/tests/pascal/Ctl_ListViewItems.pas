// covers: UIF-820, UIF-821, UIF-822, UIF-824, UIF-825, UIF-826
// needs: desktop
program Ctl_ListViewItems;
{ U-087, controls-standard.md section 25 (run-time object model). TListItems / TListItem without a window (the
  objects alone, UIF-821), the native items inserted with callback text and image and the item as lParam, the text
  of the caption and the sub-items and the image always read from the Pascal objects (UIF-820), the state restored
  when the window is created and recreated without events (UIF-821), LVN_ITEMCHANGED mapped to OnSelectItem,
  OnItemChecked and OnChange (UIF-822; the user toggling a check box with the space key), OnChange for text and
  image changes, the public helpers of the list view (Selected, ItemIndex, ItemFocused, SelCount, GetItemAt,
  GetNextItem, ClearSelection, SelectAll, TopItem, VisibleRowCount, DisplayRect, MakeVisible), deleting and clearing,
  the end of a label edit (UIF-824), OnClick / OnDblClick from the mouse path (UIF-825), a report view
  without columns (UIF-826), no events while destroying, and a leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, CommCtrl, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject; Item: TListItem; Change: TItemChange);
    procedure Edited(Sender: TObject; Item: TListItem; var S: string);
    procedure Checked(Sender: TObject; Item: TListItem);
    procedure Selected(Sender: TObject; Item: TListItem; Selected: Boolean);
    procedure Clicked(Sender: TObject);
    procedure DblClicked(Sender: TObject);
  end;

var
  Log: TLog;

const
  ChangeNames: array[TItemChange] of string = ('text', 'image', 'state');

function B(V: Boolean): string;
begin
  if V then
    Result := 'T'
  else
    Result := 'F';
end;

procedure TLog.Changed(Sender: TObject; Item: TListItem; Change: TItemChange);
begin
  Text := Text + 'change:' + IntToStr(Item.Index) + ':' + ChangeNames[Change] + ';';
end;

procedure TLog.Edited(Sender: TObject; Item: TListItem; var S: string);
begin
  Text := Text + 'edited:' + IntToStr(Item.Index) + ':' + S + ';';
  S := UpperCase(S);
end;

procedure TLog.Checked(Sender: TObject; Item: TListItem);
begin
  Text := Text + 'checked:' + IntToStr(Item.Index) + ':' + B(Item.Checked) + ';';
end;

procedure TLog.Selected(Sender: TObject; Item: TListItem; Selected: Boolean);
begin
  Text := Text + 'select:' + IntToStr(Item.Index) + ':' + B(Selected) + ';';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TLog.DblClicked(Sender: TObject);
begin
  Text := Text + 'dblclick;';
end;

function ItemText(L: TListView; Index, Sub: Integer): string;
var
  It: TLVItemW;
  Buf: array[0..127] of WideChar;
begin
  FillChar(It, SizeOf(It), 0);
  It.iSubItem := Sub;
  It.pszText := @Buf[0];
  It.cchTextMax := 128;
  Buf[0] := #0;
  SendMessageW(L.Handle, LVM_GETITEMTEXTW, WPARAM(Index), LPARAM(@It));
  Result := PChar(@Buf[0]);
end;

{ the raw native item: callback markers and lParam }
function RawItem(L: TListView; Index: Integer): TLVItemW;
begin
  FillChar(Result, SizeOf(Result), 0);
  Result.mask := LVIF_PARAM or LVIF_IMAGE;
  Result.iItem := Index;
  SendMessageW(L.Handle, LVM_GETITEMW, 0, LPARAM(@Result));
end;

function NativeState(L: TListView; Index: Integer): string;
var
  S: UINT;
begin
  S := UINT(SendMessageW(L.Handle, LVM_GETITEMSTATE, WPARAM(Index),
    LVIS_SELECTED or LVIS_FOCUSED or LVIS_STATEIMAGEMASK));
  Result := 'sel ' + B((S and LVIS_SELECTED) <> 0) + ' foc ' + B((S and LVIS_FOCUSED) <> 0)
    + ' img ' + IntToStr((S and LVIS_STATEIMAGEMASK) shr 12);
end;

function NativeCount(L: TListView): Integer;
begin
  Result := Integer(SendMessageW(L.Handle, LVM_GETITEMCOUNT, 0, 0));
end;

function Captions(L: TListView): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Items.Count - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + L.Items[I].Caption;
  end;
end;

function NativeCaptions(L: TListView): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to NativeCount(L) - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + ItemText(L, I, 0);
  end;
end;

{ the notification the window sends when a label edit ends (nil text: cancelled) }
function EndEdit(L: TListView; Index: Integer; Text: PChar): LRESULT;
var
  D: TLVDispInfoW;
begin
  FillChar(D, SizeOf(D), 0);
  D.hdr.hwndFrom := L.Handle;
  D.hdr.code := LVN_ENDLABELEDITW;
  D.item.mask := LVIF_TEXT;
  D.item.iItem := Index;
  D.item.pszText := Text;
  Result := SendMessageW(L.Parent.Handle, WM_NOTIFY, 0, LPARAM(@D));
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure WithoutWindow(L: TListView);
var
  A, Bi, C: TListItem;
begin
  Section('items without a window (UIF-821)');
  CheckEqI(0, L.Items.Count, 'no items');
  CheckEqB(True, L.Items.Owner = L, 'Items.Owner');
  A := L.Items.Add;
  CheckEqB(True, A is TListItem, 'Add returns a TListItem');
  CheckEqS('', A.Caption, 'Caption empty');
  CheckEqI(-1, A.ImageIndex, 'ImageIndex -1');
  CheckEqI(0, A.SubItems.Count, 'no sub-items');
  CheckEqB(False, A.Checked or A.Selected or A.Focused, 'not checked, selected or focused');
  CheckEqB(True, A.Data = nil, 'Data nil');
  CheckEqB(True, A.ListView = L, 'ListView');
  CheckEqB(True, A.Owner = L.Items, 'Owner');
  A.Caption := 'alpha';
  C := L.Items.Add;
  C.Caption := 'gamma';
  Bi := L.Items.Insert(1);
  Bi.Caption := 'beta';
  CheckEqS('alpha,beta,gamma', Captions(L), 'Insert(1) between');
  CheckEqI(1, Bi.Index, 'Index');
  CheckEqI(2, L.Items.IndexOf(C), 'IndexOf');
  CheckEqB(True, L.Items[2] = C, 'Item[2] (default)');
  A.SubItems.Add('a1');
  A.SubItems.Add('a2');
  A.ImageIndex := 3;
  Bi.SubItems.Add('b1');
  C.Data := Pointer(7);
  L.Items.Insert(0).Caption := 'zero';
  L.Items.Delete(0);
  L.Items.Add.Caption := 'last';
  L.Items[3].Delete;
  CheckEqS('alpha,beta,gamma', Captions(L), 'Delete(0) and Item.Delete');
  Bi.Selected := True;
  Bi.Focused := True;
  A.Checked := True;
  CheckEqB(True, L.Selected = Bi, 'Selected (first selected)');
  CheckEqI(1, L.ItemIndex, 'ItemIndex');
  CheckEqB(True, L.ItemFocused = Bi, 'ItemFocused');
  CheckEqI(1, L.SelCount, 'SelCount');
  CheckEqB(True, L.GetItemAt(5, 5) = nil, 'GetItemAt without a window: nil');
  CheckEqI(0, L.VisibleRowCount, 'VisibleRowCount without a window: 0');
  CheckEqS('', Log.Text, 'no events without a window');
end;

procedure Run;
var
  F: TForm;
  L, L2: TListView;
  A, Bi, C: TListItem;
  It: TLVItemW;
  R: TRect;
  H: HWND;
  I, Before: Integer;
  P: LPARAM;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    F.Left := 40;
    F.Top := 40;
    L := TListView.Create(F);
    L.SetBounds(8, 8, 400, 300);
    L.ViewStyle := vsReport;
    L.Checkboxes := True;
    L.Columns.Add.Caption := 'Name';
    L.Columns.Add.Caption := 'One';
    L.Columns.Add.Caption := 'Two';
    L.OnChange := Log.Changed;
    L.OnEdited := Log.Edited;
    L.OnItemChecked := Log.Checked;
    L.OnSelectItem := Log.Selected;
    L.OnClick := Log.Clicked;
    L.OnDblClick := Log.DblClicked;
    Log.Text := '';
    WithoutWindow(L);
    A := L.Items[0];
    Bi := L.Items[1];
    C := L.Items[2];

    Section('the window is created (UIF-821)');
    L.Parent := F;
    L.HandleNeeded;
    CheckEqI(3, NativeCount(L), 'three native items');
    CheckEqS('sel F foc F img 2', NativeState(L, 0), 'item 0 checked');
    CheckEqS('sel T foc T img 1', NativeState(L, 1), 'item 1 selected and focused');
    CheckEqS('sel F foc F img 1', NativeState(L, 2), 'item 2 plain');
    CheckEqS('', Log.Text, 'no events while restoring');

    Section('callback text and image (UIF-820)');
    It := RawItem(L, 0);
    CheckEqB(True, TListItem(Pointer(It.lParam)) = A, 'lParam is the item');
    CheckEqS('alpha', ItemText(L, 0, 0), 'caption');
    CheckEqS('a1', ItemText(L, 0, 1), 'sub-item 1');
    CheckEqS('a2', ItemText(L, 0, 2), 'sub-item 2');
    CheckEqS('b1', ItemText(L, 1, 1), 'item 1 sub-item 1');
    CheckEqS('', ItemText(L, 1, 2), 'a missing sub-item is empty');
    CheckEqS('', ItemText(L, 2, 1), 'no sub-items: empty');
    CheckEqI(3, RawItem(L, 0).iImage, 'image from ImageIndex');
    CheckEqI(-1, RawItem(L, 1).iImage, 'no image');
    A.Caption := 'ALPHA';
    CheckEqS('change:0:text;', Log.Text, 'Caption: OnChange(ctText)');
    CheckEqS('ALPHA', ItemText(L, 0, 0), 'the new caption is read back');
    Log.Text := '';
    A.SubItems[1] := 'A2';
    CheckEqS('change:0:text;', Log.Text, 'SubItems: OnChange(ctText)');
    CheckEqS('A2', ItemText(L, 0, 2), 'the new sub-item is read back');
    Log.Text := '';
    C.ImageIndex := 5;
    CheckEqS('change:2:image;', Log.Text, 'ImageIndex: OnChange(ctImage)');
    CheckEqI(5, RawItem(L, 2).iImage, 'the new image is read back');
    Log.Text := '';
    C.ImageIndex := 5;
    A.Caption := 'ALPHA';
    CheckEqS('', Log.Text, 'unchanged values: no event');

    Section('state notifications (UIF-822)');
    C.Selected := True;
    CheckEqS('select:1:F;change:1:state;select:2:T;change:2:state;', Log.Text,
      'single selection: the old item is deselected first');
    CheckEqB(False, Bi.Selected, 'item 1 mirror deselected');
    CheckEqB(True, L.Selected = C, 'Selected follows');
    Log.Text := '';
    A.Checked := False;
    CheckEqS('checked:0:F;change:0:state;', Log.Text, 'Checked False');
    CheckEqS('sel F foc F img 1', NativeState(L, 0), 'native unchecked');
    Log.Text := '';
    C.Focused := True;
    CheckEqS('change:1:state;change:2:state;', Log.Text, 'focus moves: OnChange(ctState) only');
    CheckEqB(False, Bi.Focused, 'item 1 lost the focus');
    CheckEqB(True, L.ItemFocused = C, 'ItemFocused follows');
    Log.Text := '';
    SendMessageW(L.Handle, WM_KEYDOWN, VK_SPACE, 1);
    SendMessageW(L.Handle, WM_KEYUP, VK_SPACE, LPARAM($C0000001));
    CheckEqS('checked:2:T;change:2:state;', Log.Text, 'the space key checks the focused item');
    CheckEqB(True, C.Checked, 'mirror checked');
    Log.Text := '';
    L.ItemIndex := 0;
    CheckEqS('select:2:F;change:2:state;select:0:T;change:0:state;', Log.Text, 'ItemIndex := 0');
    CheckEqI(0, L.ItemIndex, 'ItemIndex 0');
    Log.Text := '';
    L.ItemIndex := -1;
    CheckEqS('select:0:F;change:0:state;', Log.Text, 'ItemIndex := -1');
    CheckEqB(True, L.Selected = nil, 'Selected nil');
    CheckEqI(-1, L.ItemIndex, 'ItemIndex -1');

    Section('recreating keeps the state without events (UIF-821)');
    L.MultiSelect := True;
    L.SelectAll;
    CheckEqI(3, L.SelCount, 'SelectAll');
    Log.Text := '';
    H := L.Handle;
    L.ReadOnly := True;
    CheckEqB(True, L.Handle <> H, 'recreated');
    CheckEqS('', Log.Text, 'no events');
    CheckEqI(3, NativeCount(L), 'the items are back');
    CheckEqS('sel T foc F img 1', NativeState(L, 0), 'item 0 state');
    CheckEqS('sel T foc F img 1', NativeState(L, 1), 'item 1 state');
    CheckEqS('sel T foc T img 2', NativeState(L, 2), 'item 2 state');
    CheckEqS('ALPHA', ItemText(L, 0, 0), 'texts are back');
    CheckEqI(3, L.SelCount, 'SelCount kept');
    L.ClearSelection;
    CheckEqI(0, L.SelCount, 'ClearSelection');
    CheckEqS('sel F foc F img 1', NativeState(L, 0), 'native deselected');
    L.ReadOnly := False;
    Log.Text := '';

    Section('helpers');
    R := Bi.DisplayRect(drBounds);
    Check(R.Bottom > R.Top, 'DisplayRect(drBounds) has a height');
    CheckEqB(True, L.GetItemAt(R.Left + 5, (R.Top + R.Bottom) div 2) = Bi, 'GetItemAt');
    CheckEqB(True, L.GetItemAt(R.Left + 5, 290) = nil, 'GetItemAt below the items: nil');
    R := Bi.DisplayRect(drLabel);
    Check(R.Right > R.Left, 'DisplayRect(drLabel)');
    CheckEqB(True, L.GetNextItem(A, sdBelow, []) = Bi, 'GetNextItem(sdBelow)');
    CheckEqB(True, L.GetNextItem(nil, sdAll, [isFocused]) = C, 'GetNextItem([isFocused])');
    CheckEqB(True, L.GetNextItem(nil, sdAll, [isSelected]) = nil, 'GetNextItem([isSelected]): none');
    CheckEqB(True, L.TopItem = A, 'TopItem');
    CheckEqI(Integer(SendMessageW(L.Handle, LVM_GETCOUNTPERPAGE, 0, 0)), L.VisibleRowCount, 'VisibleRowCount');
    Check(L.VisibleRowCount > 3, 'more rows than items fit');
    L.Items.BeginUpdate;
    try
      L.Items.Add;
      CheckEqS('', Log.Text, 'adding an item fires nothing');
      for I := 2 to 60 do
        L.Items.Add.Caption := 'row' + IntToStr(I);
    finally
      L.Items.EndUpdate;
    end;
    CheckEqI(63, NativeCount(L), 'items added inside BeginUpdate');
    CheckEqS('change:4:text;', Copy(Log.Text, 1, 14), 'a caption set after Add: OnChange(ctText)');
    Log.Text := '';
    L.Items[50].MakeVisible(False);
    Check(L.TopItem <> A, 'MakeVisible scrolls');
    Check(L.TopItem.Index <= 50, 'the item is not above the top');
    Check(L.TopItem.Index + L.VisibleRowCount > 50, 'and not below the page');
    while L.Items.Count > 3 do
      L.Items.Delete(L.Items.Count - 1);
    CheckEqI(3, NativeCount(L), 'deleted natively');

    Section('deleting and clearing');
    L.Items.Insert(1).Caption := 'new';
    CheckEqS('ALPHA,new,beta,gamma', NativeCaptions(L), 'Insert with a window');
    CheckEqS('change:1:text;', Log.Text, 'only the caption change is reported');
    Log.Text := '';
    CheckEqB(True, TListItem(Pointer(RawItem(L, 1).lParam)) = L.Items[1], 'its lParam');
    L.Items.Delete(1);
    CheckEqS('ALPHA,beta,gamma', NativeCaptions(L), 'Delete with a window');
    CheckEqS('', Log.Text, 'no events');

    Section('label edit (UIF-824)');
    Log.Text := '';
    CheckEqI(1, EndEdit(L, 1, 'renamed'), 'an accepted edit returns 1');
    CheckEqS('edited:1:renamed;change:1:text;', Log.Text, 'OnEdited, then the caption is set');
    CheckEqS('RENAMED', Bi.Caption, 'Caption from the edited S');
    CheckEqS('RENAMED', ItemText(L, 1, 0), 'the native text is the callback');
    Log.Text := '';
    CheckEqI(0, EndEdit(L, 1, nil), 'a cancelled edit returns 0');
    CheckEqS('', Log.Text, 'a cancelled edit: no OnEdited');
    CheckEqS('RENAMED', Bi.Caption, 'Caption unchanged');

    Section('mouse (UIF-825)');
    L.MultiSelect := False;
    F.Show;
    Bi.Selected := True;
    Bi.Focused := True;
    Log.Text := '';
    R := C.DisplayRect(drLabel);
    P := MakeLParam(R.Left + 4, (R.Top + R.Bottom) div 2);
    PostMessageW(L.Handle, WM_LBUTTONUP, 0, P);
    SendMessageW(L.Handle, WM_LBUTTONDOWN, MK_LBUTTON, P);
    Application.ProcessMessages;
    CheckEqS('change:1:state;select:1:F;change:1:state;select:2:T;change:2:state;click;', Log.Text,
      'a click moves the focus and the selection, then OnClick');
    Log.Text := '';
    PostMessageW(L.Handle, WM_LBUTTONUP, 0, P);
    SendMessageW(L.Handle, WM_LBUTTONDBLCLK, MK_LBUTTON, P);
    Application.ProcessMessages;
    Check(Pos('dblclick;', Log.Text) > 0, 'a double click fires OnDblClick');
    F.Hide;

    Section('report view without columns (UIF-826)');
    L2 := TListView.Create(F);
    L2.ViewStyle := vsReport;
    L2.Parent := F;
    L2.Items.Add.Caption := 'hidden';
    CheckEqI(1, NativeCount(L2), 'the item exists');
    CheckEqI(0, Integer(SendMessageW(HWND(SendMessageW(L2.Handle, LVM_GETHEADER, 0, 0)), HDM_GETITEMCOUNT, 0, 0)),
      'no header items');
    R := L2.Items[0].DisplayRect(drBounds);
    CheckEqI(0, R.Right - R.Left, 'the item has no width');
    L2.Free;

    Section('destroying fires nothing (UIF-822)');
    L.MultiSelect := True;
    L.SelectAll;
    Log.Text := '';
    L.Free;
    CheckEqS('', Log.Text, 'no events');

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      L2 := TListView.Create(nil);
      L2.ViewStyle := vsReport;
      L2.Columns.Add.Caption := 'x';
      L2.Items.Add.Caption := 'a';
      L2.Items.Add.SubItems.Add('b');
      L2.Parent := F;
      L2.HandleNeeded;
      L2.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
