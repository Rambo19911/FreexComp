// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ActnList - actions and action lists (actions-menus.md section 1.1). }
unit ActnList;

interface

uses
  Windows, Messages, SysUtils, Classes, Graphics, ImgList, UIConsts;

type
  THintEvent = procedure(var HintStr: string; var CanShow: Boolean) of object;
  TActionEvent = procedure(Action: TBasicAction; var Handled: Boolean) of object;
  TActionListState = (asNormal, asSuspended, asSuspendedEnabled);

  TCustomActionList = class;

  TContainedAction = class(TBasicAction)
  private
    FActionList: TCustomActionList;
    FCategory: string;
    function GetIndex: Integer;
    procedure SetIndex(Value: Integer);
    procedure SetActionList(Value: TCustomActionList);
    procedure SetCategory(const Value: string);
  protected
    { UIF-902 step 1: the list is suspended }
    function Suspended: Boolean;
    { UIF-902 steps 5 to 7: the list's OnExecute, then the action's own; True if handled }
    function DoExecute: Boolean;
    procedure SetParentComponent(Value: TComponent); override;
    property Category: string read FCategory write SetCategory;
  public
    destructor Destroy; override;
    function GetParentComponent: TComponent; override;
    function HasParent: Boolean; override;
    { UIF-902 (without the TCustomAction steps 3 and 4) }
    function Execute: Boolean; override;
    { UIF-903 }
    function Update: Boolean; override;
    property ActionList: TCustomActionList read FActionList write SetActionList;
    property Index: Integer read GetIndex write SetIndex;
  end;

  TCustomAction = class(TContainedAction)
  private
    FAutoCheck: Boolean;
    FCaption: string;
    FChecked: Boolean;
    FEnabled: Boolean;
    FGroupIndex: Integer;
    FHint: string;
    FImageIndex: Integer;
    FShortCut: TShortCut;
    FVisible: Boolean;
    FOnHint: THintEvent;
    procedure SetAutoCheck(Value: Boolean);
    procedure SetCaption(const Value: string);
    procedure SetChecked(Value: Boolean);
    procedure SetEnabled(Value: Boolean);
    procedure SetGroupIndex(Value: Integer);
    procedure SetHint(const Value: string);
    procedure SetImageIndex(Value: Integer);
    procedure SetShortCut(Value: TShortCut);
    procedure SetVisible(Value: Boolean);
    procedure UncheckGroup;
  public
    property AutoCheck: Boolean read FAutoCheck write SetAutoCheck default False;
    property Caption: string read FCaption write SetCaption;
    property Checked: Boolean read FChecked write SetChecked default False;
    property Enabled: Boolean read FEnabled write SetEnabled default True;
    property GroupIndex: Integer read FGroupIndex write SetGroupIndex default 0;
    property Hint: string read FHint write SetHint;
    property ImageIndex: Integer read FImageIndex write SetImageIndex default -1;
    property ShortCut: TShortCut read FShortCut write SetShortCut default 0;
    property Visible: Boolean read FVisible write SetVisible default True;
    property OnHint: THintEvent read FOnHint write FOnHint;
    constructor Create(AOwner: TComponent); override;
    { UIF-902 }
    function Execute: Boolean; override;
    { UIF-903 }
    function Update: Boolean; override;
    { calls OnHint (actions-menus.md UIF-943); True when the hint may be shown }
    function DoHint(var HintStr: string): Boolean;
  end;

  TAction = class(TCustomAction)
  published
    property AutoCheck;
    property Caption;
    property Category;
    property Checked;
    property Enabled;
    property GroupIndex;
    property Hint;
    property ImageIndex;
    property ShortCut;
    property Visible;
    property OnExecute;
    property OnHint;
    property OnUpdate;
  end;

  TCustomActionList = class(TComponent)
  private
    FActions: TList;
    FImages: TCustomImageList;
    FState: TActionListState;
    FOnChange: TNotifyEvent;
    FOnExecute: TActionEvent;
    FOnUpdate: TActionEvent;
    function GetAction(Index: Integer): TContainedAction;
    function GetActionCount: Integer;
    procedure SetImages(Value: TCustomImageList);
    procedure SetState(Value: TActionListState);
    procedure AddAction(Action: TContainedAction);
    procedure RemoveAction(Action: TContainedAction);
  protected
    procedure Change; virtual;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); override;
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    property Images: TCustomImageList read FImages write SetImages;
    property State: TActionListState read FState write SetState default asNormal;
    property OnChange: TNotifyEvent read FOnChange write FOnChange;
    property OnExecute: TActionEvent read FOnExecute write FOnExecute;
    property OnUpdate: TActionEvent read FOnUpdate write FOnUpdate;
  public
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    { UIF-902 step 5: the list's OnExecute for Action; True if it handled it }
    function ExecuteAction(Action: TBasicAction): Boolean; override;
    { UIF-903: the list's OnUpdate for Action; True if it handled it }
    function UpdateAction(Action: TBasicAction): Boolean; override;
    { UIF-923: the first action (index order) whose ShortCut is the key's and whose Execute returns True; False
      while the list is not asNormal }
    function IsShortCut(var Message: TWMKey): Boolean;
    property Actions[Index: Integer]: TContainedAction read GetAction; default;
    property ActionCount: Integer read GetActionCount;
  end;

  TActionList = class(TCustomActionList)
  published
    property Images;
    property State;
    property OnChange;
    property OnExecute;
    property OnUpdate;
  end;

  { actions-menus.md section 1.2: the link of one client. Change propagates each property the client still shares
    with the action: the client's value equals the action's value before the change (UIF-907), which the link
    recorded at the previous Change (or when the action was assigned) }
  TActionLink = class(TBasicActionLink)
  protected
    FCaption: string;
    FChecked: Boolean;
    FEnabled: Boolean;
    FHint: string;
    FImageIndex: Integer;
    FShortCut: TShortCut;
    FVisible: Boolean;
    FOnExecute: TNotifyEvent;
    procedure Change; override;
    procedure SetAction(Value: TBasicAction); override;
    { a client property is linked: the client's value equals the recorded one; base: the client has none }
    function IsCaptionLinked: Boolean; virtual;
    function IsCheckedLinked: Boolean; virtual;
    function IsEnabledLinked: Boolean; virtual;
    function IsHintLinked: Boolean; virtual;
    function IsImageIndexLinked: Boolean; virtual;
    function IsShortCutLinked: Boolean; virtual;
    function IsVisibleLinked: Boolean; virtual;
    function IsOnExecuteLinked: Boolean; override;
    procedure SetCaption(const Value: string); virtual;
    procedure SetChecked(Value: Boolean); virtual;
    procedure SetEnabled(Value: Boolean); virtual;
    procedure SetHint(const Value: string); virtual;
    procedure SetImageIndex(Value: Integer); virtual;
    procedure SetShortCut(Value: TShortCut); virtual;
    procedure SetVisible(Value: Boolean); virtual;
  public
    { records the action's current values as the reference of UIF-907 }
    procedure RecordValues;
  end;

{ the two events are the same method of the same object (both nil counts as the same) }
function SameEvent(const A, B: TNotifyEvent): Boolean;

implementation

function SameEvent(const A, B: TNotifyEvent): Boolean;
begin
  Result := (TMethod(A).Code = TMethod(B).Code) and (TMethod(A).Data = TMethod(B).Data);
end;

{ UIF-921, UIF-930: the shortcut of a key message (Shift/Ctrl from the key state, Alt from bit 29); 0 for a key
  above 255 or a key that is only a modifier }
function KeyShortCut(Key: Word; KeyData: LPARAM): TShortCut;
begin
  Result := 0;
  if (Key > 255) or (Key = VK_SHIFT) or (Key = VK_CONTROL) or (Key = VK_MENU) or (Key = VK_LWIN)
    or (Key = VK_RWIN) then
    Exit;
  Result := Key;
  if GetKeyState(VK_SHIFT) < 0 then
    Result := Result or $2000;
  if GetKeyState(VK_CONTROL) < 0 then
    Result := Result or $4000;
  if (KeyData and $20000000) <> 0 then
    Result := Result or $8000;
end;

{ ---------------------------------------------------------------- TContainedAction }

destructor TContainedAction.Destroy;
begin
  SetActionList(nil);
  inherited Destroy;
end;

procedure TContainedAction.SetActionList(Value: TCustomActionList);
begin
  if FActionList = Value then
    Exit;
  if FActionList <> nil then
    FActionList.RemoveAction(Self);
  if Value <> nil then
    Value.AddAction(Self);
end;

function TContainedAction.GetIndex: Integer;
begin
  if FActionList = nil then
    Result := -1
  else
    Result := FActionList.FActions.IndexOf(Pointer(Self));
end;

procedure TContainedAction.SetIndex(Value: Integer);
var
  I: Integer;
begin
  if FActionList = nil then
    Exit;
  I := GetIndex;
  if Value < 0 then
    Value := 0;
  if Value >= FActionList.FActions.Count then
    Value := FActionList.FActions.Count - 1;
  if Value <> I then
  begin
    FActionList.FActions.Move(I, Value);
    FActionList.Change;
  end;
end;

procedure TContainedAction.SetCategory(const Value: string);
begin
  if FCategory <> Value then
  begin
    FCategory := Value;
    Change;
  end;
end;

{ UIF-900 }
function TContainedAction.HasParent: Boolean;
begin
  Result := FActionList <> nil;
end;

function TContainedAction.GetParentComponent: TComponent;
begin
  Result := FActionList;
end;

procedure TContainedAction.SetParentComponent(Value: TComponent);
begin
  if Value is TCustomActionList then
    ActionList := TCustomActionList(Value)
  else if Value <> nil then
    raise EInvalidOperation.CreateFmt(SInvalidParentClass, [Name, Value.ClassName]);
end;

function TContainedAction.Suspended: Boolean;
begin
  Result := (FActionList <> nil) and (FActionList.FState <> asNormal);
end;

function TContainedAction.DoExecute: Boolean;
begin
  Result := False;
  if FActionList <> nil then
    Result := FActionList.ExecuteAction(Self);
  if not Result and Assigned(OnExecute) then
  begin
    OnExecute(Self);
    Result := True;
  end;
end;

function TContainedAction.Execute: Boolean;
begin
  Result := False;
  if Suspended then
    Exit;
  Update;
  Result := DoExecute;
end;

function TContainedAction.Update: Boolean;
var
  Handled: Boolean;
begin
  Result := False;
  Handled := False;
  if (FActionList <> nil) and Assigned(FActionList.FOnUpdate) then
  begin
    Handled := FActionList.UpdateAction(Self);
    Result := True;
  end;
  if not Handled and Assigned(OnUpdate) then
  begin
    OnUpdate(Self);
    Result := True;
  end;
end;

{ ---------------------------------------------------------------- TCustomAction }

constructor TCustomAction.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FEnabled := True;
  FVisible := True;
  FImageIndex := -1;
end;

{ UIF-902 }
function TCustomAction.Execute: Boolean;
begin
  Result := False;
  if Suspended then
    Exit;
  Update;
  if not FEnabled then
    Exit;
  if FAutoCheck and not (FChecked and (FGroupIndex > 0)) then
    Checked := not FChecked;
  Result := DoExecute;
end;

{ UIF-903: asSuspendedEnabled keeps every action enabled }
function TCustomAction.Update: Boolean;
begin
  Result := inherited Update;
  if (FActionList <> nil) and (FActionList.FState = asSuspendedEnabled) then
    Enabled := True;
end;

function TCustomAction.DoHint(var HintStr: string): Boolean;
begin
  Result := True;
  if Assigned(FOnHint) then
    FOnHint(HintStr, Result);
end;

{ UIF-901: a property change to a different value calls Change }

procedure TCustomAction.SetAutoCheck(Value: Boolean);
begin
  if FAutoCheck <> Value then
  begin
    FAutoCheck := Value;
    Change;
  end;
end;

procedure TCustomAction.SetCaption(const Value: string);
begin
  if FCaption <> Value then
  begin
    FCaption := Value;
    Change;
  end;
end;

{ checking an action with GroupIndex > 0 unchecks the other actions of its list with the same GroupIndex }
procedure TCustomAction.UncheckGroup;
var
  I: Integer;
  A: TContainedAction;
begin
  if (FActionList = nil) or (FGroupIndex <= 0) or (csLoading in ComponentState) then
    Exit;
  for I := 0 to FActionList.ActionCount - 1 do
  begin
    A := FActionList.Actions[I];
    if (A <> Self) and (A is TCustomAction) and (TCustomAction(A).FGroupIndex = FGroupIndex) then
      TCustomAction(A).Checked := False;
  end;
end;

procedure TCustomAction.SetChecked(Value: Boolean);
begin
  if FChecked <> Value then
  begin
    FChecked := Value;
    if Value then
      UncheckGroup;
    Change;
  end;
end;

procedure TCustomAction.SetEnabled(Value: Boolean);
begin
  if (FActionList <> nil) and (FActionList.FState = asSuspendedEnabled) then
    Value := True;
  if FEnabled <> Value then
  begin
    FEnabled := Value;
    Change;
  end;
end;

procedure TCustomAction.SetGroupIndex(Value: Integer);
begin
  if FGroupIndex <> Value then
  begin
    FGroupIndex := Value;
    if FChecked then
      UncheckGroup;
    Change;
  end;
end;

procedure TCustomAction.SetHint(const Value: string);
begin
  if FHint <> Value then
  begin
    FHint := Value;
    Change;
  end;
end;

procedure TCustomAction.SetImageIndex(Value: Integer);
begin
  if FImageIndex <> Value then
  begin
    FImageIndex := Value;
    Change;
  end;
end;

procedure TCustomAction.SetShortCut(Value: TShortCut);
begin
  if FShortCut <> Value then
  begin
    FShortCut := Value;
    Change;
  end;
end;

procedure TCustomAction.SetVisible(Value: Boolean);
begin
  if FVisible <> Value then
  begin
    FVisible := Value;
    Change;
  end;
end;

{ ---------------------------------------------------------------- TCustomActionList }

constructor TCustomActionList.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  FActions := TList.Create;
end;

{ UIF-900: the actions are not destroyed, only released from the list }
destructor TCustomActionList.Destroy;
begin
  SetImages(nil);
  while FActions.Count > 0 do
    TContainedAction(FActions.Last).SetActionList(nil);
  FActions.Free;
  inherited Destroy;
end;

function TCustomActionList.GetAction(Index: Integer): TContainedAction;
begin
  Result := TContainedAction(FActions[Index]);
end;

function TCustomActionList.GetActionCount: Integer;
begin
  Result := FActions.Count;
end;

procedure TCustomActionList.AddAction(Action: TContainedAction);
begin
  FActions.Add(Pointer(Action));
  Action.FActionList := Self;
  Action.FreeNotification(Self);
  Change;
end;

procedure TCustomActionList.RemoveAction(Action: TContainedAction);
begin
  if FActions.Remove(Pointer(Action)) >= 0 then
  begin
    Action.FActionList := nil;
    Action.RemoveFreeNotification(Self);
    if not (csDestroying in ComponentState) then
      Change;
  end;
end;

{ OnChange: the list's contents or order changed }
procedure TCustomActionList.Change;
begin
  if Assigned(FOnChange) then
    FOnChange(Self);
end;

procedure TCustomActionList.GetChildren(Proc: TGetChildProc; Root: TComponent);
var
  I: Integer;
begin
  for I := 0 to FActions.Count - 1 do
    Proc(TComponent(FActions[I]));
end;

procedure TCustomActionList.Notification(AComponent: TComponent; Operation: TOperation);
begin
  inherited Notification(AComponent, Operation);
  if Operation = opRemove then
  begin
    if AComponent = FImages then
      FImages := nil
    else if AComponent is TContainedAction then
      RemoveAction(TContainedAction(AComponent));
  end;
end;

procedure TCustomActionList.SetImages(Value: TCustomImageList);
begin
  if FImages = Value then
    Exit;
  if FImages <> nil then
    FImages.RemoveFreeNotification(Self);
  FImages := Value;
  if FImages <> nil then
    FImages.FreeNotification(Self);
end;

procedure TCustomActionList.SetState(Value: TActionListState);
var
  I: Integer;
begin
  if FState = Value then
    Exit;
  FState := Value;
  if FState = asSuspendedEnabled then
    for I := 0 to FActions.Count - 1 do
      if TObject(FActions[I]) is TCustomAction then
        TCustomAction(FActions[I]).Enabled := True;
end;

function TCustomActionList.ExecuteAction(Action: TBasicAction): Boolean;
begin
  Result := False;
  if Assigned(FOnExecute) then
    FOnExecute(Action, Result);
end;

function TCustomActionList.UpdateAction(Action: TBasicAction): Boolean;
begin
  Result := False;
  if Assigned(FOnUpdate) then
    FOnUpdate(Action, Result);
end;

function TCustomActionList.IsShortCut(var Message: TWMKey): Boolean;
var
  SC: TShortCut;
  I: Integer;
  A: TContainedAction;
begin
  Result := False;
  SC := KeyShortCut(Message.CharCode, Message.KeyData);
  if (SC = 0) or (FState <> asNormal) then
    Exit;
  for I := 0 to FActions.Count - 1 do
  begin
    A := TContainedAction(FActions[I]);
    if (A is TCustomAction) and (TCustomAction(A).FShortCut = SC) and A.Execute then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

{ ---------------------------------------------------------------- TActionLink }

procedure TActionLink.RecordValues;
var
  A: TCustomAction;
begin
  if Action = nil then
    Exit;
  FOnExecute := Action.OnExecute;
  if Action is TCustomAction then
  begin
    A := TCustomAction(Action);
    FCaption := A.FCaption;
    FChecked := A.FChecked;
    FEnabled := A.FEnabled;
    FHint := A.FHint;
    FImageIndex := A.FImageIndex;
    FShortCut := A.FShortCut;
    FVisible := A.FVisible;
  end;
end;

procedure TActionLink.SetAction(Value: TBasicAction);
begin
  inherited SetAction(Value);
  RecordValues;
end;

{ UIF-907 }
procedure TActionLink.Change;
var
  A: TCustomAction;
begin
  if Action is TCustomAction then
  begin
    A := TCustomAction(Action);
    if (A.FCaption <> FCaption) and IsCaptionLinked then
      SetCaption(A.FCaption);
    if (A.FChecked <> FChecked) and IsCheckedLinked then
      SetChecked(A.FChecked);
    if (A.FEnabled <> FEnabled) and IsEnabledLinked then
      SetEnabled(A.FEnabled);
    if (A.FHint <> FHint) and IsHintLinked then
      SetHint(A.FHint);
    if (A.FImageIndex <> FImageIndex) and IsImageIndexLinked then
      SetImageIndex(A.FImageIndex);
    if (A.FShortCut <> FShortCut) and IsShortCutLinked then
      SetShortCut(A.FShortCut);
    if (A.FVisible <> FVisible) and IsVisibleLinked then
      SetVisible(A.FVisible);
  end;
  if (Action <> nil) and not SameEvent(Action.OnExecute, FOnExecute) and IsOnExecuteLinked then
    SetOnExecute(Action.OnExecute);
  RecordValues;
  inherited Change;
end;

function TActionLink.IsCaptionLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsCheckedLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsEnabledLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsHintLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsImageIndexLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsShortCutLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsVisibleLinked: Boolean;
begin
  Result := False;
end;

function TActionLink.IsOnExecuteLinked: Boolean;
begin
  Result := False;
end;

procedure TActionLink.SetCaption(const Value: string);
begin
end;

procedure TActionLink.SetChecked(Value: Boolean);
begin
end;

procedure TActionLink.SetEnabled(Value: Boolean);
begin
end;

procedure TActionLink.SetHint(const Value: string);
begin
end;

procedure TActionLink.SetImageIndex(Value: Integer);
begin
end;

procedure TActionLink.SetShortCut(Value: TShortCut);
begin
end;

procedure TActionLink.SetVisible(Value: Boolean);
begin
end;

initialization
  RegisterClasses([TAction, TActionList]);
end.
