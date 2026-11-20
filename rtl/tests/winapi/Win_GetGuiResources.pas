// phase: 4
// covers: RTL-820, RTL-860, RTL-862
program Win_GetGuiResources;
{ Windows unit, user32 part (winapi.md section 5.3): GetGuiResources and its GR_* flags (task R-212). The counts
  are relative to the program's own objects, so the checks hold on any host: a memory DC it creates adds one GDI
  object and deleting it removes it again (a deleted solid brush stays counted: it may stay in the process's GDI
  handle cache); a window it creates adds at least one user object; a peak is never below the current count. }
uses Windows, TestKit;

var
  Process: THandle;
  Gdi, User: DWORD;
  DC: HDC;
  Wnd: HWND;

begin
  Section('GR_* values');
  CheckEqI(0, GR_GDIOBJECTS, 'GR_GDIOBJECTS');
  CheckEqI(1, GR_USEROBJECTS, 'GR_USEROBJECTS');
  CheckEqI(2, GR_GDIOBJECTS_PEAK, 'GR_GDIOBJECTS_PEAK');
  CheckEqI(4, GR_USEROBJECTS_PEAK, 'GR_USEROBJECTS_PEAK');

  Section('GDI objects');
  Process := GetCurrentProcess;
  Gdi := GetGuiResources(Process, GR_GDIOBJECTS);
  DC := CreateCompatibleDC(0);
  Check(DC <> 0, 'CreateCompatibleDC');
  CheckEqI(Gdi + 1, GetGuiResources(Process, GR_GDIOBJECTS), 'a memory DC adds one GDI object');
  Check(GetGuiResources(Process, GR_GDIOBJECTS) > 0, 'GDI count is positive');
  Check(GetGuiResources(Process, GR_GDIOBJECTS_PEAK) >= Gdi + 1, 'GDI peak is at least the count');
  Check(DeleteDC(DC), 'DeleteDC');
  CheckEqI(Gdi, GetGuiResources(Process, GR_GDIOBJECTS), 'deleting it removes it');

  Section('user objects');
  User := GetGuiResources(Process, GR_USEROBJECTS);
  Wnd := CreateWindowExW(0, 'STATIC', '', WS_POPUP, 0, 0, 10, 10, 0, 0, HINST(HInstance), nil);
  Check(Wnd <> 0, 'CreateWindowExW STATIC');
  Check(GetGuiResources(Process, GR_USEROBJECTS) > User, 'a window adds a user object');
  Check(GetGuiResources(Process, GR_USEROBJECTS_PEAK) >= GetGuiResources(Process, GR_USEROBJECTS),
    'user peak is at least the count');
  Check(DestroyWindow(Wnd), 'DestroyWindow');
  Done;
end.
