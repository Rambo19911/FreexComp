// covers: UIF-145, UIF-146, UIF-147, UIF-148, UIF-149
// opc: -U ..\..\src
// needs: desktop
program Core_ZOrder;
{ U-033, core.md section 11: BringToFront / SendToBack / SetChildOrder move the control in Controls[] and keep
  the Win32 Z order of the windowed siblings equal to the list order (walked top first); graphic controls move
  in the list only; the tab order is not changed; the parent realigns, so align ties broken by list index
  follow the new order; SetChildOrder clamps the index. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    procedure MoveTo(Child: TComponent; Order: Integer);
    property TabOrder;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
  public
    Passes: Integer;
  end;

var
  Root: TTopWin;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.MoveTo(Child: TComponent; Order: Integer);
begin
  SetChildOrder(Child, Order);
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TTopWin.AlignControls(AControl: TControl; var Rect: TRect);
begin
  Passes := Passes + 1;
  inherited AlignControls(AControl, Rect);
end;

function ZOrder: string;
var
  W: HWND;
  C: TWinControl;
begin
  Result := '';
  W := GetWindow(Root.Handle, GW_CHILD);
  while W <> 0 do
  begin
    C := FindControl(W);
    if C = nil then
      Result := Result + '?'
    else
      Result := Result + C.Name;
    W := GetWindow(W, GW_HWNDNEXT);
  end;
end;

function ListOrder: string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to Root.ControlCount - 1 do
    Result := Result + Root.Controls[I].Name;
end;

function NewWin(const AName: string): TTestWin;
begin
  Result := TTestWin.Create(Root);
  Result.Name := AName;
  Result.SetBounds(0, 0, 40, 20);
  Result.Parent := Root;
end;

procedure Run;
var
  A, B, C: TTestWin;
  G: TControl;
begin
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 200, 150);
    A := NewWin('A');
    G := TControl.Create(Root);
    G.Name := 'G';
    G.Parent := Root;
    B := NewWin('B');
    C := NewWin('C');
    Root.HandleNeeded;
    CheckEqS('AGBC', ListOrder, 'creation order');
    CheckEqS('CBA', ZOrder, 'Z order top first');

    Section('BringToFront (UIF-146)');
    Root.Passes := 0;
    A.BringToFront;
    CheckEqS('GBCA', ListOrder, 'moved to the end');
    CheckEqS('ACB', ZOrder, 'window on top');
    CheckEqI(1, Root.Passes, 'one layout pass of the parent (UIF-148)');
    CheckEqI(0, A.TabOrder, 'tab order unchanged');

    Section('SendToBack (UIF-146)');
    C.SendToBack;
    CheckEqS('CGBA', ListOrder, 'moved to index 0');
    CheckEqS('ABC', ZOrder, 'window at the bottom');
    CheckEqI(2, C.TabOrder, 'tab order unchanged');
    G.BringToFront;
    CheckEqS('CBAG', ListOrder, 'graphic control moves in the list');
    CheckEqS('ABC', ZOrder, 'windows untouched');

    Section('SetChildOrder (UIF-149)');
    Root.MoveTo(A, 0);
    CheckEqS('ACBG', ListOrder, 'to index 0');
    CheckEqS('BCA', ZOrder, 'window follows');
    Root.MoveTo(A, 99);
    CheckEqS('CBGA', ListOrder, 'clamped to the last index');
    CheckEqS('ABC', ZOrder, 'window on top');
    Root.MoveTo(C, 2);
    CheckEqS('BGCA', ListOrder, 'middle position');
    CheckEqS('ACB', ZOrder, 'window between');
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
