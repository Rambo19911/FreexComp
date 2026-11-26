// covers: RTL-1144, RTL-1145, RTL-1146, RTL-1147
// phase: 8
// opc: --app-type gui
program Win_UxTheme;
{ UxTheme unit (tasks R-255, R-256) and ODT_TAB (CommCtrl) against the system. A gui program, so its manifest binds comctl32
  version 6, whose controls use visual styles. Only facts that hold with and without visual styles are printed: after
  WM_THEMECHANGED a SysTabControl32 window has a theme handle exactly when IsAppThemed says so; SetWindowTheme with two
  empty strings (no theme section matches) returns S_OK and leaves the window without a theme handle; OpenThemeData of
  the class TAB returns a handle exactly when IsAppThemed says so. Only with a handle (themed): DrawThemeBackground of
  the selected tab item into a 32-bit memory bitmap returns S_OK and changes pixels, a clip rectangle keeps the pixels
  outside it, and CloseThemeData returns S_OK; without one the same lines are printed without drawing. The part and
  state numbers are compared with the reference values.
  Measured on the system (2026-11-27): IsAppThemed is false until the thread has created its first window, so it is
  read after the windows exist; a tab control just created reports no theme handle (GetWindowTheme 0) even when the
  program is themed and gets one when it handles WM_THEMECHANGED, which the system sends when visual styles change
  and SetWindowTheme sends too. The raw BOOL of IsAppThemed is not printed: a BOOL result reads as all-ones for True
  (LANG-215). Needs a desktop; the windows are never shown. }
uses Windows, Messages, CommCtrl, Types, UxTheme, TestKit;

const
  W = 40;
  H = 24;
  Sentinel = $00123456;   // DIB pixel order: $00RRGGBB

type
  PPixels = ^TPixels;
  TPixels = array[0..W * H - 1] of Cardinal;

procedure Clear(P: PPixels);
var
  I: Integer;
begin
  for I := 0 to W * H - 1 do
    P^[I] := Sentinel;
end;

