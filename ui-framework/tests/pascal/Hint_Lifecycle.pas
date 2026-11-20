// covers: UIF-940, UIF-941, UIF-942, UIF-943, UIF-944, UIF-945, UIF-946, UIF-947, UIF-948
// needs: desktop
program Hint_Lifecycle;
{ U-079, actions-menus.md section 4: short and long hint parts; the effective hint control (first non-empty Hint up
  the parent chain) and the effective ShowHint; Application.Hint follows the control under the mouse and the
  highlighted menu item (OnHint on every change, cleared when the menu closes); the hint window (class, styles,
  size from CalcHintRect, colour, text, position below the cursor, kept inside the work area, above the cursor
  when it would cover it); CM_HINTSHOW, the action's OnHint and OnShowHint in that order, each able to change or
  cancel the hint; the conditions (Application.ShowHint, effective ShowHint, active application); the timer life
  cycle with shortened pauses: shown after HintPause, HintShortPause right after a hint was visible, hidden after
  HintHidePause, on a key or mouse button message, when the mouse leaves, when the application deactivates, when
  the control is hidden (idle) or destroyed.
  The mouse is simulated by sending WM_MOUSEMOVE / WM_MOUSELEAVE to the windows; queued mouse-move and leave
  messages (real input, TrackMouseEvent) are swallowed by OnMessage so that the real cursor never interferes. The
  application is made active by sending WM_ACTIVATEAPP. Waits pump the loop until a condition holds, bounded by a
  guard timer. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, Menus, ActnList,
  TestKit;

const
  GuardTimer = 77;

type
  TLog = class
  public
    Text: string;
    GuardFired: Boolean;
    CancelCM: Boolean;
    ChangeCM: Boolean;
    ActionCanShow: Boolean;
    ShowCanShow: Boolean;
    MovePos: Boolean;
    NewPos: TPoint;
    OldProc: TWndMethod;
    IdleRan: Boolean;
    procedure Message(var Msg: TMsg; var Handled: Boolean);
    procedure AppHint(Sender: TObject);
    procedure ShowHint(var HintStr: string; var CanShow: Boolean; var HintInfo: THintInfo);
    procedure ActionHint(var HintStr: string; var CanShow: Boolean);
    procedure ButtonProc(var Message: TMessage);
    procedure Idle(Sender: TObject; var Done: Boolean);
  end;

var
  Log: TLog;
  F: TForm;

procedure TLog.Message(var Msg: TMsg; var Handled: Boolean);
begin
  if (Msg.message = WM_MOUSEMOVE) or (Msg.message = WM_MOUSELEAVE) or (Msg.message = WM_NCMOUSEMOVE)
    or (Msg.message = WM_NCMOUSELEAVE) then
    Handled := True;
  if (Msg.message = WM_TIMER) and (Msg.wParam = GuardTimer) then
  begin
    GuardFired := True;
    Handled := True;
  end;
end;

procedure TLog.AppHint(Sender: TObject);
begin
  Text := Text + 'hint:' + Application.Hint + ';';
end;

procedure TLog.ShowHint(var HintStr: string; var CanShow: Boolean; var HintInfo: THintInfo);
begin
  Text := Text + 'show:' + HintStr + ';';
  if HintInfo.HintControl <> nil then
    Text := Text + 'ctl:' + HintInfo.HintControl.Name + ';';
  CanShow := ShowCanShow;
  HintStr := HintStr + '!';
  if MovePos then
    HintInfo.HintPos := NewPos;
end;

procedure TLog.ActionHint(var HintStr: string; var CanShow: Boolean);
begin
  Text := Text + 'action:' + HintStr + ';';
  CanShow := ActionCanShow;
end;

procedure TLog.ButtonProc(var Message: TMessage);
begin
  if Message.Msg = CM_HINTSHOW then
  begin
    Text := Text + 'cm;';
    if ChangeCM then
      PHintInfo(Message.LParam)^.HintStr := 'changed';
    if CancelCM then
    begin
      Message.Result := 1;
      Exit;
    end;
  end;
  OldProc(Message);
end;

procedure TLog.Idle(Sender: TObject; var Done: Boolean);
begin
  IdleRan := True;
  PostMessageW(Application.Handle, WM_NULL, 0, 0);
end;

type
  THintAccess = class(THintWindow);

function ClassNameOf(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := GetClassNameW(H, @Buf[0], 128);
  SetString(Result, PWideChar(@Buf[0]), N);
end;

function HintWindow: THintWindow;
var
  I: Integer;
begin
  Result := nil;
  for I := 0 to Application.ComponentCount - 1 do
    if Application.Components[I] is THintWindow then
      Result := THintWindow(Application.Components[I]);
end;

function HintShown: Boolean;
var
  W: THintWindow;
begin
  W := HintWindow;
  Result := (W <> nil) and W.HandleAllocated and IsWindowVisible(W.Handle);
end;

function HintText: string;
var
  Buffer: array[0..255] of WideChar;
begin
  Result := '';
  if HintWindow <> nil then
    SetString(Result, PWideChar(@Buffer[0]), GetWindowTextW(HintWindow.Handle, @Buffer[0], Length(Buffer)));
end;

{ pumps the loop (waiting for messages, timers included) until the hint window is (in)visible; the guard timer
  bounds the wait }
function WaitHint(Visible: Boolean): Boolean;
var
  I: Integer;
begin
  Log.GuardFired := False;
  SetTimer(F.Handle, GuardTimer, 4000, nil);
  for I := 1 to 1000 do
  begin
    if (HintShown = Visible) or Log.GuardFired then
      Break;
    Application.HandleMessage;
  end;
  KillTimer(F.Handle, GuardTimer);
  Result := HintShown = Visible;
end;

procedure RunIdle;
var
  I: Integer;
begin
  Application.OnIdle := Log.Idle;
  Log.IdleRan := False;
  for I := 1 to 50 do
  begin
    Application.HandleMessage;
    if Log.IdleRan then
      Break;
  end;
  Application.OnIdle := nil;
  Application.ProcessMessages;
end;

{ the simulated mouse: a WM_MOUSEMOVE at a client position of the window }
procedure MoveTo(W: TWinControl; X, Y: Integer);
begin
  SendMessageW(W.Handle, WM_MOUSEMOVE, 0, MakeLParam(X, Y));
end;

function MonitorAt(const P: TPoint): TMonitor;
var
  I: Integer;
  R: TRect;
begin
  Result := Screen.Monitors[0];
  for I := 0 to Screen.MonitorCount - 1 do
  begin
    R := Screen.Monitors[I].BoundsRect;
    if PtInRect(R, P) then
      Result := Screen.Monitors[I];
  end;
end;

function S(V: Integer; const P: TPoint): Integer;
begin
  Result := LayoutMulDiv(V, MonitorAt(P).PixelsPerInch, 96);
end;

function RectStr(const R: TRect): string;
begin
  Result := Format('(%d, %d, %d, %d)', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function WindowRectOf(W: TWinControl): TRect;
begin
  GetWindowRect(W.Handle, Result);
end;

procedure Parts;
begin
  Section('short and long parts (UIF-940)');
  CheckEqS('a', GetShortHint('a|b'), 'short part');
  CheckEqS('b', GetLongHint('a|b'), 'long part');
  CheckEqS('ab', GetShortHint('ab'), 'no bar: short = whole');
  CheckEqS('ab', GetLongHint('ab'), 'no bar: long = whole');
  CheckEqS('a', GetShortHint('a|b|c'), 'first bar splits (short)');
  CheckEqS('b|c', GetLongHint('a|b|c'), 'first bar splits (long)');
  CheckEqS('', GetShortHint('|b'), 'empty short part');
  CheckEqS('', GetLongHint(''), 'empty hint');
end;

procedure Run;
var
  P, Inner, Q, N, NChild, Tmp: TPanel;
  B: TButton;
  L: TLabel;
  A: TAction;
  W: THintWindow;
  Work, R, Expected: TRect;
  Pt: TPoint;
  Style, ExStyle: NativeInt;
  M: TMainMenu;
  MTop, MItem: TMenuItem;
  PM: TPopupMenu;
  PItem: TMenuItem;
  Bmp: TBitmap;
begin
  Section('defaults (application-forms.md 2.1)');
  CheckEqS('', Application.Hint, 'Hint');
  CheckEqB(True, Application.ShowHint, 'ShowHint');
  CheckEqI(500, Application.HintPause, 'HintPause');
  CheckEqI(2500, Application.HintHidePause, 'HintHidePause');
  CheckEqI(50, Application.HintShortPause, 'HintShortPause');
  CheckEqB(True, Application.HintColor = clInfoBk, 'HintColor clInfoBk');
  CheckEqB(True, HintWindowClass = THintWindow, 'HintWindowClass');
  CheckEqB(True, HintWindow = nil, 'no hint window before the first hint');

  F := TForm.CreateNew(nil);
  F.Name := 'F';
  F.SetBounds(100, 100, 400, 300);
  F.ShowHint := True;
  P := TPanel.Create(F);
  P.Name := 'P';
  P.SetBounds(8, 8, 150, 100);
  P.Hint := 'Pshort|Plong';
  P.Parent := F;
  Inner := TPanel.Create(F);
  Inner.Name := 'Inner';
  Inner.SetBounds(8, 8, 60, 40);
  Inner.Parent := P;
  B := TButton.Create(F);
  B.Name := 'B';
  B.SetBounds(200, 8, 80, 25);
  B.Hint := 'Bonly';
  B.Parent := F;
  Q := TPanel.Create(F);
  Q.Name := 'Q';
  Q.SetBounds(200, 50, 80, 40);
  Q.Parent := F;
  N := TPanel.Create(F);
  N.Name := 'N';
  N.SetBounds(8, 150, 150, 100);
  N.Hint := 'nope';
  N.ShowHint := False;
  N.Parent := F;
  NChild := TPanel.Create(F);
  NChild.Name := 'NChild';
  NChild.SetBounds(8, 8, 60, 40);
  NChild.Hint := 'child';
  NChild.Parent := N;
  L := TLabel.Create(F);
  L.Name := 'L';
  L.SetBounds(200, 150, 60, 20);
  L.Hint := 'Lshort|Llong';
  L.Parent := F;

  Application.OnMessage := Log.Message;
  Application.OnHint := Log.AppHint;
  F.Visible := True;
  Application.ProcessMessages;
  SendMessageW(F.Handle, WM_ACTIVATEAPP, 1, 0);
  CheckEqB(True, Application.Active, 'application active (WM_ACTIVATEAPP)');
  Application.HintPause := 60000;
  Application.HintHidePause := 60000;

  Section('effective hint control and Application.Hint (UIF-941, UIF-942)');
  Log.Text := '';
  MoveTo(Inner, 5, 5);
  CheckEqS('Plong', Application.Hint, 'a child without Hint: the parent''s long hint');
  CheckEqS('hint:Plong;', Log.Text, 'OnHint fired once');
  Log.Text := '';
  MoveTo(P, 100, 80);
  CheckEqS('', Log.Text, 'same hint text: no OnHint');
  MoveTo(B, 5, 5);
  CheckEqS('hint:Bonly;', Log.Text, 'no bar: the whole hint');
  Log.Text := '';
  MoveTo(F, 210, 155);
  CheckEqS('hint:Llong;', Log.Text, 'graphic control under the mouse');
  Log.Text := '';
  MoveTo(Q, 5, 5);
  CheckEqS('hint:;', Log.Text, 'no hint up the chain: cleared');
  MoveTo(NChild, 5, 5);
  CheckEqS('child', Application.Hint, 'Application.Hint does not depend on ShowHint');

  Section('conditions (UIF-941, UIF-943)');
  Pt := Point(MonitorAt(Point(0, 0)).WorkareaRect.Left + 100, MonitorAt(Point(0, 0)).WorkareaRect.Top + 100);
  Application.ActivateHint(Pt);
  CheckEqB(False, HintShown, 'ParentShowHint with a parent whose ShowHint is False: no hint');
  MoveTo(N, 100, 80);
  Application.ActivateHint(Pt);
  CheckEqB(False, HintShown, 'own ShowHint False: no hint');
  MoveTo(B, 5, 5);
  Application.ShowHint := False;
  Application.ActivateHint(Pt);
  CheckEqB(False, HintShown, 'Application.ShowHint False: no hint');
  Application.ShowHint := True;
  SendMessageW(F.Handle, WM_ACTIVATEAPP, 0, 0);
  Application.ActivateHint(Pt);
  CheckEqB(False, HintShown, 'inactive application: no hint');
  SendMessageW(F.Handle, WM_ACTIVATEAPP, 1, 0);

  Section('window (UIF-944, UIF-945, UIF-947, UIF-948)');
  Application.ActivateHint(Pt);
  CheckEqB(True, HintShown, 'ActivateHint shows at once');
  W := HintWindow;
  CheckEqB(True, W.Owner = Application, 'owned by Application');
  CheckEqS('Bonly', HintText, 'the short hint');
  CheckEqS('PN.HintWindow', ClassNameOf(W.Handle), 'window class');
  Style := GetWindowLongPtrW(W.Handle, GWL_STYLE);
  ExStyle := GetWindowLongPtrW(W.Handle, GWL_EXSTYLE);
  CheckEqB(True, (Style and (WS_POPUP or WS_BORDER)) = (WS_POPUP or WS_BORDER), 'WS_POPUP or WS_BORDER');
  CheckEqB(False, (Style and WS_CHILD) <> 0, 'not a child window');
  CheckEqB(True, (ExStyle and (WS_EX_TOOLWINDOW or WS_EX_TOPMOST or WS_EX_NOACTIVATE))
    = (WS_EX_TOOLWINDOW or WS_EX_TOPMOST or WS_EX_NOACTIVATE), 'WS_EX_TOOLWINDOW, TOPMOST, NOACTIVATE');
  CheckEqB(True, GetForegroundWindow <> W.Handle, 'never the foreground window');
  CheckEqB(True, THintAccess(W).Color = clInfoBk, 'background HintColor');
  R := W.CalcHintRect((MonitorAt(Pt).BoundsRect.Right - MonitorAt(Pt).BoundsRect.Left) div 4, 'Bonly', nil);
  CheckEqB(True, (R.Left = 0) and (R.Top = 0) and (R.Right > 2) and (R.Bottom > 2), 'CalcHintRect at (0, 0)');
  Expected := Bounds(Pt.X, Pt.Y + S(20, Pt), R.Right, R.Bottom);
  CheckEqS(RectStr(Expected), RectStr(WindowRectOf(W)), 'below the cursor by s(20), CalcHintRect size');
  Bmp := TBitmap.Create;
  try
    Bmp.PixelFormat := pf32bit;
    Bmp.SetSize(R.Right, R.Bottom);
    SendMessageW(W.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT);
    CheckEqB(True, ColorToRGB(Bmp.Canvas.Pixels[0, 0]) = ColorToRGB(clInfoBk), 'painted background clInfoBk');
  finally
    Bmp.Free;
  end;
  CheckEqI(HTTRANSPARENT, SendMessageW(W.Handle, WM_NCHITTEST, 0, 0), 'transparent to the mouse');
  Work := MonitorAt(Pt).WorkareaRect;
  Pt := Point(Work.Right - 2, Work.Top + 100);
  Application.ActivateHint(Pt);
  CheckEqI(Work.Right, WindowRectOf(W).Right, 'kept inside the work area (right edge)');
  Pt := Point(Work.Left + 100, Work.Bottom - 3);
  Application.ActivateHint(Pt);
  CheckEqI(Pt.Y - S(2, Pt), WindowRectOf(W).Bottom, 'would cover the cursor: above it');
  CheckEqB(True, WindowRectOf(W).Bottom <= Work.Bottom, 'inside the work area');
  Application.HideHint;
  CheckEqB(False, HintShown, 'HideHint');
  Pt := Point(Work.Left + 100, Work.Top + 100);

  Section('CM_HINTSHOW, action OnHint, OnShowHint (UIF-943)');
  Log.OldProc := B.WindowProc;
  B.WindowProc := Log.ButtonProc;
  A := TAction.Create(F);
  A.Name := 'A';
  A.OnHint := Log.ActionHint;
  B.Action := A;
  CheckEqS('Bonly', B.Hint, 'the button keeps its own Hint');
  Application.OnShowHint := Log.ShowHint;
  Log.ActionCanShow := True;
  Log.ShowCanShow := True;
  Log.ChangeCM := True;
  Log.Text := '';
  Application.ActivateHint(Pt);
  CheckEqS('cm;action:changed;show:changed;ctl:B;', Log.Text, 'order: CM_HINTSHOW, action OnHint, OnShowHint');
  CheckEqS('changed!', HintText, 'the text after the handlers');
  Log.ChangeCM := False;
  Log.MovePos := True;
  Log.NewPos := Point(Pt.X + 50, Pt.Y + 60);
  Application.ActivateHint(Pt);
  CheckEqI(Pt.X + 50, WindowRectOf(W).Left, 'HintPos from OnShowHint (left)');
  CheckEqI(Pt.Y + 60, WindowRectOf(W).Top, 'HintPos from OnShowHint (top)');
  Log.MovePos := False;
  Log.ShowCanShow := False;
  Log.Text := '';
  Application.ActivateHint(Pt);
  CheckEqB(False, HintShown, 'OnShowHint CanShow False: hidden');
  Log.ShowCanShow := True;
  Log.ActionCanShow := False;
  Log.Text := '';
  Application.ActivateHint(Pt);
  CheckEqS('cm;action:Bonly;', Log.Text, 'action OnHint CanShow False: OnShowHint not called');
  CheckEqB(False, HintShown, 'action OnHint cancels');
  Log.ActionCanShow := True;
  Log.CancelCM := True;
  Log.Text := '';
  Application.ActivateHint(Pt);
  CheckEqS('cm;', Log.Text, 'CM_HINTSHOW result <> 0 cancels');
  CheckEqB(False, HintShown, 'no window');
  Log.CancelCM := False;
  Application.OnShowHint := nil;
  B.Action := nil;
  A.Free;
  B.WindowProc := Log.OldProc;

  Section('timers (UIF-943, UIF-946)');
  Application.HintPause := 1;
  MoveTo(Q, 5, 5);
  MoveTo(B, 5, 5);
  CheckEqB(False, HintShown, 'not at once');
  CheckEqB(True, WaitHint(True), 'shown after HintPause');
  CheckEqS('Bonly', HintText, 'text');
  PostMessageW(B.Handle, WM_KEYDOWN, VK_F24, 1);
  Application.ProcessMessages;
  CheckEqB(False, HintShown, 'hidden on a key message');
  MoveTo(Q, 5, 5);
  MoveTo(B, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown again after moving to another control');
  PostMessageW(Q.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(5, 5));
  PostMessageW(Q.Handle, WM_LBUTTONUP, 0, MakeLParam(5, 5));
  Application.ProcessMessages;
  CheckEqB(False, HintShown, 'hidden on a mouse button message');
  MoveTo(Q, 5, 5);
  MoveTo(B, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown');
  Log.Text := '';
  SendMessageW(B.Handle, WM_MOUSELEAVE, 0, 0);
  CheckEqB(False, HintShown, 'hidden when the mouse leaves');
  CheckEqS('hint:;', Log.Text, 'Application.Hint cleared when the mouse leaves');
  MoveTo(B, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown');
  SendMessageW(F.Handle, WM_ACTIVATEAPP, 0, 0);
  CheckEqB(False, HintShown, 'hidden when the application deactivates');
  SendMessageW(F.Handle, WM_ACTIVATEAPP, 1, 0);
  MoveTo(Q, 5, 5);
  MoveTo(B, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown');
  B.Visible := False;
  RunIdle;
  CheckEqB(False, HintShown, 'hidden at idle once the control is hidden');
  B.Visible := True;
  Tmp := TPanel.Create(F);
  Tmp.SetBounds(300, 200, 50, 50);
  Tmp.Hint := 'tmp';
  Tmp.Parent := F;
  MoveTo(Tmp, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown over a new control');
  Tmp.Free;
  CheckEqB(False, HintShown, 'hidden when the control is destroyed');
  CheckEqS('', Application.Hint, 'Application.Hint cleared');
  Application.HintHidePause := 1;
  MoveTo(Q, 5, 5);
  MoveTo(B, 5, 5);
  CheckEqB(True, WaitHint(True), 'shown');
  CheckEqB(True, WaitHint(False), 'hidden after HintHidePause');
  Application.HintHidePause := 60000;
  Application.HintPause := 60000;
  Application.HintShortPause := 1;
  Application.ActivateHint(Pt);
  CheckEqB(True, HintShown, 'a hint is visible');
  MoveTo(Inner, 5, 5);
  CheckEqB(False, HintShown, 'moving on hides it');
  CheckEqB(True, WaitHint(True), 'the next control''s hint after HintShortPause (HintPause is 60 s)');
  CheckEqS('Pshort', HintText, 'the new control''s short hint');
  Application.CancelHint;
  CheckEqB(False, HintShown, 'CancelHint');
  Application.HintShortPause := 50;

  Section('menu hints (UIF-942)');
  M := TMainMenu.Create(F);
  MTop := TMenuItem.Create(F);
  MTop.Caption := '&File';
  MTop.Hint := 'File|File commands';
  M.Items.Add(MTop);
  MItem := TMenuItem.Create(F);
  MItem.Caption := '&Open';
  MItem.Hint := 'Open|Opens a file';
  MTop.Add(MItem);
  F.Menu := M;
  Log.Text := '';
  SendMessageW(F.Handle, WM_MENUSELECT, MakeWParam(MItem.Command, MF_HILITE), LPARAM(MTop.Handle));
  CheckEqS('Opens a file', Application.Hint, 'highlighted command: its long hint');
  SendMessageW(F.Handle, WM_MENUSELECT, MakeWParam(0, MF_POPUP or MF_HILITE), LPARAM(M.Handle));
  CheckEqS('File commands', Application.Hint, 'highlighted sub-menu entry (by position)');
  SendMessageW(F.Handle, WM_MENUSELECT, MakeWParam(0, $FFFF), 0);
  CheckEqS('', Application.Hint, 'menu closed: cleared');
  CheckEqS('hint:Opens a file;hint:File commands;hint:;', Log.Text, 'OnHint on every change');
  PM := TPopupMenu.Create(F);
  PItem := TMenuItem.Create(F);
  PItem.Hint := 'Cut|Cuts the selection';
  PM.Items.Add(PItem);
  SendMessageW(PopupListWindow, WM_MENUSELECT, MakeWParam(PItem.Command, MF_HILITE), LPARAM(PM.Handle));
  CheckEqS('Cuts the selection', Application.Hint, 'pop-up menu item');
  SendMessageW(PopupListWindow, WM_MENUSELECT, MakeWParam(0, $FFFF), 0);
  CheckEqS('', Application.Hint, 'pop-up menu closed: cleared');

  Application.OnHint := nil;
  Application.OnMessage := nil;
  F.Free;
  CheckEqB(True, HintWindow <> nil, 'the hint window belongs to Application');
end;

begin
  Application.Initialize;
  Log := TLog.Create;
  try
    Parts;
    Run;
  finally
    Log.Free;
  end;
  Done;
end.
