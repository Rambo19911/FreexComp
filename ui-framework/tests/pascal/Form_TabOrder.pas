// covers: UIF-100, UIF-101, UIF-102, UIF-103, UIF-104, UIF-105
// needs: desktop
program Form_TabOrder;
{ U-044, core.md 8.2: the tab list holds windowed children (InsertControl appends, RemoveControl removes);
  TabOrder is the index in it (-1 without a parent), the setter moves and clamps, -1 is ignored; while loading the
  value is stored and the parent's Loaded stable-sorts by it, unassigned controls after all explicit ones in
  creation order; GetTabOrderList is depth-first; FindNextControl wraps once, honours CanFocus, TabStop and the
  parent filter; SelectNext / SelectFirst focus the result. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TTabBox = class(TCustomControl)
  public
    constructor Create(AOwner: TComponent); override;
  published
    property TabOrder;
    property TabStop;
  end;

  TPaintBox = class(TGraphicControl)
  end;

  TTabForm = class(TForm)
  published
    A: TTabBox;
    B: TTabBox;
    C: TTabBox;
    D: TTabBox;
    Panel: TTabBox;
    P1: TTabBox;
    P2: TTabBox;
  end;

{$FORM Form_TabOrder.form}

constructor TTabBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 40, 20);
  TabStop := True;
end;

function Names(L: TList): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to L.Count - 1 do
    Result := Result + TComponent(L[I]).Name + ' ';
end;

function TabNames(W: TWinControl): string;
var
  L: TList;
begin
  L := TList.Create;
  try
    W.GetTabOrderList(L);
    Result := Names(L);
  finally
    L.Free;
  end;
end;

function NameOf(C: TComponent): string;
begin
  if C = nil then
    Result := 'nil'
  else
    Result := C.Name;
end;

function NewBox(const Name: string; Parent: TWinControl): TTabBox;
begin
  Result := TTabBox.Create(nil);
  Result.Name := Name;
  Result.Parent := Parent;
end;

procedure Run;
var
  F: TTabForm;
  G: TForm;
  X, Y, Z, Q: TTabBox;
  Paint: TPaintBox;
begin
  Application.Initialize;

  Section('tab list and TabOrder (UIF-100, UIF-101)');
  G := TForm.CreateNew(nil);
  try
    X := NewBox('X', G);
    Paint := TPaintBox.Create(G);
    Paint.Parent := G;
    Y := NewBox('Y', G);
    Z := NewBox('Z', G);
    CheckEqS('X Y Z ', TabNames(G), 'InsertControl appends windowed children only');
    CheckEqI(0, X.TabOrder, 'X TabOrder 0');
    CheckEqI(2, Z.TabOrder, 'Z TabOrder 2');
    Q := TTabBox.Create(nil);
    try
      CheckEqI(-1, Q.TabOrder, 'no parent: -1');
    finally
      Q.Free;
    end;
    Z.TabOrder := 0;
    CheckEqS('Z X Y ', TabNames(G), 'setter moves');
    X.TabOrder := 99;
    CheckEqS('Z Y X ', TabNames(G), 'setter clamps to Count - 1');
    CheckEqI(2, X.TabOrder, 'clamped value read back');
    Y.TabOrder := -1;
    CheckEqS('Z Y X ', TabNames(G), '-1 is ignored');
    Y.Parent := nil;
    CheckEqS('Z X ', TabNames(G), 'RemoveControl removes');
    Y.Free;

    Section('FindNextControl and SelectNext (UIF-104, UIF-105)');
    G.Show;
    CheckEqS('X', NameOf(G.FindNextControl(Z, True, True, False)), 'forward');
    CheckEqS('Z', NameOf(G.FindNextControl(X, True, True, False)), 'forward wraps around');
    CheckEqS('X', NameOf(G.FindNextControl(Z, False, True, False)), 'backward wraps around');
    CheckEqS('Z', NameOf(G.FindNextControl(nil, True, True, False)), 'nil: from the start');
    CheckEqS('X', NameOf(G.FindNextControl(nil, False, True, False)), 'nil backward: from the end');
    X.TabStop := False;
    CheckEqS('Z', NameOf(G.FindNextControl(Z, True, True, False)), 'the only candidate: Cur itself');
    CheckEqS('X', NameOf(G.FindNextControl(Z, True, False, False)), 'CheckTabStop False');
    X.TabStop := True;
    X.Enabled := False;
    CheckEqS('Z', NameOf(G.FindNextControl(Z, True, False, False)), 'CanFocus required');
    Z.Enabled := False;
    CheckEqS('nil', NameOf(G.FindNextControl(Z, True, False, False)), 'nothing qualifies: nil');
    X.Enabled := True;
    Z.Enabled := True;
    G.SelectFirst;
    CheckEqS('Z', NameOf(G.ActiveControl), 'SelectFirst');
    G.SelectNext(G.ActiveControl, True, True);
    CheckEqS('X', NameOf(G.ActiveControl), 'SelectNext forward');
    G.SelectNext(G.ActiveControl, False, True);
    CheckEqS('Z', NameOf(G.ActiveControl), 'SelectNext backward');
  finally
    G.Free;
  end;

  Section('loading (UIF-102) and the depth-first list (UIF-103)');
  F := TTabForm.Create(nil);
  try
    CheckEqS('C D Panel P2 P1 A B ', TabNames(F), 'explicit values exact, ties and missing values in creation order');
    CheckEqI(0, F.C.TabOrder, 'C 0');
    CheckEqI(1, F.D.TabOrder, 'D 1');
    CheckEqI(2, F.Panel.TabOrder, 'Panel 2 (tie with D: creation order)');
    CheckEqI(3, F.A.TabOrder, 'A 3');
    CheckEqI(4, F.B.TabOrder, 'B 4 (missing: after the explicit ones)');
    CheckEqI(0, F.P2.TabOrder, 'P2 0 inside Panel');
    CheckEqI(1, F.P1.TabOrder, 'P1 1 inside Panel');
    CheckEqS('P2 P1 ', TabNames(F.Panel), 'Self-wide list of Panel');
    F.Show;
    CheckEqS('P2', NameOf(F.FindNextControl(F.Panel, True, True, False)), 'depth-first: Panel then its children');
    CheckEqS('A', NameOf(F.FindNextControl(F.P1, True, True, False)), 'after the last child: the next sibling of Panel');
    CheckEqS('P2', NameOf(F.FindNextControl(F.P1, True, True, True)), 'CheckParent: siblings only, wrapping');
  finally
    F.Free;
  end;
end;

begin
  RegisterClass(TTabBox);
  Run;
  Done;
end.
