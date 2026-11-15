// covers: UIF-014, UIF-060, UIF-061
// needs: desktop
program Core_MessagePath;
{ U-024, core.md 7.1: a message sent to the window runs StdWndProc -> MainWndProc -> WindowProc -> WndProc ->
  (framework handler | Dispatch -> message method | DefaultHandler -> native procedure). A replaced WindowProc
  sees messages from SendMessageW and from Perform and can consume them; ids without a framework handler reach
  user 'message' methods through Dispatch, and 'inherited' there reaches the native default; an exception in a
  handler is passed to the application hook (the sender is the control, ExceptObject is the exception) and
  SendMessageW returns normally; text messages of handle-less controls are served from the field. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

const
  WM_PING = WM_USER + 1;
  WM_RAISE = WM_USER + 2;
  WM_PLAIN = WM_USER + 3;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    property Caption;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TMsgWin = class(TTestWin)
  public
    Pinged: Integer;
    procedure WMPing(var Message: TMessage); message WM_PING;
    procedure WMRaise(var Message: TMessage); message WM_RAISE;
    procedure WMSetText(var Message: TMessage); message WM_SETTEXT;
  end;

  TSpy = class
  public
    Old: TWndMethod;
    Log: string;
    ConsumeId: Cardinal;
    Reported: string;
    procedure Hook(var Message: TMessage);
    procedure HandleException(Sender: TObject);
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TMsgWin.WMPing(var Message: TMessage);
begin
  Pinged := Pinged + 1;
  Message.Result := 42;
end;

procedure TMsgWin.WMRaise(var Message: TMessage);
begin
  Message.Result := 7;
  raise EConvertError.Create('handler failed');
end;

procedure TMsgWin.WMSetText(var Message: TMessage);
begin
  Pinged := Pinged + 100;
  inherited;
end;

procedure TSpy.Hook(var Message: TMessage);
begin
  if Message.Msg >= WM_USER then
    Log := Log + IntToStr(Message.Msg - WM_USER) + ';';
  if (ConsumeId <> 0) and (Message.Msg = ConsumeId) then
  begin
    Message.Result := 99;
    Exit;
  end;
  Old(Message);
end;

procedure TSpy.HandleException(Sender: TObject);
var
  E: TObject;
begin
  E := ExceptObject;
  Reported := TComponent(Sender).Name + '/' + E.ClassName + '/' + Exception(E).Message;
end;

function NativeText(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 128, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

procedure Run;
var
  Root: TTopWin;
  W: TMsgWin;
  G: TControl;
  Spy: TSpy;
  H: HWND;
  R: LRESULT;
  Buf: array[0..7] of WideChar;
begin
  Spy := TSpy.Create;
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 300, 200);
    W := TMsgWin.Create(Root);
    W.Name := 'W';
    W.SetBounds(10, 10, 100, 50);
    W.Parent := Root;
    H := W.Handle;

    Section('Dispatch fallback to message methods (UIF-060)');
    R := SendMessageW(H, WM_PING, 0, 0);
    CheckEqI(1, W.Pinged, 'message method called');
    CheckEqI(42, R, 'its result returned to SendMessageW');
    CheckEqI(0, SendMessageW(H, WM_PLAIN, 0, 0), 'unhandled id: native default result');
    SendMessageW(H, WM_SETTEXT, 0, LPARAM(PChar('via inherited')));
    CheckEqI(101, W.Pinged, 'message method for a system message');
    CheckEqS('via inherited', NativeText(H), 'inherited reached the native default');
    CheckEqS('via inherited', W.Caption, 'Caption reads the native text');

    Section('WindowProc replacement (UIF-014)');
    Spy.Old := W.WindowProc;
    W.WindowProc := Spy.Hook;
    SendMessageW(H, WM_PING, 0, 0);
    W.Perform(WM_PLAIN, 0, 0);
    CheckEqS('1;3;', Spy.Log, 'hook saw SendMessageW and Perform');
    CheckEqI(2, W.Pinged - 100, 'old WindowProc still reached');
    Spy.ConsumeId := WM_PING;
    R := SendMessageW(H, WM_PING, 0, 0);
    CheckEqI(99, R, 'hook result returned');
    CheckEqI(2, W.Pinged - 100, 'consumed: handler not reached');
    Spy.ConsumeId := 0;
    W.WindowProc := Spy.Old;
    Spy.Log := '';
    SendMessageW(H, WM_PING, 0, 0);
    CheckEqS('', Spy.Log, 'restored WindowProc');

    Section('exceptions stop at MainWndProc (UIF-060, UIF-463)');
    ApplicationHandleException := Spy.HandleException;
    R := SendMessageW(H, WM_RAISE, 0, 0);
    CheckEqS('W/EConvertError/handler failed', Spy.Reported, 'reported with the control as sender');
    CheckEqI(7, R, 'SendMessageW returned normally with the result set so far');
    Spy.Reported := '';
    SendMessageW(H, WM_PING, 0, 0);
    CheckEqS('', Spy.Reported, 'later messages work');
    CheckEqB(True, IsWindow(H) and (FindControl(H) = W), 'window still attached');
    ApplicationHandleException := nil;

    Section('text messages without a handle');
    G := TControl.Create(Root);
    G.Parent := Root;
    G.Perform(WM_SETTEXT, 0, LPARAM(PChar('graphic text')));
    CheckEqI(12, G.Perform(WM_GETTEXTLENGTH, 0, 0), 'WM_GETTEXTLENGTH');
    R := G.Perform(WM_GETTEXT, 8, LPARAM(@Buf[0]));
    CheckEqI(7, R, 'WM_GETTEXT copies at most BufSize - 1');
    CheckEqB(True, Buf[7] = #0, 'terminated');
    CheckEqB(True, (Buf[0] = 'g') and (Buf[6] = 'c'), 'copied characters');
  finally
    Root.Free;
    Spy.Free;
  end;
end;

begin
  Run;
  Done;
end.
