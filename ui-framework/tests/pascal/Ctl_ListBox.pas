// covers: UIF-690, UIF-691, UIF-692, UIF-693
// needs: desktop
program Ctl_ListBox;
{ U-061, controls-standard.md section 9: TListBox defaults (121 x 97, clWindow, TabStop); a sub-classed system
  LISTBOX with LBS_NOTIFY, LBS_HASSTRINGS, LBS_NOINTEGRALHEIGHT, WS_VSCROLL and WS_EX_CLIENTEDGE, LBS_SORT,
  LBS_EXTENDEDSEL / LBS_MULTIPLESEL and LBS_MULTICOLUMN re-creating the window; Items with Objects before the
  handle, pushed to and pulled from the window with the selection; ItemIndex range rule and deletion; OnClick only
  for user selection changes (LBN_SELCHANGE), OnDblClick on LBN_DBLCLK; sorted lists (Add index, Insert/Put
  raise); selection API, TopIndex, ItemRect, ItemAtPos; streaming; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure DblClicked(Sender: TObject);
  end;

  TListForm = class(TForm)
  published
    Names: TListBox;
    procedure NamesClick(Sender: TObject);
  end;

{$FORM Ctl_ListBox.form}

var
  Log: TLog;
  O1, O2, O3: TObject;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click:' + IntToStr(TListBox(Sender).ItemIndex) + ';';
end;

procedure TLog.DblClicked(Sender: TObject);
begin
  Text := Text + 'dbl:' + IntToStr(TListBox(Sender).ItemIndex) + ';';
end;

procedure TListForm.NamesClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'form;';
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

function HasStyle(C: TWinControl; S: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and S) = S;
end;

function HasExStyle(C: TWinControl; S: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_EXSTYLE) and S) = S;
end;

{ the items as one line, the selected ones marked with * }
function Dump(L: TListBox): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Items.Count - 1 do
  begin
    if L.Selected[I] then
      Result := Result + '*';
    Result := Result + L.Items[I] + ';';
  end;
end;

{ the native strings }
function NativeDump(L: TListBox): string;
var
  I, N: Integer;
  S: string;
begin
  Result := '';
  N := Integer(SendMessageW(L.Handle, LB_GETCOUNT, 0, 0));
  for I := 0 to N - 1 do
  begin
    SetLength(S, Integer(SendMessageW(L.Handle, LB_GETTEXTLEN, WPARAM(I), 0)) + 1);
    SetLength(S, Integer(SendMessageW(L.Handle, LB_GETTEXT, WPARAM(I), LPARAM(PChar(S)))));
    Result := Result + S + ';';
  end;
end;

