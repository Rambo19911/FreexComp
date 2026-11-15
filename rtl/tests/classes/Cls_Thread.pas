// phase: 4
// covers: RTL-510, RTL-511, RTL-512, RTL-513
program Cls_Thread;
{ TThread (classes.md section 11): Execute runs on its own thread, ReturnValue is the WaitFor result, an escaping
  exception becomes FatalException, Finished, OnTerminate through Synchronize, FreeOnTerminate (RTL-510);
  Synchronize from a thread runs on the main thread while it waits (WaitFor pumps), transports exceptions back and
  calls WakeMainThread, on the main thread it runs at once (RTL-511); Queue returns at once and runs in FIFO order
  at CheckSynchronize, exceptions go to ApplicationHandleException (RTL-512); CheckSynchronize off the main thread
  raises EThread (RTL-513); Start twice, suspended creation, Terminate, Destroy of a running and of a never started
  thread, CurrentThread. Waits use events, never timing. }
uses
  Windows, SysUtils, SyncObjs, Classes, TestKit;

var
  Log: string;
  Wakes: Integer;

type
  TMain = class
    procedure Wake(Sender: TObject);
    procedure Bump;
    procedure HandleExc(Sender: TObject);
    procedure Terminated(Sender: TObject);
  end;

  TWorker = class(TThread)
  public
    Kind: Integer;
    Msg: string;
    Me: TThread;
    Ran: Boolean;
    Go, Started: TEvent;
    destructor Destroy; override;
  protected
    procedure Execute; override;
    procedure OnMain;
    procedure Boom;
    procedure Q1;
    procedure Q2;
    procedure Q3;
  end;

var
  Freed: TEvent;

procedure TMain.Wake(Sender: TObject);
begin
  Wakes := Wakes + 1;
end;

procedure TMain.Bump;
begin
  Wakes := Wakes + 1;
end;

procedure TMain.HandleExc(Sender: TObject);
begin
  Log := Log + 'handled ' + Exception(ExceptObject).Message + ';';
end;

procedure TMain.Terminated(Sender: TObject);
begin
  Log := Log + 'terminated main=' + BoolToStr(GetCurrentThreadId = MainThreadID, True) + ' finished=' +
    BoolToStr(TThread(Sender).Finished, True) + ';';
end;

destructor TWorker.Destroy;
begin
  if Kind = 6 then
    Freed.SetEvent;
  inherited Destroy;
end;

procedure TWorker.OnMain;
begin
  Log := Log + 'sync main=' + BoolToStr(GetCurrentThreadId = MainThreadID, True) + ';';
end;

procedure TWorker.Boom;
begin
  raise Exception.Create('boom');
end;

procedure TWorker.Q1;
begin
  Log := Log + 'q1;';
end;

procedure TWorker.Q2;
begin
  raise Exception.Create('queued boom');
end;

procedure TWorker.Q3;
begin
  Log := Log + 'q3;';
end;

procedure TWorker.Execute;
var
  Method: TThreadMethod;
begin
  Ran := True;
  Me := TThread.CurrentThread;
  case Kind of
    0: ReturnValue := 42;
    1:
      begin
        Method := OnMain;   { a method passed straight to the overloaded Synchronize hits a compiler bug (reported) }
        Synchronize(Method);
      end;
    2:
      try
        Method := Boom;
        Synchronize(Method);
      except
        on E: Exception do
          Msg := 'caught ' + E.Message;
      end;
    3:
      begin
        TThread.Queue(Self, Q1);
        TThread.Queue(Self, Q2);
        TThread.Queue(Self, Q3);
      end;
    4: raise Exception.Create('fatal');
    5:
      begin
        Started.SetEvent;
        Go.WaitFor(INFINITE);
        ReturnValue := Ord(Terminated);
      end;
    6: ReturnValue := 6;
    7:
      while not Terminated do
        TThread.Sleep(1);
    8:
      try
        CheckSynchronize;
      except
        on E: EThread do
          Msg := E.Message + '|' + Format('$%x', [GetCurrentThreadId]);
      end;
  end;
end;

function Make(Kind: Integer; Suspended: Boolean): TWorker;
begin
  Result := TWorker.Create(True);
  Result.Kind := Kind;
  if not Suspended then
    Result.Start;
end;

var
  M: TMain;
  T: TWorker;
  P: Integer;
  S: string;
  Method: TThreadMethod;
