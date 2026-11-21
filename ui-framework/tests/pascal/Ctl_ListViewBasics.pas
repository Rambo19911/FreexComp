// covers: UIF-823
// needs: desktop
program Ctl_ListViewBasics;
{ U-086, controls-standard.md section 25 (properties, styles, columns, view styles). Defaults (250 x 150, a tab
  stop, clWindow), the sub-classed SysListView32 window with its styles (LVS_SHAREIMAGELISTS always, the view
  style, LVS_SINGLESEL / LVS_SHOWSELALWAYS / LVS_EDITLABELS / LVS_NOCOLUMNHEADER / LVS_NOSORTHEADER from the
  negated properties, WS_EX_CLIENTEDGE for bsSingle), the recreating properties keeping the columns, the extended
  styles (LVS_EX_DOUBLEBUFFER always) and ViewStyle sent without recreating, the colours, the image lists (freed
  lists clear the reference), the columns (text, width, format; column 0 always left; a native width change read
  back; widths scaled), OnColumnClick from a click on the header (UIF-823; none without ColumnClick), streaming and
  a leak check. The user is simulated with mouse messages sent to the header window. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ImgList, CommCtrl, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    procedure ColumnClicked(Sender: TObject; Column: TListColumn);
    procedure Changed(Sender: TObject; Item: TListItem; Change: TItemChange);
    procedure Edited(Sender: TObject; Item: TListItem; var S: string);
    procedure Checked(Sender: TObject; Item: TListItem);
    procedure Selected(Sender: TObject; Item: TListItem; Selected: Boolean);
  end;

  TListForm = class(TForm)
  published
    List: TListView;
    Plain: TListView;
    procedure ListColumnClick(Sender: TObject; Column: TListColumn);
  end;

{$FORM Ctl_ListViewBasics.form}

var
  Log: TLog;

procedure TLog.ColumnClicked(Sender: TObject; Column: TListColumn);
begin
  Text := Text + 'column:' + IntToStr(Column.Index) + ';';
end;

procedure TLog.Changed(Sender: TObject; Item: TListItem; Change: TItemChange);
begin
  Text := Text + 'change;';
end;

procedure TLog.Edited(Sender: TObject; Item: TListItem; var S: string);
begin
  Text := Text + 'edited;';
end;

procedure TLog.Checked(Sender: TObject; Item: TListItem);
begin
  Text := Text + 'checked;';
end;

procedure TLog.Selected(Sender: TObject; Item: TListItem; Selected: Boolean);
begin
  Text := Text + 'selected;';
end;

procedure TListForm.ListColumnClick(Sender: TObject; Column: TListColumn);
begin
  Log.Text := Log.Text + 'streamed column:' + IntToStr(Column.Index) + ';';
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

function HasStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bits) = Bits;
end;

function HasExStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_EXSTYLE) and Bits) = Bits;
end;

function TypeBits(C: TWinControl): Integer;
begin
  Result := Integer(GetWindowLongPtrW(C.Handle, GWL_STYLE) and LVS_TYPEMASK);
end;

function LVExStyle(L: TListView): DWORD;
begin
  Result := DWORD(SendMessageW(L.Handle, LVM_GETEXTENDEDLISTVIEWSTYLE, 0, 0));
end;

function HasLVEx(L: TListView; Bits: DWORD): Boolean;
begin
  Result := (LVExStyle(L) and Bits) = Bits;
end;

function View(L: TListView): Integer;
begin
  Result := Integer(SendMessageW(L.Handle, LVM_GETVIEW, 0, 0));
end;

function Header(L: TListView): HWND;
begin
  Result := HWND(SendMessageW(L.Handle, LVM_GETHEADER, 0, 0));
end;

function HeaderCount(L: TListView): Integer;
begin
  Result := Integer(SendMessageW(Header(L), HDM_GETITEMCOUNT, 0, 0));
end;