{ the pixels of columns X0 .. X1 - 1 that differ from the sentinel }
function Changed(P: PPixels; X0, X1: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  for Y := 0 to H - 1 do
    for X := X0 to X1 - 1 do
      if P^[Y * W + X] <> Sentinel then
        Result := Result + 1;
end;

var
  ICC: TInitCommonControlsEx;
  Parent, Tab: HWND;
  Themed: Boolean;
  Theme: HTHEME;
  Info: TBitmapInfo;
  DC: HDC;
  Bmp: HBITMAP;
  Old: HGDIOBJ;
  Bits: Pointer;
  Px: PPixels;
  R, Clip: TRect;
  DrawHr, ClipHr, CloseHr: HRESULT;
  Inside, Outside, ClipInside, ClipOutside: Integer;

begin
  Section('import');
  Check(GetProcAddress(GetModuleHandleW('uxtheme.dll'), 'SetWindowTheme') <> nil,
    'the loaded uxtheme exports SetWindowTheme');

  Section('GetWindowTheme');
  ICC.dwSize := SizeOf(ICC);
  ICC.dwICC := ICC_TAB_CLASSES;
  Check(InitCommonControlsEx(ICC), 'InitCommonControlsEx');
  Parent := CreateWindowExW(0, 'STATIC', nil, WS_POPUP, 0, 0, 200, 100, 0, 0, HINST(HInstance), nil);
  Check(Parent <> 0, 'a hidden top-level window');
  Tab := CreateWindowExW(0, WC_TABCONTROL, nil, WS_CHILD or WS_VISIBLE, 0, 0, 200, 100, Parent, HMENU(1),
    HINST(HInstance), nil);
  Check(Tab <> 0, 'a SysTabControl32 child');
  if IsAppThemed then
    Themed := True
  else
    Themed := False;
  SendMessageW(Tab, WM_THEMECHANGED, 0, 0);
  CheckEqB(Themed, GetWindowTheme(Tab) <> 0,
    'after WM_THEMECHANGED the tab control has a theme handle exactly when IsAppThemed');

  Section('SetWindowTheme');
  CheckEqI(S_OK, SetWindowTheme(Tab, '', ''), 'SetWindowTheme(h, '''', '''') returns S_OK');
  CheckEqI(0, GetWindowTheme(Tab), 'no theme handle afterwards');

  Section('OpenThemeData');
  Theme := OpenThemeData(Parent, 'TAB');
  CheckEqB(Themed, Theme <> 0, 'OpenThemeData(h, ''TAB'') returns a handle exactly when IsAppThemed');

  Section('DrawThemeBackground');
  FillChar(Info, SizeOf(Info), 0);
  Info.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  Info.bmiHeader.biWidth := W;
  Info.bmiHeader.biHeight := -H;
  Info.bmiHeader.biPlanes := 1;
  Info.bmiHeader.biBitCount := 32;
  Info.bmiHeader.biCompression := BI_RGB;
  DC := CreateCompatibleDC(0);
  Bits := nil;
  Bmp := CreateDIBSection(DC, @Info, DIB_RGB_COLORS, Bits, 0, 0);
  Check((DC <> 0) and (Bmp <> 0), 'a 32-bit memory bitmap');
  Old := SelectObject(DC, Bmp);
  Px := PPixels(Bits);
  R := Rect(0, 0, W, H);
  Clip := Rect(0, 0, W div 2, H);
  if Theme <> 0 then
  begin
    Clear(Px);
    DrawHr := DrawThemeBackground(Theme, DC, TABP_TABITEM, TIS_SELECTED, R, nil);
    GdiFlush;
    Inside := Changed(Px, 0, W);
    Clear(Px);
    ClipHr := DrawThemeBackground(Theme, DC, TABP_TABITEM, TIS_SELECTED, R, @Clip);
    GdiFlush;
    ClipInside := Changed(Px, 0, W div 2);
    ClipOutside := Changed(Px, W div 2, W);
    CloseHr := CloseThemeData(Theme);
  end
  else
  begin
    DrawHr := S_OK;
    ClipHr := S_OK;
    Inside := 1;
    ClipInside := 1;
    ClipOutside := 0;
    CloseHr := S_OK;
  end;
  CheckEqI(S_OK, DrawHr, 'DrawThemeBackground(TABP_TABITEM, TIS_SELECTED) returns S_OK');
  Check(Inside > 0, 'it changes pixels of the bitmap');
  CheckEqI(S_OK, ClipHr, 'DrawThemeBackground with a clip rectangle returns S_OK');
  Check(ClipInside > 0, 'it changes pixels inside the clip rectangle');
  CheckEqI(0, ClipOutside, 'it keeps the pixels outside the clip rectangle');
  CheckEqI(S_OK, CloseHr, 'CloseThemeData returns S_OK');
  SelectObject(DC, Old);
  Check(DeleteObject(Bmp), 'DeleteObject');
  Check(DeleteDC(DC), 'DeleteDC');

  Section('parts and states');
  CheckEqI(1, TABP_TABITEM, 'TABP_TABITEM');
  CheckEqI(9, TABP_PANE, 'TABP_PANE');
  CheckEqI(1, TIS_NORMAL, 'TIS_NORMAL');
  CheckEqI(2, TIS_HOT, 'TIS_HOT');
  CheckEqI(3, TIS_SELECTED, 'TIS_SELECTED');
  CheckEqI(4, TIS_DISABLED, 'TIS_DISABLED');
  CheckEqI(5, TIS_FOCUSED, 'TIS_FOCUSED');

  Section('ODT_TAB');
  CheckEqI(101, ODT_TAB, 'ODT_TAB');

  Section('cleanup');
  Check(DestroyWindow(Parent), 'DestroyWindow');
  Done;
end.
