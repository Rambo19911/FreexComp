// covers: UIF-730, UIF-731, UIF-732
// needs: desktop
program Ctl_Timer;
{ U-069, controls-standard.md section 13: TTimer defaults (Enabled, Interval 1000); an OS timer at the application
  window exists exactly when Enabled, Interval > 0 and OnTimer are set (checked with KillTimer on the id; ids count
  from 1), WM_TIMER calls OnTimer with the timer as Sender, only while messages are processed (UIF-731), Enabled
  False / Interval 0 stop it, an exception in OnTimer goes to Application.OnException (guarded, UIF-463) and the
  timer keeps running, no timer in dump mode (UIF-732), a streamed timer starts after loading, freeing kills it.
  The test waits on messages (Application.HandleMessage until a counter is reached), never on fixed delays. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit;

type
  TRec = class
  public
    Count, Count2, Errors: Integer;
    Last: TObject;
    ErrorText: string;
    Fail: Boolean;
    procedure Tick(Sender: TObject);
    procedure Tick2(Sender: TObject);
    procedure Error(Sender: TObject; E: Exception);
  end;

  TTimerForm = class(TForm)
  published
    Clock: TTimer;
    procedure ClockTimer(Sender: TObject);
  end;

{$FORM Ctl_Timer.form}

var
  Rec: TRec;
  FormTicks: Integer;

procedure TRec.Tick(Sender: TObject);
begin
  Inc(Count);
  Last := Sender;
  if Fail then
  begin
    Fail := False;
    raise Exception.Create('tick failed');
  end;
end;

procedure TRec.Tick2(Sender: TObject);
begin
  Inc(Count2);
end;

procedure TRec.Error(Sender: TObject; E: Exception);
begin
  Inc(Errors);
  ErrorText := E.Message;
  Last := Sender;
end;

procedure TTimerForm.ClockTimer(Sender: TObject);
begin
  Inc(FormTicks);
end;

{ True when an OS timer with this id existed at the application window (it is killed by the check) }
function Exists(ID: UINT_PTR): Boolean;
begin
  Result := KillTimer(Application.Handle, ID);
end;

{ processes messages (waiting for them) until Counter reaches N }
procedure WaitFor(var Counter: Integer; N: Integer);
begin
  while Counter < N do
    Application.HandleMessage;
end;

procedure Run;
var
  T1, T2, T3: TTimer;
  TF: TTimerForm;
begin
  Application.Initialize;
  Rec := TRec.Create;
  T1 := TTimer.Create(nil);
  T2 := TTimer.Create(nil);
  try
    Section('defaults');
    CheckEqB(True, T1.Enabled, 'Enabled True');
    CheckEqI(1000, T1.Interval, 'Interval 1000');
    Check(not Assigned(T1.OnTimer), 'OnTimer nil');
    Check(GetClass('TTimer') <> nil, 'registered');

    Section('OS timer (UIF-730)');
    T1.Interval := 10;
    CheckEqB(False, Exists(1), 'no OnTimer: no OS timer');
    T1.OnTimer := Rec.Tick;
    T2.Interval := 10;
    T2.OnTimer := Rec.Tick2;
    Rec.Count := 0;
    Application.ProcessMessages;
    WaitFor(Rec.Count, 3);
    CheckEqB(True, Rec.Count >= 3, 'OnTimer fires while messages are processed');
    Check(Rec.Last = T1, 'Sender is the timer');
    Rec.Count := 0;
    Rec.Count2 := 0;
    T1.Enabled := False;
    WaitFor(Rec.Count2, 3);
    CheckEqI(0, Rec.Count, 'Enabled False: no events');
    CheckEqB(False, Exists(1), 'Enabled False: OS timer killed');
    T1.Interval := 0;
    T1.Enabled := True;
    CheckEqB(False, Exists(1), 'Interval 0: no OS timer');
    Rec.Count2 := 0;
    WaitFor(Rec.Count2, 3);
    CheckEqI(0, Rec.Count, 'Interval 0: no events');
    T1.Interval := 10;
    WaitFor(Rec.Count, 2);
    CheckEqB(True, Rec.Count >= 2, 'Interval > 0 again: events');
    T1.OnTimer := nil;
    CheckEqB(False, Exists(1), 'OnTimer nil: OS timer killed');
    T1.OnTimer := Rec.Tick;
    CheckEqB(True, Exists(1), 'OnTimer assigned: an OS timer with id 1');
    T1.Enabled := False;

    Section('guarded (UIF-463)');
    Application.OnException := Rec.Error;
    Rec.Fail := True;
    Rec.Count := 0;
    T1.Enabled := True;
    WaitFor(Rec.Count, 3);
    CheckEqI(1, Rec.Errors, 'the exception reached Application.OnException');
    CheckEqS('tick failed', Rec.ErrorText, 'exception message');
    CheckEqB(True, Rec.Count >= 3, 'the timer keeps running after the exception');
    Application.OnException := nil;
    T1.Enabled := False;

    Section('dump mode (UIF-732)');
    LayoutDumpMode := True;
    T3 := TTimer.Create(nil);
    try
      T3.Interval := 10;
      T3.OnTimer := Rec.Tick;
      CheckEqB(False, Exists(3), 'dump mode: no OS timer');
    finally
      LayoutDumpMode := False;
      T3.Free;
    end;

    Section('streamed timer');
    TF := TTimerForm.Create(nil);
    try
      CheckEqI(10, TF.Clock.Interval, 'Interval loaded');
      Check(Assigned(TF.Clock.OnTimer), 'OnTimer bound');
      FormTicks := 0;
      WaitFor(FormTicks, 2);
      CheckEqB(True, FormTicks >= 2, 'started after loading');
    finally
      TF.Free;
    end;
    CheckEqB(False, Exists(4), 'freed with its form: OS timer killed');
  finally
    T1.Free;
    T2.Free;
  end;
  CheckEqB(False, Exists(2), 'freed: OS timer killed');
  Rec.Free;
end;

begin
  Run;
  Done;
end.
