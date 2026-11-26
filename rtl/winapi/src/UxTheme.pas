// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ UxTheme - bindings of the visual styles library (uxtheme.dll): the theme handle type, the functions that switch a
  window's visual style off and read whether a window or the program uses visual styles (rule RTL-1144, task R-255),
  the functions that open, draw and close the theme data of a class, and the part and state numbers of the tab
  class (rule RTL-1146, task R-256) (ui-framework phase8/tab-control.md section 9). Uses Windows.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the function pages SetWindowTheme,
  GetWindowTheme, IsAppThemed, OpenThemeData, CloseThemeData and DrawThemeBackground (uxtheme.h) and the page
  "Parts and States" (class TAB: the parts TABP_TABITEM and TABP_PANE, the states TIS_*). That page names the parts
  and states without their numbers; the numbers are those of the Win32 API metadata Microsoft publishes with its
  windows-rs project (MIT licence) - facts only, no text - and agree with the part and state numbers of the visual
  style elements of the .NET desktop runtime (TabItem part 1, Pane part 9, tab item states 1 to 4).
  Measured (test Win_UxTheme): IsAppThemed reads false until the thread has created its first window. }
unit UxTheme;

interface

uses Windows;

const
  uxtheme = 'uxtheme.dll';

  { parts of the class TAB (TABPARTS) }
  TABP_TABITEM = 1;
  TABP_PANE = 9;

  { states of the part TABP_TABITEM (TABITEMSTATES) }
  TIS_NORMAL = 1;
  TIS_HOT = 2;
  TIS_SELECTED = 3;
  TIS_DISABLED = 4;
  TIS_FOCUSED = 5;

type
  { a theme handle (HTHEME of the reference): 0 when no visual style applies }
  HTHEME = THandle;

function SetWindowTheme(hwnd: HWND; pszSubAppName, pszSubIdList: LPCWSTR): HRESULT; stdcall;
  external uxtheme name 'SetWindowTheme';
function GetWindowTheme(hwnd: HWND): HTHEME; stdcall; external uxtheme name 'GetWindowTheme';
function IsAppThemed: BOOL; stdcall; external uxtheme name 'IsAppThemed';
function OpenThemeData(hwnd: HWND; pszClassList: LPCWSTR): HTHEME; stdcall; external uxtheme name 'OpenThemeData';
function CloseThemeData(hTheme: HTHEME): HRESULT; stdcall; external uxtheme name 'CloseThemeData';
{ pRect is never NULL (a const record, RTL-822); pClipRect may be nil }
function DrawThemeBackground(hTheme: HTHEME; hdc: HDC; iPartId, iStateId: Integer; const pRect: TRect;
  pClipRect: PRect): HRESULT; stdcall; external uxtheme name 'DrawThemeBackground';

implementation

end.
