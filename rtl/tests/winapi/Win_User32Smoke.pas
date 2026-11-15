// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-823, RTL-832
program Win_User32Smoke;
{ Windows unit, user32 part (winapi.md section 5.3): registers a class, creates a hidden top-level window with a
  child, pumps posted messages, and calls the user32 functions of classes, windows, window text and data,
  geometry, state, messages, accelerators and timers (input, painting, scroll bars, menus, icons, system and DPI,
  text helpers: Win_User32Input). Only results that do not depend on the machine, the desktop or the user are
  printed; the others are called and ignored. Functions that would show UI or make a sound are listed in
  README.md. The message numbers are declared here until the Messages unit exists (R-111). }
uses Windows, Types, TestKit;

{$POINTERMATH ON}

const
  WM_NULL = $0000;
  WM_CREATE = $0001;
  WM_DESTROY = $0002;
  WM_QUIT = $0012;
  WM_GETMINMAXINFO = $0024;
  WM_WINDOWPOSCHANGED = $0047;
  WM_NCCREATE = $0081;
  WM_KEYDOWN = $0100;
  WM_CHAR = $0102;
  WM_COMMAND = $0111;
  WM_USER = $0400;
  WM_TEST = WM_USER + 1;
  WM_POSTED = WM_USER + 2;
  WM_THREAD = WM_USER + 3;
  SmokeClass = 'FreexCompUser32Smoke';

var
  NcCreateSeen, MinMaxSeen, DestroySeen, ThreadSeen: Boolean;
  CreateParam: Pointer;
  PosChangedCx, PostedSum, CommandId, EnumCount: Integer;
  Main, Child: HWND;
  Cursor: HCURSOR;
  Icon: HICON;
  Acc: HACCEL;

