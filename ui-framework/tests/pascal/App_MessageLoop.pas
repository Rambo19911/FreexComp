// covers: UIF-402, UIF-410, UIF-411, UIF-412
// opc: -U ..\..\src
// needs: desktop
program App_MessageLoop;
{ U-034, application-forms.md 2.2 and 2.4: Forms creates the global Application; Initialize (idempotent) creates
  the hidden message-only window of class PN.Application; ProcessMessages handles every queued message, each seen
  first by OnMessage, which can swallow it (the window never gets it), and never runs idle handling; HandleMessage
  with an empty queue runs Idle: OnIdle, then WaitMessage while Done (returns on a new message); Terminate posts
  WM_QUIT, whose retrieval sets Terminated without reaching OnMessage; Run without a main form returns at once. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, TestKit;

const
  WM_TEST = WM_APP + 1;
  BCM_GETIDEALSIZE = $1601;                                 { version 6 button message }

type
  TLog = class
  public
    Text: string;
    Swallow: WPARAM;                                        { OnMessage swallows WM_TEST with this WParam }
    IdleDone: Boolean;
    IdlePost: Boolean;                                      { OnIdle posts a WM_TEST (wakes WaitMessage) }
    IdleTerminate: Boolean;
    procedure Message(var Msg: TMsg; var Handled: Boolean);
    procedure Idle(Sender: TObject; var Done: Boolean);
  end;

  TTopWin = class(TWinControl)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure WndProc(var Message: TMessage); override;
  end;

var
  Log: TLog;

procedure TLog.Message(var Msg: TMsg; var Handled: Boolean);
begin
  if (Msg.message = WM_TEST) or (Msg.message = WM_QUIT) then
  begin
    Text := Text + 'Msg(' + IntToStr(Integer(Msg.wParam)) + ');';
    Handled := (Msg.message = WM_TEST) and (Msg.wParam = Swallow);
  end;
end;

procedure TLog.Idle(Sender: TObject; var Done: Boolean);
begin
  Text := Text + 'Idle;';
  Done := IdleDone;
  if IdlePost then
  begin
    IdlePost := False;
    PostMessageW(Application.Handle, WM_TEST, 9, 0);
  end;
  if IdleTerminate then
    Application.Terminate;
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP;
  Params.WndParent := 0;
end;

procedure TTopWin.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_TEST then
    Log.Text := Log.Text + 'Wnd(' + IntToStr(Integer(Message.WParam)) + ');'
  else
    inherited WndProc(Message);
end;

function ClassOf(Wnd: HWND): string;
var
  Buffer: array[0..63] of Char;
begin
  GetClassNameW(Wnd, @Buffer[0], 64);
  Result := string(PChar(@Buffer[0]));
end;

procedure Run;
var
  W: TTopWin;
  H, Btn: HWND;
  Ideal: TSize;
  I: Integer;
begin
  Log := TLog.Create;
  W := TTopWin.Create(nil);
  try
    Section('initialize (UIF-402)');
    CheckEqB(True, Application <> nil, 'Application exists');
    Application.Initialize;
    H := Application.Handle;
    CheckEqB(True, IsWindow(H), 'Handle is a window');
    CheckEqS('PN.Application', ClassOf(H), 'window class');
    CheckEqB(True, FindWindowExW(HWND_MESSAGE, 0, 'PN.Application', nil) = H, 'message-only window');
    CheckEqB(False, IsWindowVisible(H), 'hidden');
    Application.Initialize;
    CheckEqB(True, Application.Handle = H, 'Initialize is idempotent');
    Btn := CreateWindowExW(0, 'BUTTON', 'b', WS_POPUP, 0, 0, 80, 25, 0, 0, HINST(HInstance), nil);
    Ideal.cx := 0;
    Ideal.cy := 0;
    CheckEqB(True, SendMessageW(Btn, BCM_GETIDEALSIZE, 0, LPARAM(@Ideal)) <> 0,
      'common controls 6 active (a version-6-only button message works)');
    DestroyWindow(Btn);
    W.HandleNeeded;
    Application.OnMessage := Log.Message;
    Application.OnIdle := Log.Idle;
    Application.ProcessMessages;

    Section('ProcessMessages and OnMessage (UIF-411)');
    Log.Text := '';
    PostMessageW(W.Handle, WM_TEST, 1, 0);
    PostMessageW(W.Handle, WM_TEST, 2, 0);
    PostMessageW(W.Handle, WM_TEST, 3, 0);
    Application.ProcessMessages;
    CheckEqS('Msg(1);Wnd(1);Msg(2);Wnd(2);Msg(3);Wnd(3);', Log.Text, 'all queued messages, OnMessage first, no idle');
    Log.Text := '';
    Log.Swallow := 2;
    PostMessageW(W.Handle, WM_TEST, 1, 0);
    PostMessageW(W.Handle, WM_TEST, 2, 0);
    Application.ProcessMessages;
    CheckEqS('Msg(1);Wnd(1);Msg(2);', Log.Text, 'OnMessage swallows');
    Log.Swallow := 0;
    Log.Text := '';
    PostMessageW(0, WM_TEST, 5, 0);
    Application.ProcessMessages;
    CheckEqS('Msg(5);', Log.Text, 'thread message: OnMessage only');

    Section('HandleMessage and Idle (UIF-412)');
    Log.Text := '';
    PostMessageW(W.Handle, WM_TEST, 4, 0);
    Application.HandleMessage;
    CheckEqS('Msg(4);Wnd(4);', Log.Text, 'a queued message: no idle');
    Log.Text := '';
    Log.IdleDone := False;
    Application.HandleMessage;
    CheckEqS('Idle;', Log.Text, 'empty queue: OnIdle');
    Log.Text := '';
    Log.IdleDone := True;
    Log.IdlePost := True;
    Application.HandleMessage;
    CheckEqS('Idle;', Log.Text, 'Done: WaitMessage returns on a new message');
    Log.Text := '';
    Application.ProcessMessages;
    CheckEqS('Msg(9);', Log.Text, 'the waking message is processed next');

    Section('Terminate (UIF-410)');
    CheckEqB(False, Application.Terminated, 'not terminated');
    Log.Text := '';
    Log.IdleDone := False;
    Log.IdleTerminate := True;
    I := 0;
    repeat
      Application.HandleMessage;
      Inc(I);
    until Application.Terminated or (I > 100);
    CheckEqB(True, Application.Terminated, 'Terminated after WM_QUIT');
    CheckEqS('Idle;', Log.Text, 'WM_QUIT does not reach OnMessage');
    CheckEqI(2, I, 'loop ended on the next message');
    Log.IdleTerminate := False;
    Application.Run;
    Check(True, 'Run without a main form returns');
  finally
    Application.OnMessage := nil;
    Application.OnIdle := nil;
    W.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
