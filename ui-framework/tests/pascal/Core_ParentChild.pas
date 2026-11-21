// covers: UIF-037, UIF-045, UIF-046, UIF-047, UIF-048, UIF-049, UIF-051, UIF-100, UIF-101, UIF-145
program Core_ParentChild;
{ U-015, core.md sections 3 and 5 (handle-less part): Controls[] in creation order (one list for graphic and
  windowed children), tab list of the windowed children, Parent assignment rules and the InsertControl message
  order, moving between parents, owner and parent independence, child destruction highest index first
  regardless of owner, owned controls parented elsewhere, GetChildren, SetParentComponent, FindChildControl,
  ContainsControl, ControlAtPos, Broadcast. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

var
  Log: string;

type
  TLogControl = class(TControl)
  public
    destructor Destroy; override;
    procedure PlaceOn(Value: TComponent);
    procedure WndProc(var Message: TMessage); override;
  end;

  TLogWin = class(TWinControl)
  public
    Answer: Integer;
    constructor Create(AOwner: TComponent); override;
    destructor Destroy; override;
    procedure WndProc(var Message: TMessage); override;
    procedure GetChildren(Proc: TGetChildProc; Root: TComponent); override;
    property TabOrder;
  end;

  TCollector = class
    Names: string;
    procedure Add(Child: TComponent);
  end;

procedure TCollector.Add(Child: TComponent);
begin
  Names := Names + Child.Name + ';';
end;

destructor TLogControl.Destroy;
begin
  Log := Log + 'free ' + Name + ';';
  inherited Destroy;
end;

procedure TLogControl.PlaceOn(Value: TComponent);
begin
  SetParentComponent(Value);
end;

procedure TLogControl.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    CM_PARENTFONTCHANGED: Log := Log + Name + ':font;';
    CM_PARENTCOLORCHANGED: Log := Log + Name + ':color;';
    CM_PARENTSHOWHINTCHANGED: Log := Log + Name + ':showhint;';
    WM_APP: Log := Log + Name + ':app;';
  end;
  inherited WndProc(Message);
end;

constructor TLogWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

destructor TLogWin.Destroy;
begin
  Log := Log + 'free ' + Name + ';';
  inherited Destroy;
end;

procedure TLogWin.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_APP then
  begin
    Log := Log + Name + ':app;';
    Message.Result := Answer;
  end;
  inherited WndProc(Message);
end;

procedure TLogWin.GetChildren(Proc: TGetChildProc; Root: TComponent);
begin
  inherited GetChildren(Proc, Root);
end;

function NewCtl(AOwner: TComponent; const AName: string; AParent: TWinControl): TLogControl;
begin
  Result := TLogControl.Create(AOwner);
  Result.Name := AName;
  Result.Parent := AParent;
end;

function NewWin(AOwner: TComponent; const AName: string; AParent: TWinControl): TLogWin;
begin
  Result := TLogWin.Create(AOwner);
  Result.Name := AName;
  Result.Parent := AParent;
end;

function ChildNames(P: TWinControl): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to P.ControlCount - 1 do
    Result := Result + P.Controls[I].Name + ';';
end;

procedure CheckLists;
var
  Root: TComponent;
  P, Q, W1, W2: TLogWin;
  G1, G2: TLogControl;
  M: TMessage;
begin
  Section('Controls[] and tab list (UIF-100, UIF-145)');
  Root := TComponent.Create(nil);
  try
    P := NewWin(Root, 'P', nil);
    Q := NewWin(Root, 'Q', nil);
    CheckEqI(0, P.ControlCount, 'empty');
    G1 := NewCtl(Root, 'G1', P);
    W1 := NewWin(Root, 'W1', P);
    G2 := NewCtl(Root, 'G2', P);
    W2 := NewWin(Root, 'W2', P);
    CheckEqS('G1;W1;G2;W2;', ChildNames(P), 'creation order, one list');
    CheckEqB(True, G1.Parent = P, 'Parent');
    CheckEqI(0, W1.TabOrder, 'W1 first in the tab list');
    CheckEqI(1, W2.TabOrder, 'W2 second in the tab list');
    CheckEqI(-1, Q.TabOrder, 'no parent: TabOrder -1');
    W2.TabOrder := 0;
    CheckEqI(0, W2.TabOrder, 'TabOrder setter moves');
    CheckEqI(1, W1.TabOrder, 'other control shifts');
    CheckEqS('G1;W1;G2;W2;', ChildNames(P), 'tab order does not change Controls[]');
    W2.TabOrder := 99;
    CheckEqI(1, W2.TabOrder, 'clamped to Count - 1');
    W2.TabOrder := -1;
    CheckEqI(1, W2.TabOrder, '-1 ignored');

    Section('Parent rules (UIF-046, UIF-047)');
    Log := '';
    G1.Parent := P;
    CheckEqS('', Log, 'same parent is a no-op');
    CheckEqS('G1;W1;G2;W2;', ChildNames(P), 'no reorder');
    G1.Parent := Q;
    CheckEqS('G1:font;G1:color;G1:showhint;', Log, 'InsertControl sends the parent messages in order');
    CheckEqS('W1;G2;W2;', ChildNames(P), 'removed from the old parent');
    CheckEqS('G1;', ChildNames(Q), 'appended to the new parent');
    W2.Parent := Q;
    CheckEqI(0, W1.TabOrder, 'tab list of the old parent updated');
    CheckEqI(0, W2.TabOrder, 'appended to the new tab list');
    Expect('EInvalidOperation', 'A control cannot have itself as its parent');
    try
      Q.Parent := Q;
      Check(False, 'self as parent raises');
    except
      on E: Exception do
        CheckRaises('self as parent raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidOperation', 'A control cannot have itself as its parent');
    try
      Q.Parent := W2;
      Check(False, 'descendant as parent raises');
    except
      on E: Exception do
        CheckRaises('descendant as parent raises', E.ClassName, E.Message);
    end;
    CheckEqB(True, Q.Parent = nil, 'rejected parent leaves Parent unchanged');
    W1.ControlStyle := W1.ControlStyle - [csAcceptsControls];
    G2.Parent := W1;
    CheckEqB(True, G2.Parent = W1, 'parent without csAcceptsControls is allowed at run time');
    G2.Parent := nil;
    CheckEqB(True, G2.Parent = nil, 'Parent := nil');
    CheckEqS('W1;', ChildNames(P), 'removed');
    CheckEqB(False, G2.Showing, 'no parent: not showing (UIF-048)');

    Section('SetParentComponent and lookups');
    G2.Free;
    G2 := TLogControl.Create(Root);
    G2.Name := 'G2';
    G2.PlaceOn(P);
    CheckEqB(True, G2.Parent = P, 'SetParentComponent with a TWinControl');
    Expect('EInvalidOperation', 'Control "G2" cannot be placed on a TComponent');
    try
      G2.PlaceOn(Root);
      Check(False, 'SetParentComponent with a non-control raises');
    except
      on E: Exception do
        CheckRaises('SetParentComponent with a non-control raises', E.ClassName, E.Message);
    end;
    CheckEqB(True, P.FindChildControl('g2') = G2, 'FindChildControl is case-insensitive');
    CheckEqB(True, P.FindChildControl('G1') = nil, 'FindChildControl: direct children only');
    CheckEqB(True, Q.ContainsControl(Q) and Q.ContainsControl(W2) and not Q.ContainsControl(P), 'ContainsControl');
    CheckEqB(True, G2.GetParentComponent = P, 'GetParentComponent');
    CheckEqB(True, G2.HasParent, 'HasParent');

    Section('ControlAtPos');
    G2.SetBounds(0, 0, 50, 50);
    W1.SetBounds(10, 10, 50, 50);
    CheckEqB(True, P.ControlAtPos(Point(20, 20), False) = G2, 'graphic child; windowed skipped');
    CheckEqB(True, P.ControlAtPos(Point(20, 20), False, True) = G2, 'highest index first');
    G2.BringToFront;
    CheckEqS('W1;G2;', ChildNames(P), 'BringToFront moves to the end');
    W1.BringToFront;
    CheckEqB(True, P.ControlAtPos(Point(20, 20), False, True) = W1, 'windowed child when allowed');
    G2.Enabled := False;
    CheckEqB(True, P.ControlAtPos(Point(5, 5), False) = nil, 'disabled skipped');
    CheckEqB(True, P.ControlAtPos(Point(5, 5), True) = G2, 'disabled allowed');
    G2.Visible := False;
    CheckEqB(True, P.ControlAtPos(Point(5, 5), True) = nil, 'invisible skipped');
    CheckEqB(True, P.ControlAtPos(Point(50, 50), True) = nil, 'right/bottom edge excluded');
    G2.Visible := True;
    G2.Enabled := True;
    G2.SendToBack;
    CheckEqS('G2;W1;', ChildNames(P), 'SendToBack moves to index 0');

    Section('Broadcast');
    Log := '';
    W1.Answer := 0;
    P.NotifyControls(WM_APP);
    CheckEqS('G2:app;W1:app;', Log, 'index order');
    Log := '';
    G2.BringToFront;
    W1.Answer := 1;
    M.Msg := WM_APP;
    M.WParam := 0;
    M.LParam := 0;
    M.Result := 0;
    P.Broadcast(M);
    CheckEqS('W1:app;', Log, 'stops at the first non-zero result');
    CheckEqI(1, M.Result, 'Broadcast result');
  finally
    Root.Free;
  end;
end;

procedure CheckDestruction;
var
  Owner1, Owner2: TComponent;
  P, Inner: TLogWin;
  C: TCollector;
begin
  Section('destruction (UIF-037, UIF-049)');
  Owner1 := TComponent.Create(nil);
  Owner2 := TComponent.Create(nil);
  C := TCollector.Create;
  try
    P := NewWin(nil, 'P', nil);
    NewCtl(Owner1, 'A', P);
    Inner := NewWin(Owner2, 'B', P);
    NewCtl(Owner1, 'B1', Inner);
    NewCtl(Owner2, 'B2', Inner);
    NewCtl(nil, 'C', P);

    Section('GetChildren (UIF-051)');
    P.GetChildren(C.Add, Owner1);
    CheckEqS('A;', C.Names, 'only children owned by Root, direct only');
    C.Names := '';
    Inner.GetChildren(C.Add, Owner2);
    CheckEqS('B2;', C.Names, 'B2 owned by Owner2');

    Log := '';
    P.Free;
    CheckEqS('free P;free C;free B;free B2;free B1;free A;', Log, 'children destroyed highest index first, depth first');
    CheckEqI(0, Owner1.ComponentCount, 'owned children left their owner');
    CheckEqI(0, Owner2.ComponentCount, 'owned children left their owner (2)');

    Section('owner destroys a control parented elsewhere');
    P := NewWin(nil, 'P', nil);
    NewCtl(Owner1, 'X', P);
    NewCtl(nil, 'Y', P);
    Log := '';
    Owner1.Free;
    Owner1 := nil;
    CheckEqS('free X;', Log, 'owner frees X');
    CheckEqS('Y;', ChildNames(P), 'X removed itself from its parent');
    P.Free;
  finally
    Owner1.Free;
    Owner2.Free;
    C.Free;
  end;
end;

begin
  CheckLists;
  CheckDestruction;
  Done;
end.
