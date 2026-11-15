// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-823, RTL-832, RTL-860, RTL-862
program Win_User32Input;
{ Windows unit, user32 part (winapi.md section 5.3), continued from Win_User32Smoke: on a hidden top-level window
  of its own (registered and created without checks; those calls are checked in Win_User32Smoke) calls the
  user32 functions of input, painting, scroll bars, menus, icons, system, DPI and monitors, and the text helpers.
  Only results that do not depend on the machine, the desktop or the user are printed; the others are called and
  ignored. Functions that would show UI or make a sound are listed in README.md. }
uses Windows, Types, TestKit;

const
  SmokeClass = 'FreexCompUser32Input';

var
  MonitorCount: Integer;
  Main: HWND;
  Cursor: HCURSOR;
  Icon: HICON;

function WndProc(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
begin
  Result := DefWindowProcW(hWnd, Msg, wParam, lParam);
end;

function CountMonitor(hMonitor: HMONITOR; hdcMonitor: HDC; lprcMonitor: PRect; dwData: LPARAM): BOOL; stdcall;
begin
  MonitorCount := MonitorCount + 1;
  Result := True;
end;

function BufStr(P: PWideChar): string;
begin
  Result := string(P);
end;

{ the class and the window of Win_User32Smoke, without its checks }
procedure Setup;
var
  WC: TWndClassExW;
begin
  Cursor := LoadCursorW(0, IDC_ARROW);
  Icon := LoadIconW(0, IDI_APPLICATION);
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.style := CS_HREDRAW or CS_VREDRAW;
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.hIcon := Icon;
  WC.hCursor := Cursor;
  WC.hbrBackground := HBRUSH(COLOR_WINDOW + 1);
  WC.lpszClassName := SmokeClass;
  RegisterClassExW(WC);
  Main := CreateWindowExW(0, SmokeClass, 'Smoke', WS_OVERLAPPEDWINDOW or WS_VSCROLL, 100, 100, 300, 200, 0, 0,
    HINST(HInstance), nil);
end;

procedure KeyboardMouse;
var
  Keys: TKeyboardState;
  Buffer: array[0..63] of WideChar;
  P: TPoint;
  A, B: Integer;
  TME: TTrackMouseEvent;
begin
  Section('input');
  GetKeyState(VK_SHIFT);
  GetAsyncKeyState(VK_SHIFT);
  Check(GetKeyboardState(Keys), 'GetKeyboardState');
  CheckEqI(1, MapVirtualKeyW(VK_ESCAPE, MAPVK_VK_TO_VSC), 'MapVirtualKeyW');
  Check(GetKeyNameTextW(1 shl 16, @Buffer[0], 64) > 0, 'GetKeyNameTextW');
  if GetCursorPos(P) then
    SetCursorPos(P.X, P.Y);
  A := ShowCursor(True);
  B := ShowCursor(False);
  CheckEqI(A - 1, B, 'ShowCursor');
  SetCursor(Cursor);
  SetCapture(Main);
  GetCapture;
  ReleaseCapture;
  SetFocus(0);
  GetFocus;
  SetActiveWindow(Main);
  GetActiveWindow;
  FillChar(TME, SizeOf(TME), 0);
  TME.cbSize := SizeOf(TME);
  TME.dwFlags := TME_LEAVE;
  TME.hwndTrack := Main;
  TrackMouseEvent(TME);
  Check(GetDoubleClickTime > 0, 'GetDoubleClickTime');
end;

procedure Painting;
var
  DC: HDC;
  R: TRect;
  PS: TPaintStruct;
begin
  Section('painting');
  DC := GetDC(Main);
  Check(DC <> 0, 'GetDC');
  R := Rect(0, 0, 40, 30);
  Check(FillRect(DC, R, GetSysColorBrush(COLOR_WINDOW)) <> 0, 'FillRect');
  Check(FrameRect(DC, R, GetSysColorBrush(COLOR_WINDOWTEXT)) <> 0, 'FrameRect');
  Check(InvertRect(DC, R), 'InvertRect');
  Check(DrawFocusRect(DC, R), 'DrawFocusRect');
  Check(DrawEdge(DC, R, EDGE_RAISED, BF_RECT), 'DrawEdge');
  Check(DrawFrameControl(DC, R, DFC_BUTTON, DFCS_BUTTONPUSH), 'DrawFrameControl');
  Check(DrawIconEx(DC, 0, 0, Icon, 16, 16, 0, 0, DI_NORMAL), 'DrawIconEx');
  R := Rect(0, 0, 0, 0);
  Check(DrawTextW(DC, 'Abc', -1, R, DT_CALCRECT or DT_SINGLELINE) > 0, 'DrawTextW');
  Check(R.Right > 0, 'DrawTextW DT_CALCRECT width');
  CheckEqI(1, ReleaseDC(Main, DC), 'ReleaseDC');
  DC := GetWindowDC(Main);
  Check(DC <> 0, 'GetWindowDC');
  CheckEqI(1, ReleaseDC(Main, DC), 'ReleaseDC window DC');
  GetSysColor(COLOR_WINDOW);
  Check(GetSysColorBrush(COLOR_BTNFACE) <> 0, 'GetSysColorBrush');
  Check(InvalidateRect(Main, nil, True), 'InvalidateRect');
  GetUpdateRect(Main, nil, False);
  Check(ValidateRect(Main, nil), 'ValidateRect');
  Check(RedrawWindow(Main, nil, 0, RDW_INVALIDATE or RDW_NOERASE), 'RedrawWindow');
  DC := BeginPaint(Main, PS);
  Check(DC <> 0, 'BeginPaint');
  CheckEqI(Int64(DC), Int64(PS.hdc), 'TPaintStruct.hdc');
  Check(EndPaint(Main, PS), 'EndPaint');
  ScrollWindowEx(Main, 0, 5, nil, nil, 0, nil, SW_INVALIDATE);
end;

procedure ScrollBars;
var
  SI: TScrollInfo;
begin
  Section('scroll bars');
  FillChar(SI, SizeOf(SI), 0);
  SI.cbSize := SizeOf(SI);
  SI.fMask := SIF_RANGE or SIF_PAGE or SIF_POS;
  SI.nMin := 0;
  SI.nMax := 100;
  SI.nPage := 10;
  SI.nPos := 40;
  CheckEqI(40, SetScrollInfo(Main, SB_VERT, SI, False), 'SetScrollInfo');
  FillChar(SI, SizeOf(SI), 0);
  SI.cbSize := SizeOf(SI);
  SI.fMask := SIF_ALL;
  Check(GetScrollInfo(Main, SB_VERT, SI), 'GetScrollInfo');
  CheckEqI(100, SI.nMax, 'GetScrollInfo nMax');
  CheckEqI(10, SI.nPage, 'GetScrollInfo nPage');
  CheckEqI(40, SI.nPos, 'GetScrollInfo nPos');
  Check(ShowScrollBar(Main, SB_VERT, True), 'ShowScrollBar');
  Check(EnableScrollBar(Main, SB_VERT, ESB_DISABLE_BOTH), 'EnableScrollBar disable');
  Check(EnableScrollBar(Main, SB_VERT, ESB_ENABLE_BOTH), 'EnableScrollBar enable');
end;

procedure Menus;
var
  Bar, Popup: HMENU;
  MII: TMenuItemInfoW;
  Buffer: array[0..31] of WideChar;
begin
  Section('menus');
  Bar := CreateMenu;
  Check(Bar <> 0, 'CreateMenu');
  Popup := CreatePopupMenu;
  Check(Popup <> 0, 'CreatePopupMenu');
  FillChar(MII, SizeOf(MII), 0);
  MII.cbSize := SizeOf(MII);
  MII.fMask := MIIM_STRING or MIIM_ID;
  MII.wID := 11;
  MII.dwTypeData := 'Open';
  Check(InsertMenuItemW(Popup, 0, True, MII), 'InsertMenuItemW');
  MII.wID := 12;
  MII.dwTypeData := 'Save';
  Check(InsertMenuItem(Popup, 1, True, MII), 'InsertMenuItem alias');
  CheckEqI(2, GetMenuItemCount(Popup), 'GetMenuItemCount');
  MII.fMask := MIIM_STRING or MIIM_SUBMENU;
  MII.hSubMenu := Popup;
  MII.dwTypeData := 'File';
  Check(InsertMenuItemW(Bar, 0, True, MII), 'InsertMenuItemW submenu');
  CheckEqI(Int64(Popup), Int64(GetSubMenu(Bar, 0)), 'GetSubMenu');
  FillChar(MII, SizeOf(MII), 0);
  MII.cbSize := SizeOf(MII);
  MII.fMask := MIIM_STRING;
  MII.dwTypeData := 'Load';
  Check(SetMenuItemInfoW(Popup, 11, False, MII), 'SetMenuItemInfoW');
  FillChar(MII, SizeOf(MII), 0);
  MII.cbSize := SizeOf(MII);
  MII.fMask := MIIM_STRING;
  MII.dwTypeData := @Buffer[0];
  MII.cch := 32;
  Check(GetMenuItemInfoW(Popup, 11, False, MII), 'GetMenuItemInfoW');
  CheckEqS('Load', BufStr(@Buffer[0]), 'GetMenuItemInfoW text');
  CheckEqI(4, MII.cch, 'GetMenuItemInfoW cch');
  CheckEqB(False, EnableMenuItem(Popup, 12, MF_BYCOMMAND or MF_GRAYED), 'EnableMenuItem previous state');
  CheckEqI(MF_UNCHECKED, CheckMenuItem(Popup, 11, MF_BYCOMMAND or MF_CHECKED), 'CheckMenuItem previous state');
  Check(CheckMenuRadioItem(Popup, 11, 12, 12, MF_BYCOMMAND), 'CheckMenuRadioItem');
  Check(SetMenu(Main, Bar), 'SetMenu');
  CheckEqI(Int64(Bar), Int64(GetMenu(Main)), 'GetMenu');
  Check(DrawMenuBar(Main), 'DrawMenuBar');
  CheckEqB(False, TrackPopupMenuEx(0, TPM_RETURNCMD, 0, 0, Main, nil), 'TrackPopupMenuEx of no menu');
  Check(GetSystemMenu(Main, False) <> 0, 'GetSystemMenu');
  Check(RemoveMenu(Popup, 12, MF_BYCOMMAND), 'RemoveMenu');
  CheckEqI(1, GetMenuItemCount(Popup), 'RemoveMenu count');
  Check(DeleteMenu(Popup, 11, MF_BYCOMMAND), 'DeleteMenu');
  CheckEqI(0, GetMenuItemCount(Popup), 'DeleteMenu count');
  Check(SetMenu(Main, 0), 'SetMenu none');
  Check(DestroyMenu(Bar), 'DestroyMenu');
end;

procedure Icons;
var
  II: TIconInfo;
  H: THandle;
  NewIcon: HICON;
  NewCursor: HCURSOR;
begin
  Section('icons');
  H := LoadImageW(0, IDI_APPLICATION, IMAGE_ICON, 0, 0, LR_SHARED or LR_DEFAULTSIZE);
  Check(H <> 0, 'LoadImageW');
  Check(GetIconInfo(Icon, II), 'GetIconInfo');
  CheckEqB(True, II.fIcon, 'GetIconInfo fIcon');
  Check(II.hbmMask <> 0, 'GetIconInfo hbmMask');
  NewIcon := CreateIconIndirect(II);
  Check(NewIcon <> 0, 'CreateIconIndirect icon');
  Check(DestroyIcon(NewIcon), 'DestroyIcon');
  II.fIcon := False;
  II.xHotspot := 1;
  II.yHotspot := 1;
  NewCursor := HCURSOR(CreateIconIndirect(II));
  Check(NewCursor <> 0, 'CreateIconIndirect cursor');
  Check(DestroyCursor(NewCursor), 'DestroyCursor');
  { the two bitmaps of II are left to process exit: this program does not use gdi32 (Win_Gdi32Smoke releases them) }
end;

procedure SystemAndDpi;
var
  NCM: TNonClientMetricsW;
  Ctx: DPI_AWARENESS_CONTEXT;
  Mon: HMONITOR;
  MI: TMonitorInfo;
begin
  Section('system, DPI, monitors');
  Check(GetSystemMetrics(SM_CXSCREEN) > 0, 'GetSystemMetrics');
  Check(GetSystemMetricsForDpi(SM_CXVSCROLL, 96) > 0, 'GetSystemMetricsForDpi');
  FillChar(NCM, SizeOf(NCM), 0);
  NCM.cbSize := SizeOf(NCM);
  Check(SystemParametersInfoW(SPI_GETNONCLIENTMETRICS, SizeOf(NCM), @NCM, 0), 'SystemParametersInfoW');
  Check(NCM.lfMessageFont.lfFaceName[0] <> #0, 'TNonClientMetricsW.lfMessageFont');
  Check(SystemParametersInfoForDpi(SPI_GETNONCLIENTMETRICS, SizeOf(NCM), @NCM, 0, 96), 'SystemParametersInfoForDpi');
  Check(GetDpiForWindow(Main) > 0, 'GetDpiForWindow');
  Check(GetDpiForSystem > 0, 'GetDpiForSystem');
  Ctx := GetThreadDpiAwarenessContext;
  Check(Ctx <> 0, 'GetThreadDpiAwarenessContext');
  Check(AreDpiAwarenessContextsEqual(Ctx, Ctx), 'AreDpiAwarenessContextsEqual');
  Mon := MonitorFromWindow(Main, MONITOR_DEFAULTTONEAREST);
  Check(Mon <> 0, 'MonitorFromWindow');
  FillChar(MI, SizeOf(MI), 0);
  MI.cbSize := SizeOf(MI);
  Check(GetMonitorInfoW(Mon, MI), 'GetMonitorInfoW');
  Check(MI.rcMonitor.Right > MI.rcMonitor.Left, 'TMonitorInfo.rcMonitor');
  CheckEqI(Int64(Mon), Int64(MonitorFromPoint(Point(MI.rcMonitor.Left, MI.rcMonitor.Top), MONITOR_DEFAULTTONULL)),
    'MonitorFromPoint');
  CheckEqI(Int64(Mon), Int64(MonitorFromRect(MI.rcMonitor, MONITOR_DEFAULTTONULL)), 'MonitorFromRect');
  MonitorCount := 0;
  Check(EnumDisplayMonitors(0, nil, @CountMonitor, 0), 'EnumDisplayMonitors');
  Check(MonitorCount >= 1, 'EnumDisplayMonitors count');
end;

procedure TextHelpers;
var
  Buffer: array[0..4] of WideChar;
begin
  Section('text helpers');
  Buffer[0] := 'a';
  Buffer[1] := 'b';
  Buffer[2] := 'Z';
  Buffer[3] := #$E4;
  Buffer[4] := #0;
  CheckEqI(4, CharUpperBuffW(@Buffer[0], 4), 'CharUpperBuffW count');
  CheckEqS('ABZ'#$C4, BufStr(@Buffer[0]), 'CharUpperBuffW');
  CheckEqI(4, CharLowerBuffW(@Buffer[0], 4), 'CharLowerBuffW count');
  CheckEqS('abz'#$E4, BufStr(@Buffer[0]), 'CharLowerBuffW');
end;

begin
  { the result depends on the manifest (the call fails when the awareness is already set) }
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
  Setup;
  KeyboardMouse;
  Painting;
  ScrollBars;
  Menus;
  Icons;
  SystemAndDpi;
  TextHelpers;
  DestroyWindow(Main);
  UnregisterClassW(SmokeClass, HINST(HInstance));
  Done;
end.