function WndProc(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  case Msg of
    WM_NCCREATE:
      begin
        NcCreateSeen := True;
        Result := DefWindowProcW(hWnd, Msg, wParam, lParam);
      end;
    WM_CREATE: CreateParam := PCreateStructW(lParam)^.lpCreateParams;
    WM_GETMINMAXINFO: MinMaxSeen := PMinMaxInfo(lParam)^.ptMaxTrackSize.X > 0;
    WM_WINDOWPOSCHANGED: PosChangedCx := PWindowPos(lParam)^.cx;
    WM_TEST: Result := LRESULT(wParam) + lParam;
    WM_POSTED: PostedSum := PostedSum + Integer(wParam);
    WM_COMMAND: CommandId := LoWord(wParam);
    WM_DESTROY: DestroySeen := True;
  else
    Result := DefWindowProcW(hWnd, Msg, wParam, lParam);
  end;
end;

function CountWindow(Wnd: HWND; Data: LPARAM): BOOL; stdcall;
begin
  if Wnd = HWND(Data) then
    EnumCount := EnumCount + 1;
  Result := True;
end;

function BufStr(P: PWideChar): string;
begin
  Result := string(P);
end;

procedure Classes;
var
  WC, Info: TWndClassExW;
begin
  Section('classes');
  Cursor := LoadCursorW(0, IDC_ARROW);
  Check(Cursor <> 0, 'LoadCursorW');
  CheckEqI(Int64(Cursor), Int64(LoadCursor(0, IDC_ARROW)), 'LoadCursor alias, shared cursor');
  Icon := LoadIconW(0, IDI_APPLICATION);
  Check(Icon <> 0, 'LoadIconW');
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.style := CS_HREDRAW or CS_VREDRAW;
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.hIcon := Icon;
  WC.hCursor := Cursor;
  WC.hbrBackground := HBRUSH(COLOR_WINDOW + 1);
  WC.lpszClassName := SmokeClass;
  Check(RegisterClassExW(WC) <> 0, 'RegisterClassExW');
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  Check(GetClassInfoExW(HINST(HInstance), SmokeClass, Info), 'GetClassInfoExW');
  CheckEqI(CS_HREDRAW or CS_VREDRAW, Info.style, 'GetClassInfoExW style');
  CheckEqI(Int64(NativeUInt(@WndProc)), Int64(NativeUInt(Info.lpfnWndProc)), 'GetClassInfoExW lpfnWndProc');
end;

procedure Windows_;
var
  Buffer: array[0..63] of WideChar;
  R: TRect;
  P: TPoint;
  Pid: DWORD;
  Dwp: HDWP;
  WP: TWindowPlacement;
begin
  Section('windows');
  Main := CreateWindowExW(0, SmokeClass, 'Smoke', WS_OVERLAPPEDWINDOW or WS_VSCROLL, 100, 100, 300, 200, 0, 0,
    HINST(HInstance), Pointer(1234));
  Check(Main <> 0, 'CreateWindowExW');
  CheckEqB(True, NcCreateSeen, 'WM_NCCREATE');
  CheckEqB(True, MinMaxSeen, 'WM_GETMINMAXINFO TMinMaxInfo');
  CheckEqI(1234, NativeInt(CreateParam), 'WM_CREATE TCreateStructW.lpCreateParams');
  Child := CreateWindowEx(0, SmokeClass, 'Child', WS_CHILD, 10, 20, 50, 40, Main, HMENU(7), HINST(HInstance), nil);
  Check(Child <> 0, 'CreateWindowEx alias, child');
  CheckEqB(True, IsWindow(Main), 'IsWindow');
  CheckEqB(False, IsWindowVisible(Main), 'IsWindowVisible');
  CheckEqB(True, IsWindowEnabled(Main), 'IsWindowEnabled');
  CheckEqB(False, IsIconic(Main), 'IsIconic');
  CheckEqB(False, IsZoomed(Main), 'IsZoomed');
  CheckEqB(True, IsChild(Main, Child), 'IsChild');
  CheckEqI(Int64(Main), Int64(GetParent(Child)), 'GetParent');
  CheckEqI(Int64(Main), Int64(GetAncestor(Child, GA_PARENT)), 'GetAncestor');
  CheckEqI(Int64(Child), Int64(GetWindow(Main, GW_CHILD)), 'GetWindow GW_CHILD');
  Check(GetDesktopWindow <> 0, 'GetDesktopWindow');
  CheckEqI(Int64(GetDesktopWindow), Int64(GetAncestor(Main, GA_PARENT)), 'top-level parent is the desktop');
  CheckEqI(Int64(Main), Int64(SetParent(Child, Main)), 'SetParent returns the old parent');
  CheckEqI(GetCurrentThreadId, GetWindowThreadProcessId(Main, @Pid), 'GetWindowThreadProcessId thread');
  CheckEqI(GetCurrentProcessId, Pid, 'GetWindowThreadProcessId process');
  CheckEqI(20, GetClassNameW(Main, @Buffer[0], 64), 'GetClassNameW length');
  CheckEqS(SmokeClass, BufStr(@Buffer[0]), 'GetClassNameW');
  CheckEqI(7, GetDlgCtrlID(Child), 'GetDlgCtrlID');
  GetNextDlgTabItem(Main, Child, False);

  Section('window text and data');
  Check(SetWindowTextW(Main, 'Hello'), 'SetWindowTextW');
  CheckEqI(5, GetWindowTextLengthW(Main), 'GetWindowTextLengthW');
  CheckEqI(5, GetWindowTextW(Main, @Buffer[0], 64), 'GetWindowTextW length');
  CheckEqS('Hello', BufStr(@Buffer[0]), 'GetWindowTextW');
  CheckEqI(Int64(Main), Int64(FindWindowW(SmokeClass, 'Hello')), 'FindWindowW');
  CheckEqI(Int64(Child), Int64(FindWindowExW(Main, 0, SmokeClass, nil)), 'FindWindowExW');
  Check((GetWindowLongPtrW(Main, GWL_STYLE) and WS_CAPTION) = WS_CAPTION, 'GetWindowLongPtrW GWL_STYLE');
  CheckEqI(0, SetWindowLongPtrW(Main, GWLP_USERDATA, 99), 'SetWindowLongPtrW previous');
  CheckEqI(99, GetWindowLongPtr(Main, GWLP_USERDATA), 'GetWindowLongPtr alias');
  CheckEqI(Int64(Cursor), Int64(GetClassLongPtrW(Main, GCLP_HCURSOR)), 'GetClassLongPtrW');
  CheckEqI(Int64(Cursor), Int64(SetClassLongPtrW(Main, GCLP_HCURSOR, LONG_PTR(Cursor))), 'SetClassLongPtrW');
  Check(SetPropW(Main, 'FreexCompProp', 42), 'SetPropW');
  CheckEqI(42, Int64(GetPropW(Main, 'FreexCompProp')), 'GetPropW');
  CheckEqI(42, Int64(RemovePropW(Main, 'FreexCompProp')), 'RemovePropW');
  CheckEqI(0, Int64(GetPropW(Main, 'FreexCompProp')), 'property removed');

  Section('geometry');
  Check(GetClientRect(Child, R), 'GetClientRect');
  CheckEqI(0, R.Left, 'client Left');
  CheckEqI(50, R.Right, 'client Right');
  CheckEqI(40, R.Bottom, 'client Bottom');
  Check(GetWindowRect(Child, R), 'GetWindowRect');
  P := Point(R.Left, R.Top);
  Check(ScreenToClient(Main, P), 'ScreenToClient');
  CheckEqI(10, P.X, 'ScreenToClient X');
  CheckEqI(20, P.Y, 'ScreenToClient Y');
  Check(ClientToScreen(Main, P), 'ClientToScreen');
  CheckEqI(R.Left, P.X, 'ClientToScreen X');
  P := Point(0, 0);
  MapWindowPoints(Child, Main, @P, 1);
  CheckEqI(10, P.X, 'MapWindowPoints X');
  CheckEqI(20, P.Y, 'MapWindowPoints Y');
  Check(SetWindowPos(Child, HWND_TOP, 5, 6, 60, 30, SWP_NOZORDER or SWP_NOACTIVATE), 'SetWindowPos');
  CheckEqI(60, PosChangedCx, 'WM_WINDOWPOSCHANGED TWindowPos.cx');
  Check(MoveWindow(Child, 10, 20, 50, 40, False), 'MoveWindow');
  CheckEqI(50, PosChangedCx, 'MoveWindow cx');
  Dwp := BeginDeferWindowPos(1);
  Check(Dwp <> 0, 'BeginDeferWindowPos');
  Dwp := DeferWindowPos(Dwp, Child, 0, 10, 20, 70, 40, SWP_NOZORDER or SWP_NOACTIVATE);
  Check(Dwp <> 0, 'DeferWindowPos');
  Check(EndDeferWindowPos(Dwp), 'EndDeferWindowPos');
  CheckEqI(70, PosChangedCx, 'EndDeferWindowPos cx');
  CheckEqI(Int64(Child), Int64(ChildWindowFromPointEx(Main, Point(15, 25), CWP_ALL)), 'ChildWindowFromPointEx');
  WindowFromPoint(Point(0, 0));
  R := Rect(0, 0, 100, 100);
  Check(AdjustWindowRectEx(R, WS_OVERLAPPEDWINDOW, False, 0), 'AdjustWindowRectEx');
  Check((R.Left < 0) and (R.Top < 0) and (R.Right > 100) and (R.Bottom > 100), 'AdjustWindowRectEx grows');
  R := Rect(0, 0, 100, 100);
  Check(AdjustWindowRectExForDpi(R, WS_OVERLAPPEDWINDOW, False, 0, 96), 'AdjustWindowRectExForDpi');
  Check((R.Left < 0) and (R.Right > 100), 'AdjustWindowRectExForDpi grows');
  FillChar(WP, SizeOf(WP), 0);
  WP.length := SizeOf(WP);
  Check(GetWindowPlacement(Main, WP), 'GetWindowPlacement');
  CheckEqI(SizeOf(WP), WP.length, 'GetWindowPlacement length');
  { written back hidden: the returned showCmd would show the window }
  WP.showCmd := SW_HIDE;
  Check(SetWindowPlacement(Main, WP), 'SetWindowPlacement');
  CheckEqB(False, IsWindowVisible(Main), 'still hidden');

  Section('state');
  CheckEqB(False, EnableWindow(Main, False), 'EnableWindow was enabled');
  CheckEqB(False, IsWindowEnabled(Main), 'disabled');
  CheckEqB(True, EnableWindow(Main, True), 'EnableWindow was disabled');
  CheckEqB(False, ShowWindow(Main, SW_HIDE), 'ShowWindow was hidden');
  Check(UpdateWindow(Main), 'UpdateWindow');
  Check(BringWindowToTop(Child), 'BringWindowToTop');
  FlashWindow(Main, False);
  GetForegroundWindow;
  SetForegroundWindow(0);
  EnumCount := 0;
  EnumChildWindows(Main, @CountWindow, LPARAM(Child));
  CheckEqI(1, EnumCount, 'EnumChildWindows');
  Check(EnumThreadWindows(GetCurrentThreadId, @CountWindow, LPARAM(Main)), 'EnumThreadWindows');
  CheckEqI(2, EnumCount, 'EnumThreadWindows found Main');
end;

procedure Messages_;
var
  M: TMsg;
  R: DWORD_PTR;
  Id: UINT;
  Accel: array[0..0] of TAccel;
begin
  Section('sent messages');
  Id := RegisterWindowMessageW('FreexComp.User32Smoke');
  Check(Id >= $C000, 'RegisterWindowMessageW');
  CheckEqI(Id, RegisterWindowMessage('FreexComp.User32Smoke'), 'RegisterWindowMessage alias, same id');
  CheckEqI(11, SendMessageW(Main, WM_TEST, 5, 6), 'SendMessageW');
  CheckEqI(13, SendMessage(Main, WM_TEST, 6, 7), 'SendMessage alias');
  CheckEqI(5, CallWindowProcW(@WndProc, Main, WM_TEST, 2, 3), 'CallWindowProcW');
  CheckEqI(0, DefWindowProcW(Main, WM_TEST, 2, 3), 'DefWindowProcW');
  R := 0;
  Check(SendMessageTimeoutW(Main, WM_TEST, 7, 8, SMTO_NORMAL, 5000, @R) <> 0, 'SendMessageTimeoutW');
  CheckEqI(15, R, 'SendMessageTimeoutW result');

  Section('accelerators and keys');
  Accel[0].fVirt := FVIRTKEY;
  Accel[0].key := VK_F5;
  Accel[0].cmd := 100;
  Acc := CreateAcceleratorTableW(@Accel[0], 1);
  Check(Acc <> 0, 'CreateAcceleratorTableW');
  FillChar(M, SizeOf(M), 0);
  M.hwnd := Main;
  M.message := WM_KEYDOWN;
  M.wParam := VK_F5;
  M.lParam := 1;
  Check(TranslateAcceleratorW(Main, Acc, M) <> 0, 'TranslateAcceleratorW');
  CheckEqI(100, CommandId, 'accelerator WM_COMMAND');
  Check(DestroyAcceleratorTable(Acc), 'DestroyAcceleratorTable');
  M.wParam := Ord('A');
  Check(TranslateMessage(M), 'TranslateMessage');
  while PeekMessageW(M, 0, WM_CHAR, WM_CHAR, PM_REMOVE) do
    ;
  FillChar(M, SizeOf(M), 0);
  CheckEqB(False, IsDialogMessageW(Main, M), 'IsDialogMessageW of a foreign message');

  Section('posted messages');
  Check(PostMessageW(Main, WM_POSTED, 1, 0), 'PostMessageW');
  Check(WaitMessage, 'WaitMessage');
  Check(PostMessage(Main, WM_POSTED, 2, 0), 'PostMessage alias');
  Check(PostThreadMessageW(GetCurrentThreadId, WM_THREAD, 3, 0), 'PostThreadMessageW');
  CheckEqI(WAIT_OBJECT_0, MsgWaitForMultipleObjects(0, nil, False, 0, QS_POSTMESSAGE), 'MsgWaitForMultipleObjects');
  Check((HiWord(GetQueueStatus(QS_POSTMESSAGE)) and QS_POSTMESSAGE) <> 0, 'GetQueueStatus');
  while PeekMessageW(M, 0, 0, 0, PM_REMOVE) do
  begin
    if (M.hwnd = 0) and (M.message = WM_THREAD) then
      ThreadSeen := True;
    DispatchMessageW(M);
  end;
  CheckEqI(3, PostedSum, 'DispatchMessageW');
  CheckEqB(True, ThreadSeen, 'thread message');
  GetMessageTime;
  GetMessagePos;
  PostQuitMessage(5);
  while GetMessageW(M, 0, 0, 0) do
    DispatchMessageW(M);
  CheckEqI(WM_QUIT, M.message, 'GetMessageW WM_QUIT');
  CheckEqI(5, M.wParam, 'PostQuitMessage exit code');

  Section('timers');
  CheckEqI(1, SetTimer(Main, 1, 10, nil), 'SetTimer');
  Check(KillTimer(Main, 1), 'KillTimer');
end;

procedure Cleanup;
begin
  Section('cleanup');
  Check(DestroyWindow(Main), 'DestroyWindow');
  CheckEqB(True, DestroySeen, 'WM_DESTROY');
  CheckEqB(False, IsWindow(Main), 'window destroyed');
  CheckEqB(False, IsWindow(Child), 'child destroyed with its parent');
  Check(UnregisterClassW(SmokeClass, HINST(HInstance)), 'UnregisterClassW');
end;

begin
  { the result depends on the manifest (the call fails when the awareness is already set) }
  SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
  Classes;
  Windows_;
  Messages_;
  Cleanup;
  Done;
end.
