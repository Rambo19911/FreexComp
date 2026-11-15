// phase: 4
// covers: RTL-462
program Cls_BasicAction;
{ TBasicAction and TBasicActionLink (classes.md section 8.2): Execute/Update fire OnExecute/OnUpdate and return
  whether a handler is assigned; Change fires OnChange and every link's Change; link registration through
  Action :=, unregistration when the link or the action is freed; TBasicActionLink.Execute sets ActionComponent
  for the duration of the call; ActionComponent is cleared when that component is freed; TComponent.ExecuteAction /
  UpdateAction call ExecuteTarget / UpdateTarget only when HandlesTarget (RTL-462). }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TTarget = class(TComponent)
  end;

  TMyAction = class(TBasicAction)
  public
    function HandlesTarget(Target: TObject): Boolean; override;
    procedure UpdateTarget(Target: TObject); override;
    procedure ExecuteTarget(Target: TObject); override;
    procedure Poke;
  end;

  TMyLink = class(TBasicActionLink)
  protected
    procedure AssignClient(AClient: TObject); override;
    procedure Change; override;
  end;

  THandlers = class
    procedure DoExecute(Sender: TObject);
    procedure DoUpdate(Sender: TObject);
    procedure DoChange(Sender: TObject);
  end;

function Nm(C: TObject): string;
begin
  if C = nil then
    Result := 'nil'
  else if C is TComponent then
    Result := TComponent(C).Name
  else
    Result := C.ClassName;
end;

function TMyAction.HandlesTarget(Target: TObject): Boolean;
begin
  Result := Target is TTarget;
end;

procedure TMyAction.UpdateTarget(Target: TObject);
begin
  Log := Log + 'update ' + Nm(Target) + ';';
end;

procedure TMyAction.ExecuteTarget(Target: TObject);
begin
  Log := Log + 'execute ' + Nm(Target) + ';';
end;

procedure TMyAction.Poke;
begin
  Change;
end;

procedure TMyLink.AssignClient(AClient: TObject);
begin
  Log := Log + 'client ' + Nm(AClient) + ';';
end;

procedure TMyLink.Change;
begin
  Log := Log + 'link change;';
  inherited Change;
end;

procedure THandlers.DoExecute(Sender: TObject);
begin
  Log := Log + 'OnExecute ' + Nm(Sender) + ' by ' + Nm(TBasicAction(Sender).ActionComponent) + ';';
end;

procedure THandlers.DoUpdate(Sender: TObject);
begin
  Log := Log + 'OnUpdate ' + Nm(Sender) + ';';
end;

procedure THandlers.DoChange(Sender: TObject);
begin
  Log := Log + 'OnChange ' + Nm(Sender) + ';';
end;

var
  H: THandlers;
  A: TMyAction;
  L1, L2: TMyLink;
  T, Other, Btn: TComponent;
begin
  H := THandlers.Create;
  A := TMyAction.Create(nil);
  A.Name := 'Act';

  Section('Execute and Update');
  CheckEqB(False, A.Execute, 'Execute without handler');
  CheckEqB(False, A.Update, 'Update without handler');
  A.OnExecute := H.DoExecute;
  A.OnUpdate := H.DoUpdate;
  Log := '';
  CheckEqB(True, A.Execute, 'Execute with handler');
  CheckEqB(True, A.Update, 'Update with handler');
  CheckEqS('OnExecute Act by nil;OnUpdate Act;', Log, 'handlers fired');

  Section('links');
  Log := '';
  L1 := TMyLink.Create(H);
  CheckEqS('client THandlers;', Log, 'AssignClient from the constructor');
  L2 := TMyLink.Create(nil);
  L1.Action := A;
  L2.Action := A;
  Check(L1.Action = A, 'Action');
  L1.OnChange := H.DoChange;
  Log := '';
  A.Poke;
  CheckEqS('link change;OnChange Act;link change;', Log, 'Change reaches every link; link OnChange gets the action');
  Btn := TComponent.Create(nil);
  Btn.Name := 'Btn';
  Log := '';
  CheckEqB(True, L1.Execute(Btn), 'link Execute');
  CheckEqS('OnExecute Act by Btn;', Log, 'ActionComponent during the call');
  Check(A.ActionComponent = nil, 'ActionComponent reset afterwards');
  CheckEqB(True, L1.Update, 'link Update');
  A.ActionComponent := Btn;
  Btn.Free;
  Check(A.ActionComponent = nil, 'freed ActionComponent is cleared');
  L2.Free;
  Log := '';
  A.Poke;
  CheckEqS('link change;OnChange Act;', Log, 'a freed link is unregistered');
  L1.Action := nil;
  Log := '';
  A.Poke;
  CheckEqS('', Log, 'Action := nil unregisters');
  L1.Action := A;
  A.Free;
  Check(L1.Action = nil, 'freeing the action clears the link');
  CheckEqB(False, L1.Execute, 'link Execute without action');
  CheckEqB(False, L1.Update, 'link Update without action');
  L1.Free;

  Section('ExecuteAction and UpdateAction (RTL-462)');
  A := TMyAction.Create(nil);
  T := TTarget.Create(nil);
  T.Name := 'Tgt';
  Other := TComponent.Create(nil);
  Other.Name := 'Oth';
  Log := '';
  CheckEqB(True, T.ExecuteAction(A), 'ExecuteAction handled');
  CheckEqB(True, T.UpdateAction(A), 'UpdateAction handled');
  CheckEqB(False, Other.ExecuteAction(A), 'ExecuteAction not handled');
  CheckEqB(False, Other.UpdateAction(A), 'UpdateAction not handled');
  CheckEqS('execute Tgt;update Tgt;', Log, 'targets');
  A.Free;
  T.Free;
  Other.Free;
  H.Free;
  Done;
end.