{ 'text/width/fmt' of every native column, separated by commas }
function NativeColumns(L: TListView): string;
var
  C: TLVColumnW;
  Buf: array[0..63] of WideChar;
  I: Integer;
  S: string;
begin
  Result := '';
  I := 0;
  while True do
  begin
    FillChar(C, SizeOf(C), 0);
    C.mask := LVCF_TEXT or LVCF_WIDTH or LVCF_FMT;
    C.pszText := @Buf[0];
    C.cchTextMax := 64;
    Buf[0] := #0;
    if SendMessageW(L.Handle, LVM_GETCOLUMNW, WPARAM(I), LPARAM(@C)) = 0 then
      Break;
    S := PChar(@Buf[0]);
    if Result <> '' then
      Result := Result + ',';
    Result := Result + S + '/' + IntToStr(C.cx) + '/' + IntToStr(C.fmt and LVCFMT_JUSTIFYMASK);
    Inc(I);
  end;
end;

{ a left click in the middle of header item Index }
procedure ClickHeader(L: TListView; Index: Integer);
var
  R: TRect;
  H: HWND;
  P: LPARAM;
begin
  H := Header(L);
  SendMessageW(H, HDM_GETITEMRECT, WPARAM(Index), LPARAM(@R));
  P := MakeLParam((R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
  SendMessageW(H, WM_LBUTTONDOWN, MK_LBUTTON, P);
  SendMessageW(H, WM_LBUTTONUP, 0, P);
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Defaults;
var
  L: TListView;
  C: TListColumn;
begin
  Section('defaults');
  L := TListView.Create(nil);
  try
    CheckEqI(250, L.Width, 'Width 250');
    CheckEqI(150, L.Height, 'Height 150');
    CheckEqB(True, L.BorderStyle = bsSingle, 'BorderStyle bsSingle');
    CheckEqB(False, L.Checkboxes, 'Checkboxes');
    CheckEqB(True, L.Color = clWindow, 'Color clWindow');
    CheckEqB(True, L.ColumnClick, 'ColumnClick');
    CheckEqI(0, L.Columns.Count, 'Columns empty');
    CheckEqB(False, L.GridLines, 'GridLines');
    CheckEqB(True, L.HideSelection, 'HideSelection');
    CheckEqB(True, L.LargeImages = nil, 'LargeImages nil');
    CheckEqB(False, L.MultiSelect, 'MultiSelect');
    CheckEqB(False, L.ParentColor, 'ParentColor False');
    CheckEqB(True, L.ParentFont, 'ParentFont');
    CheckEqB(False, L.ReadOnly, 'ReadOnly');
    CheckEqB(False, L.RowSelect, 'RowSelect');
    CheckEqB(True, L.ShowColumnHeaders, 'ShowColumnHeaders');
    CheckEqB(True, L.SmallImages = nil, 'SmallImages nil');
    CheckEqI(-1, L.TabOrder, 'TabOrder -1 without a parent');
    CheckEqB(True, L.TabStop, 'TabStop');
    CheckEqB(True, L.ViewStyle = vsIcon, 'ViewStyle vsIcon');
    CheckEqB(False, csAcceptsControls in L.ControlStyle, 'does not accept children');
    Check(GetClass('TListView') <> nil, 'TListView registered');
    C := L.Columns.Add;
    CheckEqB(True, C is TListColumn, 'Add returns a TListColumn');
    CheckEqB(True, C.Alignment = taLeftJustify, 'column Alignment taLeftJustify');
    CheckEqS('', C.Caption, 'column Caption');
    CheckEqI(50, C.Width, 'column Width 50');
    CheckEqB(True, L.Columns[0] = C, 'Columns[0]');
    CheckEqB(True, L.Columns.Owner = L, 'the collection belongs to the list view');
    CheckEqS('', C.DisplayName, 'DisplayName is the caption');
    C.Caption := 'Name';
    CheckEqS('Name', C.DisplayName, 'DisplayName follows the caption');
  finally
    L.Free;
  end;
end;

procedure Run;
var
  F: TForm;
  L, L2: TListView;
  LF: TListForm;
  IL1, IL2: TImageList;
  H: HWND;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    Defaults;

    Section('window and styles');
    L := TListView.Create(F);
    L.Parent := F;
    H := L.Handle;
    CheckEqS('SysListView32', ClassOf(H), 'window class SysListView32');
    CheckEqI(LVS_ICON, TypeBits(L), 'LVS_ICON for vsIcon');
    Check(HasStyle(L, LVS_SHAREIMAGELISTS), 'LVS_SHAREIMAGELISTS');
    Check(HasStyle(L, LVS_SINGLESEL), 'LVS_SINGLESEL (not MultiSelect)');
    CheckEqB(False, HasStyle(L, LVS_SHOWSELALWAYS), 'no LVS_SHOWSELALWAYS (HideSelection)');
    Check(HasStyle(L, LVS_EDITLABELS), 'LVS_EDITLABELS (not ReadOnly)');
    CheckEqB(False, HasStyle(L, LVS_NOCOLUMNHEADER), 'no LVS_NOCOLUMNHEADER');
    CheckEqB(False, HasStyle(L, LVS_NOSORTHEADER), 'no LVS_NOSORTHEADER');
    Check(HasStyle(L, WS_TABSTOP), 'WS_TABSTOP');
    Check(HasExStyle(L, WS_EX_CLIENTEDGE), 'WS_EX_CLIENTEDGE (bsSingle)');
    Check(HasLVEx(L, LVS_EX_DOUBLEBUFFER), 'LVS_EX_DOUBLEBUFFER always');
    CheckEqB(False, HasLVEx(L, LVS_EX_GRIDLINES), 'no LVS_EX_GRIDLINES');
    CheckEqB(False, HasLVEx(L, LVS_EX_FULLROWSELECT), 'no LVS_EX_FULLROWSELECT');
    CheckEqB(False, HasLVEx(L, LVS_EX_CHECKBOXES), 'no LVS_EX_CHECKBOXES');
    CheckEqI(LV_VIEW_ICON, View(L), 'LVM_GETVIEW: icon');

    Section('extended styles without recreating');
    L.GridLines := True;
    Check(HasLVEx(L, LVS_EX_GRIDLINES), 'GridLines: LVS_EX_GRIDLINES');
    L.RowSelect := True;
    Check(HasLVEx(L, LVS_EX_FULLROWSELECT), 'RowSelect: LVS_EX_FULLROWSELECT');
    L.Checkboxes := True;
    Check(HasLVEx(L, LVS_EX_CHECKBOXES), 'Checkboxes: LVS_EX_CHECKBOXES');
    Check(HasLVEx(L, LVS_EX_DOUBLEBUFFER), 'LVS_EX_DOUBLEBUFFER kept');
    CheckEqB(True, L.Handle = H, 'not recreated');
    L.GridLines := False;
    L.RowSelect := False;
    L.Checkboxes := False;
    CheckEqI(LVS_EX_DOUBLEBUFFER, LVExStyle(L) and (LVS_EX_DOUBLEBUFFER or LVS_EX_GRIDLINES
      or LVS_EX_FULLROWSELECT or LVS_EX_CHECKBOXES), 'cleared again');

    Section('view style');
    L.ViewStyle := vsSmallIcon;
    CheckEqI(LV_VIEW_SMALLICON, View(L), 'vsSmallIcon');
    L.ViewStyle := vsList;
    CheckEqI(LV_VIEW_LIST, View(L), 'vsList');
    L.ViewStyle := vsReport;
    CheckEqI(LV_VIEW_DETAILS, View(L), 'vsReport');
    CheckEqB(True, L.Handle = H, 'ViewStyle does not recreate');
    L.ViewStyle := vsIcon;
    CheckEqI(LV_VIEW_ICON, View(L), 'vsIcon');

    Section('columns');
    L.ViewStyle := vsReport;
    L.Columns.Add.Caption := 'One';
    with L.Columns.Add do
    begin
      Caption := 'Two';
      Width := 80;
      Alignment := taCenter;
    end;
    with L.Columns.Add do
    begin
      Caption := 'Three';
      Width := 60;
      Alignment := taRightJustify;
    end;
    CheckEqS('One/50/0,Two/80/2,Three/60/1', NativeColumns(L), 'three native columns');
    CheckEqI(3, HeaderCount(L), 'three header items');
    L.Columns[0].Alignment := taRightJustify;
    CheckEqS('One/50/0,Two/80/2,Three/60/1', NativeColumns(L), 'column 0 stays left');
    CheckEqB(True, L.Columns[0].Alignment = taRightJustify, 'its Alignment is kept');
    L.Columns[1].Caption := 'Deux';
    L.Columns[2].Width := 70;
    CheckEqS('One/50/0,Deux/80/2,Three/70/1', NativeColumns(L), 'caption and width re-sent');
    L.Columns.Delete(1);
    CheckEqS('One/50/0,Three/70/1', NativeColumns(L), 'Delete: the columns follow');
    L.Columns.BeginUpdate;
    try
      L.Columns[0].Caption := 'Uno';
      CheckEqS('One/50/0,Three/70/1', NativeColumns(L), 'nothing sent inside BeginUpdate');
    finally
      L.Columns.EndUpdate;
    end;
    CheckEqS('Uno/50/0,Three/70/1', NativeColumns(L), 'sent at EndUpdate');
    SendMessageW(L.Handle, LVM_SETCOLUMNWIDTH, 1, 95);
    CheckEqI(95, L.Columns[1].Width, 'a native width change is read back');
    L.Columns.Clear;
    CheckEqS('', NativeColumns(L), 'Clear: no native columns');
    CheckEqI(0, HeaderCount(L), 'Clear: no header items');

    Section('recreating properties keep the columns');
    L.Columns.Add.Caption := 'A';
    L.Columns.Add.Caption := 'B';
    SendMessageW(L.Handle, LVM_SETCOLUMNWIDTH, 0, 66);
    L.GridLines := True;
    L.Color := clYellow;
    H := L.Handle;
    L.MultiSelect := True;
    CheckEqB(True, L.Handle <> H, 'MultiSelect recreates the window');
    CheckEqB(False, HasStyle(L, LVS_SINGLESEL), 'MultiSelect: no LVS_SINGLESEL');
    CheckEqS('A/66/0,B/50/0', NativeColumns(L), 'columns kept with the native width');
    CheckEqI(LV_VIEW_DETAILS, View(L), 'view kept');
    CheckEqI(LVS_REPORT, TypeBits(L), 'created with LVS_REPORT');
    Check(HasLVEx(L, LVS_EX_GRIDLINES or LVS_EX_DOUBLEBUFFER), 'extended styles kept');
    CheckEqI(ColorToRGB(clYellow), SendMessageW(L.Handle, LVM_GETBKCOLOR, 0, 0), 'background kept');
    H := L.Handle;
    L.HideSelection := False;
    CheckEqB(True, L.Handle <> H, 'HideSelection recreates the window');
    Check(HasStyle(L, LVS_SHOWSELALWAYS), 'LVS_SHOWSELALWAYS');
    H := L.Handle;
    L.ReadOnly := True;
    CheckEqB(True, L.Handle <> H, 'ReadOnly recreates the window');
    CheckEqB(False, HasStyle(L, LVS_EDITLABELS), 'ReadOnly: no LVS_EDITLABELS');
    H := L.Handle;
    L.ShowColumnHeaders := False;
    CheckEqB(True, L.Handle <> H, 'ShowColumnHeaders recreates the window');
    Check(HasStyle(L, LVS_NOCOLUMNHEADER), 'LVS_NOCOLUMNHEADER');
    L.ShowColumnHeaders := True;
    H := L.Handle;
    L.ColumnClick := False;
    CheckEqB(True, L.Handle <> H, 'ColumnClick recreates the window');
    Check(HasStyle(L, LVS_NOSORTHEADER), 'LVS_NOSORTHEADER');
    L.ColumnClick := True;
    H := L.Handle;
    L.BorderStyle := bsNone;
    CheckEqB(True, L.Handle <> H, 'BorderStyle recreates the window');
    CheckEqB(False, HasExStyle(L, WS_EX_CLIENTEDGE), 'bsNone: no WS_EX_CLIENTEDGE');
    L.BorderStyle := bsSingle;
    CheckEqS('A/66/0,B/50/0', NativeColumns(L), 'columns still there');

    Section('colours');
    CheckEqI(ColorToRGB(clYellow), SendMessageW(L.Handle, LVM_GETTEXTBKCOLOR, 0, 0), 'text background = Color');
    L.Color := clWindow;
    CheckEqI(ColorToRGB(clWindow), SendMessageW(L.Handle, LVM_GETBKCOLOR, 0, 0), 'clWindow background');
    CheckEqI(ColorToRGB(clWindow), SendMessageW(L.Handle, LVM_GETTEXTBKCOLOR, 0, 0), 'clWindow text background');
    CheckEqI(ColorToRGB(F.Font.Color), SendMessageW(L.Handle, LVM_GETTEXTCOLOR, 0, 0), 'text colour from the font');
    F.Font.Color := clRed;
    CheckEqI(ColorToRGB(clRed), SendMessageW(L.Handle, LVM_GETTEXTCOLOR, 0, 0), 'a parent font colour arrives');
    L.Font.Color := clBlue;
    CheckEqI(ColorToRGB(clBlue), SendMessageW(L.Handle, LVM_GETTEXTCOLOR, 0, 0), 'own font colour');
    CheckEqB(False, L.ParentFont, 'ParentFont cleared');
    TWinAccess(L).RecreateWnd;
    CheckEqI(ColorToRGB(clBlue), SendMessageW(L.Handle, LVM_GETTEXTCOLOR, 0, 0), 'text colour kept on recreate');
    F.Font.Color := clWindowText;

    Section('image lists');
    IL1 := TImageList.Create(F);
    IL2 := TImageList.Create(F);
    L.LargeImages := IL1;
    L.SmallImages := IL2;
    CheckEqB(True, HIMAGELIST(SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_NORMAL, 0)) = IL1.Handle,
      'LargeImages: LVSIL_NORMAL');
    CheckEqB(True, HIMAGELIST(SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_SMALL, 0)) = IL2.Handle,
      'SmallImages: LVSIL_SMALL');
    TWinAccess(L).RecreateWnd;
    CheckEqB(True, HIMAGELIST(SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_NORMAL, 0)) = IL1.Handle,
      'LVSIL_NORMAL after recreating');
    CheckEqB(True, HIMAGELIST(SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_SMALL, 0)) = IL2.Handle,
      'LVSIL_SMALL after recreating');
    IL2.Free;
    CheckEqB(True, L.SmallImages = nil, 'a freed list clears SmallImages');
    CheckEqI(0, SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_SMALL, 0), 'and the native list');
    L.LargeImages := nil;
    CheckEqI(0, SendMessageW(L.Handle, LVM_GETIMAGELIST, LVSIL_NORMAL, 0), 'LargeImages nil');
    IL1.Free;

    Section('scaling');
    L.Columns[0].Width := 100;
    L.ScaleBy(2, 1);
    CheckEqI(200, L.Columns[0].Width, 'column Width scaled');
    CheckEqI(100, L.Columns[1].Width, 'second column scaled');
    CheckEqS('A/200/0,B/100/0', NativeColumns(L), 'scaled widths sent');
    L.ScaleBy(1, 2);

    Section('column click (UIF-823)');
    L.OnColumnClick := Log.ColumnClicked;
    L.OnChange := Log.Changed;
    L.OnEdited := Log.Edited;
    L.OnItemChecked := Log.Checked;
    L.OnSelectItem := Log.Selected;
    Log.Text := '';
    ClickHeader(L, 1);
    CheckEqS('column:1;', Log.Text, 'a click on header item 1');
    Log.Text := '';
    ClickHeader(L, 0);
    CheckEqS('column:0;', Log.Text, 'a click on header item 0');
    L.ColumnClick := False;
    Log.Text := '';
    ClickHeader(L, 1);
    CheckEqS('', Log.Text, 'no event without ColumnClick');

    Section('streaming');
    Log.Text := '';
    LF := TListForm.Create(nil);
    try
      L2 := LF.List;
      CheckEqI(2, L2.Columns.Count, 'two columns');
      CheckEqS('Name', L2.Columns[0].Caption, 'column 0 Caption');
      CheckEqI(120, L2.Columns[0].Width, 'column 0 Width');
      CheckEqB(True, L2.Columns[1].Alignment = taRightJustify, 'column 1 Alignment');
      CheckEqI(50, L2.Columns[1].Width, 'column 1 default Width');
      CheckEqB(True, L2.ViewStyle = vsReport, 'ViewStyle vsReport');
      CheckEqB(True, L2.Checkboxes and L2.GridLines and L2.RowSelect and L2.MultiSelect and L2.ReadOnly,
        'Checkboxes, GridLines, RowSelect, MultiSelect, ReadOnly');
      CheckEqB(False, L2.HideSelection, 'HideSelection False');
      CheckEqB(False, L2.ColumnClick, 'ColumnClick False');
      CheckEqB(True, L2.Color = clInfoBk, 'Color clInfoBk');
      CheckEqS('Name/120/0,Size/50/1', NativeColumns(L2), 'native columns');
      CheckEqI(LV_VIEW_DETAILS, View(L2), 'native view');
      Check(HasLVEx(L2, LVS_EX_CHECKBOXES or LVS_EX_GRIDLINES or LVS_EX_FULLROWSELECT or LVS_EX_DOUBLEBUFFER),
        'native extended styles');
      Check(HasStyle(L2, LVS_SHOWSELALWAYS or LVS_NOSORTHEADER), 'native styles');
      CheckEqB(False, HasStyle(L2, LVS_SINGLESEL or LVS_EDITLABELS), 'no LVS_SINGLESEL, no LVS_EDITLABELS');
      CheckEqI(ColorToRGB(clInfoBk), SendMessageW(L2.Handle, LVM_GETBKCOLOR, 0, 0), 'native background');
      Check(Assigned(L2.OnColumnClick), 'OnColumnClick bound');
      CheckEqB(True, LF.Plain.BorderStyle = bsNone, 'BorderStyle bsNone');
      CheckEqB(False, LF.Plain.ShowColumnHeaders, 'ShowColumnHeaders False');
      CheckEqB(False, LF.Plain.TabStop, 'TabStop False');
      CheckEqI(LV_VIEW_LIST, View(LF.Plain), 'vsList');
      CheckEqI(LVS_LIST, TypeBits(LF.Plain), 'created with LVS_LIST');
      Check(HasStyle(LF.Plain, LVS_NOCOLUMNHEADER), 'LVS_NOCOLUMNHEADER');
      CheckEqB(False, HasExStyle(LF.Plain, WS_EX_CLIENTEDGE), 'no WS_EX_CLIENTEDGE');
      CheckEqS('', Log.Text, 'no events while loading');
    finally
      LF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      L2 := TListView.Create(nil);
      L2.ViewStyle := vsReport;
      L2.Columns.Add.Caption := 'x';
      L2.Columns.Add.Caption := 'y';
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
