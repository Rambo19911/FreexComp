// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1360, UIF-1361, UIF-1362, UIF-1363, UIF-1364
// phase: 8
program Act_TargetDispatch;
{ U-151, standard-actions.md section 2: an action whose handlers did not handle a request looks for a target. The
  test action handles one marker component (HandlesTarget). The candidates are asked in the order active control,
  active form, main form, application; the forms and the control log each question in their ExecuteAction /
  UpdateAction and may answer it themselves. The active form is the form a shortcut search starts from
  (TCustomForm.IsShortCut, no window needed); without one the search starts at the main form. Handlers win over
  targets; a disabled action asks nobody to execute; without a target the result is False; DisableIfNoHandler
  disables only an action without an execute handler and without a target; a TAction without a handler stays
  enabled; an exception of a target method propagates; with ActnList.ActionTargetSearch cleared the search is off.
  Needs no desktop. }
uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, ActnList, TestKit, KeyStateKit;

type
  { handles the component Marker; the target methods log, UpdateTarget enables the action }
  TMarkAction = class(TAction)
  public
    Marker: TObject;
    RaiseInTarget: Boolean;
    function HandlesTarget(Target: TObject): Boolean; override;
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
  end;

  TProbeEdit = class(TEdit)
  public
    Claim: Boolean;
    function ExecuteAction(Action: TBasicAction): Boolean; override;
    function UpdateAction(Action: TBasicAction): Boolean; override;
  end;

  TProbeForm = class(TForm)
  public
    Claim: Boolean;
    constructor Create(AOwner: TComponent); override;
    function ExecuteAction(Action: TBasicAction): Boolean; override;
    function UpdateAction(Action: TBasicAction): Boolean; override;
  end;

  THandlers = class
  public
    Handle: Boolean;
    procedure Exec(Sender: TObject);
    procedure Upd(Sender: TObject);
    procedure ListExec(Action: TBasicAction; var Handled: Boolean);
  end;

var
  Log: string;
  Main, Second: TProbeForm;
  Edit: TProbeEdit;
  List: TActionList;
  Act: TMarkAction;
  H: THandlers;

function TargetName(Target: TObject): string;
begin
  if Target is TApplication then
    Result := 'Application'
  else
    Result := TComponent(Target).Name;
end;

function TMarkAction.HandlesTarget(Target: TObject): Boolean;
begin
  Result := (Marker <> nil) and (Target = Marker);
end;

procedure TMarkAction.UpdateTarget(Target: TObject);
begin
  Log := Log + 'U>' + TargetName(Target) + ';';
  Enabled := True;
end;

procedure TMarkAction.ExecuteTarget(Target: TObject);
begin
  Log := Log + 'X>' + TargetName(Target) + ';';
  if RaiseInTarget then
    raise Exception.Create('target failed');
end;

function TProbeEdit.ExecuteAction(Action: TBasicAction): Boolean;
begin
  Log := Log + 'x:' + Name + ';';
  Result := Claim or inherited ExecuteAction(Action);
end;

function TProbeEdit.UpdateAction(Action: TBasicAction): Boolean;
begin
  Log := Log + 'u:' + Name + ';';
  Result := Claim or inherited UpdateAction(Action);
end;

constructor TProbeForm.Create(AOwner: TComponent);
begin
  inherited CreateNew(AOwner);
end;

function TProbeForm.ExecuteAction(Action: TBasicAction): Boolean;
begin
  Log := Log + 'x:' + Name + ';';
  Result := Claim or inherited ExecuteAction(Action);
end;

function TProbeForm.UpdateAction(Action: TBasicAction): Boolean;
begin
  Log := Log + 'u:' + Name + ';';
  Result := Claim or inherited UpdateAction(Action);
end;

procedure THandlers.Exec(Sender: TObject);
begin
  Log := Log + 'exec;';
end;

procedure THandlers.Upd(Sender: TObject);
begin
  Log := Log + 'upd;';
end;

procedure THandlers.ListExec(Action: TBasicAction; var Handled: Boolean);
begin
  Log := Log + 'list;';
  Handled := Handle;
end;

{ F5 through the shortcut search that starts at F: the action of List (owned by Second) executes }
function Key(F: TCustomForm): Boolean;
var
  Msg: TWMKey;
begin
  Log := '';
  FillChar(Msg, SizeOf(Msg), 0);
  Msg.Msg := WM_KEYDOWN;
  Msg.CharCode := VK_F5;
  Result := F.IsShortCut(Msg);
end;

function Direct: Boolean;
begin
  Log := '';
  Result := Act.Execute;
end;

