// covers: UIF-211, UIF-213
// opc: -U ..\..\src
// needs: desktop
program Core_BoundsHwndSync;
{ U-025, layout.md UIF-211 and UIF-213: SetBounds of a windowed control with a handle moves the window
  (GetWindowRect in parent client coordinates equals the fields) without feeding back; a SetWindowPos from
  outside updates the fields from the real window rectangle and then runs the explicit-change steps - the anchor
  baseline is re-captured, the control's children are realigned, OnResize fires once per size change and the
  parent's layout pass runs (an aligned control is put back in place). A pure move fires no OnResize; a Z-order-
  only change changes nothing. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    Resizes: Integer;
    constructor Create(AOwner: TComponent); override;
    procedure Resize; override;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.Resize;
begin
  Resizes := Resizes + 1;
  inherited Resize;
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

function WinRect(C: TWinControl): string;
var
  R: TRect;
begin
  GetWindowRect(C.Handle, R);
  MapWindowPoints(0, GetParent(C.Handle), @R, 2);
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right - R.Left, R.Bottom - R.Top]);
end;

function Fields(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

procedure MoveWin(C: TWinControl; L, T, W, H: Integer);
begin
  SetWindowPos(C.Handle, 0, L, T, W, H, SWP_NOZORDER or SWP_NOACTIVATE);
end;

procedure Run;
var
  Root: TTopWin;
  A, Fill, Bar: TTestWin;
begin
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 400, 300);
    A := TTestWin.Create(Root);
    A.SetBounds(10, 20, 100, 50);
    A.Parent := Root;
    Fill := TTestWin.Create(Root);
    Fill.Align := alClient;
    Fill.Parent := A;
    Bar := TTestWin.Create(Root);
    Bar.SetBounds(0, 0, 50, 25);
    Bar.Align := alTop;
    Bar.Parent := Root;

    Section('created with the field bounds');
    CheckEqS('10,20,100,50', WinRect(A), 'window rect at creation');
    CheckEqS('0,0,100,50', WinRect(Fill), 'aligned child');

    Section('SetBounds moves the window (UIF-211)');
    A.Resizes := 0;
    A.SetBounds(5, 6, 70, 30);
    CheckEqS('5,6,70,30', WinRect(A), 'window follows');
    CheckEqS('5,6,70,30', Fields(A), 'fields as set, no feedback change');
    CheckEqI(1, A.Resizes, 'one OnResize');
    CheckEqS('0,0,70,30', WinRect(Fill), 'child realigned and moved');
    A.Left := 9;
    CheckEqS('9,6,70,30', WinRect(A), 'Left setter');
    CheckEqI(1, A.Resizes, 'move only: no OnResize');

    Section('OS feedback (UIF-213)');
    A.Resizes := 0;
    MoveWin(A, 15, 16, 80, 40);
    CheckEqS('15,16,80,40', Fields(A), 'fields from the real window rectangle');
    CheckEqI(1, A.Resizes, 'OnResize once');
    CheckEqS('0,0,80,40', Fields(Fill), 'children realigned');
    CheckEqS('0,0,80,40', WinRect(Fill), 'child window follows');
    MoveWin(A, 25, 26, 80, 40);
    CheckEqS('25,26,80,40', Fields(A), 'pure move updates the fields');
    CheckEqI(1, A.Resizes, 'pure move: no OnResize');
    SetWindowPos(A.Handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE or SWP_NOSIZE or SWP_NOACTIVATE);
    CheckEqS('25,26,80,40', Fields(A), 'Z-order-only change keeps the fields');

    Section('baseline re-captured (UIF-213 step 5)');
    A.Anchors := [akLeft, akTop, akRight];
    MoveWin(A, 25, 26, 90, 40);
    Root.Width := Root.Width + 20;
    CheckEqI(110, A.Width, 'anchored width grows from the OS-given baseline');

    Section('parent pass (UIF-213 step 9)');
    MoveWin(Bar, 30, 40, 50, 33);
    CheckEqS('0,0,420,33', Fields(Bar), 'aligned control put back, height from the OS');
    CheckEqS('0,0,420,33', WinRect(Bar), 'its window follows');
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
