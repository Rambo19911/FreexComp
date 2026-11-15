// covers: UIF-430, UIF-431, UIF-432, UIF-433, UIF-434, UIF-435
// needs: desktop
program Form_ShowHideEvents;
{ U-038, application-forms.md 5: the event log of a main form equals the normative start-up order OnCreate, OnShow,
  OnActivate, OnResize, OnPaint; OnResize is suppressed before the first show, fires exactly once during it, then
  once per client size change; later shows fire OnShow (and the activation) but no OnResize without a size change;
  hiding the active form fires OnDeactivate before OnHide; Screen.ActiveForm follows the activation and
  OnActiveFormChange fires after the change; destruction hides (OnHide) before OnDestroy. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Add(Sender: TObject; const S: string);
    procedure Created(Sender: TObject);
    procedure Shown(Sender: TObject);
    procedure Hidden(Sender: TObject);
    procedure Activated(Sender: TObject);
    procedure Deactivated(Sender: TObject);
    procedure Resized(Sender: TObject);
    procedure Painted(Sender: TObject);
    procedure Destroyed(Sender: TObject);
    procedure ActiveFormChanged(Sender: TObject);
    procedure Hook(F: TForm);
  end;

  TMainForm = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  Log: TLog;
  MainForm: TMainForm;

procedure TLog.Add(Sender: TObject; const S: string);
begin
  Text := Text + TComponent(Sender).Name + '.' + S + ';';
end;

procedure TLog.Created(Sender: TObject);
begin
  Add(Sender, 'Create');
end;

procedure TLog.Shown(Sender: TObject);
begin
  Add(Sender, 'Show');
end;

procedure TLog.Hidden(Sender: TObject);
begin
  Add(Sender, 'Hide');
end;

procedure TLog.Activated(Sender: TObject);
begin
  Add(Sender, 'Activate');
end;

procedure TLog.Deactivated(Sender: TObject);
begin
  Add(Sender, 'Deactivate');
end;

procedure TLog.Resized(Sender: TObject);
begin
  Add(Sender, 'Resize');
end;

procedure TLog.Painted(Sender: TObject);
begin
  Add(Sender, 'Paint');
end;

procedure TLog.Destroyed(Sender: TObject);
begin
  Add(Sender, 'Destroy');
end;

procedure TLog.ActiveFormChanged(Sender: TObject);
begin
  if Screen.ActiveForm <> nil then
    Text := Text + 'Screen.Active=' + Screen.ActiveForm.Name + ';';
end;

procedure TLog.Hook(F: TForm);
begin
  F.OnCreate := Created;
  F.OnShow := Shown;
  F.OnHide := Hidden;
  F.OnActivate := Activated;
  F.OnDeactivate := Deactivated;
  F.OnResize := Resized;
  F.OnPaint := Painted;
  F.OnDestroy := Destroyed;
end;

constructor TMainForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  Name := 'Main';
  Log.Hook(Self);
end;

{ the queue holds nothing of ours any more (ProcessMessages ends with an empty queue) }
procedure Pump;
begin
  Application.ProcessMessages;
end;

var
  Second: TForm;
  WasMain, WasSecond: Boolean;

{ whether a window becomes active depends on the foreground rights of the process (an environment property), so the
  expected activation events are derived from the observed activation states before and after a step: deactivations
  first, then the activation (Win32 order); the rest of each log is fixed }
procedure Before;
begin
  WasMain := (MainForm <> nil) and MainForm.Active;
  WasSecond := (Second <> nil) and Second.Active;
end;

function Moved: string;
var
  NowMain, NowSecond: Boolean;
begin
  NowMain := (MainForm <> nil) and MainForm.Active;
  NowSecond := (Second <> nil) and Second.Active;
  Result := '';
  if WasMain and not NowMain then
    Result := Result + 'Main.Deactivate;';
  if WasSecond and not NowSecond then
    Result := Result + 'Second.Deactivate;';
  if NowMain and not WasMain then
    Result := Result + 'Screen.Active=Main;Main.Activate;';
  if NowSecond and not WasSecond then
    Result := Result + 'Screen.Active=Second;Second.Activate;';
end;

procedure Run;
begin
  Log := TLog.Create;
  try
    Section('start-up order (UIF-432)');
    Application.CreateForm(TMainForm, MainForm);
    MainForm.ClientWidth := 300;
    MainForm.Left := 200;
    MainForm.Top := 200;
    CheckEqS('Main.Create;', Log.Text, 'OnCreate; no OnResize before the first show');
    Screen.OnActiveFormChange := Log.ActiveFormChanged;
    Log.Text := '';
    Before;
    MainForm.Visible := True;                                { what Application.Run does }
    Pump;
    CheckEqS('Main.Show;' + Moved + 'Main.Resize;Main.Paint;', Log.Text,
      'OnShow, OnActivate (if activated), OnResize (once), OnPaint');
    CheckEqB(MainForm.Active, Screen.ActiveForm = MainForm, 'Screen.ActiveForm follows the activation');
    CheckEqB(True, fsVisible in MainForm.FormState, 'fsVisible');
    CheckEqB(True, MainForm.Showing, 'Showing');

    Section('OnResize after the first show (UIF-431)');
    Log.Text := '';
    MainForm.ClientWidth := 360;
    CheckEqS('Main.Resize;', Log.Text, 'a client size change: once');
    Log.Text := '';
    MainForm.ClientWidth := 360;
    MainForm.Left := 220;
    CheckEqS('', Log.Text, 'no size change: none');
    Log.Text := '';
    MainForm.SetBounds(MainForm.Left, MainForm.Top, MainForm.Width + 10, MainForm.Height + 10);
    CheckEqS('Main.Resize;', Log.Text, 'width and height at once: once');
    Pump;

    Section('a second form');
    Second := TForm.CreateNew(nil);
    try
      Second.Name := 'Second';
      Log.Hook(Second);
      Second.Left := 260;
      Second.Top := 260;
      Log.Text := '';
      Before;
      Second.Show;
      Pump;
      CheckEqS('Second.Show;' + Moved + 'Second.Resize;Second.Paint;', Log.Text, 'first show of a second form');
      if not Application.Active then
        CheckEqB(False, Second.Active, 'inactive application: shown without activation');
      Log.Text := '';
      Before;
      Second.Hide;
      Pump;
      CheckEqS(Moved + 'Second.Hide;', Log.Text, 'hide: OnDeactivate (if it was active) before OnHide');
      CheckEqB(False, fsVisible in Second.FormState, 'fsVisible cleared');
      CheckEqB(False, Second.Active, 'a hidden form is not active');
      Log.Text := '';
      Before;
      Second.Show;
      Pump;
      CheckEqS('Second.Show;' + Moved + 'Second.Paint;', Log.Text, 'later show: no OnResize (UIF-433)');
      Log.Text := '';
      Before;
    finally
      Second.Free;
      Second := nil;
    end;
    Pump;
    WasSecond := False;                                      { the freed form's deactivation is logged by name }
    CheckEqB(True, (Pos('Second.Hide;Second.Destroy;', Log.Text) > 0)
      and (Pos('Second.Hide;Second.Destroy;', Log.Text) = Length(Log.Text) - Length('Second.Hide;Second.Destroy;') + 1),
      'Free of a visible form: OnHide, then OnDestroy last (UIF-442)');
    CheckEqB(MainForm.Active, Screen.ActiveForm = MainForm, 'Screen.ActiveForm follows the activation');

    Section('main form');
    Log.Text := '';
    Before;
    MainForm.Hide;
    CheckEqS(Moved + 'Main.Hide;', Log.Text, 'hiding the main form: OnDeactivate (if active) before OnHide');
    CheckEqB(False, MainForm.Active, 'not active');
    Log.Text := '';
    MainForm.Free;
    CheckEqS('Main.Destroy;', Log.Text, 'hidden form: OnDestroy only');
  finally
    Log.Free;
  end;
end;
begin
  Run;
  Done;
end.