procedure Order;
begin
  Section('no target (UIF-1360, UIF-1362)');
  CheckEqB(False, Direct, 'Execute without an active form: False');
  CheckEqS('u:Main;x:Main;', Log, 'the search starts at the main form');
  CheckEqB(False, Key(Second), 'through the shortcut search of the second form: False, the key is not consumed');
  CheckEqS('u:Edit1;u:Second;u:Main;x:Edit1;x:Second;x:Main;', Log,
    'active control, active form, main form: first for the update, then for the execution');
  CheckEqB(True, Act.Enabled, 'a TAction descendant without a handler and a target stays enabled');

  Section('each level in turn (UIF-1362, UIF-1364)');
  Edit.Claim := True;
  CheckEqB(True, Key(Second), 'the active control answers in its ExecuteAction: True');
  CheckEqS('u:Edit1;x:Edit1;', Log, 'the answer of the active control ends the search');
  Edit.Claim := False;
  Second.Claim := True;
  CheckEqB(True, Key(Second), 'the active form answers');
  CheckEqS('u:Edit1;u:Second;x:Edit1;x:Second;', Log, 'the active form is asked after its active control');
  Second.Claim := False;
  Main.Claim := True;
  CheckEqB(True, Key(Second), 'the main form answers');
  CheckEqS('u:Edit1;u:Second;u:Main;x:Edit1;x:Second;x:Main;', Log, 'the main form is asked after the active form');
  CheckEqB(True, Direct, 'without an active form the main form still answers');
  CheckEqS('u:Main;x:Main;', Log, 'only the main form was asked');
  Main.Claim := False;

  Section('the target methods (UIF-1360, UIF-1361)');
  Act.Marker := Edit;
  CheckEqB(True, Key(Second), 'the marker is the active control: True');
  CheckEqS('u:Edit1;U>Edit1;x:Edit1;X>Edit1;', Log, 'UpdateTarget, then ExecuteTarget, for the active control');
  Act.Marker := Second;
  CheckEqB(True, Key(Second), 'the marker is the active form');
  CheckEqS('u:Edit1;u:Second;U>Second;x:Edit1;x:Second;X>Second;', Log, 'the target methods get the form');
  Act.Marker := Main;
  CheckEqB(True, Key(Second), 'the marker is the main form');
  CheckEqS('u:Edit1;u:Second;u:Main;U>Main;x:Edit1;x:Second;x:Main;X>Main;', Log, 'the target methods get the main form');
  Act.Marker := Application;
  CheckEqB(True, Key(Second), 'the marker is the application');
  CheckEqS('u:Edit1;u:Second;u:Main;U>Application;x:Edit1;x:Second;x:Main;X>Application;', Log,
    'the application is asked last');
  CheckEqB(True, Direct, 'the application without an active form');
  CheckEqS('u:Main;U>Application;x:Main;X>Application;', Log, 'main form, then the application');

  Section('which form is the active one (UIF-1362)');
  Act.Marker := nil;
  Second.ActiveControl := nil;
  CheckEqB(False, Key(Second), 'no active control');
  CheckEqS('u:Second;u:Main;x:Second;x:Main;', Log, 'the search starts at the active form');
  Second.ActiveControl := Edit;
  Second.RemoveComponent(List);
  Main.InsertComponent(List);
  CheckEqB(False, Key(Main), 'the shortcut search starts at the main form');
  CheckEqS('u:Main;x:Main;', Log, 'the main form is asked once; the controls of another form are not asked');
  Main.RemoveComponent(List);
  Second.InsertComponent(List);
end;

procedure HandlersWin;
begin
  Section('handlers win over targets (UIF-1360, UIF-1361)');
  Act.Marker := Edit;
  Act.OnExecute := H.Exec;
  CheckEqB(True, Key(Second), 'OnExecute assigned: True');
  CheckEqS('u:Edit1;U>Edit1;exec;', Log, 'the handler ran; nobody was asked to execute');
  Act.OnExecute := nil;
  List.OnExecute := H.ListExec;
  H.Handle := True;
  CheckEqB(True, Key(Second), 'the list''s OnExecute handled it');
  CheckEqS('u:Edit1;U>Edit1;list;', Log, 'nobody was asked to execute');
  H.Handle := False;
  CheckEqB(True, Key(Second), 'the list''s OnExecute left it unhandled');
  CheckEqS('u:Edit1;U>Edit1;list;x:Edit1;X>Edit1;', Log, 'the target is asked after the handlers');
  List.OnExecute := nil;
  Act.OnUpdate := H.Upd;
  CheckEqB(True, Key(Second), 'OnUpdate assigned');
  CheckEqS('upd;x:Edit1;X>Edit1;', Log, 'an update handler ran: nobody is asked to update');
  Act.OnUpdate := nil;

  Section('a disabled action (UIF-1360)');
  Second.Claim := True;
  Act.Marker := nil;
  Act.Enabled := False;
  CheckEqB(False, Key(Second), 'disabled: False although the form would answer');
  CheckEqS('u:Edit1;u:Second;', Log, 'updated, but nobody is asked to execute');
  Act.Enabled := True;
  Second.Claim := False;

  Section('a suspended list (UIF-1360)');
  Act.Marker := Edit;
  List.State := asSuspended;
  CheckEqB(False, Direct, 'suspended: False');
  CheckEqS('', Log, 'nobody is asked');
  List.State := asNormal;

  Section('an exception of a target method (UIF-1364)');
  Act.RaiseInTarget := True;
  Expect('Exception', 'target failed');
  try
    Key(Second);
    Check(False, 'Execute raises');
  except
    on E: Exception do
      CheckRaises('Execute raises', E.ClassName, E.Message);
  end;
  Act.RaiseInTarget := False;
  Act.Marker := nil;
