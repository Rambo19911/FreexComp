// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1505, UIF-1506, UIF-1507, UIF-1508, UIF-1509, UIF-1510, UIF-1511, UIF-1512, UIF-1516
// phase: 8
// needs: desktop
program Ctl_TabControl;
{ U-173, phase8/tab-control.md section 5: TTabControl, the plain tab strip. Defaults and the published members
  (UIF-1505); Tabs held by the control and mirrored to the native items with and without a handle and through a
  recreation of the window (UIF-1506); every TabIndex rule in code: ignored out-of-range values, the auto-select of
  the first tab, edits keeping the same tab selected, no events (UIF-1507); loading: TabIndex read before Tabs, no
  auto-select, an out-of-range stored value becomes -1 (UIF-1508); user selection by a click with OnChanging (veto)
  and OnChange (UIF-1509, the card's acceptance check); arrow keys, Ctrl+Tab / Ctrl+Shift+Tab with the focus inside
  the control or on the strip and accelerators (UIF-1510); a container whose align rect is DisplayRect, children
  never switched by the selection (UIF-1511); the default image index i and OnGetImageIndex, also on a page control
  (UIF-1512); the .form property lines and their defaults (UIF-1516).
  The Ctrl / Shift state is set with KeyStateKit (whole thread key state; no real input). }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ImgList,
  CommCtrl, ComCtrls, TestKit, KeyStateKit;

type
  TFocusBox = class(TCustomControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    Allow: Boolean;
    procedure Changing(Sender: TObject; var AllowChange: Boolean);
    procedure Changed(Sender: TObject);
    procedure Reverse(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer);
  end;

  TTabsForm = class(TForm)
  published
    Strip: TTabControl;
    Inside: TPanel;
    procedure StripChange(Sender: TObject);
    procedure StripGetImageIndex(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer);
  end;

{$FORM Ctl_TabControl.form}

const
  LoadText =
    'object Root: TForm' + #10 +
    '  object Wrong: TTabControl' + #10 +
    '    TabIndex = 5' + #10 +
    '    Tabs = (' + #10 +
    '      ''A''' + #10 +
    '      ''B''' + #10 +
    '    )' + #10 +
    '  end' + #10 +
    '  object Plain: TTabControl' + #10 +
    '    Tabs = (' + #10 +
    '      ''A''' + #10 +
    '    )' + #10 +
    '  end' + #10 +
    '  object Second: TTabControl' + #10 +
    '    TabIndex = 1' + #10 +
    '    Tabs = (' + #10 +
    '      ''A''' + #10 +
    '      ''B''' + #10 +
    '    )' + #10 +
    '  end' + #10 +
    'end' + #10;

var
  Log: TLog;

constructor TFocusBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  TabStop := True;
  SetBounds(0, 0, 20, 20);
end;

procedure TLog.Changing(Sender: TObject; var AllowChange: Boolean);
begin
  Text := Text + 'changing:' + IntToStr(TTabControl(Sender).TabIndex) + ';';
  AllowChange := Allow;
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change:' + IntToStr(TTabControl(Sender).TabIndex) + ';';
end;

{ tab i shows image 3 - (the default index); the default proves the index passed in }
procedure TLog.Reverse(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer);
begin
  ImageIndex := 3 - ImageIndex;
end;

procedure TTabsForm.StripChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed change;';
end;

procedure TTabsForm.StripGetImageIndex(Sender: TObject; TabIndex: Integer; var ImageIndex: Integer);
begin
  ImageIndex := 10 + TabIndex;
end;

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function RectBounds(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top]);
end;

function TabCount(T: TCustomTabControl): Integer;
begin
  Result := Integer(SendMessageW(T.Handle, TCM_GETITEMCOUNT, 0, 0));
end;

function CurSel(T: TCustomTabControl): Integer;
begin
  Result := Integer(SendMessageW(T.Handle, TCM_GETCURSEL, 0, 0));
end;

function TabText(T: TCustomTabControl; Index: Integer): string;
var
  Item: TTCItemW;
  Buf: array[0..127] of WideChar;
begin
  FillChar(Item, SizeOf(Item), 0);
  FillChar(Buf, SizeOf(Buf), 0);
  Item.mask := TCIF_TEXT;
  Item.pszText := @Buf[0];
  Item.cchTextMax := 128;
  if SendMessageW(T.Handle, TCM_GETITEMW, WPARAM(Index), LPARAM(@Item)) = 0 then
    Result := '<none>'
  else
    Result := PChar(@Buf[0]);
end;

{ the texts of all native tabs, separated by '|' }
function NativeTabs(T: TCustomTabControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to TabCount(T) - 1 do
  begin
    if I > 0 then
      Result := Result + '|';
    Result := Result + TabText(T, I);
  end;
end;

{ the strings of Tabs, separated by '|' }
function TabList(T: TTabControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to T.Tabs.Count - 1 do
  begin
    if I > 0 then
      Result := Result + '|';
    Result := Result + T.Tabs[I];
  end;
end;

function TabImage(T: TCustomTabControl; Index: Integer): Integer;
var
  Item: TTCItemW;
begin
  FillChar(Item, SizeOf(Item), 0);
  Item.mask := TCIF_IMAGE;
  SendMessageW(T.Handle, TCM_GETITEMW, WPARAM(Index), LPARAM(@Item));
  Result := Item.iImage;
end;

{ the images of all native tabs, separated by ',' }
function NativeImages(T: TCustomTabControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to TabCount(T) - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(TabImage(T, I));
  end;
end;

procedure Press(C: TWinControl; Key: Word; Ctrl, Shift: Boolean);
begin
  Log.Text := '';
  SetKeyState(Shift, Ctrl);
  PostKeyMessage(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
  SetKeyState(False, False);
  PostKeyMessage(C.Handle, WM_KEYUP, Key, LPARAM($C0000001));
  Application.ProcessMessages;
end;

{ the card's click: WM_LBUTTONDOWN / WM_LBUTTONUP at the centre of TabRect(Index) }
procedure ClickTab(T: TCustomTabControl; Index: Integer);
var
  R: TRect;
  P: LPARAM;
begin
  R := T.TabRect(Index);
  P := MakeLParam((R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
  Log.Text := '';
  SendMessageW(T.Handle, WM_LBUTTONDOWN, MK_LBUTTON, P);
  SendMessageW(T.Handle, WM_LBUTTONUP, 0, P);
  Application.ProcessMessages;
end;

function NewStrip(AOwner: TComponent): TTabControl;
begin
  Result := TTabControl.Create(AOwner);
  Result.OnChanging := Log.Changing;
  Result.OnChange := Log.Changed;
end;

procedure SetTabs(T: TTabControl; const Items: array of string);
var
  L: TStringList;
  I: Integer;
begin
  L := TStringList.Create;
  try
    for I := 0 to High(Items) do
      L.Add(Items[I]);
    T.Tabs := L;
  finally
    L.Free;
  end;
end;

procedure CheckPublished(const Name: string);
begin
  CheckEqB(True, IsPublishedProp(TTabControl, Name), Name + ' published');
end;

procedure CheckDefault(const Name: string; Expected: Int64);
var
  P: PPropInfo;
begin
  P := GetPropInfo(TTabControl, Name);
  if P = nil then
    Check(False, Name + ' has property information')
  else
    CheckEqI(Expected, P^.Default, Name + ' default (not written to .form)');
end;

procedure Defaults;
var
  T: TTabControl;
begin
  Section('defaults (UIF-1505)');
  T := TTabControl.Create(nil);
  try
    CheckEqI(289, T.Width, 'Width 289');
    CheckEqI(193, T.Height, 'Height 193');
    CheckEqI(0, T.Tabs.Count, 'Tabs empty');
    CheckEqI(-1, T.TabIndex, 'TabIndex -1');
    CheckEqB(True, T.TabStop, 'TabStop');
    CheckEqI(-1, T.TabOrder, 'TabOrder -1');
    CheckEqB(False, T.HotTrack, 'HotTrack');
    CheckEqB(True, T.Images = nil, 'Images nil');
    CheckEqB(False, T.MultiLine, 'MultiLine');
    CheckEqB(False, T.RaggedRight, 'RaggedRight');
    CheckEqB(False, T.ScrollOpposite, 'ScrollOpposite');
    CheckEqB(True, T.Style = tsTabs, 'Style tsTabs');
    CheckEqI(0, T.TabHeight, 'TabHeight 0');
    CheckEqB(True, T.TabPosition = tpTop, 'TabPosition tpTop');
    CheckEqI(0, T.TabWidth, 'TabWidth 0');
    CheckEqB(True, T.ParentFont, 'ParentFont');
    CheckEqB(True, csAcceptsControls in T.ControlStyle, 'a container');
    CheckEqB(True, T is TCustomTabControl, 'a TCustomTabControl');
    CheckEqB(False, T.HandleAllocated, 'no handle');
    CheckEqS('0,0,0,0', RectBounds(T.TabRect(0)), 'TabRect without a handle');
    CheckEqI(-1, T.IndexOfTabAt(10, 10), 'IndexOfTabAt without a handle');
  finally
    T.Free;
  end;
  CheckPublished('Align');
  CheckPublished('Anchors');
  CheckPublished('Constraints');
  CheckPublished('Enabled');
  CheckPublished('Font');
  CheckPublished('Hint');
  CheckPublished('HotTrack');
  CheckPublished('Images');
  CheckPublished('MultiLine');
  CheckPublished('ParentFont');
  CheckPublished('ParentShowHint');
  CheckPublished('PopupMenu');
  CheckPublished('RaggedRight');
  CheckPublished('ScrollOpposite');
  CheckPublished('ShowHint');
  CheckPublished('Style');
  CheckPublished('TabHeight');
  CheckPublished('TabIndex');
  CheckPublished('TabOrder');
  CheckPublished('TabPosition');
  CheckPublished('Tabs');
  CheckPublished('TabStop');
  CheckPublished('TabWidth');
  CheckPublished('Visible');
  CheckPublished('OnChange');
  CheckPublished('OnChanging');
  CheckPublished('OnClick');
  CheckPublished('OnContextPopup');
  CheckPublished('OnEnter');
  CheckPublished('OnExit');
  CheckPublished('OnGetImageIndex');
  CheckPublished('OnKeyDown');
  CheckPublished('OnMouseDown');
  CheckPublished('OnResize');
  CheckEqB(False, IsPublishedProp(TTabControl, 'ActivePage'), 'no ActivePage');
  CheckEqB(False, IsPublishedProp(TPageControl, 'TabIndex'), 'TabIndex not published on the page control');

  Section('.form defaults (UIF-1516)');
  CheckDefault('MultiLine', 0);
  CheckDefault('RaggedRight', 0);
  CheckDefault('ScrollOpposite', 0);
  CheckDefault('Style', Ord(tsTabs));
  CheckDefault('TabIndex', -1);
  CheckDefault('TabPosition', Ord(tpTop));
  CheckDefault('TabStop', 1);
  CheckEqB(True, GetPropInfo(TTabControl, 'Tabs')^.Default = NoDefault, 'Tabs: no default (written when not empty)');
end;

procedure IndexRules;
var
  T: TTabControl;
  L: TStringList;
begin
  Section('TabIndex in code without a handle (UIF-1507)');
  Log.Text := '';
  Log.Allow := True;
  T := NewStrip(nil);
  L := TStringList.Create;
  try
    T.TabIndex := 0;
    CheckEqI(-1, T.TabIndex, 'empty: 0 ignored');
    T.Tabs.Add('A');
    CheckEqI(0, T.TabIndex, 'the first tab of an empty control is selected');
    T.Tabs.Add('B');
    T.Tabs.Add('C');
    CheckEqI(0, T.TabIndex, 'later tabs keep the selection');
    T.TabIndex := 2;
    CheckEqI(2, T.TabIndex, 'TabIndex 2');
    T.TabIndex := 3;
    CheckEqI(2, T.TabIndex, '3 = Count ignored');
    T.TabIndex := -2;
    CheckEqI(2, T.TabIndex, '-2 ignored');
    T.TabIndex := -1;
    CheckEqI(-1, T.TabIndex, '-1 deselects');
    T.TabIndex := 1;
    CheckEqI(1, T.TabIndex, 'TabIndex 1');

    Section('tab edits keep the selected tab (UIF-1507)');
    T.Tabs.Insert(1, 'X');
    CheckEqS('A|X|B|C', TabList(T), 'insert at 1');
    CheckEqI(2, T.TabIndex, 'insert at TabIndex: + 1');
    T.Tabs.Insert(3, 'Y');
    CheckEqI(2, T.TabIndex, 'insert after TabIndex: unchanged');
    T.Tabs.Delete(0);
    CheckEqS('X|B|Y|C', TabList(T), 'delete 0');
    CheckEqI(1, T.TabIndex, 'delete before TabIndex: - 1');
    T.Tabs.Delete(2);
    CheckEqI(1, T.TabIndex, 'delete after TabIndex: unchanged');
    T.Tabs.Delete(1);
    CheckEqS('X|C', TabList(T), 'selected tab deleted');
    CheckEqI(1, T.TabIndex, 'deleting the selected tab: the tab now at that index');
    T.Tabs.Delete(1);
    CheckEqI(0, T.TabIndex, 'deleting the selected last tab: the new last one');
    T.Tabs.Delete(0);
    CheckEqI(-1, T.TabIndex, 'no tab left: -1');
    T.Tabs.Add('A');
    T.Tabs.Add('B');
    T.Tabs.Add('C');
    T.Tabs.Add('D');
    CheckEqI(0, T.TabIndex, 'auto-select again');
    T.TabIndex := 1;
    T.Tabs.Move(1, 3);
    CheckEqS('A|C|D|B', TabList(T), 'Move(1, 3)');
    CheckEqI(3, T.TabIndex, 'Move of the selected tab: followed');
    T.Tabs.Move(0, 3);
    CheckEqS('C|D|B|A', TabList(T), 'Move(0, 3)');
    CheckEqI(2, T.TabIndex, 'Move across the selected tab (down): followed');
    T.Tabs.Move(3, 0);
    CheckEqS('A|C|D|B', TabList(T), 'Move(3, 0)');
    CheckEqI(3, T.TabIndex, 'Move across the selected tab (up): followed');
    T.Tabs.Exchange(3, 0);
    CheckEqS('B|C|D|A', TabList(T), 'Exchange(3, 0)');
    CheckEqI(0, T.TabIndex, 'Exchange of the selected tab: followed');
    T.Tabs.Exchange(1, 2);
    CheckEqI(0, T.TabIndex, 'Exchange of other tabs: unchanged');
    T.Tabs[0] := 'BB';
    CheckEqS('BB|D|C|A', TabList(T), 'Put');
    CheckEqI(0, T.TabIndex, 'Put: unchanged');
    T.Tabs.Objects[1] := L;
    CheckEqB(True, T.Tabs.Objects[1] = L, 'objects held');
    T.Tabs.Clear;
    CheckEqI(-1, T.TabIndex, 'Clear: -1');
    L.Add('P');
    L.Add('Q');
    T.Tabs := L;
    CheckEqS('P|Q', TabList(T), 'Tabs := copies the strings');
    CheckEqB(True, T.Tabs <> L, 'Tabs := does not take the list');
    CheckEqI(0, T.TabIndex, 'Assign of a non-empty list: 0');
    T.TabIndex := 1;
    SetTabs(T, ['R', 'S', 'T']);
    CheckEqI(0, T.TabIndex, 'Assign replaces: 0');
    T.Tabs.Text := 'a' + sLineBreak + 'b';
    CheckEqS('a|b', TabList(T), 'Text');
    CheckEqI(0, T.TabIndex, 'Text: 0');
    CheckEqS('', Log.Text, 'no OnChanging / OnChange in code');
  finally
    L.Free;
    T.Free;
  end;
end;

procedure Children;
var
  T: TTabControl;
  C: TPanel;
  B: TFocusBox;
begin
  Section('children (UIF-1511, corpus E47)');
  T := TTabControl.Create(nil);
  try
    T.Font.Height := -12;
    T.ParentFont := False;
    SetTabs(T, ['A', 'B', 'C']);
    C := TPanel.Create(T);
    C.Align := alClient;
    C.Parent := T;
    CheckEqS('4,25,281,164', RectBounds(T.DisplayRect), 'DisplayRect at 96 DPI');
    CheckEqS('4,25,281,164', BoundsText(C), 'alClient child fills the display rectangle');
    B := TFocusBox.Create(T);
    B.SetBounds(10, 30, 20, 20);
    B.Anchors := [akRight, akBottom];
    B.Parent := T;
    T.TabPosition := tpBottom;
    CheckEqS('4,4,281,164', BoundsText(C), 'alClient child follows TabPosition');
    CheckEqS('10,30,20,20', BoundsText(B), 'anchored child: anchors follow the client size (UIF-231), not the strip');
    T.SetBounds(0, 0, 389, 293);
    CheckEqS('110,130,20,20', BoundsText(B), 'anchored child follows a resize');
    CheckEqS('4,4,381,264', BoundsText(C), 'alClient child after the resize');
    T.TabIndex := 2;
    CheckEqB(True, C.Visible and B.Visible, 'children not switched by the selection');
  finally
    T.Free;
  end;
end;

procedure Run;
var
  F: TForm;
  T: TTabControl;
  PC: TPageControl;
  S: TTabSheet;
  Box, Other: TFocusBox;
  IL: TImageList;
  Bmp: TBitmap;
  H: HWND;
  SF: TTabsForm;
  Root: TForm;
  W: TTabControl;
  I, P, Th: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 600;
    F.ClientHeight := 400;
    Defaults;
    IndexRules;
    Children;

    Section('native items (UIF-1506)');
    Log.Text := '';
    T := NewStrip(F);
    T.SetBounds(10, 10, 289, 193);
    SetTabs(T, ['A', 'B', 'C']);
    T.TabIndex := 1;
    T.Parent := F;
    H := T.Handle;
    CheckEqI(3, TabCount(T), 'CreateWnd inserts every tab');
    CheckEqS('A|B|C', NativeTabs(T), 'texts');
    CheckEqI(1, CurSel(T), 'CreateWnd selects TabIndex');
    T.Tabs.Insert(0, 'X');
    CheckEqS('X|A|B|C', NativeTabs(T), 'Insert -> TCM_INSERTITEMW');
    CheckEqI(2, CurSel(T), 'selection restored after Insert');
    T.Tabs.Delete(1);
    CheckEqS('X|B|C', NativeTabs(T), 'Delete -> TCM_DELETEITEM');
    CheckEqI(1, CurSel(T), 'selection restored after Delete');
    T.Tabs[2] := 'CC';
    CheckEqS('X|B|CC', NativeTabs(T), 'Put -> TCM_SETITEMW');
    T.Tabs.Move(0, 2);
    CheckEqS('B|CC|X', NativeTabs(T), 'Move');
    CheckEqI(0, CurSel(T), 'selection follows Move');
    T.Tabs.Exchange(0, 2);
    CheckEqS('X|CC|B', NativeTabs(T), 'Exchange');
    CheckEqI(2, CurSel(T), 'selection follows Exchange');
    T.Tabs.BeginUpdate;
    T.Tabs.Add('D');
    T.Tabs.Delete(0);
    T.Tabs[0] := 'C';
    CheckEqS('X|CC|B', NativeTabs(T), 'BeginUpdate defers the native items');
    T.Tabs.EndUpdate;
    CheckEqS('C|B|D', NativeTabs(T), 'EndUpdate rebuilds them');
    CheckEqI(1, T.TabIndex, 'TabIndex followed the selected tab during the update');
    CheckEqI(1, CurSel(T), 'selection restored after the rebuild');
    T.Tabs.Clear;
    CheckEqI(0, TabCount(T), 'Clear -> TCM_DELETEALLITEMS');
    CheckEqI(-1, CurSel(T), 'nothing selected');
    T.Tabs.Add('A');
    CheckEqI(0, CurSel(T), 'the first tab of an empty control is selected natively too');
    T.Tabs.Add('B');
    T.Tabs.Add('C');
    CheckEqB(True, T.Handle = H, 'tab edits keep the window');
    CheckEqS('', Log.Text, 'no events from tab edits');

    Section('recreation (UIF-1506)');
    T.TabIndex := 2;
    T.Tabs.Objects[1] := F;
    T.Style := tsButtons;
    CheckEqB(True, T.Handle <> H, 'Style recreates the window');
    CheckEqB(True, (GetWindowLongPtrW(T.Handle, GWL_STYLE) and TCS_BUTTONS) <> 0, 'TCS_BUTTONS');
    CheckEqS('A|B|C', NativeTabs(T), 'tabs kept through RecreateWnd');
    CheckEqI(2, CurSel(T), 'selection kept through RecreateWnd');
    CheckEqI(2, T.TabIndex, 'TabIndex kept');
    CheckEqB(True, T.Tabs.Objects[1] = F, 'objects kept');
    T.Style := tsTabs;
    CheckEqS('A|B|C', NativeTabs(T), 'tabs kept through a second recreation');

    Section('TabIndex in code (UIF-1507)');
    Log.Text := '';
    T.TabIndex := 0;
    CheckEqI(0, CurSel(T), 'TCM_SETCURSEL 0');
    T.TabIndex := 1;
    CheckEqI(1, CurSel(T), 'TCM_SETCURSEL 1');
    T.TabIndex := 7;
    CheckEqI(1, CurSel(T), 'ignored value: selection unchanged');
    T.TabIndex := -1;
    CheckEqI(-1, CurSel(T), '-1: no tab selected');
    CheckEqS('', Log.Text, 'setting TabIndex in code fires nothing');

    Section('user selection (UIF-1509, card acceptance)');
    T.TabIndex := 0;
    ClickTab(T, 1);
    CheckEqS('changing:0;change:1;', Log.Text, 'click on the second tab: OnChanging, then OnChange');
    CheckEqI(1, T.TabIndex, 'TabIndex 1');
    CheckEqI(1, CurSel(T), 'TCM_GETCURSEL 1');
    T.TabIndex := 0;
    Log.Allow := False;
    ClickTab(T, 1);
    CheckEqS('changing:0;', Log.Text, 'vetoed: no OnChange');
    CheckEqI(0, T.TabIndex, 'vetoed: TabIndex 0');
    CheckEqI(0, CurSel(T), 'vetoed: TCM_GETCURSEL 0');
    Log.Allow := True;

    Section('keys (UIF-1510)');
    Box := TFocusBox.Create(F);
    Box.SetBounds(20, 40, 20, 20);
    Box.Parent := T;
    Other := TFocusBox.Create(F);
    Other.SetBounds(400, 300, 20, 20);
    Other.Parent := F;
    F.Show;
    T.SetFocus;
    Press(T, VK_RIGHT, False, False);
    CheckEqS('changing:0;change:1;', Log.Text, 'Right on the strip (native)');
    Press(T, VK_LEFT, False, False);
    CheckEqS('changing:1;change:0;', Log.Text, 'Left on the strip (native)');
    Box.SetFocus;
    Press(Box, VK_TAB, True, False);
    CheckEqS('changing:0;change:1;', Log.Text, 'Ctrl+Tab with the focus on a child: next tab');
    CheckEqB(True, F.ActiveControl = Box, 'the focus stays on the child');
    T.SetFocus;
    Press(T, VK_TAB, True, False);
    CheckEqS('changing:1;change:2;', Log.Text, 'Ctrl+Tab on the strip');
    Press(T, VK_TAB, True, False);
    CheckEqS('changing:2;change:0;', Log.Text, 'Ctrl+Tab wraps to the first tab');
    Press(T, VK_TAB, True, True);
    CheckEqS('changing:0;change:2;', Log.Text, 'Ctrl+Shift+Tab: previous tab (wrapping)');
    CheckEqI(2, CurSel(T), 'native selection follows');
    Log.Allow := False;
    Press(T, VK_TAB, True, False);
    CheckEqS('changing:2;', Log.Text, 'Ctrl+Tab vetoed');
    CheckEqI(2, T.TabIndex, 'tab unchanged');
    Log.Allow := True;
    Other.SetFocus;
    Press(Other, VK_TAB, True, False);
    CheckEqS('', Log.Text, 'Ctrl+Tab outside the tab control: no change');
    T.Tabs[1] := '&Beta';
    Log.Text := '';
    CheckEqI(0, T.Perform(CM_DIALOGCHAR, Ord('x'), 0), 'other character: not consumed');
    CheckEqI(1, T.Perform(CM_DIALOGCHAR, Ord('b'), 0), 'accelerator consumed');
    CheckEqS('changing:2;change:1;', Log.Text, 'accelerator selects the tab');
    Log.Text := '';
    CheckEqI(1, T.Perform(CM_DIALOGCHAR, Ord('b'), 0), 'accelerator of the selected tab consumed');
    CheckEqS('', Log.Text, 'no events for the selected tab');
    T.TabIndex := 0;
    Log.Allow := False;
    Log.Text := '';
    CheckEqI(1, T.Perform(CM_DIALOGCHAR, Ord('b'), 0), 'vetoed accelerator consumed');
    CheckEqS('changing:0;', Log.Text, 'accelerator vetoed');
    CheckEqI(0, T.TabIndex, 'tab unchanged');
    Log.Allow := True;
    Box.Free;
    Other.Free;

    Section('images (UIF-1512)');
    IL := TImageList.Create(F);
    Bmp := TBitmap.Create;
    try
      Bmp.SetSize(16, 16);
      for I := 1 to 4 do
        IL.Add(Bmp, nil);
    finally
      Bmp.Free;
    end;
    CheckEqS('-1,-1,-1', NativeImages(T), 'without Images: -1');
    T.Images := IL;
    CheckEqB(True, HIMAGELIST(SendMessageW(T.Handle, TCM_GETIMAGELIST, 0, 0)) = IL.Handle, 'TCM_SETIMAGELIST');
    CheckEqS('0,1,2', NativeImages(T), 'default: tab i shows image i');
    T.Tabs.Add('D');
    CheckEqS('0,1,2,3', NativeImages(T), 'an inserted tab gets its image');
    T.OnGetImageIndex := Log.Reverse;
    T.Tabs.Insert(0, 'E');
    CheckEqS('3,0,1,2,3', NativeImages(T), 'OnGetImageIndex for the inserted tab (default 0 -> 3)');
    T.Tabs.Delete(0);
    IL.Width := 20;
    CheckEqS('3,2,1,0', NativeImages(T), 'the image-list link re-queries every tab');
    T.Images := nil;
    CheckEqS('-1,-1,-1,-1', NativeImages(T), 'Images nil: -1');
    T.Images := IL;
    CheckEqS('3,2,1,0', NativeImages(T), 'Images assigned: re-queried');
    T.Style := tsFlatButtons;
    CheckEqS('3,2,1,0', NativeImages(T), 'images after RecreateWnd');
    T.Style := tsTabs;
    T.OnGetImageIndex := nil;
    T.Tabs.Delete(3);
    PC := TPageControl.Create(F);
    PC.Images := IL;
    PC.OnGetImageIndex := Log.Reverse;
    PC.Parent := F;
    S := TTabSheet.Create(F);
    S.Caption := 'P0';
    S.PageControl := PC;
    S := TTabSheet.Create(F);
    S.Caption := 'P1';
    S.ImageIndex := 1;
    S.PageControl := PC;
    CheckEqS('4,2', NativeImages(PC), 'page control: OnGetImageIndex gets the page''s ImageIndex');
    PC.Free;
    T.Images := nil;
    IL.Free;

    Section('display rectangle at the form''s DPI (UIF-1511)');
    Box := TFocusBox.Create(F);
    Box.Align := alClient;
    Box.Parent := T;
    T.Font.Height := -12;
    T.ParentFont := False;
    P := LayoutMulDiv(4, F.PixelsPerInch, 96);
    Th := 12 + LayoutMulDiv(9, F.PixelsPerInch, 96);
    CheckEqS(Format('%d,%d,%d,%d', [P, Th + P, 289 - 2 * P, 193 - P - (Th + P)]), BoundsText(Box),
      'alClient child = DisplayRect');
    CheckEqS(RectBounds(T.DisplayRect), BoundsText(Box), 'the child equals DisplayRect');
    ClickTab(T, 2);
    CheckEqB(True, Box.Visible, 'a user selection does not hide children');
    T.Free;

    Section('streaming (UIF-1508, UIF-1516)');
    Log.Text := '';
    SF := TTabsForm.Create(nil);
    try
      T := SF.Strip;
      CheckEqB(True, T.MultiLine, 'MultiLine = True');
      CheckEqB(True, T.RaggedRight, 'RaggedRight = True');
      CheckEqB(True, T.ScrollOpposite, 'ScrollOpposite = True');
      CheckEqB(True, T.Style = tsFlatButtons, 'Style = tsFlatButtons');
      CheckEqB(True, T.TabPosition = tpBottom, 'TabPosition = tpBottom');
      CheckEqS('One|Two|Three', TabList(T), 'Tabs = ( ... )');
      CheckEqI(2, T.TabIndex, 'TabIndex = 2 read before Tabs');
      Check(Assigned(T.OnChange), 'OnChange bound');
      Check(Assigned(T.OnGetImageIndex), 'OnGetImageIndex bound');
      CheckEqS('', Log.Text, 'no events while loading');
      CheckEqS('One|Two|Three', NativeTabs(T), 'native tabs');
      CheckEqI(2, CurSel(T), 'native selection');
      CheckEqS(RectBounds(T.DisplayRect), BoundsText(SF.Inside), 'alClient child of the loaded control');
    finally
      SF.Free;
    end;
    Root := TForm.CreateNew(nil);
    try
      ReadComponentFromText(LoadText, Root, 'Ctl_TabControl');
      W := Root.FindComponent('Wrong') as TTabControl;
      CheckEqS('A|B', TabList(W), 'tabs read');
      CheckEqI(-1, W.TabIndex, 'a stored TabIndex out of range becomes -1 in Loaded');
      W := Root.FindComponent('Plain') as TTabControl;
      CheckEqI(-1, W.TabIndex, 'no auto-select while loading');
      W.Parent := Root;
      CheckEqI(-1, CurSel(W), 'no native selection either');
      W := Root.FindComponent('Second') as TTabControl;
      CheckEqI(1, W.TabIndex, 'TabIndex before Tabs is kept');
      W.Tabs.Add('C');
      CheckEqI(1, W.TabIndex, 'after loading the rules apply again');
    finally
      Root.Free;
    end;
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
