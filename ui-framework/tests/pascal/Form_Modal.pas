// covers: UIF-413, UIF-445, UIF-446, UIF-447
// needs: desktop
program Form_Modal;
{ U-048, application-forms.md UIF-445 ... 447: ShowModal checks its preconditions (visible, disabled), disables the
  visible and enabled top-level windows of the thread except its own, shows the form, runs the loop until
  ModalResult <> mrNone (CloseModal: CloseQuery and OnClose may veto by resetting ModalResult to mrNone), returns the
  result, re-enables exactly the windows it disabled and hides the form; Close of a modal form sets mrCancel,
  Application.Terminate ends the loop with mrCancel, caFree releases the form after the loop; an exception escaping
  the sequence still re-enables the windows; a modal form may show another one (nesting). The loop is re-entrant
  (UIF-413): ProcessMessages inside the modal loop, and again inside a handler it dispatches, runs every message and
  leaves the modal state alone (message boxes run no loop in non-interactive mode). A posted message stands for the
  click on the OK button (buttons come with U-056). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

const
  WM_TEST = WM_APP + 1;
  WM_COUNT = WM_APP + 2;

type
  TModalForm = class;

  TStep = procedure(F: TModalForm) of object;

  TModalForm = class(TForm)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    OnStep: TStep;
    Counted, CountedInside: Integer;
  end;

  TLog = class
  public
    Text: string;
    CanClose: Boolean;
    SetAction: Boolean;
    Action: TCloseAction;
    RaiseOnShow: Boolean;
    Steps: Integer;
    MainEnabled, OtherEnabled, SelfEnabled, IsModal, Nested2Disabled, StillModal: Boolean;
    Level, InnerLevel, InnerResult, Inside: Integer;
    procedure Add(Sender: TObject; const S: string);
    procedure Shown(Sender: TObject);
    procedure Hidden(Sender: TObject);
    procedure Query(Sender: TObject; var ACanClose: Boolean);
    procedure Closing(Sender: TObject; var AAction: TCloseAction);
    procedure Destroyed(Sender: TObject);
    procedure StepOk(F: TModalForm);
    procedure StepVeto(F: TModalForm);
    procedure StepClose(F: TModalForm);
    procedure StepTerminate(F: TModalForm);
    procedure StepOuter(F: TModalForm);
    procedure StepInner(F: TModalForm);
    procedure StepReentrant(F: TModalForm);
  end;

var
  Log: TLog;
  Main, Other: TForm;
  Inner, Outer: TModalForm;

procedure TModalForm.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_TEST then
  begin
    if Assigned(OnStep) then
      OnStep(Self);
  end
  else if Message.Msg = WM_COUNT then
  begin
    Inc(Counted);
    if Counted = 1 then
    begin
      PostMessageW(Handle, WM_COUNT, 0, 0);
      Application.ProcessMessages;                           { a loop inside a handler of a nested loop }
      CountedInside := Counted;
    end;
  end
  else
    inherited WndProc(Message);
end;

procedure TLog.Add(Sender: TObject; const S: string);
begin
  Text := Text + TComponent(Sender).Name + '.' + S + ';';
end;

procedure TLog.Shown(Sender: TObject);
begin
  Add(Sender, 'Show');
  if RaiseOnShow then
    raise EInvalidOperation.Create('show failed');
  PostMessageW(TForm(Sender).Handle, WM_TEST, 0, 0);
end;

procedure TLog.Hidden(Sender: TObject);
begin
  Add(Sender, 'Hide');
end;

procedure TLog.Query(Sender: TObject; var ACanClose: Boolean);
begin
  Add(Sender, 'CloseQuery');
  ACanClose := CanClose;
end;

procedure TLog.Closing(Sender: TObject; var AAction: TCloseAction);
begin
  Add(Sender, 'Close');
  if SetAction then
    AAction := Action;
end;

procedure TLog.Destroyed(Sender: TObject);
begin
  Add(Sender, 'Destroy');
end;

{ the "OK button": records the state during the loop, then sets the result }
procedure TLog.StepOk(F: TModalForm);
begin
  MainEnabled := IsWindowEnabled(Main.Handle);
  OtherEnabled := IsWindowEnabled(Other.Handle);
  SelfEnabled := IsWindowEnabled(F.Handle);
  IsModal := fsModal in F.FormState;
  Level := Application.ModalLevel;
  F.ModalResult := mrOk;
end;

{ first message: a vetoed close; second: closes with mrYes }
procedure TLog.StepVeto(F: TModalForm);
begin
  Inc(Steps);
  if Steps = 1 then
  begin
    F.ModalResult := mrOk;
    PostMessageW(F.Handle, WM_TEST, 0, 0);
  end
  else
  begin
    CanClose := True;
    SetAction := False;
    F.ModalResult := mrYes;
  end;
end;

procedure TLog.StepClose(F: TModalForm);
begin
  F.Close;
  Add(F, 'Visible=' + BoolToStr(F.Visible, True));
end;

procedure TLog.StepTerminate(F: TModalForm);
begin
  Application.Terminate;
end;

procedure TLog.StepOuter(F: TModalForm);
begin
  Outer := F;
  Inner := TModalForm.CreateNew(nil);
  Inner.Name := 'Inner';
  Inner.OnShow := Shown;
  Inner.OnStep := StepInner;
  InnerResult := Inner.ShowModal;
  MainEnabled := IsWindowEnabled(Main.Handle);
  SelfEnabled := IsWindowEnabled(F.Handle);
  Level := Application.ModalLevel;
  F.ModalResult := mrCancel;
end;

procedure TLog.StepInner(F: TModalForm);
begin
  Nested2Disabled := not IsWindowEnabled(Main.Handle) and not IsWindowEnabled(Outer.Handle);
  InnerLevel := Application.ModalLevel;
  F.ModalResult := mrOk;
end;

{ UIF-413: ProcessMessages inside the modal loop; the WM_COUNT handler nests one more level }
procedure TLog.StepReentrant(F: TModalForm);
begin
  F.Counted := 0;
  F.CountedInside := 0;
  PostMessageW(F.Handle, WM_COUNT, 0, 0);
  Application.ProcessMessages;
  Steps := F.Counted;
  Inside := F.CountedInside;
  StillModal := (fsModal in F.FormState) and (F.ModalResult = mrNone) and F.Visible;
  Level := Application.ModalLevel;
  F.ModalResult := mrOk;
end;

function NewModal(const AName: string; Step: TStep): TModalForm;
begin
  Result := TModalForm.CreateNew(nil);
  Result.Name := AName;
  Result.OnShow := Log.Shown;
  Result.OnHide := Log.Hidden;
  Result.OnCloseQuery := Log.Query;
  Result.OnClose := Log.Closing;
  Result.OnDestroy := Log.Destroyed;
  Result.OnStep := Step;
end;

procedure Reset;
begin
  Log.Text := '';
  Log.CanClose := True;
  Log.SetAction := False;
  Log.RaiseOnShow := False;
  Log.Steps := 0;
end;

procedure Run;
var
  M: TModalForm;
  R, N: Integer;
  H: HWND;
begin
  Log := TLog.Create;
  try
    Application.Initialize;
    Application.CreateForm(TForm, Main);
    Main.Name := 'Main';
    Main.Show;
    Other := TForm.CreateNew(nil);
    Other.Name := 'Other';
    Other.Show;
    CheckEqI(0, Application.ModalLevel, 'ModalLevel 0 outside');

    Section('preconditions (UIF-445 step 1)');
    M := NewModal('M', Log.StepOk);
    M.OnShow := nil;                                         { no "click" for a non-modal show }
    M.Show;
    Expect('EInvalidOperation', 'Cannot make a visible window modal');
    try
      M.ShowModal;
      Check(False, 'visible: raises');
    except
      on E: Exception do
        CheckRaises('visible: raises', E.ClassName, E.Message);
    end;
    M.Hide;
    M.OnShow := Log.Shown;
    M.Enabled := False;
    Expect('EInvalidOperation', 'Cannot make a disabled window modal');
    try
      M.ShowModal;
      Check(False, 'disabled: raises');
    except
      on E: Exception do
        CheckRaises('disabled: raises', E.ClassName, E.Message);
    end;
    M.Enabled := True;
    CheckEqI(0, Application.ModalLevel, 'no level left behind');

    Section('loop and result (UIF-445 steps 2-7)');
    Reset;
    R := M.ShowModal;
    CheckEqI(mrOk, R, 'result mrOk');
    CheckEqI(mrOk, M.ModalResult, 'ModalResult kept');
    CheckEqB(False, Log.MainEnabled, 'main form disabled during');
    CheckEqB(False, Log.OtherEnabled, 'other form disabled during');
    CheckEqB(True, Log.SelfEnabled, 'modal form enabled');
    CheckEqB(True, Log.IsModal, 'fsModal during');
    CheckEqI(1, Log.Level, 'ModalLevel 1 during');
    CheckEqB(True, IsWindowEnabled(Main.Handle), 'main form enabled after');
    CheckEqB(True, IsWindowEnabled(Other.Handle), 'other form enabled after');
    CheckEqB(False, fsModal in M.FormState, 'fsModal cleared');
    CheckEqI(0, Application.ModalLevel, 'ModalLevel 0 after');
    CheckEqB(False, M.Visible, 'hidden after');
    CheckEqS('M.Show;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'event order');

    Section('a window disabled before stays disabled');
    Reset;
    EnableWindow(Other.Handle, False);
    M.ShowModal;
    CheckEqB(False, IsWindowEnabled(Other.Handle), 'not in the list: still disabled');
    CheckEqB(True, IsWindowEnabled(Main.Handle), 'main re-enabled');
    EnableWindow(Other.Handle, True);

    Section('CloseQuery and OnClose veto (CloseModal)');
    Reset;
    Log.CanClose := False;
    M.OnStep := Log.StepVeto;
    R := M.ShowModal;
    CheckEqI(mrYes, R, 'vetoed once, then mrYes');
    CheckEqS('M.Show;M.CloseQuery;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'CloseQuery False: no OnClose');
    Reset;
    Log.SetAction := True;
    Log.Action := caNone;
    R := M.ShowModal;
    CheckEqI(mrYes, R, 'caNone vetoes once');
    CheckEqS('M.Show;M.CloseQuery;M.Close;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'caNone: loop continues');

    Section('Close of a modal form (UIF-440, UIF-446)');
    Reset;
    M.OnStep := Log.StepClose;
    R := M.ShowModal;
    CheckEqI(mrCancel, R, 'Close gives mrCancel');
    CheckEqS('M.Show;M.Visible=True;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'Close only sets the result');
    M.ModalResult := mrOk;
    CheckEqB(False, M.Visible, 'ModalResult on a non-modal form: no effect');

    Section('exception escaping the sequence');
    Reset;
    Log.RaiseOnShow := True;
    Expect('EInvalidOperation', 'show failed');
    try
      M.ShowModal;
      Check(False, 'OnShow exception escapes');
    except
      on E: Exception do
        CheckRaises('OnShow exception escapes', E.ClassName, E.Message);
    end;
    CheckEqB(True, IsWindowEnabled(Main.Handle), 'main re-enabled after the exception');
    CheckEqB(True, IsWindowEnabled(Other.Handle), 'other re-enabled after the exception');
    CheckEqI(0, Application.ModalLevel, 'ModalLevel restored');
    CheckEqB(False, fsModal in M.FormState, 'fsModal cleared after the exception');
    Log.RaiseOnShow := False;

    Section('nesting (UIF-447)');
    Reset;
    M.OnStep := Log.StepOuter;
    R := M.ShowModal;
    CheckEqI(mrCancel, R, 'outer result');
    CheckEqI(mrOk, Log.InnerResult, 'inner result');
    CheckEqB(True, Log.Nested2Disabled, 'inner level: main and outer disabled');
    CheckEqI(2, Log.InnerLevel, 'ModalLevel 2 inside');
    CheckEqB(False, Log.MainEnabled, 'after the inner loop: main still disabled');
    CheckEqB(True, Log.SelfEnabled, 'after the inner loop: outer re-enabled');
    CheckEqI(1, Log.Level, 'ModalLevel back to 1');
    CheckEqB(True, IsWindowEnabled(Main.Handle), 'main enabled at the end');
    Inner.Free;

    Section('re-entrant loop (UIF-413)');
    Reset;
    M.OnStep := Log.StepReentrant;
    R := M.ShowModal;
    CheckEqI(mrOk, R, 'the modal loop continues after the nested loops');
    CheckEqI(2, Log.Steps, 'ProcessMessages in the modal loop: both messages dispatched');
    CheckEqI(2, Log.Inside, 'ProcessMessages inside a dispatched handler: the second one ran before it returned');
    CheckEqB(True, Log.StillModal, 'nested loops leave the modal state alone');
    CheckEqI(1, Log.Level, 'ModalLevel unchanged by ProcessMessages');
    CheckEqS('M.Show;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'one show, one close');
    CheckEqB(False, Application.Terminated, 'not terminated');
    CheckEqI(0, Application.ModalLevel, 'ModalLevel 0 after');

    Section('caFree releases after the loop');
    Reset;
    M.OnStep := Log.StepOk;
    Log.SetAction := True;
    Log.Action := caFree;
    N := Screen.FormCount;
    H := M.Handle;
    R := M.ShowModal;
    CheckEqI(mrOk, R, 'caFree: result');
    CheckEqS('M.Show;M.CloseQuery;M.Close;M.Hide;', Log.Text, 'not freed yet');
    Application.ProcessMessages;
    CheckEqS('M.Show;M.CloseQuery;M.Close;M.Hide;M.Destroy;', Log.Text, 'freed by the release');
    CheckEqI(N - 1, Screen.FormCount, 'removed from Screen');
    CheckEqB(False, IsWindow(H), 'window destroyed');

    Section('Terminate ends the loop');
    Reset;
    M := NewModal('T', Log.StepTerminate);
    R := M.ShowModal;
    CheckEqI(mrCancel, R, 'terminated: mrCancel');
    CheckEqB(True, Application.Terminated, 'terminated');
    CheckEqB(True, IsWindowEnabled(Main.Handle), 'main re-enabled');
  finally
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