end;

procedure AutoDisable;
var
  Plain: TAction;
begin
  Section('DisableIfNoHandler (UIF-1361)');
  CheckEqB(False, Act.DisableIfNoHandler, 'False by default');
  Act.DisableIfNoHandler := True;
  Log := '';
  Act.Update;
  CheckEqB(False, Act.Enabled, 'no handler and no target: disabled by Update');
  CheckEqS('u:Main;', Log, 'the candidates were asked');
  Act.Marker := Main;
  Act.Update;
  CheckEqB(True, Act.Enabled, 'a target exists: its UpdateTarget decides');
  Act.Marker := nil;
  Act.OnExecute := H.Exec;
  Act.Update;
  CheckEqB(True, Act.Enabled, 'an action with OnExecute is never disabled by the rule');
  Act.OnExecute := nil;
  List.OnExecute := H.ListExec;
  Act.Update;
  CheckEqB(True, Act.Enabled, 'nor an action whose list has OnExecute');
  List.OnExecute := nil;
  Act.OnUpdate := H.Upd;
  Act.Update;
  CheckEqB(True, Act.Enabled, 'an update handler ran: the rule does not apply');
  Act.OnUpdate := nil;
  CheckEqB(False, Direct, 'Execute updates first: disabled, False');
  CheckEqS('u:Main;', Log, 'nobody is asked to execute');
  CheckEqB(False, Act.Enabled, 'disabled');
  Act.DisableIfNoHandler := False;
  Act.Enabled := True;

  Section('TAction without a handler (UIF-904)');
  Plain := TAction.Create(Second);
  Plain.ActionList := List;
  CheckEqB(False, Plain.DisableIfNoHandler, 'DisableIfNoHandler False');
  CheckEqB(False, Plain.Update, 'Update: no handler ran');
  CheckEqB(True, Plain.Enabled, 'stays enabled');
  CheckEqB(False, Plain.Execute, 'Execute: False');
  CheckEqB(True, Plain.Enabled, 'still enabled');
  Plain.Free;
end;

procedure SearchOff;
var
  Saved: TActionTargetSearch;
begin
  Section('the search variable (UIF-1363)');
  Check(Assigned(ActionTargetSearch), 'Forms assigned ActnList.ActionTargetSearch');
  Saved := ActionTargetSearch;
  ActionTargetSearch := nil;
  try
    Act.Marker := Main;
    Main.Claim := True;
    CheckEqB(False, Direct, 'cleared: Execute is False although a target exists');
    CheckEqS('', Log, 'nobody is asked');
    CheckEqB(False, Key(Second), 'the shortcut is not consumed');
    CheckEqS('', Log, 'nobody is asked by the shortcut search');
  finally
    ActionTargetSearch := Saved;
  end;
  CheckEqB(True, Direct, 'assigned again: the search is back');
  CheckEqS('u:Main;x:Main;', Log, 'the main form answered');
  Main.Claim := False;
  Act.Marker := nil;
end;

begin
  Application.Initialize;
  SetKeyState(False, False);
  H := THandlers.Create;
  Application.CreateForm(TProbeForm, Main);
  Main.Name := 'Main';
  Second := TProbeForm.Create(nil);
  try
    Second.Name := 'Second';
    Edit := TProbeEdit.Create(Second);
    Edit.Name := 'Edit1';
    Edit.Parent := Second;
    Second.ActiveControl := Edit;
    List := TActionList.Create(Second);
    Act := TMarkAction.Create(Second);
    Act.ActionList := List;
    Act.ShortCut := VK_F5;
    Check(Application.MainForm = Main, 'the first form is the main form');
    Check(Screen.ActiveCustomForm = nil, 'no form is active: no window was shown');
    Order;
    HandlersWin;
    AutoDisable;
    SearchOff;
  finally
    Second.Free;
    H.Free;
  end;
  Done;
end.
