// phase: 4
// covers: RTL-450, RTL-451, RTL-452, RTL-453, RTL-460, RTL-461
program Cls_ComponentOwnership;
{ TComponent ownership (classes.md section 8.1): construction through a virtual constructor and TComponentClass,
  ComponentStyle (RTL-450); InsertComponent validates, moves a component from its old owner, propagates csDesigning
  and notifies (RTL-451); RemoveComponent (RTL-452); destruction order: Destroying on the whole tree, owned
  components freed last to first, each removed before it is destroyed (RTL-453); ComponentIndex moves and clamps
  (RTL-460); Loaded clears csLoading (RTL-461); Components[] index errors. }
uses
  SysUtils, Classes, TestKit;

var
  Log: string;

type
  TLog = class(TComponent)
  protected
    procedure Notification(AComponent: TComponent; Operation: TOperation); override;
    procedure ValidateInsert(AComponent: TComponent); override;
  public
    Made: Boolean;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure StartLoading;
    procedure FinishLoading;
    procedure Design(Value: Boolean);
  end;

  TPicky = class(TLog)
  protected
    procedure ValidateInsert(AComponent: TComponent); override;
  end;

function Nm(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else
    Result := C.Name;
end;

constructor TLog.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  Made := True;
end;

destructor TLog.Destroy;
begin
  Log := Log + 'destroy ' + Name + BoolToStr(csDestroying in ComponentState, True) + ';';
  inherited Destroy;
end;

procedure TLog.Notification(AComponent: TComponent; Operation: TOperation);
const
  Ops: array[TOperation] of string = ('ins', 'rem');
begin
  Log := Log + Name + '.' + Ops[Operation] + '(' + Nm(AComponent) + ');';   { logged before forwarding }
  inherited Notification(AComponent, Operation);
end;

procedure TLog.ValidateInsert(AComponent: TComponent);
begin
  Log := Log + Name + '.validate(' + Nm(AComponent) + ');';
end;

procedure TLog.StartLoading;
begin
  Loading;
end;

procedure TLog.FinishLoading;
begin
  Loaded;
end;

procedure TLog.Design(Value: Boolean);
begin
  SetDesigning(Value);
end;

procedure TPicky.ValidateInsert(AComponent: TComponent);
begin
  raise EComponentError.Create('no children here');
end;

function Children(C: TComponent): string;
var
  I: Integer;
begin
  Result := IntToStr(C.ComponentCount) + ':';
  for I := 0 to C.ComponentCount - 1 do
    Result := Result + ' ' + C.Components[I].Name + '@' + IntToStr(C.Components[I].ComponentIndex);
end;

function Make(AOwner: TComponent; const AName: string): TLog;
begin
  Result := TLog.Create(nil);
  Result.Name := AName;
  if AOwner <> nil then
    AOwner.InsertComponent(Result);
end;

var
  Root, A, B, C, X: TLog;
  P: TPicky;
  Cls: TComponentClass;
  Comp: TComponent;
  Msg: string;
begin
  Section('construction (RTL-450)');
  Cls := TLog;
  Comp := Cls.Create(nil);
  Check(Comp is TLog, 'class reference creates the class');
  Check(TLog(Comp).Made, 'the overriding constructor ran');
  Check(Comp.ComponentStyle = [csInheritable], 'ComponentStyle');
  Check(Comp.ComponentState = [], 'ComponentState');
  Check(Comp.Owner = nil, 'no owner');
  CheckEqI(0, Comp.ComponentCount, 'no components');
  CheckEqI(-1, Comp.ComponentIndex, 'ComponentIndex without owner');
  Comp.Free;
  Log := '';
  Root := Make(nil, 'Root');
  A := TLog.Create(Root);
  Check(A.Owner = Root, 'Create(AOwner) inserts');
  CheckEqS('Root.validate();Root.ins();.ins();', Log, 'owner validates and is notified on insert');

  Section('insert (RTL-451)');
  A.Name := 'A';
  Log := '';
  B := Make(Root, 'B');
  CheckEqS('Root.validate(B);Root.ins(B);A.ins(B);B.ins(B);', Log, 'the owner validates, then owner and owned are notified');
  C := Make(Root, 'C');
  CheckEqS('3: A@0 B@1 C@2', Children(Root), 'Components and ComponentIndex');
  X := Make(nil, 'X');
  Log := '';
  X.InsertComponent(B);
  CheckEqS('X.validate(B);Root.rem(B);A.rem(B);B.rem(B);C.rem(B);X.ins(B);B.ins(B);', Log, 'moved from the old owner');
  Check(B.Owner = X, 'new owner');
  CheckEqS('2: A@0 C@1', Children(Root), 'old owner list');
  P := TPicky.Create(nil);
  Comp := TLog.Create(nil);
  Msg := '';
  try
    P.InsertComponent(Comp);
  except
    on E: EComponentError do
      Msg := E.Message;
  end;
  CheckEqS('no children here', Msg, 'the owner''s ValidateInsert can refuse');
  Check(Comp.Owner = nil, 'refused component not inserted');
  Comp.Free;
  P.Free;
  X.Design(True);
  Comp := TLog.Create(X);
  Check(csDesigning in Comp.ComponentState, 'csDesigning propagated on insert');
  Check(csDesigning in B.ComponentState, 'SetDesigning reaches owned components');
  X.Design(False);
  Check(not (csDesigning in B.ComponentState), 'SetDesigning False');
  Comp.Free;

  Section('remove (RTL-452)');
  Log := '';
  X.RemoveComponent(B);
  CheckEqS('X.rem(B);B.rem(B);', Log, 'RemoveComponent notifies before removing');
  Check(B.Owner = nil, 'owner cleared');
  CheckEqI(0, X.ComponentCount, 'removed from the list');
  Root.InsertComponent(B);
  X.Free;

  Section('ComponentIndex (RTL-460)');
  CheckEqS('3: A@0 C@1 B@2', Children(Root), 'before');
  B.ComponentIndex := 0;
  CheckEqS('3: B@0 A@1 C@2', Children(Root), 'moved to 0');
  B.ComponentIndex := 99;
  CheckEqS('3: A@0 C@1 B@2', Children(Root), 'clamped high');
  B.ComponentIndex := -5;
  CheckEqS('3: B@0 A@1 C@2', Children(Root), 'clamped low');
  Msg := '';
  try
    Comp := Root.Components[3];
  except
    on E: EListError do
      Msg := E.Message;
  end;
  CheckEqS('List index out of bounds (3)', Msg, 'Components index error');
  Msg := '';
  try
    Comp := B.Components[0];
  except
    on E: EListError do
      Msg := E.Message;
  end;
  CheckEqS('List index out of bounds (0)', Msg, 'Components of a component without children');

  Section('loading (RTL-461)');
  A.StartLoading;
  Check(csLoading in A.ComponentState, 'Loading sets csLoading');
  A.FinishLoading;
  Check(not (csLoading in A.ComponentState), 'Loaded clears csLoading');

  Section('destruction (RTL-453)');
  TLog.Create(A).Name := 'A1';
  Log := '';
  Root.Destroying;
  Check(csDestroying in A.Components[0].ComponentState, 'Destroying reaches the whole tree');
  Root.Destroying;
  Root.Free;
  CheckEqS('destroy RootTrue;' +
    'Root.rem(C);B.rem(C);A.rem(C);A1.rem(C);C.rem(C);destroy CTrue;' +
    'Root.rem(A);B.rem(A);A.rem(A);A1.rem(A);destroy ATrue;A.rem(A1);A1.rem(A1);destroy A1True;' +
    'Root.rem(B);B.rem(B);destroy BTrue;', Log, 'last to first, removed before destroyed');
  Done;
end.
