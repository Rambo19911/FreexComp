// covers: UIF-700, UIF-701, UIF-702, UIF-703, UIF-704, UIF-250
// needs: desktop
program Ctl_ComboBox;
{ U-062, controls-standard.md section 10: TComboBox defaults (145 x 23, clWindow, TabStop); the intrinsic height of
  rule I1 (layout.md UIF-250, corpus E33 a/b) and the native height that agrees with it; a sub-classed system
  COMBOBOX with CBS_DROPDOWN / CBS_SIMPLE / CBS_DROPDOWNLIST, CBS_HASSTRINGS, CBS_AUTOHSCROLL, CBS_SORT and the
  CharCase styles; Items with Objects before the handle, pushed to and pulled from the window with the selection
  and the text; list-style Text selecting the matching item; OnChange / OnSelect / OnClick order on a user
  selection, OnChange on editing, OnDropDown / OnCloseUp; nothing for code; Enter / Escape wanted while dropped;
  streaming (ItemIndex after Items); leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit, KeyStateKit;

type
  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
    procedure Selected(Sender: TObject);
    procedure Clicked(Sender: TObject);
    procedure Dropped(Sender: TObject);
    procedure Closed(Sender: TObject);
  end;

  TComboForm = class(TForm)
  published
    Pick: TComboBox;
    Simple: TComboBox;
    procedure PickChange(Sender: TObject);
  end;

{$FORM Ctl_ComboBox.form}

var
  Log: TLog;
  O1, O2: TObject;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change:' + TComboBox(Sender).Text + ';';
end;

procedure TLog.Selected(Sender: TObject);
begin
  Text := Text + 'select:' + IntToStr(TComboBox(Sender).ItemIndex) + ';';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TLog.Dropped(Sender: TObject);
begin
  Text := Text + 'dropdown;';
end;

procedure TLog.Closed(Sender: TObject);
begin
  Text := Text + 'closeup;';
end;

procedure TComboForm.PickChange(Sender: TObject);
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

function StyleOf(C: TWinControl): NativeInt;
begin
  Result := GetWindowLongPtrW(C.Handle, GWL_STYLE);
end;

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function WindowHeight(C: TWinControl): Integer;
var
  R: TRect;
begin
  GetWindowRect(C.Handle, R);
  Result := R.Bottom - R.Top;
end;

function Dump(C: TComboBox): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to C.Items.Count - 1 do
  begin
    if I = C.ItemIndex then
      Result := Result + '*';
    Result := Result + C.Items[I] + ';';
  end;
end;

function NativeDump(C: TComboBox): string;
var
  I, N: Integer;
  S: string;
begin
  Result := '';
  N := Integer(SendMessageW(C.Handle, CB_GETCOUNT, 0, 0));
  for I := 0 to N - 1 do
  begin
    SetLength(S, Integer(SendMessageW(C.Handle, CB_GETLBTEXTLEN, WPARAM(I), 0)) + 1);
    SetLength(S, Integer(SendMessageW(C.Handle, CB_GETLBTEXT, WPARAM(I), LPARAM(PChar(S)))));
    Result := Result + S + ';';
  end;
end;

function Raises(C: TComboBox; Insert: Boolean): string;
begin
  Result := 'none';
  try
    if Insert then
      C.Items.Insert(0, 'x')
    else
      C.Items[0] := 'x';
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
  C: TComboBox;
  CF: TComboForm;
  H, Edit: HWND;
  I, Before, S11: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  O1 := TObject.Create;
  O2 := TObject.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    S11 := LayoutMulDiv(11, F.PixelsPerInch, 96);
    C := TComboBox.Create(nil);
    try
      Section('defaults');
      CheckEqI(145, C.Width, 'Width 145');
      CheckEqI(23, C.Height, 'Height 23 (rule I1, 12 + 11)');
      CheckEqB(True, C.CharCase = ecNormal, 'CharCase');
      CheckEqB(True, C.Color = clWindow, 'Color clWindow');
      CheckEqI(8, C.DropDownCount, 'DropDownCount 8');
      CheckEqI(-1, C.ItemIndex, 'ItemIndex -1');
      CheckEqI(0, C.Items.Count, 'Items empty');
      CheckEqI(0, C.MaxLength, 'MaxLength');
      CheckEqB(False, C.ParentColor, 'ParentColor False');
      CheckEqB(True, C.ParentFont, 'ParentFont');
      CheckEqB(False, C.Sorted, 'Sorted');
      CheckEqB(True, C.Style = csDropDown, 'Style csDropDown');
      CheckEqI(-1, C.TabOrder, 'TabOrder');
      CheckEqB(True, C.TabStop, 'TabStop True');
      CheckEqS('', C.Text, 'Text');
      CheckEqS('', C.TextHint, 'TextHint');
      CheckEqB(False, C.DroppedDown, 'DroppedDown');
      Check(GetClass('TComboBox') <> nil, 'registered');

      Section('intrinsic height (layout.md UIF-250, UIF-700, E33)');
      C.Font.Height := -12;
      C.SetBounds(10, 10, 145, 40);
      CheckEqS('10,10,145,23', BoundsText(C), 'E33 a without a form: 12 + 11');
      C.Font.Height := -16;
      CheckEqI(27, C.Height, 'font change re-applies I1');
      C.Style := csDropDownList;
      CheckEqI(27, C.Height, 'csDropDownList: I1');
      C.Style := csSimple;
      C.Height := 100;
      CheckEqI(100, C.Height, 'csSimple: the stored height');
      C.Style := csDropDown;
      CheckEqI(27, C.Height, 'back to csDropDown: I1');
      C.Font.Height := -12;
      C.Parent := F;
      CheckEqS(Format('10,10,145,%d', [12 + S11]), BoundsText(C), 'E33 a on a form: s(11) at the form''s DPI');
      CheckEqI(C.Height, WindowHeight(C), 'UIF-701: the native height agrees');
      { the system adds its own frame to the selection field: the item height is measured, not s(6) }
      CheckEqB(True, Integer(SendMessageW(C.Handle, CB_GETITEMHEIGHT, WPARAM(-1), 0)) < C.Height,
        'CB_SETITEMHEIGHT(-1, ...) below the control height');
      C.Align := alLeft;
      CheckEqS(Format('0,0,145,%d', [12 + S11]), BoundsText(C), 'E33 b: alLeft keeps the intrinsic height');
      C.Align := alNone;
      C.Font.Height := -20;
      CheckEqI(20 + S11, C.Height, 'with a handle: font change re-applies I1');
      CheckEqI(C.Height, WindowHeight(C), 'native height after the font change');
      C.Font.Height := -12;
      C.Style := csSimple;
      C.Height := 120;
      CheckEqI(120, WindowHeight(C), 'csSimple: the window has the stored height');
      C.Style := csDropDown;
      C.Parent := nil;
    finally
      C.Free;
    end;

    Section('handle-less items (UIF-703)');
    C := TComboBox.Create(F);
    C.OnChange := Log.Changed;
    C.OnSelect := Log.Selected;
    C.OnClick := Log.Clicked;
    C.OnDropDown := Log.Dropped;
    C.OnCloseUp := Log.Closed;
    Log.Text := '';
    CheckEqI(0, C.Items.AddObject('one', O1), 'AddObject index');
    C.Items.Add('three');
    C.Items.Insert(1, 'two');
    C.AddItem('four', O2);
    CheckEqS('one;two;three;four;', Dump(C), 'items');
    Check(C.Items.Objects[3] = O2, 'AddItem object');
    C.ItemIndex := 2;
    CheckEqS('three', C.Text, 'ItemIndex sets the text');
    C.ItemIndex := 9;
    CheckEqI(2, C.ItemIndex, 'out of range ignored');
    C.Items.Delete(0);
    CheckEqI(1, C.ItemIndex, 'deleting before the selection keeps the item');
    C.Text := 'free text';
    CheckEqS('free text', C.Text, 'csDropDown: any text');
    CheckEqI(1, C.ItemIndex, 'the selection stays');
    CheckEqS('', Log.Text, 'nothing fires for code');

    Section('window');
    C.Parent := F;
    C.SetBounds(10, 10, 145, 23);
    H := C.Handle;
    CheckEqS('ComboBox', ClassOf(H), 'system class COMBOBOX');
    CheckEqI(CBS_DROPDOWN, Integer(StyleOf(C) and 3), 'CBS_DROPDOWN');
    CheckEqB(True, (StyleOf(C) and (CBS_HASSTRINGS or CBS_AUTOHSCROLL)) = (CBS_HASSTRINGS or CBS_AUTOHSCROLL),
      'CBS_HASSTRINGS, CBS_AUTOHSCROLL');
    CheckEqS('two;three;four;', NativeDump(C), 'items pushed');
    CheckEqI(1, Integer(SendMessageW(H, CB_GETCURSEL, 0, 0)), 'selection pushed');
    CheckEqS('free text', C.Text, 'text pushed after the selection');
    Check(TObject(Pointer(SendMessageW(H, CB_GETITEMDATA, 2, 0))) = O2, 'objects pushed');
    CheckEqI(8, Integer(SendMessageW(H, CB_GETMINVISIBLE, 0, 0)), 'CB_SETMINVISIBLE(DropDownCount)');
    C.DropDownCount := 4;
    CheckEqI(4, Integer(SendMessageW(H, CB_GETMINVISIBLE, 0, 0)), 'DropDownCount with a handle');
    C.Items.Add('five');
    CheckEqS('two;*three;four;five;', Dump(C), 'with a handle');
    C.Items[0] := 'TWO';
    C.Items[1] := 'THREE';
    CheckEqS('TWO;*THREE;four;five;', Dump(C), 'Put keeps the selection');
    C.ItemIndex := 3;
    CheckEqS('five', C.Text, 'ItemIndex sets the text with a handle');
    C.ItemIndex := -1;
    CheckEqS('', C.Text, 'ItemIndex -1 clears the text');
    C.ItemIndex := 0;
    C.SelectAll;
    CheckEqI(0, C.SelStart, 'SelStart');
    CheckEqI(3, C.SelLength, 'SelectAll: SelLength');
    C.SelStart := 1;
    C.SelLength := 2;
    CheckEqS('WO', C.SelText, 'SelText');
    C.SelText := 'o';
    CheckEqS('To', C.Text, 'SelText replaces the selection');
    CheckEqS('', Log.Text, 'nothing fires for code');

    Section('recreate keeps the state');
    C.ItemIndex := 1;
    H := C.Handle;
    C.CharCase := ecLowerCase;
    Check(C.Handle <> H, 'CharCase recreates');
    CheckEqB(True, (StyleOf(C) and CBS_LOWERCASE) <> 0, 'CBS_LOWERCASE');
    CheckEqS('two;*three;four;five;', Dump(C), 'items and selection kept (CBS_LOWERCASE converts the list too)');
    Check(C.Items.Objects[2] = O2, 'objects kept');
    C.CharCase := ecNormal;
    H := C.Handle;
    C.Style := csDropDownList;
    Check(C.Handle <> H, 'Style recreates');
    CheckEqI(CBS_DROPDOWNLIST, Integer(StyleOf(C) and 3), 'CBS_DROPDOWNLIST');
    CheckEqS('three', C.Text, 'list style: the text of the selected item');
    C.Text := 'Four';
    CheckEqI(2, C.ItemIndex, 'list style: Text selects the matching item (case-insensitive)');
    CheckEqS('four', C.Text, 'the item''s text');
    C.Text := 'nothing';
    CheckEqI(-1, C.ItemIndex, 'no match: -1');
    CheckEqS('', Log.Text, 'nothing fires for code');
    C.Style := csSimple;
    CheckEqI(CBS_SIMPLE, Integer(StyleOf(C) and 3), 'CBS_SIMPLE');
    C.Style := csOwnerDrawFixed;
    CheckEqI(CBS_DROPDOWNLIST, Integer(StyleOf(C) and 3), 'owner-draw behaves as csDropDownList');
    C.Style := csDropDown;
    C.Parent := nil;
    C.Style := csDropDownList;
    C.Text := 'five';
    CheckEqI(3, C.ItemIndex, 'list style without a handle: Text selects');
    C.Style := csDropDown;
    C.Parent := F;

    Section('sorted (UIF-703 as UIF-693)');
    C.Clear;
    CheckEqI(0, C.Items.Count, 'Clear');
    CheckEqS('', C.Text, 'Clear empties the text');
    C.Items.Add('pear');
    C.Items.Add('apple');
    C.ItemIndex := 0;
    C.Sorted := True;
    CheckEqB(True, (StyleOf(C) and CBS_SORT) <> 0, 'CBS_SORT');
    CheckEqS('apple;*pear;', Dump(C), 'sorted, selection kept');
    CheckEqI(1, C.Items.Add('Fig'), 'Add returns the actual index');
    CheckEqS('Operation not allowed on sorted list', Raises(C, True), 'Insert raises');
    CheckEqS('Operation not allowed on sorted list', Raises(C, False), 'Put raises');
    C.Sorted := False;

    Section('user (UIF-702, UIF-704)');
    C.Clear;
    C.Items.Add('a');
    C.Items.Add('b');
    C.Items.Add('c');
    C.ItemIndex := 0;
    F.Show;
    Edit := GetWindow(C.Handle, GW_CHILD);
    CheckEqS('Edit', ClassOf(Edit), 'the inner edit');
    Log.Text := '';
    SendKeyMessage(Edit, WM_KEYDOWN, VK_DOWN, 0);
    CheckEqS('change:b;select:1;click;', Log.Text, 'user selection: text updated, OnChange, OnSelect, OnClick');
    C.SelStart := 1;
    Log.Text := '';
    SendKeyMessage(Edit, WM_CHAR, Ord('x'), 0);
    CheckEqS('change:bx;', Log.Text, 'editing: OnChange');
    Log.Text := '';
    C.DroppedDown := True;
    CheckEqB(True, C.DroppedDown, 'DroppedDown');
    CheckEqS('dropdown;', Log.Text, 'OnDropDown');
    CheckEqI(1, C.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'dropped: Enter wanted');
    CheckEqI(1, C.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 'dropped: Escape wanted');
    Log.Text := '';
    C.DroppedDown := False;
    CheckEqB(False, C.DroppedDown, 'closed');
    Check(Pos('closeup;', Log.Text) > 0, 'OnCloseUp');
    CheckEqI(0, C.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'closed: Enter not wanted');
    Log.Text := '';
    C.ItemIndex := 2;
    C.Text := 'code';
    CheckEqS('', Log.Text, 'ItemIndex/Text by code: nothing');
    C.Style := csDropDownList;
    C.ItemIndex := 0;
    Log.Text := '';
    SendKeyMessage(C.Handle, WM_KEYDOWN, VK_DOWN, 0);
    CheckEqS('change:b;select:1;click;', Log.Text, 'list style: the same order');
    F.Hide;

    Section('streaming');
    Log.Text := '';
    CF := TComboForm.Create(nil);
    try
      S11 := LayoutMulDiv(11, CF.PixelsPerInch, 96);
      CheckEqS('', Log.Text, 'no event while loading');
      CheckEqS(Format('10,10,145,%d', [16 + S11]), BoundsText(CF.Pick), 'stored height replaced by I1');
      CheckEqS('APPLE;*FIG;PEAR;', Dump(CF.Pick), 'Items, ItemIndex after Items, then Sorted');
      CheckEqS('FIG', CF.Pick.Text, 'Text of the selected item');
      CheckEqB(True, CF.Pick.Style = csDropDownList, 'Style');
      CheckEqB(True, CF.Pick.CharCase = ecUpperCase, 'CharCase');
      CheckEqB(True, CF.Pick.Color = clYellow, 'Color');
      CheckEqI(5, CF.Pick.DropDownCount, 'DropDownCount');
      CheckEqI(10, CF.Pick.MaxLength, 'MaxLength');
      CheckEqB(False, CF.Pick.TabStop, 'TabStop');
      CheckEqS('choose', CF.Pick.TextHint, 'TextHint');
      Check(Assigned(CF.Pick.OnChange) and Assigned(CF.Pick.OnSelect), 'events bound');
      CheckEqS('typed', CF.Simple.Text, 'Text');
      CheckEqI(120, CF.Simple.Height, 'csSimple: stored height kept');
      CheckEqS('APPLE;FIG;PEAR;', NativeDump(CF.Pick), 'pushed to the window');
      CheckEqI(1, Integer(SendMessageW(CF.Pick.Handle, CB_GETCURSEL, 0, 0)), 'selection pushed');
    finally
      CF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      C := TComboBox.Create(nil);
      C.Items.Add('Leak');
      C.Parent := F;
      C.HandleNeeded;
      C.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    O1.Free;
    O2.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