function Raises(L: TListBox; Insert: Boolean): string;
begin
  Result := 'none';
  try
    if Insert then
      L.Items.Insert(0, 'x')
    else
      L.Items[0] := 'x';
  except
    on E: EStringListError do
      Result := E.Message;
  end;
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  L: TListBox;
  LF: TListForm;
  H: HWND;
  R: TRect;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  O1 := TObject.Create;
  O2 := TObject.Create;
  O3 := TObject.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    L := TListBox.Create(F);

    Section('defaults');
    CheckEqI(121, L.Width, 'Width 121');
    CheckEqI(97, L.Height, 'Height 97');
    CheckEqB(True, L.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqB(True, L.Color = clWindow, 'Color clWindow');
    CheckEqB(False, L.ParentColor, 'ParentColor False');
    CheckEqI(0, L.Columns, 'Columns');
    CheckEqB(True, L.ExtendedSelect, 'ExtendedSelect True');
    CheckEqI(15, L.ItemHeight, 'ItemHeight 15');
    CheckEqI(0, L.Items.Count, 'Items empty');
    CheckEqI(0, L.Count, 'Count');
    CheckEqB(False, L.MultiSelect, 'MultiSelect');
    CheckEqB(True, L.ParentFont, 'ParentFont');
    CheckEqB(False, L.Sorted, 'Sorted');
    CheckEqB(True, L.Style = lbStandard, 'Style lbStandard');
    CheckEqI(-1, L.TabOrder, 'TabOrder');
    CheckEqB(True, L.TabStop, 'TabStop True');
    CheckEqI(-1, L.ItemIndex, 'ItemIndex -1');
    CheckEqI(0, L.SelCount, 'SelCount');
    Check(GetClass('TListBox') <> nil, 'registered');

    Section('handle-less items (UIF-690, UIF-692)');
    L.OnClick := Log.Clicked;
    L.OnDblClick := Log.DblClicked;
    Log.Text := '';
    CheckEqI(0, L.Items.AddObject('one', O1), 'AddObject index');
    CheckEqI(1, L.Items.Add('two'), 'Add index');
    L.Items.InsertObject(0, 'zero', O3);
    L.AddItem('three', O2);
    CheckEqS('zero;one;two;three;', Dump(L), 'items');
    Check(L.Items.Objects[1] = O1, 'object of one');
    Check(L.Items.Objects[3] = O2, 'AddItem object');
    L.ItemIndex := 2;
    CheckEqI(2, L.ItemIndex, 'ItemIndex');
    L.ItemIndex := 4;
    CheckEqI(2, L.ItemIndex, 'ItemIndex = Count ignored');
    L.ItemIndex := -2;
    CheckEqI(2, L.ItemIndex, 'ItemIndex < -1 ignored');
    CheckEqS('zero;one;*two;three;', Dump(L), 'Selected follows ItemIndex');
    L.Items.Delete(0);
    CheckEqI(1, L.ItemIndex, 'deleting before the selection keeps the item selected');
    L.Items[1] := 'TWO';
    CheckEqS('one;*TWO;three;', Dump(L), 'Put keeps the selection');
    L.Items.Delete(1);
    CheckEqI(-1, L.ItemIndex, 'deleting the selected item: -1');
    CheckEqS('', Log.Text, 'no OnClick by code');
    L.Items.Insert(1, 'two');
    L.ItemIndex := 1;

    Section('window');
    L.Parent := F;
    H := L.Handle;
    CheckEqS('ListBox', ClassOf(H), 'system class LISTBOX');
    CheckEqB(True, HasStyle(L, LBS_NOTIFY or LBS_HASSTRINGS or LBS_NOINTEGRALHEIGHT), 'base styles');
    CheckEqB(False, HasStyle(L, LBS_SORT), 'no LBS_SORT');
    CheckEqB(False, HasStyle(L, LBS_EXTENDEDSEL), 'no LBS_EXTENDEDSEL');
    CheckEqB(False, HasStyle(L, LBS_MULTICOLUMN), 'no LBS_MULTICOLUMN');
    CheckEqB(True, HasExStyle(L, WS_EX_CLIENTEDGE), 'WS_EX_CLIENTEDGE');
    CheckEqS('one;two;three;', NativeDump(L), 'items pushed');
    Check(TObject(Pointer(SendMessageW(H, LB_GETITEMDATA, 0, 0))) = O1, 'objects pushed (LB_SETITEMDATA)');
    CheckEqI(1, Integer(SendMessageW(H, LB_GETCURSEL, 0, 0)), 'selection pushed');
    CheckEqI(Integer(SendMessageW(H, LB_GETITEMHEIGHT, 0, 0)), L.ItemHeight, 'ItemHeight = LB_GETITEMHEIGHT');
    CheckEqI(3, L.Count, 'Count');
    L.Items.Add('four');
    L.Items.Insert(0, 'zero');
    CheckEqS('zero;one;*two;three;four;', Dump(L), 'with a handle');
    CheckEqS('zero;one;two;three;four;', NativeDump(L), 'natively');
    L.Items.Objects[4] := O3;
    Check(L.Items.Objects[4] = O3, 'Objects with a handle');
    Check(L.Items.Objects[1] = O1, 'object kept by the insertion');
    L.Items[1] := 'ONE';
    Check(L.Items.Objects[1] = O1, 'Put keeps the object');
    L.Items[1] := 'one';
    L.ItemIndex := 7;
    CheckEqI(2, L.ItemIndex, 'out of range ignored with a handle');
    L.Items.Delete(2);
    CheckEqI(-1, L.ItemIndex, 'deleting the selected item: -1 with a handle');
    L.Items.Insert(2, 'two');
    L.ItemIndex := 2;
    L.Items.BeginUpdate;
    L.Items.Add('five');
    L.Items.EndUpdate;
    CheckEqS('zero;one;*two;three;four;five;', Dump(L), 'BeginUpdate/EndUpdate');
    L.Items.Delete(5);

    Section('recreate keeps the state');
    L.BorderStyle := bsNone;
    Check(L.Handle <> H, 'BorderStyle recreates');
    CheckEqB(False, HasExStyle(L, WS_EX_CLIENTEDGE), 'bsNone: no WS_EX_CLIENTEDGE');
    CheckEqS('zero;one;*two;three;four;', Dump(L), 'items and selection kept');
    Check(L.Items.Objects[1] = O1, 'objects kept');
    Check(L.Items.Objects[4] = O3, 'objects kept (last)');
    L.BorderStyle := bsSingle;
    H := L.Handle;
    L.Columns := 2;
    Check(L.Handle <> H, 'Columns recreates');
    CheckEqB(True, HasStyle(L, LBS_MULTICOLUMN), 'LBS_MULTICOLUMN (WS_HSCROLL shows only when needed)');
    L.Columns := 0;
    H := L.Handle;
    L.MultiSelect := True;
    Check(L.Handle <> H, 'MultiSelect recreates');
    CheckEqB(True, HasStyle(L, LBS_EXTENDEDSEL), 'MultiSelect + ExtendedSelect: LBS_EXTENDEDSEL');
    CheckEqS('zero;one;*two;three;four;', Dump(L), 'selection kept');
    H := L.Handle;
    L.ExtendedSelect := False;
    Check(L.Handle <> H, 'ExtendedSelect recreates');
    CheckEqB(True, HasStyle(L, LBS_MULTIPLESEL), 'MultiSelect only: LBS_MULTIPLESEL');
    CheckEqB(False, HasStyle(L, LBS_EXTENDEDSEL), 'no LBS_EXTENDEDSEL');

    Section('multi-select');
    L.Selected[0] := True;
    L.Selected[4] := True;
    CheckEqS('*zero;one;*two;three;*four;', Dump(L), 'Selected[]');
    CheckEqI(3, L.SelCount, 'SelCount');
    L.Selected[2] := False;
    CheckEqI(2, L.SelCount, 'unselect');
    L.ItemIndex := 3;
    CheckEqI(3, L.ItemIndex, 'ItemIndex = the focused item');
    CheckEqI(2, L.SelCount, 'ItemIndex does not select');
    L.SelectAll;
    CheckEqI(5, L.SelCount, 'SelectAll');
    L.ClearSelection;
    CheckEqI(0, L.SelCount, 'ClearSelection');
    L.Selected[1] := True;
    L.Selected[3] := True;
    L.ExtendedSelect := True;
    L.ExtendedSelect := False;
    CheckEqS('zero;*one;two;*three;four;', Dump(L), 'multiple selection kept over a recreation');
    L.DeleteSelected;
    CheckEqS('zero;two;four;', Dump(L), 'DeleteSelected');
    CheckEqS('', Log.Text, 'no OnClick by code');
    L.MultiSelect := False;
    L.ExtendedSelect := True;
    L.ItemIndex := 1;
    L.DeleteSelected;
    CheckEqS('zero;four;', Dump(L), 'DeleteSelected (single)');
    L.Clear;
    CheckEqI(0, L.Count, 'Clear');

    Section('sorted (UIF-693)');
    L.Items.Add('pear');
    L.Items.Add('apple');
    H := L.Handle;
    L.Sorted := True;
    Check(L.Handle <> H, 'Sorted recreates');
    CheckEqB(True, HasStyle(L, LBS_SORT), 'LBS_SORT');
    CheckEqS('apple;pear;', Dump(L), 'sorted');
    CheckEqI(1, L.Items.Add('Fig'), 'Add returns the actual index (case-insensitive)');
    CheckEqI(0, L.Items.Add('Aardvark'), 'Add at the front');
    CheckEqS('Aardvark;apple;Fig;pear;', Dump(L), 'order');
    CheckEqS('Operation not allowed on sorted list', Raises(L, True), 'Insert raises');
    CheckEqS('Operation not allowed on sorted list', Raises(L, False), 'Put raises');
    L.Sorted := False;
    CheckEqS('none', Raises(L, True), 'unsorted: Insert allowed');
    L.Parent := nil;
    L.Clear;
    L.Items.Add('pear');
    L.Items.Add('apple');
    L.Sorted := True;
    CheckEqS('apple;pear;', Dump(L), 'sorted without a handle');
    CheckEqI(1, L.Items.Add('Fig'), 'Add index without a handle');
    L.Parent := F;
    CheckEqS('apple;Fig;pear;', NativeDump(L), 'the native order agrees');
    L.Sorted := False;

    Section('positions');
    L.Clear;
    for I := 0 to 19 do
      L.Items.Add('item ' + IntToStr(I));
    { the system keeps WS_VSCROLL only while the scroll bar is needed }
    CheckEqB(True, HasStyle(L, WS_VSCROLL), 'WS_VSCROLL with more items than fit');
    L.TopIndex := 5;
    CheckEqI(5, L.TopIndex, 'TopIndex');
    R := L.ItemRect(5);
    CheckEqI(0, R.Top, 'ItemRect of the top item');
    CheckEqI(L.ItemHeight, R.Bottom - R.Top, 'ItemRect height');
    CheckEqI(6, L.ItemAtPos(Point(2, L.ItemRect(6).Top + 1), True), 'ItemAtPos');
    L.TopIndex := 0;
    L.Clear;
    L.Items.Add('a');
    L.Items.Add('b');
    CheckEqI(-1, L.ItemAtPos(Point(2, L.ItemRect(1).Bottom + 5), True), 'below the items, Existing: -1');
    CheckEqI(2, L.ItemAtPos(Point(2, L.ItemRect(1).Bottom + 5), False), 'below the items: Count');

    Section('user (UIF-691)');
    L.Clear;
    L.Items.Add('a');
    L.Items.Add('b');
    L.Items.Add('c');
    L.ItemIndex := 0;
    F.Show;
    Log.Text := '';
    SendMessageW(L.Handle, WM_KEYDOWN, VK_DOWN, 0);
    CheckEqS('click:1;', Log.Text, 'keyboard selection change: OnClick');
    R := L.ItemRect(2);
    Log.Text := '';
    SendMessageW(L.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(Word(R.Left + 2), Word(R.Top + 2)));
    SendMessageW(L.Handle, WM_LBUTTONUP, 0, MakeLParam(Word(R.Left + 2), Word(R.Top + 2)));
    CheckEqS('click:2;', Log.Text, 'mouse selection change: one OnClick');
    Log.Text := '';
    SendMessageW(L.Handle, WM_LBUTTONDBLCLK, MK_LBUTTON, MakeLParam(Word(R.Left + 2), Word(R.Top + 2)));
    SendMessageW(L.Handle, WM_LBUTTONUP, 0, MakeLParam(Word(R.Left + 2), Word(R.Top + 2)));
    CheckEqS('dbl:2;', Log.Text, 'double click: OnDblClick');
    Log.Text := '';
    L.ItemIndex := 0;
    L.Selected[1] := True;
    CheckEqS('', Log.Text, 'ItemIndex/Selected by code: no OnClick');
    F.Hide;

    Section('streaming');
    Log.Text := '';
    LF := TListForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no event while loading');
      CheckEqS('Apple;fig;pear;', Dump(LF.Names), 'Items sorted');
      CheckEqB(True, LF.Names.Sorted, 'Sorted');
      CheckEqB(True, LF.Names.MultiSelect, 'MultiSelect');
      CheckEqB(False, LF.Names.ExtendedSelect, 'ExtendedSelect');
      CheckEqI(2, LF.Names.Columns, 'Columns');
      CheckEqB(True, LF.Names.BorderStyle = bsNone, 'BorderStyle');
      CheckEqB(True, LF.Names.Color = clYellow, 'Color');
      CheckEqI(-15, LF.Names.Font.Height, 'Font.Height');
      CheckEqB(False, LF.Names.TabStop, 'TabStop');
      CheckEqI(150, LF.Names.Width, 'Width');
      Check(Assigned(LF.Names.OnClick) and Assigned(LF.Names.OnDblClick), 'events bound');
      CheckEqS('Apple;fig;pear;', NativeDump(LF.Names), 'pushed to the window');
      CheckEqB(True, HasStyle(LF.Names, LBS_MULTIPLESEL or LBS_SORT or LBS_MULTICOLUMN), 'styles');
    finally
      LF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      L := TListBox.Create(nil);
      L.Items.Add('Leak');
      L.Parent := F;
      L.HandleNeeded;
      L.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    O1.Free;
    O2.Free;
    O3.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
