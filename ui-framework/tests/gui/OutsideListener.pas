// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
program OutsideListener;
{ U-168: a UI Automation client of another process that listens, as a client test of another area does while the
  accessibility client tests run (FreexComp.UiFramework.Automation.Tests): it registers a focus-changed event
  handler on the desktop through the flat client API of the system's UI Automation core (UiaGetRootNode,
  UiaAddEvent; bound with GetProcAddress, the RTL's import definitions name only the provider functions), so that
  UiaClientsAreListening of every program becomes True, and prints 'listening' (flushed) once the handler is
  registered, after 'window <its window handle in hex>' ('failed <HRESULT in hex>' and exit code 1 when it could
  not be). Its handler does nothing. Plain Win32, no framework unit; one hidden top-level window (class
  FxcOutsideListener): WM_CLOSE removes the handler and ends it with exit code 0 (the test closes it so, as a client
  test of another area removes its handlers). }
uses
  Windows, Messages, ActiveX, SysUtils;

const
  ListenerClass = 'FxcOutsideListener';
  UIA_AutomationFocusChangedEventId = 20005;
  TreeScope_Element = 1;
  TreeScope_Subtree = 7;
  ConditionType_True = 0;
  AutomationElementMode_Full = 1;

type
  { UiaCondition and UiaCacheRequest of the flat client API }
  TUiaCondition = record
    ConditionType: Integer;
  end;

  TUiaCacheRequest = record
    pViewCondition: Pointer;
    Scope: Integer;
    pProperties: Pointer;
    cProperties: Integer;
    pPatterns: Pointer;
    cPatterns: Integer;
    automationElementMode: Integer;
  end;

  TUiaGetRootNode = function(out phnode: THandle): HResult; stdcall;
  TUiaAddEvent = function(hnode: THandle; eventId: Integer; pCallback: Pointer; scope: Integer; pProperties: Pointer;
    cProperties: Integer; pRequest: Pointer; out phEvent: THandle): HResult; stdcall;
  TUiaRemoveEvent = function(hEvent: THandle): HResult; stdcall;
  TUiaNodeRelease = function(hnode: THandle): BOOL; stdcall;

var
  Main: HWND;
  Root, Handler: THandle;
  Condition: TUiaCondition;
  Request: TUiaCacheRequest;

procedure FocusChanged(pArgs, pRequestedData, pTreeStructure: Pointer); stdcall;
begin
end;

procedure Say(const S: string);
begin
  WriteLn(S);
  Flush(Output);
end;

function WndProc(Wnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
begin
  Result := 0;
  case Msg of
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
  HR: HResult;
  Core: HMODULE;
  UiaGetRootNode: TUiaGetRootNode;
  UiaAddEvent: TUiaAddEvent;
  UiaRemoveEvent: TUiaRemoveEvent;
  UiaNodeRelease: TUiaNodeRelease;
begin
  CoInitializeEx(nil, COINIT_APARTMENTTHREADED);
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.lpszClassName := ListenerClass;
  RegisterClassExW(WC);
  Main := CreateWindowExW(0, ListenerClass, 'Outside Listener', WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, 0, 0,
    HINST(HInstance), nil);
  Core := LoadLibraryW('uiautomationcore.dll');
  UiaGetRootNode := TUiaGetRootNode(GetProcAddress(Core, 'UiaGetRootNode'));
  UiaAddEvent := TUiaAddEvent(GetProcAddress(Core, 'UiaAddEvent'));
  UiaRemoveEvent := TUiaRemoveEvent(GetProcAddress(Core, 'UiaRemoveEvent'));
  UiaNodeRelease := TUiaNodeRelease(GetProcAddress(Core, 'UiaNodeRelease'));
  HR := UiaGetRootNode(Root);
  if HR = S_OK then
  begin
    FillChar(Request, SizeOf(Request), 0);
    Condition.ConditionType := ConditionType_True;
    Request.pViewCondition := @Condition;
    Request.Scope := TreeScope_Element;
    Request.automationElementMode := AutomationElementMode_Full;
    HR := UiaAddEvent(Root, UIA_AutomationFocusChangedEventId, @FocusChanged, TreeScope_Subtree, nil, 0, @Request,
      Handler);
  end;
  if HR <> S_OK then
  begin
    Say('failed ' + IntToHex(HR, 8));
    Halt(1);
  end;
  Say('window ' + IntToHex(Int64(Main), 1));
  Say('listening');
  while GetMessageW(M, 0, 0, 0) do
  begin
    TranslateMessage(M);
    DispatchMessageW(M);
  end;
  UiaRemoveEvent(Handler);
  UiaNodeRelease(Root);
  Halt(Integer(M.wParam));
end.
