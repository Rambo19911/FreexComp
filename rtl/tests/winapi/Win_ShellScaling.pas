// covers: RTL-1300, RTL-1302
// phase: 8
// opc: --app-type gui
program Win_ShellScaling;
{ ShellScaling unit (task R-276) against the system. A gui program, so its manifest declares the per-monitor (v2) DPI
  awareness the framework runs with. Only machine-independent facts are printed, never a DPI value:
  GetDpiForMonitor of the primary monitor (MonitorFromPoint of the origin with MONITOR_DEFAULTTOPRIMARY) returns S_OK
  with equal horizontal and vertical values, and they equal GetDpiForWindow of a hidden window on that monitor; a DPI
  type outside the enumeration and the null monitor handle give a failure code (measured 2026-11-28: $800700A0, the
  code of ERROR_BAD_ARGUMENTS, and $80070006, the code of ERROR_INVALID_HANDLE - not the E_INVALIDARG the reference
  page names - so only the failure is checked); the MDT_* values are those of the MONITOR_DPI_TYPE reference page.
  Needs a desktop; the window is never shown. }
uses Windows, Types, ShellScaling, TestKit;

var
  Mon: HMONITOR;
  Wnd: HWND;
  X, Y: UINT;
  Hr: HRESULT;

begin
  Section('import');
  Check(GetProcAddress(GetModuleHandleW('shcore.dll'), 'GetDpiForMonitor') <> nil,
    'the loaded shcore exports GetDpiForMonitor');

  Section('GetDpiForMonitor');
  Mon := MonitorFromPoint(Point(0, 0), MONITOR_DEFAULTTOPRIMARY);
  Check(Mon <> 0, 'MonitorFromPoint((0, 0), MONITOR_DEFAULTTOPRIMARY)');
  Wnd := CreateWindowExW(0, 'STATIC', nil, WS_POPUP, 0, 0, 100, 100, 0, 0, HINST(HInstance), nil);
  Check(Wnd <> 0, 'a hidden top-level window at the origin');
  CheckEqI(Int64(Mon), Int64(MonitorFromWindow(Wnd, MONITOR_DEFAULTTONEAREST)), 'the window is on the primary monitor');
  X := 0;
  Y := 0;
  Hr := GetDpiForMonitor(Mon, MDT_EFFECTIVE_DPI, X, Y);
  CheckEqI(S_OK, Hr, 'GetDpiForMonitor(primary, MDT_EFFECTIVE_DPI) returns S_OK');
  CheckEqI(X, Y, 'its horizontal and vertical values are equal');
  CheckEqI(GetDpiForWindow(Wnd), X, 'they equal GetDpiForWindow of the window');
  X := 0;
  Y := 0;
  CheckEqI(S_OK, GetDpiForMonitor(Mon, MDT_DEFAULT, X, Y), 'GetDpiForMonitor(primary, MDT_DEFAULT) returns S_OK');
  CheckEqI(GetDpiForWindow(Wnd), X, 'MDT_DEFAULT gives the effective value');
  Check(GetDpiForMonitor(Mon, 3, X, Y) < 0, 'a DPI type outside MONITOR_DPI_TYPE gives a failure code');
  Check(GetDpiForMonitor(0, MDT_EFFECTIVE_DPI, X, Y) < 0, 'the null monitor handle gives a failure code');

  Section('MONITOR_DPI_TYPE');
  CheckEqI(0, MDT_EFFECTIVE_DPI, 'MDT_EFFECTIVE_DPI');
  CheckEqI(1, MDT_ANGULAR_DPI, 'MDT_ANGULAR_DPI');
  CheckEqI(2, MDT_RAW_DPI, 'MDT_RAW_DPI');
  CheckEqI(MDT_EFFECTIVE_DPI, MDT_DEFAULT, 'MDT_DEFAULT = MDT_EFFECTIVE_DPI');

  Section('cleanup');
  Check(DestroyWindow(Wnd), 'DestroyWindow');
  Done;
end.
