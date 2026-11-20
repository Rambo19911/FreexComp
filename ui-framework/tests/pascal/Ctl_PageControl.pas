// covers: UIF-219, UIF-770, UIF-771, UIF-772, UIF-773, UIF-774, UIF-775, UIF-776, UIF-777
// needs: desktop
program Ctl_PageControl;
{ U-082, controls-standard.md section 20: TPageControl / TTabSheet defaults; pages = the TTabSheet children in order,
  a tab sheet only on a page control (UIF-770); exactly one active page, the others invisible (UIF-771); the page
  rectangle of layout.md UIF-219 (E27, TabHeight, font change, clamping, DPI constants, scaling of TabHeight and
  TabWidth) applied to every page (UIF-772); the native SysTabControl32 window (styles, HotTrack and TabWidth
  recreate the window keeping the state, tab items in sync with TabVisible pages: text, image, order, UIF-776);
  switching in code without OnChanging/OnChange (UIF-774) and by the user with them, including the veto (a mouse
  click on a tab, UIF-773); focus moving from the old page to the page control; keys: Left/Right on the tab strip,
  Ctrl+Tab / Ctrl+Shift+Tab inside the page control through the form's dialog-key handling, accelerators (UIF-775,
  UIF-776); destroying the active page (UIF-777); MultiLine / TabPosition not supported in Phase 4; streaming with
  ActivePage stored and not stored; 50 instances with handles leak no GUI objects.
  The Ctrl state is set with SetKeyboardState (thread key state; no real input). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ImgList, CommCtrl,
  ComCtrls, UIConsts, TestKit;

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
    procedure Shown(Sender: TObject);
    procedure Hidden(Sender: TObject);
  end;

  TPagesForm = class(TForm)
  published
    Book: TPageControl;
    First: TTabSheet;
    Second: TTabSheet;
    Third: TTabSheet;
    Inside: TPanel;
    Book2: TPageControl;
    Hidden: TTabSheet;
    Shown: TTabSheet;
    procedure BookChange(Sender: TObject);
  end;

{$FORM Ctl_PageControl.form}

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
  Text := Text + 'changing;';
  AllowChange := Allow;
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change:' + TPageControl(Sender).ActivePage.Name + ';';
end;

