// covers: UIF-900, UIF-901, UIF-902, UIF-903, UIF-904
program Act_ExecuteUpdate;
{ U-076, actions-menus.md section 1.1: TAction and TActionList defaults; the list/action relation (ActionList,
  Index, Actions[], ActionCount, SetParentComponent, GetParentComponent, GetChildren in index order; destroying the
  list keeps the actions and clears their ActionList; a freed action leaves its list); every property change
  calls Change, which reaches registered links; Execute in the normative order (suspended list, Update, Enabled,
  AutoCheck with group behaviour, list OnExecute, action OnExecute, result); Update (list OnUpdate, then the
  action's, result = a handler ran); asSuspendedEnabled keeps actions enabled; no auto-disable without handlers;
  checking in a group unchecks the others; DoHint; streaming of nested actions. }
uses
  SysUtils, Classes, ActnList, TestKit;

type
  TActForm = class(TComponent)
  published
    ActionList1: TActionList;
    FileOpen: TAction;
    ViewA: TAction;
    ViewB: TAction;
    procedure FileOpenExecute(Sender: TObject);
    procedure ListChange(Sender: TObject);
  end;

  TLog = class
  public
    Text: string;
    HandleList: Boolean;
    procedure Exec(Sender: TObject);
    procedure Upd(Sender: TObject);
    procedure ListExec(Action: TBasicAction; var Handled: Boolean);
    procedure ListUpd(Action: TBasicAction; var Handled: Boolean);
    procedure Changed(Sender: TObject);
    procedure Child(Child: TComponent);
    procedure Hint(var HintStr: string; var CanShow: Boolean);
  end;

  TListAccess = class(TActionList);
  TActionAccess = class(TAction);

var
  Log: TLog;

procedure TActForm.FileOpenExecute(Sender: TObject);
begin
  Log.Text := Log.Text + 'open;';
end;

procedure TActForm.ListChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'listchange;';
end;

procedure TLog.Exec(Sender: TObject);
begin
  Text := Text + 'exec:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Upd(Sender: TObject);
begin
  Text := Text + 'upd:' + TComponent(Sender).Name + ';';
end;

procedure TLog.ListExec(Action: TBasicAction; var Handled: Boolean);
begin
  Text := Text + 'listexec:' + Action.Name + ';';
  Handled := HandleList;
end;

procedure TLog.ListUpd(Action: TBasicAction; var Handled: Boolean);
begin
  Text := Text + 'listupd:' + Action.Name + ';';
  Handled := HandleList;
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

procedure TLog.Child(Child: TComponent);
begin
  Text := Text + Child.Name + ',';
end;

procedure TLog.Hint(var HintStr: string; var CanShow: Boolean);
begin
  HintStr := HintStr + '!';
  CanShow := False;
end;

function NewAction(Owner: TComponent; List: TActionList; const AName: string): TAction;
begin
  Result := TAction.Create(Owner);
  Result.Name := AName;
  Result.ActionList := List;
end;

procedure Defaults;
var
  A: TAction;
  L: TActionList;
begin
  Section('defaults');
  A := TAction.Create(nil);
  L := TActionList.Create(nil);
  try
    CheckEqB(False, A.AutoCheck, 'AutoCheck False');
    CheckEqS('', A.Caption, 'Caption empty');
    CheckEqS('', A.Category, 'Category empty');
    CheckEqB(False, A.Checked, 'Checked False');
    CheckEqB(True, A.Enabled, 'Enabled True');
    CheckEqI(0, A.GroupIndex, 'GroupIndex 0');
    CheckEqS('', A.Hint, 'Hint empty');
    CheckEqI(-1, A.ImageIndex, 'ImageIndex -1');
    CheckEqI(0, A.ShortCut, 'ShortCut 0');
    CheckEqB(True, A.Visible, 'Visible True');
    CheckEqB(True, A.ActionList = nil, 'no list');
    CheckEqI(-1, A.Index, 'Index -1 without a list');
    CheckEqB(False, A.HasParent, 'no parent without a list');
    CheckEqB(True, L.Images = nil, 'Images nil');
    CheckEqB(True, L.State = asNormal, 'State asNormal');
    CheckEqI(0, L.ActionCount, 'no actions');
    Check(GetClass('TAction') <> nil, 'TAction registered');
    Check(GetClass('TActionList') <> nil, 'TActionList registered');
    CheckEqB(False, A.Execute, 'no handler: Execute False');
    CheckEqB(True, A.Enabled, 'no handler: not auto-disabled (UIF-904)');
    CheckEqB(False, A.Update, 'no handler: Update False');
  finally
    A.Free;
    L.Free;
  end;
end;

procedure ListRelation;
var
  Holder: TComponent;
  L, L2: TActionList;
  A, B, C: TAction;
begin
  Section('list and actions (UIF-900)');
  Holder := TComponent.Create(nil);
  try
    L := TActionList.Create(Holder);
    L.OnChange := Log.Changed;
    Log.Text := '';
    A := NewAction(Holder, L, 'A');
    B := NewAction(Holder, L, 'B');
    C := TAction.Create(Holder);
    C.Name := 'C';
    TListAccess(L).GetChildren(Log.Child, Holder);
    CheckEqS('change;change;A,B,', Log.Text, 'adding fires OnChange; GetChildren in index order');
    CheckEqI(2, L.ActionCount, 'ActionCount');
    CheckEqB(True, L.Actions[1] = B, 'Actions[]');
    CheckEqB(True, L[0] = A, 'default property');
    CheckEqI(1, B.Index, 'Index');
    CheckEqB(True, B.GetParentComponent = L, 'GetParentComponent = the list');
    CheckEqB(True, B.HasParent, 'HasParent with a list');
    TActionAccess(C).SetParentComponent(L);
    CheckEqI(3, L.ActionCount, 'SetParentComponent appends');
    CheckEqI(2, C.Index, 'at the end');
    C.Index := 0;
    Log.Text := '';
    TListAccess(L).GetChildren(Log.Child, Holder);
    CheckEqS('C,A,B,', Log.Text, 'Index moves');
    L2 := TActionList.Create(Holder);
    A.ActionList := L2;
    CheckEqI(2, L.ActionCount, 'moved to another list: removed');
    CheckEqB(True, L2[0] = A, 'in the other list');
    B.Free;
    CheckEqI(1, L.ActionCount, 'a freed action leaves its list');
    L.Free;
    CheckEqB(True, C.ActionList = nil, 'destroying the list clears ActionList');
    CheckEqS('C', C.Name, 'the action still exists');
    CheckEqI(3, Holder.ComponentCount, 'A, C and the second list remain');
  finally
    Holder.Free;
  end;
end;

procedure Changes;
var
  A: TAction;
  Link: TBasicActionLink;
  Count: Integer;
begin
  Section('Change (UIF-901)');
  A := TAction.Create(nil);
  Link := TBasicActionLink.Create(nil);
  try
    Link.OnChange := Log.Changed;
    A.RegisterChanges(Link);
    Log.Text := '';
    A.Caption := 'X';
    A.Caption := 'X';
    CheckEqS('change;', Log.Text, 'a change reaches the link; the same value does not');
    Log.Text := '';
    A.AutoCheck := True;
    A.Category := 'Cat';
    A.Checked := True;
    A.Enabled := False;
    A.GroupIndex := 2;
    A.Hint := 'H';
    A.ImageIndex := 1;
    A.ShortCut := 16463;
    A.Visible := False;
    Count := Length(Log.Text) div Length('change;');
    CheckEqI(9, Count, 'every property change calls Change');
    A.UnRegisterChanges(Link);
  finally
    Link.Free;
    A.Free;
  end;
end;

procedure Executing;
var
  L: TActionList;
  A, G1, G2: TAction;
begin
  Section('Execute (UIF-902)');
  L := TActionList.Create(nil);
  A := NewAction(nil, L, 'A');
  G1 := NewAction(nil, L, 'G1');
  G2 := NewAction(nil, L, 'G2');
  try
    A.OnExecute := Log.Exec;
    A.OnUpdate := Log.Upd;
    Log.Text := '';
    CheckEqB(True, A.Execute, 'handled: True');
    CheckEqS('upd:A;exec:A;', Log.Text, 'Update first, then OnExecute');
    L.OnExecute := Log.ListExec;
    L.OnUpdate := Log.ListUpd;
    Log.HandleList := False;
    Log.Text := '';
    CheckEqB(True, A.Execute, 'list did not handle: action handles');
    CheckEqS('listupd:A;upd:A;listexec:A;exec:A;', Log.Text, 'list handlers before the action''s');
    Log.HandleList := True;
    Log.Text := '';
    CheckEqB(True, A.Execute, 'list handled: True');
    CheckEqS('listupd:A;listexec:A;', Log.Text, 'handled by the list: action handlers skipped');
    L.OnExecute := nil;
    L.OnUpdate := nil;
    Log.HandleList := False;
    A.Enabled := False;
    Log.Text := '';
    CheckEqB(False, A.Execute, 'disabled: False');
    CheckEqS('upd:A;', Log.Text, 'disabled: Update ran, OnExecute did not');
    A.Enabled := True;
    L.State := asSuspended;
    Log.Text := '';
    CheckEqB(False, A.Execute, 'suspended list: False');
    CheckEqS('', Log.Text, 'suspended list: nothing runs');
    L.State := asNormal;
    A.OnExecute := nil;
    CheckEqB(False, A.Execute, 'no OnExecute: False');

    Section('AutoCheck and groups');
    A.AutoCheck := True;
    A.Execute;
    CheckEqB(True, A.Checked, 'AutoCheck toggles on (before OnExecute)');
    A.Execute;
    CheckEqB(False, A.Checked, 'AutoCheck toggles off');
    G1.GroupIndex := 1;
    G2.GroupIndex := 1;
    G1.AutoCheck := True;
    G1.Execute;
    CheckEqB(True, G1.Checked, 'group: checked');
    G1.Execute;
    CheckEqB(True, G1.Checked, 'group: a checked action stays checked');
    G2.Checked := True;
    CheckEqS('G1=0 G2=1', Format('G1=%d G2=%d', [Ord(G1.Checked), Ord(G2.Checked)]),
      'checking unchecks the others of the group');
    A.Checked := True;
    G1.Checked := True;
    CheckEqB(True, A.Checked, 'GroupIndex 0 is not a group');
    G1.OnExecute := Log.Exec;
    G1.Enabled := False;
    G1.Checked := False;
    G1.Execute;
    CheckEqB(False, G1.Checked, 'disabled: no AutoCheck');
    G1.Enabled := True;

    Section('Update (UIF-903)');
    L.OnUpdate := Log.ListUpd;
    Log.HandleList := False;
    A.OnUpdate := nil;
    Log.Text := '';
    CheckEqB(True, A.Update, 'list handler ran: True');
    CheckEqS('listupd:A;', Log.Text, 'list OnUpdate');
    L.OnUpdate := nil;
    CheckEqB(False, A.Update, 'no handler: False');
    A.OnUpdate := Log.Upd;
    Log.Text := '';
    CheckEqB(True, A.Update, 'own handler: True');
    CheckEqS('upd:A;', Log.Text, 'own OnUpdate');

    Section('asSuspendedEnabled');
    A.Enabled := False;
    G1.Enabled := False;
    L.State := asSuspendedEnabled;
    CheckEqB(True, A.Enabled and G1.Enabled, 'every action enabled');
    A.Enabled := False;
    CheckEqB(True, A.Enabled, 'stays enabled while set');
    A.OnUpdate := nil;
    A.OnExecute := Log.Exec;
    Log.Text := '';
    CheckEqB(False, A.Execute, 'still suspended: Execute False');
    L.State := asNormal;
    A.Enabled := False;
    CheckEqB(False, A.Enabled, 'asNormal: can be disabled again');

    Section('DoHint');
    A.OnHint := Log.Hint;
    Log.Text := 'tip';
    CheckEqB(False, A.DoHint(Log.Text), 'OnHint can refuse');
    CheckEqS('tip!', Log.Text, 'OnHint can change the text');
    A.OnHint := nil;
    CheckEqB(True, A.DoHint(Log.Text), 'without OnHint: True');
  finally
    A.Free;
    G1.Free;
    G2.Free;
    L.Free;
  end;
end;

procedure Streaming;
var
  F: TActForm;
  Ok: Boolean;
begin
  Section('streaming');
  Log.Text := '';
  F := TActForm.Create(nil);
  try
    Ok := False;
    try
      ReadComponentFromText(
        'object ActForm: TActForm'#10 +
        '  object ActionList1: TActionList'#10 +
        '    DesignLeft = 16'#10 +
        '    DesignTop = 160'#10 +
        '    State = asSuspended'#10 +
        '    OnChange = ListChange'#10 +
        '    object FileOpen: TAction'#10 +
        '      Caption = ''&Open...'''#10 +
        '      Category = ''File'''#10 +
        '      ShortCut = 16463'#10 +
        '      Hint = ''Open|Opens a file'''#10 +
        '      ImageIndex = 3'#10 +
        '      OnExecute = FileOpenExecute'#10 +
        '    end'#10 +
        '    object ViewA: TAction'#10 +
        '      Caption = ''A'''#10 +
        '      AutoCheck = True'#10 +
        '      GroupIndex = 1'#10 +
        '      Checked = True'#10 +
        '    end'#10 +
        '    object ViewB: TAction'#10 +
        '      Caption = ''B'''#10 +
        '      GroupIndex = 1'#10 +
        '      Checked = True'#10 +
        '      Enabled = False'#10 +
        '      Visible = False'#10 +
        '    end'#10 +
        '  end'#10 +
        'end'#10, F, 'Act.form');
      Ok := True;
    except
      on E: Exception do
        Writeln('# ', E.Message);
    end;
    Check(Ok, 'read');
    CheckEqI(3, F.ActionList1.ActionCount, 'three nested actions');
    CheckEqB(True, (F.ActionList1[0] = F.FileOpen) and (F.ActionList1[2] = F.ViewB), 'in order');
    CheckEqB(True, F.FileOpen.Owner = F, 'owned by the root');
    CheckEqI(16, F.ActionList1.DesignInfo and $FFFF, 'DesignLeft');
    CheckEqI(160, F.ActionList1.DesignInfo shr 16, 'DesignTop');
    CheckEqB(True, F.ActionList1.State = asSuspended, 'State');
    CheckEqS('&Open...', F.FileOpen.Caption, 'Caption');
    CheckEqS('File', F.FileOpen.Category, 'Category');
    CheckEqI(16463, F.FileOpen.ShortCut, 'ShortCut');
    CheckEqS('Open|Opens a file', F.FileOpen.Hint, 'Hint');
    CheckEqI(3, F.FileOpen.ImageIndex, 'ImageIndex');
    CheckEqB(True, F.ViewA.AutoCheck, 'AutoCheck');
    CheckEqI(1, F.ViewA.GroupIndex, 'GroupIndex');
    CheckEqB(True, F.ViewA.Checked and F.ViewB.Checked, 'stored Checked values win while loading');
    CheckEqB(False, F.ViewB.Enabled, 'Enabled');
    CheckEqB(False, F.ViewB.Visible, 'Visible');
    F.ActionList1.State := asNormal;
    Log.Text := '';
    CheckEqB(True, F.FileOpen.Execute, 'OnExecute bound');
    CheckEqS('open;', Log.Text, 'the form method ran');
  finally
    F.Free;
  end;
end;

begin
  Log := TLog.Create;
  try
    Defaults;
    ListRelation;
    Changes;
    Executing;
    Streaming;
  finally
    Log.Free;
  end;
  Done;
end.
