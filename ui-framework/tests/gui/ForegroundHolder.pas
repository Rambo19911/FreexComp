// covers: UIF-1203
program ForegroundHolder;
{ U-126: a second process that takes the foreground away from the program under test, so that the harness's
  Gui.BringToForeground is tested against a foreground window owned by another process (what a parallel test run
  does). Plain Win32, no framework unit. One top-level window 'Foreground Holder' (class FxcForegroundHolder),
  shown with activation and asking for the foreground itself; WM_CLOSE ends it with exit code 0. U-127: the message
  WM_APP + 1 (sent, wParam = a top-level window) hands the foreground to that window while the holder has it (the
  foreground process may give it away, so the target is really activated); the result is 1 when SetForegroundWindow
  succeeded. }
uses
  Windows, Messages;

const
  HolderClass = 'FxcForegroundHolder';
  WM_HANDOVER = $8001;                                         { WM_APP + 1 }

var
  Main: HWND;

function WndProc(Wnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  case Msg of
    WM_HANDOVER:
      Result := LRESULT(Ord(SetForegroundWindow(HWND(wParam))));
    WM_CLOSE:
      DestroyWindow(Wnd);
    WM_DESTROY:
      PostQuitMessage(0);
  else
    Result := DefWindowProcW(Wnd, Msg, wParam, lParam);
  end;
end;

var
  WC: TWndClassExW;
  M: TMsg;
begin
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.hbrBackground := HBRUSH(COLOR_BTNFACE + 1);
  WC.lpszClassName := HolderClass;
  RegisterClassExW(WC);
  Main := CreateWindowExW(0, HolderClass, 'Foreground Holder', WS_OVERLAPPEDWINDOW, 200, 200, 300, 200, 0, 0,
    HINST(HInstance), nil);
  ShowWindow(Main, SW_SHOW);
  SetForegroundWindow(Main);
  while GetMessageW(M, 0, 0, 0) do
  begin
    TranslateMessage(M);
    DispatchMessageW(M);
  end;
  Halt(Integer(M.wParam));
end.
