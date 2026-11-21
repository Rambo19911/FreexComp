// covers: UIF-980
program GuiProbe;
{ U-106: the window program of GuiHarness_SelfTest (sample-apps.md section 2). Plain Win32, no framework unit, so
  the harness facilities are tested independently of the framework they later test. One top-level window
  'Probe Window' (class FxcGuiProbe, client 400 x 260 logical px scaled by the window's DPI) with an edit, a button
  '&Push', a static text that shows the event log and a status bar (part 0 'status text', read with RemoteText).
  Every message the harness sends or posts to the main window is appended to the log: 'kd<vk>' / 'ku<vk>' for
  WM_KEYDOWN / WM_KEYUP, 'ld<x>,<y>' / 'lu<x>,<y>' for the mouse button, 'cmd<id>,<code>' for WM_COMMAND from the
  button. stdout gets one line with the environment and the first argument; WM_CLOSE writes 'probe: close' to stderr;
  the exit code is 7. }
uses
  Windows, Messages, CommCtrl, Types, SysUtils;

const
  ProbeClass = 'FxcGuiProbe';
  IdEdit = 101;
  IdButton = 102;
  IdInfo = 103;
  IdStatus = 104;

var
  Main, Edit, Button, Info, Status: HWND;
  Events: UnicodeString;

procedure Log(const S: string);
begin
  if Events <> '' then
    Events := Events + ';';
  Events := Events + S;
  SetWindowTextW(Info, PWideChar(Events));
end;

function Px(V: Integer): Integer;
begin
  Result := MulDiv(V, Integer(GetDpiForWindow(Main)), 96);
end;

function WndProc(Wnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  case Msg of
    WM_COMMAND:
      if (lParam <> 0) and (LoWord(wParam) = IdButton) then           { not the edit's own EN_* notifications }
        Log(Format('cmd%d,%d', [Integer(LoWord(wParam)), Integer(HiWord(wParam))]))
      else
        Result := DefWindowProcW(Wnd, Msg, wParam, lParam);
    WM_KEYDOWN:
      Log(Format('kd%d', [Integer(wParam)]));
    WM_KEYUP:
      Log(Format('ku%d', [Integer(wParam)]));
    WM_LBUTTONDOWN:
      Log(Format('ld%d,%d', [Integer(SmallInt(LoWord(lParam))), Integer(SmallInt(HiWord(lParam)))]));
    WM_LBUTTONUP:
      Log(Format('lu%d,%d', [Integer(SmallInt(LoWord(lParam))), Integer(SmallInt(HiWord(lParam)))]));
    WM_CLOSE:
      begin
        WriteLn(ErrOutput, 'probe: close');
        DestroyWindow(Wnd);
      end;
    WM_DESTROY:
      PostQuitMessage(7);
  else
    Result := DefWindowProcW(Wnd, Msg, wParam, lParam);
  end;
end;

function NewChild(const ClassName, Text: UnicodeString; Style: DWORD; Id: Integer): HWND;
begin
  Result := CreateWindowExW(0, PWideChar(ClassName), PWideChar(Text), WS_CHILD or WS_VISIBLE or Style, 0, 0, 10, 10,
    Main, HMENU(Id), HINST(HInstance), nil);
end;

procedure Place(Wnd: HWND; L, T, W, H: Integer);
begin
  SetWindowPos(Wnd, 0, Px(L), Px(T), Px(L + W) - Px(L), Px(T + H) - Px(T), SWP_NOZORDER or SWP_NOACTIVATE);
end;

var
  WC: TWndClassExW;
  ICC: TInitCommonControlsEx;
  R: TRect;
  M: TMsg;
  Style: DWORD;
begin
  WriteLn('probe env=', GetEnvironmentVariable('FXC_UI_NONINTERACTIVE'), ' value=',
    GetEnvironmentVariable('PROBE_VALUE'), ' arg=', ParamStr(1));
  ICC.dwSize := SizeOf(ICC);
  ICC.dwICC := ICC_BAR_CLASSES;
  InitCommonControlsEx(ICC);
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.hbrBackground := HBRUSH(COLOR_BTNFACE + 1);
  WC.lpszClassName := ProbeClass;
  RegisterClassExW(WC);
  Style := WS_OVERLAPPEDWINDOW;
  Main := CreateWindowExW(0, ProbeClass, 'Probe Window', Style, 120, 120, 400, 300, 0, 0, HINST(HInstance), nil);
  R := Rect(0, 0, Px(400), Px(260));
  AdjustWindowRectExForDpi(R, Style, False, 0, GetDpiForWindow(Main));
  SetWindowPos(Main, 0, 0, 0, R.Right - R.Left, R.Bottom - R.Top, SWP_NOMOVE or SWP_NOZORDER or SWP_NOACTIVATE);
  Edit := NewChild('EDIT', '', WS_BORDER or ES_AUTOHSCROLL, IdEdit);
  Button := NewChild('BUTTON', '&Push', BS_PUSHBUTTON, IdButton);
  Info := NewChild('STATIC', '', 0, IdInfo);
  Status := NewChild(STATUSCLASSNAME, '', 0, IdStatus);
  Place(Edit, 10, 10, 150, 23);
  Place(Button, 170, 10, 75, 25);
  Place(Info, 10, 50, 360, 20);
  SendMessageW(Status, SB_SETTEXTW, 0, LPARAM(PWideChar(UnicodeString('status text'))));
  ShowWindow(Main, SW_SHOWNOACTIVATE);
  while GetMessageW(M, 0, 0, 0) do
  begin
    TranslateMessage(M);
    DispatchMessageW(M);
  end;
  Halt(Integer(M.wParam));
end.
