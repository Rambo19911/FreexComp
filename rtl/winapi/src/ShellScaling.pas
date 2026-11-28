// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ShellScaling - bindings of the shell scaling library (shcore.dll): the DPI of a monitor and the kinds of DPI it can
  report (ui-framework phase8/per-monitor-dpi.md section 9, rules RTL-1300 to RTL-1302; task R-276). Uses Windows.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the function page GetDpiForMonitor and
  the enumeration page MONITOR_DPI_TYPE (shellscalingapi.h), whose values are those of the enumeration in the order
  the page lists them (MDT_EFFECTIVE_DPI 0, MDT_ANGULAR_DPI 1, MDT_RAW_DPI 2; MDT_DEFAULT names MDT_EFFECTIVE_DPI).
  The enumeration is declared as Integer constants: dpiType is an Integer parameter.
  Measured (test Win_ShellScaling): for the per-monitor aware program the effective DPI of the primary monitor equals
  GetDpiForWindow of a window on it; a DPI type outside the enumeration gives $800700A0 (the code of
  ERROR_BAD_ARGUMENTS) and the null monitor handle $80070006 (the code of ERROR_INVALID_HANDLE), not the E_INVALIDARG
  the reference page names. }
unit ShellScaling;

interface

uses Windows;

const
  shcore = 'shcore.dll';

  { MONITOR_DPI_TYPE }
  MDT_EFFECTIVE_DPI = 0;
  MDT_ANGULAR_DPI = 1;
  MDT_RAW_DPI = 2;
  MDT_DEFAULT = MDT_EFFECTIVE_DPI;

function GetDpiForMonitor(hmonitor: HMONITOR; dpiType: Integer; out dpiX, dpiY: UINT): HRESULT; stdcall;
  external shcore name 'GetDpiForMonitor';

implementation

end.