procedure TLog.Shown(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + '.show;';
end;

procedure TLog.Hidden(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + '.hide;';
end;

procedure TPagesForm.BookChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed change;';
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

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function TabCount(PC: TPageControl): Integer;
begin
  Result := Integer(SendMessageW(PC.Handle, TCM_GETITEMCOUNT, 0, 0));
end;

function CurSel(PC: TPageControl): Integer;
begin
  Result := Integer(SendMessageW(PC.Handle, TCM_GETCURSEL, 0, 0));
end;

function TabText(PC: TPageControl; Index: Integer): string;
var
  Item: TTCItemW;
  Buf: array[0..127] of WideChar;
begin
  FillChar(Item, SizeOf(Item), 0);
  FillChar(Buf, SizeOf(Buf), 0);
  Item.mask := TCIF_TEXT;
  Item.pszText := @Buf[0];
  Item.cchTextMax := 128;
  if SendMessageW(PC.Handle, TCM_GETITEMW, WPARAM(Index), LPARAM(@Item)) = 0 then
    Result := '<none>'
  else
    Result := PChar(@Buf[0]);
end;

{ the texts of all tabs, separated by '|' }
function Tabs(PC: TPageControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to TabCount(PC) - 1 do
  begin
    if I > 0 then
      Result := Result + '|';
    Result := Result + TabText(PC, I);
  end;
end;

function TabImage(PC: TPageControl; Index: Integer): Integer;
var
  Item: TTCItemW;
begin
  FillChar(Item, SizeOf(Item), 0);
  Item.mask := TCIF_IMAGE;
  SendMessageW(PC.Handle, TCM_GETITEMW, WPARAM(Index), LPARAM(@Item));
  Result := Item.iImage;
end;

function ActiveName(PC: TPageControl): string;
begin
  if PC.ActivePage = nil then
    Result := 'nil'
  else
    Result := PC.ActivePage.Name;
end;

{ the visible pages, by name }
function VisiblePages(PC: TPageControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to PC.PageCount - 1 do
    if PC.Pages[I].Visible then
      Result := Result + PC.Pages[I].Name + ';';
end;

procedure SetCtrl(Down: Boolean);
var
  State: TKeyboardState;
begin
  GetKeyboardState(State);
  if Down then
    State[VK_CONTROL] := $80
  else
    State[VK_CONTROL] := 0;
  SetKeyboardState(State);
end;

procedure SetShift(Down: Boolean);
var
  State: TKeyboardState;
begin
  GetKeyboardState(State);
  if Down then
    State[VK_SHIFT] := $80
  else
    State[VK_SHIFT] := 0;
  SetKeyboardState(State);
end;

procedure Press(C: TWinControl; Key: Word; Ctrl, Shift: Boolean);
begin
  Log.Text := '';
  SetCtrl(Ctrl);
  SetShift(Shift);
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
  SetCtrl(False);
  SetShift(False);
  PostMessageW(C.Handle, WM_KEYUP, Key, LPARAM($C0000001));
  Application.ProcessMessages;
end;

{ a click in the middle of tab Index of the native strip }
procedure ClickTab(PC: TPageControl; Index: Integer);
var
  R: TRect;
  P: LPARAM;
begin
  SendMessageW(PC.Handle, TCM_GETITEMRECT, WPARAM(Index), LPARAM(@R));
  P := MakeLParam((R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
  Log.Text := '';
  SendMessageW(PC.Handle, WM_LBUTTONDOWN, MK_LBUTTON, P);
  SendMessageW(PC.Handle, WM_LBUTTONUP, 0, P);
  Application.ProcessMessages;
end;

function NewSheet(AOwner: TComponent; const AName: string; PC: TPageControl): TTabSheet;
begin
  Result := TTabSheet.Create(AOwner);
  Result.Name := AName;
  Result.Caption := AName;
  Result.OnShow := Log.Shown;
  Result.OnHide := Log.Hidden;
  Result.PageControl := PC;
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Defaults;
var
  PC: TPageControl;
  S: TTabSheet;
begin
  Section('defaults');
  PC := TPageControl.Create(nil);
  S := TTabSheet.Create(nil);
  try
    CheckEqI(289, PC.Width, 'Width 289');
    CheckEqI(193, PC.Height, 'Height 193');
    CheckEqB(True, PC.ActivePage = nil, 'ActivePage nil');
    CheckEqI(-1, PC.ActivePageIndex, 'ActivePageIndex -1');
    CheckEqI(0, PC.PageCount, 'PageCount 0');
    CheckEqB(False, PC.HotTrack, 'HotTrack');
    CheckEqB(True, PC.Images = nil, 'Images nil');
    CheckEqB(False, PC.MultiLine, 'MultiLine');
    CheckEqB(True, PC.ParentFont, 'ParentFont');
    CheckEqI(0, PC.TabHeight, 'TabHeight 0');
    CheckEqI(-1, PC.TabOrder, 'TabOrder -1');
    CheckEqB(True, PC.TabPosition = tpTop, 'TabPosition tpTop');
    CheckEqB(True, PC.TabStop, 'TabStop');
    CheckEqI(0, PC.TabWidth, 'TabWidth 0');
    CheckEqB(True, csAcceptsControls in PC.ControlStyle, 'page control accepts controls');
    CheckEqB(True, PC is TCustomTabControl, 'a TCustomTabControl');
    Check(GetClass('TPageControl') <> nil, 'TPageControl registered');
    Check(GetClass('TTabSheet') <> nil, 'TTabSheet registered');
    CheckEqS('', S.Caption, 'sheet Caption empty');
    CheckEqB(True, S.Color = clBtnFace, 'sheet Color clBtnFace');
    CheckEqB(True, S.ParentColor, 'sheet ParentColor');
    CheckEqB(True, S.ParentFont, 'sheet ParentFont');
    CheckEqB(True, S.Enabled, 'sheet Enabled');
    CheckEqI(-1, S.ImageIndex, 'sheet ImageIndex -1');
    CheckEqB(True, S.TabVisible, 'sheet TabVisible');
    CheckEqB(True, S.PageControl = nil, 'sheet PageControl nil');
    CheckEqI(-1, S.PageIndex, 'sheet PageIndex -1');
    CheckEqI(-1, S.TabIndex, 'sheet TabIndex -1');
    CheckEqB(True, csAcceptsControls in S.ControlStyle, 'sheet accepts controls');

    Section('not supported in Phase 4');
    try
      PC.MultiLine := True;
      CheckEqS('raised', 'nothing', 'MultiLine True');
    except
      on E: ENotSupportedException do
        CheckEqS(SMultiLineTabs, E.Message, 'MultiLine True: ENotSupportedException');
    end;
    CheckEqB(False, PC.MultiLine, 'MultiLine stays False');
    try
      PC.TabPosition := tpBottom;
      CheckEqS('raised', 'nothing', 'TabPosition tpBottom');
    except
      on E: ENotSupportedException do
        CheckEqS('Multi-line tabs are not supported', E.Message, 'TabPosition tpBottom: ENotSupportedException');
    end;
    CheckEqB(True, PC.TabPosition = tpTop, 'TabPosition stays tpTop');
    PC.MultiLine := False;
    PC.TabPosition := tpTop;
    Check(True, 'the supported values are accepted');
  finally
    S.Free;
    PC.Free;
  end;
end;

procedure PageRect(F: TForm);
var
  PC: TPageControl;
  S: TTabSheet;
  C: TFocusBox;
  P, G, TabH: Integer;
begin
  Section('page rectangle (UIF-772, layout.md UIF-219, E27)');
  PC := TPageControl.Create(nil);
  try
    PC.Font.Height := -12;
    S := TTabSheet.Create(PC);
    S.PageControl := PC;
    C := TFocusBox.Create(PC);
    C.Align := alClient;
    C.Parent := S;
    CheckEqS('4,27,281,162', BoundsText(S), 'E27: sheet');
    CheckEqS('0,0,281,162', BoundsText(C), 'E27: alClient child');
    PC.TabHeight := 30;
    CheckEqS('4,36,281,153', BoundsText(S), 'TabHeight 30');
    PC.TabHeight := 0;
    PC.Font.Height := -20;
    CheckEqS('4,35,281,154', BoundsText(S), 'font change: TabH from the font');
    PC.Font.Height := -12;
    PC.SetBounds(0, 0, 100, 50);
    CheckEqS('4,27,92,19', BoundsText(S), 'size change');
    PC.SetBounds(0, 0, 6, 20);
    CheckEqS('4,27,0,0', BoundsText(S), 'clamped to 0');
    PC.SetBounds(0, 0, 289, 193);
    S.SetBounds(1, 2, 3, 4);
    PC.Realign;
    CheckEqS('4,27,281,162', BoundsText(S), 'a sheet''s own bounds are replaced in the layout pass');
    S.Align := alLeft;
    PC.Realign;
    CheckEqS('4,27,281,162', BoundsText(S), 'a sheet''s Align is ignored');
    S.Align := alNone;
    PC.TabHeight := 30;
    PC.TabWidth := 40;
    PC.ScaleBy(2, 1);
    CheckEqI(60, PC.TabHeight, 'TabHeight scaled');
    CheckEqI(80, PC.TabWidth, 'TabWidth scaled');
    PC.TabHeight := 0;
    PC.TabWidth := 0;
    PC.Font.Height := -12;
    PC.SetBounds(0, 0, 289, 193);
    PC.Parent := F;
    P := LayoutMulDiv(4, F.PixelsPerInch, 96);
    G := LayoutMulDiv(2, F.PixelsPerInch, 96);
    TabH := 12 + LayoutMulDiv(9, F.PixelsPerInch, 96);
    PC.Realign;
    CheckEqS(Format('%d,%d,%d,%d', [P, TabH + P + G, 289 - 2 * P, 193 - P - (TabH + P + G)]), BoundsText(S),
      'constants at the form''s DPI');
  finally
    PC.Free;
  end;
end;

procedure Run;
var
  F: TForm;
  PC, PC3: TPageControl;
  S1, S2, S3, S4, X: TTabSheet;
  Pnl: TPanel;
  Box, Other: TFocusBox;
  IL: TImageList;
  Bmp: TBitmap;
  H, H2: HWND;
  Style: NativeInt;
  R: TRect;
  Msg: string;
  SF: TPagesForm;
  I, Before, P, G: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 600;
    F.ClientHeight := 400;
    Defaults;
    PageRect(F);

    Section('pages (UIF-770, UIF-771)');
    PC := TPageControl.Create(F);
    PC.Name := 'PC';
    S1 := NewSheet(F, 'S1', PC);
    CheckEqB(True, S1.Parent = PC, 'PageControl := re-parents');
    CheckEqB(True, S1.PageControl = PC, 'PageControl');
    CheckEqI(1, PC.PageCount, 'PageCount 1');
    CheckEqB(True, PC.Pages[0] = S1, 'Pages[0]');
    CheckEqS('S1', ActiveName(PC), 'the first page becomes active');
    CheckEqI(0, PC.ActivePageIndex, 'ActivePageIndex 0');
    CheckEqS('S1;', VisiblePages(PC), 'the active page is visible');
    S2 := TTabSheet.Create(F);
    S2.Name := 'S2';
    S2.Caption := 'S2';
    S2.OnShow := Log.Shown;
    S2.OnHide := Log.Hidden;
    S2.Parent := PC;
    CheckEqB(True, S2.PageControl = PC, 'Parent := sets PageControl');
    CheckEqI(2, PC.PageCount, 'PageCount 2');
    CheckEqS('S1', ActiveName(PC), 'a later page does not become active');
    CheckEqS('S1;', VisiblePages(PC), 'other pages invisible');
    CheckEqI(1, S2.PageIndex, 'PageIndex');
    CheckEqI(1, S2.TabIndex, 'TabIndex');
    Pnl := TPanel.Create(F);
    Pnl.Parent := F;
    S3 := TTabSheet.Create(F);
    S3.Name := 'S3';
    Msg := '';
    try
      S3.Parent := Pnl;
    except
      on E: EInvalidOperation do
        Msg := E.Message;
    end;
    CheckEqS('Control "S3" cannot be placed on a TPanel', Msg, 'a tab sheet on a panel raises');
    CheckEqB(True, S3.Parent = nil, 'parent unchanged');
    S3.Caption := 'S3';
    S3.OnShow := Log.Shown;
    S3.OnHide := Log.Hidden;
    S3.PageControl := PC;
    CheckEqI(3, PC.PageCount, 'PageCount 3');

    Section('window');
    PC.SetBounds(10, 10, 289, 193);
    PC.Parent := F;
    H := PC.Handle;
    CheckEqS('SysTabControl32', ClassOf(H), 'window class SysTabControl32');
    Style := GetWindowLongPtrW(H, GWL_STYLE);
    CheckEqB(True, (Style and WS_CLIPCHILDREN) <> 0, 'WS_CLIPCHILDREN');
    CheckEqB(True, (Style and WS_TABSTOP) <> 0, 'WS_TABSTOP');
    CheckEqB(False, (Style and TCS_MULTILINE) <> 0, 'single line');
    CheckEqB(False, (Style and TCS_BUTTONS) <> 0, 'tabs, not buttons');
    { TCS_HOTTRACK is not asserted absent: the version-6 tab control adds it to every window it creates }
    CheckEqB(False, (Style and TCS_FIXEDWIDTH) <> 0, 'no TCS_FIXEDWIDTH');
    CheckEqI(3, TabCount(PC), 'one tab per page');
    CheckEqS('S1|S2|S3', Tabs(PC), 'tab texts = captions');
    CheckEqI(0, CurSel(PC), 'selected tab = active page');
    CheckEqS('PN.TTabSheet', ClassOf(S1.Handle), 'sheet window class');
    CheckEqB(True, GetParent(S1.Handle) = H, 'sheets are children of the tab window');
    CheckEqB(True, (GetWindowLongPtrW(S1.Handle, GWL_STYLE) and WS_VISIBLE) <> 0, 'active sheet window visible');
    CheckEqB(False, (GetWindowLongPtrW(S2.Handle, GWL_STYLE) and WS_VISIBLE) <> 0, 'other sheet windows hidden');
    PC.ActivePage := S2;
    PC.HotTrack := True;
    H2 := PC.Handle;
    CheckEqB(True, H2 <> H, 'HotTrack recreates the window');
    CheckEqB(True, (GetWindowLongPtrW(H2, GWL_STYLE) and TCS_HOTTRACK) <> 0, 'TCS_HOTTRACK');
    CheckEqS('S1|S2|S3', Tabs(PC), 'tabs kept');
    CheckEqI(1, CurSel(PC), 'selection kept');
    CheckEqS('S2', ActiveName(PC), 'active page kept');
    PC.HotTrack := False;
    H := PC.Handle;
    PC.TabWidth := 50;
    CheckEqB(True, (GetWindowLongPtrW(PC.Handle, GWL_STYLE) and TCS_FIXEDWIDTH) <> 0, 'TabWidth > 0: TCS_FIXEDWIDTH');
    CheckEqS('S1|S2|S3', Tabs(PC), 'tabs kept after TabWidth');
    SendMessageW(PC.Handle, TCM_GETITEMRECT, 0, LPARAM(@R));
    CheckEqI(50, R.Right - R.Left, 'fixed tab width');
    PC.TabHeight := 30;
    SendMessageW(PC.Handle, TCM_GETITEMRECT, 0, LPARAM(@R));
    CheckEqI(30, R.Bottom - R.Top, 'TCM_SETITEMSIZE: tab height TabH');
    PC.TabHeight := 0;
    PC.TabWidth := 0;
    CheckEqB(False, (GetWindowLongPtrW(PC.Handle, GWL_STYLE) and TCS_FIXEDWIDTH) <> 0, 'TabWidth 0: automatic');
    PC.ActivePage := S1;

    Section('switching in code (UIF-774)');
    Log.Text := '';
    PC.OnChanging := Log.Changing;
    PC.OnChange := Log.Changed;
    Log.Allow := True;
    PC.ActivePage := S2;
    CheckEqS('S1.hide;S2.show;', Log.Text, 'OnHide, OnShow; no OnChanging/OnChange');
    CheckEqS('S2;', VisiblePages(PC), 'only the new page visible');
    CheckEqI(1, CurSel(PC), 'tab selected');
    Log.Text := '';
    PC.ActivePage := S2;
    CheckEqS('', Log.Text, 'same page: nothing');
    PC.ActivePageIndex := 2;
    CheckEqS('S2.hide;S3.show;', Log.Text, 'ActivePageIndex');
    CheckEqS('S3', ActiveName(PC), 'S3 active');
    Log.Text := '';
    S1.TabVisible := False;
    CheckEqS('S2|S3', Tabs(PC), 'TabVisible False removes the tab');
    CheckEqI(-1, S1.TabIndex, 'TabIndex -1');
    CheckEqI(1, S3.TabIndex, 'TabIndex among visible tabs');
    CheckEqI(1, CurSel(PC), 'selection follows the active page');
    PC.ActivePage := S1;
    CheckEqS('S3', ActiveName(PC), 'a page without tab cannot become active');
    S1.TabVisible := True;
    CheckEqS('S1|S2|S3', Tabs(PC), 'TabVisible True inserts the tab at its place');
    CheckEqI(2, CurSel(PC), 'selection kept');
    Log.Text := '';
    S3.TabVisible := False;
    CheckEqS('S3.hide;S2.show;', Log.Text, 'hiding the last active tab activates the previous visible page');
    CheckEqS('S2', ActiveName(PC), 'S2 active');
    S3.TabVisible := True;
    PC.ActivePage := S1;
    Log.Text := '';
    S1.TabVisible := False;
    CheckEqS('S1.hide;S2.show;', Log.Text, 'hiding an active tab activates the next visible page');
    S1.TabVisible := True;
    PC.ActivePage := S1;

    Section('focus leaves the old page (UIF-773)');
    Box := TFocusBox.Create(F);
    Box.Parent := S1;
    Other := TFocusBox.Create(F);
    Other.SetBounds(400, 300, 20, 20);
    Other.Parent := F;
    F.Show;
    Box.SetFocus;
    CheckEqB(True, F.ActiveControl = Box, 'focus inside S1');
    PC.ActivePage := S2;
    CheckEqB(True, F.ActiveControl = PC, 'focus moved to the page control');
    Other.SetFocus;
    PC.ActivePage := S1;
    CheckEqB(True, F.ActiveControl = Other, 'focus outside the page stays');

    Section('user selection (UIF-773)');
    ClickTab(PC, 1);
    CheckEqS('changing;S1.hide;S2.show;change:S2;', Log.Text, 'click on a tab: OnChanging, switch, OnChange');
    CheckEqI(1, CurSel(PC), 'tab selected');
    Log.Allow := False;
    ClickTab(PC, 2);
    CheckEqS('changing;', Log.Text, 'vetoed');
    CheckEqS('S2', ActiveName(PC), 'page unchanged');
    CheckEqI(1, CurSel(PC), 'tab unchanged');
    Log.Allow := True;

    Section('keys (UIF-775)');
    PC.SetFocus;
    Press(PC, VK_RIGHT, False, False);
    CheckEqS('changing;S2.hide;S3.show;change:S3;', Log.Text, 'Right on the tab strip');
    Press(PC, VK_LEFT, False, False);
    CheckEqS('changing;S3.hide;S2.show;change:S2;', Log.Text, 'Left on the tab strip');
    Box.Parent := S2;
    Box.SetFocus;
    Press(Box, VK_TAB, True, False);
    CheckEqS('changing;S2.hide;S3.show;change:S3;', Log.Text, 'Ctrl+Tab inside a page: next page');
    PC.SetFocus;
    Press(PC, VK_TAB, True, False);
    CheckEqS('changing;S3.hide;S1.show;change:S1;', Log.Text, 'Ctrl+Tab on the strip wraps to the first page');
    Press(PC, VK_TAB, True, True);
    CheckEqS('changing;S1.hide;S3.show;change:S3;', Log.Text, 'Ctrl+Shift+Tab: previous page (wrapping)');
    Log.Allow := False;
    Press(PC, VK_TAB, True, False);
    CheckEqS('changing;', Log.Text, 'Ctrl+Tab vetoed');
    CheckEqS('S3', ActiveName(PC), 'page unchanged');
    Log.Allow := True;
    Other.SetFocus;
    Press(Other, VK_TAB, True, False);
    CheckEqS('', Log.Text, 'Ctrl+Tab outside the page control: no page change');
    Log.Text := '';
    PC.SelectNextPage(True);
    CheckEqS('changing;S3.hide;S1.show;change:S1;', Log.Text, 'SelectNextPage fires the events');
    Log.Text := '';
    S2.TabVisible := False;
    PC.SelectNextPage(True);
    CheckEqS('changing;S1.hide;S3.show;change:S3;', Log.Text, 'SelectNextPage skips pages without tab');
    S2.TabVisible := True;

    Section('accelerators and tabs in sync (UIF-776)');
    S2.Caption := '&Two';
    CheckEqS('S1|&Two|S3', Tabs(PC), 'caption change updates the tab');
    Log.Text := '';
    CheckEqI(0, PC.Perform(CM_DIALOGCHAR, Ord('x'), 0), 'other character: not consumed');
    CheckEqI(1, PC.Perform(CM_DIALOGCHAR, Ord('t'), 0), 'accelerator consumed');
    CheckEqS('changing;S3.hide;S2.show;change:S2;', Log.Text, 'accelerator selects the page');
    IL := TImageList.Create(F);
    Bmp := TBitmap.Create;
    try
      Bmp.SetSize(16, 16);
      IL.Add(Bmp, nil);
      IL.Add(Bmp, nil);
    finally
      Bmp.Free;
    end;
    PC.Images := IL;
    CheckEqB(True, HIMAGELIST(SendMessageW(PC.Handle, TCM_GETIMAGELIST, 0, 0)) = IL.Handle, 'TCM_SETIMAGELIST');
    CheckEqI(-1, TabImage(PC, 0), 'image -1');
    S1.ImageIndex := 1;
    CheckEqI(1, TabImage(PC, 0), 'ImageIndex updates the tab image');
    S2.PageIndex := 0;
    CheckEqB(True, PC.Pages[0] = S2, 'PageIndex moves the page');
    CheckEqI(1, S1.PageIndex, 'others follow');
    CheckEqS('&Two|S1|S3', Tabs(PC), 'tab order follows');
    CheckEqI(0, CurSel(PC), 'selection follows the active page');
    CheckEqI(1, TabImage(PC, 1), 'image moves with its page');
    IL.Free;
    CheckEqB(True, PC.Images = nil, 'Images cleared when the image list is freed');
    CheckEqB(True, SendMessageW(PC.Handle, TCM_GETIMAGELIST, 0, 0) = 0, 'native image list cleared');

    Section('destroying pages (UIF-777)');
    S4 := NewSheet(F, 'S4', PC);
    CheckEqS('&Two|S1|S3|S4', Tabs(PC), 'S4 added');
    PC.ActivePage := S1;
    Log.Text := '';
    S1.Free;
    CheckEqS('S3', ActiveName(PC), 'destroying the active page activates the next visible page');
    CheckEqS('&Two|S3|S4', Tabs(PC), 'its tab is removed');
    CheckEqI(3, PC.PageCount, 'PageCount 3');
    CheckEqS('S3.show;', Log.Text, 'the next page is shown');
    PC.ActivePage := S4;
    S4.Free;
    CheckEqS('S3', ActiveName(PC), 'destroying the last active page activates the previous one');
    S3.TabVisible := False;
    CheckEqS('S2', ActiveName(PC), 'S2 active');
    S2.Free;
    CheckEqS('nil', ActiveName(PC), 'no visible page left: no active page');
    CheckEqI(-1, PC.ActivePageIndex, 'ActivePageIndex -1');
    S3.TabVisible := True;
    CheckEqS('S3', ActiveName(PC), 'a page that becomes visible is activated');
    S3.PageControl := nil;
    CheckEqI(0, PC.PageCount, 'PageControl := nil removes the page');
    CheckEqI(0, TabCount(PC), 'no tabs');
    CheckEqS('nil', ActiveName(PC), 'no active page');
    S3.Free;

    Section('streaming');
    Log.Text := '';
    SF := TPagesForm.Create(nil);
    try
      CheckEqI(3, SF.Book.PageCount, 'pages');
      CheckEqB(True, SF.Book.ActivePage = SF.Second, 'stored ActivePage');
      CheckEqS('Second;', VisiblePages(SF.Book), 'only the active page visible');
      CheckEqI(24, SF.Book.TabHeight, 'TabHeight');
      CheckEqI(2, SF.Second.ImageIndex, 'ImageIndex');
      CheckEqB(False, SF.Third.TabVisible, 'TabVisible False');
      P := LayoutMulDiv(4, SF.PixelsPerInch, 96);
      G := LayoutMulDiv(2, SF.PixelsPerInch, 96);
      CheckEqS(Format('%d,%d,%d,%d', [P, 24 + P + G, 289 - 2 * P, 193 - P - (24 + P + G)]), BoundsText(SF.Second),
        'page rectangle (TabHeight 24, form DPI)');
      CheckEqS(Format('0,0,%d,%d', [289 - 2 * P, 193 - P - (24 + P + G)]), BoundsText(SF.Inside),
        'alClient child of a page');
      CheckEqS('First|&Second', Tabs(SF.Book), 'tabs of the visible pages');
      CheckEqI(1, CurSel(SF.Book), 'selected tab');
      CheckEqB(True, SF.Book2.ActivePage = SF.Shown, 'ActivePage not stored: first page with a tab');
      CheckEqS('Shown;', VisiblePages(SF.Book2), 'Book2 visible pages');
      Check(Assigned(SF.Book.OnChange), 'OnChange bound');
      CheckEqS('', Log.Text, 'no events while loading');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 50 do
    begin
      PC3 := TPageControl.Create(nil);
      X := TTabSheet.Create(PC3);
      X.Caption := 'A';
      X.PageControl := PC3;
      X := TTabSheet.Create(PC3);
      X.Caption := 'B';
      X.PageControl := PC3;
      PC3.Parent := F;
      PC3.HandleNeeded;
      X.HandleNeeded;
      PC3.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 50 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