begin
  M := TMain.Create;

  Section('run and wait (RTL-510)');
  Check(TThread.CurrentThread = nil, 'CurrentThread is nil on the main thread');
  T := Make(0, False);
  CheckEqI(42, T.WaitFor, 'WaitFor returns ReturnValue');
  CheckEqB(True, T.Finished, 'Finished');
  Check(T.Me = T, 'CurrentThread inside Execute');
  Check(T.ThreadID <> MainThreadID, 'own thread id');
  Check(T.Handle <> 0, 'Handle');
  T.Free;
  T := Make(4, False);
  T.WaitFor;
  Check(T.FatalException is Exception, 'FatalException');
  CheckEqS('fatal', Exception(T.FatalException).Message, 'FatalException message');
  T.Free;

  Section('Synchronize (RTL-511)');
  Log := '';
  T := Make(1, True);
  WakeMainThread := M.Wake;
  T.Start;
  T.WaitFor;
  WakeMainThread := nil;
  CheckEqS('sync main=True;', Log, 'runs on the main thread while WaitFor waits');
  CheckEqI(1, Wakes, 'WakeMainThread called');
  T.Free;
  T := Make(2, False);
  T.WaitFor;
  CheckEqS('caught boom', T.Msg, 'exception re-raised in the calling thread');
  T.Free;
  Log := '';
  Method := M.Bump;
  TThread.Synchronize(nil, Method);
  CheckEqI(2, Wakes, 'on the main thread the method runs at once');

  Section('Queue and CheckSynchronize (RTL-512, RTL-513)');
  CheckEqB(False, CheckSynchronize, 'nothing pending');
  ApplicationHandleException := M.HandleExc;
  Log := '';
  T := TWorker.Create(True);
  T.Kind := 3;
  T.Start;
  T.WaitFor;
  CheckSynchronize;
  CheckEqS('q1;handled queued boom;q3;', Log, 'FIFO; the exception goes to ApplicationHandleException');
  ApplicationHandleException := nil;
  T.Free;
  Log := '';
  TThread.Queue(nil, M.Bump);
  CheckEqI(2, Wakes, 'Queue does not run at once');
  CheckEqB(True, CheckSynchronize, 'something ran');
  CheckEqI(3, Wakes, 'queued method ran');
  T := Make(8, False);
  T.WaitFor;
  P := Pos('|', T.Msg);
  S := Copy(T.Msg, P + 1, MaxInt);
  CheckEqS('CheckSynchronize called from thread ' + S + ', which is NOT the main thread', Copy(T.Msg, 1, P - 1),
    'CheckSynchronize off the main thread');
  T.Free;

  Section('OnTerminate and FreeOnTerminate (RTL-510)');
  Log := '';
  T := Make(0, True);
  T.OnTerminate := M.Terminated;
  T.Start;
  T.WaitFor;
  CheckEqS('terminated main=True finished=True;', Log, 'OnTerminate on the main thread after Finished');
  T.Free;
  Freed := TEvent.Create(nil, True, False, '');
  T := Make(6, True);
  T.FreeOnTerminate := True;
  T.Start;
  CheckEqB(True, Freed.WaitFor(INFINITE) = wrSignaled, 'the thread freed itself');
  Freed.Free;

  Section('start, terminate, destroy');
  T := Make(0, True);
  T.Start;
  S := '';
  try
    T.Start;
  except
    on E: EThread do
      S := E.Message;
  end;
  CheckEqS('Thread already started', S, 'second Start');
  T.WaitFor;
  T.Free;
  T := Make(5, True);
  T.Started := TEvent.Create(nil, True, False, '');
  T.Go := TEvent.Create(nil, True, False, '');
  T.Start;
  T.Started.WaitFor(INFINITE);
  T.Terminate;
  T.Go.SetEvent;
  CheckEqI(1, T.WaitFor, 'Terminated seen by Execute');
  T.Started.Free;
  T.Go.Free;
  T.Free;
  T := Make(7, False);
  T.Free;
  Check(True, 'Destroy of a running thread terminates and waits');
  T := Make(0, True);
  T.Free;
  Check(True, 'Destroy of a never started thread');
  T := TWorker.Create(False);
  T.WaitFor;
  CheckEqB(True, T.Ran, 'Create(False) starts after construction');
  T.Free;
  M.Free;
  Done;
end.
