// covers: UIF-460, UIF-461, UIF-462, UIF-463, UIF-465, UIF-471
// needs: desktop
program App_Exceptions;
{ U-047, application-forms.md 8: an exception in a window message handler or in OnIdle reaches
  Application.HandleException and never unwinds through Win32; EAbort is silent; OnException receives the others;
  without OnException the non-interactive report is the stderr line '<Title>: <Class>: <Message>' and ExitCode 1
  (this program therefore exits with 1, see App_Exceptions.exit and .err.txt); a non-Exception object is reported
  with its class name as the message; an exception raised inside OnException is swallowed and written to stderr;
  Forms installs ShowErrorProc (non-interactive: a stderr line) and Classes.ApplicationHandleException. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, TestKit;

const
  WM_TEST = WM_APP + 1;

type
  TLog = class
  public
    Text: string;
    RaiseInHandler: Boolean;
    IdleRaise: Boolean;
    procedure Exc(Sender: TObject; E: Exception);
    procedure Idle(Sender: TObject; var Done: Boolean);
  end;

  TTopWin = class(TWinControl)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure WndProc(var Message: TMessage); override;
  end;

var
  Log: TLog;

procedure TLog.Exc(Sender: TObject; E: Exception);
begin
  Text := Text + TComponent(Sender).Name + ':' + E.ClassName + ':' + E.Message + ';';
  if RaiseInHandler then
    raise EInvalidOperation.Create('second');
end;

procedure TLog.Idle(Sender: TObject; var Done: Boolean);
begin
  Done := False;
  if IdleRaise then
  begin
    IdleRaise := False;
    raise EConvertError.Create('in idle');
  end;
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
    case Message.WParam of
      1: raise EAbort.Create('silent');
      2: raise EConvertError.Create('bad');
      3: raise Exception.Create('boom');
      4: raise TStringList.Create;
    end
  else
    inherited WndProc(Message);
end;

function Send(C: TWinControl; Kind: WPARAM): string;
begin
  Log.Text := '';
  SendMessageW(C.Handle, WM_TEST, Kind, 0);
  Result := Log.Text + 'returned';
end;

procedure Run;
var
  W: TTopWin;
begin
  Log := TLog.Create;
  W := TTopWin.Create(nil);
  try
    W.Name := 'W';
    Application.Name := 'App';
    Application.Initialize;
    W.HandleNeeded;

    Section('hooks (UIF-400, UIF-471)');
    CheckEqB(True, Assigned(ShowErrorProc), 'ShowErrorProc installed');
    CheckEqB(True, Assigned(ApplicationHandleException), 'ApplicationHandleException installed');
    CheckEqB(True, Application.NonInteractive, 'non-interactive from FXC_UI_NONINTERACTIVE');
    CheckEqS('App_Exceptions', Application.Title, 'Title: executable name without extension');

    Section('OnException (UIF-460, UIF-463)');
    Application.OnException := Log.Exc;
    CheckEqS('returned', Send(W, 1), 'EAbort is silent');
    CheckEqS('W:EConvertError:bad;returned', Send(W, 2), 'OnException receives, Sender = the control');
    Application.OnIdle := Log.Idle;
    Log.IdleRaise := True;
    Log.Text := '';
    Application.HandleMessage;
    CheckEqS('App:EConvertError:in idle;', Log.Text, 'OnIdle is a guarded site');
    Application.OnIdle := nil;
    Log.RaiseInHandler := True;
    CheckEqS('W:EConvertError:bad;returned', Send(W, 2), 'exception inside OnException swallowed (UIF-465)');
    Log.RaiseInHandler := False;
    CheckEqI(0, ExitCode, 'ExitCode untouched so far');

    Section('default reporting (UIF-461, UIF-462)');
    Application.OnException := nil;
    CheckEqS('returned', Send(W, 3), 'reported to stderr, the message loop continues');
    CheckEqI(1, ExitCode, 'ExitCode set to 1');
    CheckEqS('returned', Send(W, 4), 'non-Exception object reported by class name');
    ShowErrorProc('fatal text');
    Check(True, 'ShowErrorProc writes a stderr line');
  finally
    W.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
