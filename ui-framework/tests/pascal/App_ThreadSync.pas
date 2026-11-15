// covers: UIF-412, UIF-414
// needs: desktop
program App_ThreadSync;
{ U-052, application-forms.md UIF-412: Forms installs the WakeMainThread hook (a WM_NULL posted to
  Application.Handle), so a worker thread's Synchronize and Queue run in the main thread while it only processes
  messages (HandleMessage / ProcessMessages, no explicit CheckSynchronize); the loop also calls CheckSynchronize
  in Idle. Nothing here sleeps: the main thread waits in the message loop until the worker's last queued method ran. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, TestKit;

type
  TWorker = class(TThread)
  protected
    procedure Execute; override;
  public
    SyncThread, QueueThread: Cardinal;
    SyncRan, QueueRan: Boolean;
    Order: string;
    procedure InSync;
    procedure InQueue;
  end;

procedure TWorker.InSync;
begin
  SyncThread := GetCurrentThreadId;
  SyncRan := True;
  Order := Order + 'sync;';
end;

procedure TWorker.InQueue;
begin
  QueueThread := GetCurrentThreadId;
  QueueRan := True;
  Order := Order + 'queue;';
end;

procedure TWorker.Execute;
begin
  Synchronize(InSync);
  Order := Order + 'after sync;';
  Queue(Self, InQueue);
end;

procedure Run;
var
  W: TWorker;
begin
  Application.Initialize;
  Check(Assigned(WakeMainThread), 'WakeMainThread installed');

  Section('Synchronize and Queue through the message loop');
  W := TWorker.Create(False);
  try
    while not W.QueueRan do
      Application.HandleMessage;
    CheckEqB(True, W.SyncRan, 'Synchronize ran');
    CheckEqI(MainThreadID, W.SyncThread, 'Synchronize in the main thread');
    CheckEqI(MainThreadID, W.QueueThread, 'Queue in the main thread');
    CheckEqS('sync;after sync;queue;', W.Order, 'order');
    W.WaitFor;
  finally
    W.Free;
  end;

  Section('Synchronize during ProcessMessages');
  W := TWorker.Create(False);
  try
    while not W.QueueRan do
    begin
      MsgWaitForMultipleObjects(0, nil, False, INFINITE, QS_ALLINPUT);
      Application.ProcessMessages;
    end;
    CheckEqI(MainThreadID, W.SyncThread, 'ProcessMessages: Synchronize in the main thread');
    CheckEqI(MainThreadID, W.QueueThread, 'ProcessMessages: Queue in the main thread');
    W.WaitFor;
  finally
    W.Free;
  end;
end;

begin
  Run;
  Done;
end.
