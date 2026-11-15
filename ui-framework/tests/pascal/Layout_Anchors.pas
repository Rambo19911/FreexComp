// covers: UIF-230, UIF-231, UIF-232
// opc: -U ..\..\src
program Layout_Anchors;
{ U-019, layout.md section 5 and the MulDiv definition of section 1: every horizontal and vertical case of
  UIF-231, the centring case with MulDiv rounding half away from zero (also for negative products), the baseline
  capture moments of UIF-232 (explicit change, Parent assigned, Anchors/Align changed, Loaded) and never by a
  layout pass (no drift), constraints after anchoring without recomputing Left/Top. Worked examples E16-E23, E38. The parent is resized and then re-aligned explicitly. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TBox = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTestControl = class(TControl)
  public
    procedure StartLoading;
    procedure EndLoading;
  end;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestControl.StartLoading;
begin
  Loading;
end;

procedure TTestControl.EndLoading;
begin
  Loaded;
end;

function BoundsStr(C: TControl): string;
begin
  Result := '(' + IntToStr(C.Left) + ', ' + IntToStr(C.Top) + ', ' + IntToStr(C.Width) + ', ' + IntToStr(C.Height) + ')';
end;

function NewParent(W, H: Integer): TBox;
begin
  Result := TBox.Create(nil);
  Result.SetBounds(0, 0, W, H);
end;

function Add(P: TWinControl; L, T, W, H: Integer; A: TAnchors): TTestControl;
begin
  Result := TTestControl.Create(P);
  Result.SetBounds(L, T, W, H);
  Result.Anchors := A;
  Result.Parent := P;
end;

procedure Resize(P: TWinControl; W, H: Integer);
begin
  P.SetBounds(P.Left, P.Top, W, H);
  P.Realign;
end;

procedure CheckMulDiv;
begin
  Section('MulDiv (layout.md section 1)');
  CheckEqI(300, LayoutMulDiv(200, 600, 400), 'exact');
  CheckEqI(177, LayoutMulDiv(265, 200, 300), '176.67 rounds to 177');
  CheckEqI(3, LayoutMulDiv(5, 1, 2), 'half rounds up for a positive product');
  CheckEqI(-3, LayoutMulDiv(-5, 1, 2), 'half rounds away from zero for a negative product');
  CheckEqI(-3, LayoutMulDiv(5, -1, 2), 'negative numerator');
  CheckEqI(-2, LayoutMulDiv(-7, 1, 4), '-1.75 rounds to -2');
  CheckEqI(-1, LayoutMulDiv(-5, 1, 4), '-1.25 truncates to -1');
  CheckEqI(MulDiv(-265, 200, 300), LayoutMulDiv(-265, 200, 300), 'agrees with Win32 MulDiv');
  CheckEqI(1500000000, LayoutMulDiv(1000000000, 3, 2), '64-bit intermediate');
end;

procedure Examples;
var
  P: TBox;
  C: TTestControl;
begin
  Section('E16 right and bottom');
  P := NewParent(400, 300);
  C := Add(P, 300, 260, 75, 25, [akRight, akBottom]);
  Resize(P, 500, 350);
  CheckEqS('(400, 310, 75, 25)', BoundsStr(C), 'Btn');
  P.Free;

  Section('E17 left, top, right stretches');
  P := NewParent(400, 300);
  C := Add(P, 16, 16, 200, 23, [akLeft, akTop, akRight]);
  Resize(P, 500, 300);
  CheckEqS('(16, 16, 300, 23)', BoundsStr(C), 'Edit');
  P.Free;

  Section('E18 all four anchors shrink');
  P := NewParent(400, 300);
  C := Add(P, 8, 48, 384, 200, [akLeft, akTop, akRight, akBottom]);
  Resize(P, 320, 240);
  CheckEqS('(8, 48, 304, 140)', BoundsStr(C), 'Memo');
  P.Free;

  Section('E19 horizontal centring');
  P := NewParent(400, 300);
  C := Add(P, 150, 100, 100, 50, [akTop]);
  Resize(P, 600, 300);
  CheckEqS('(250, 100, 100, 50)', BoundsStr(C), 'C');
  P.Free;

  Section('E20 centring both ways');
  P := NewParent(100, 100);
  C := Add(P, 10, 10, 31, 21, []);
  Resize(P, 150, 175);
  CheckEqS('(23, 25, 31, 21)', BoundsStr(C), 'C');
  P.Free;

  Section('E21 no drift');
  P := NewParent(400, 300);
  C := Add(P, 16, 16, 200, 23, [akLeft, akTop, akRight]);
  Resize(P, 150, 300);
  CheckEqS('(16, 16, 0, 23)', BoundsStr(C), 'clamped at 0');
  Resize(P, 400, 300);
  CheckEqS('(16, 16, 200, 23)', BoundsStr(C), 'restored exactly');
  P.Free;

  Section('E22 constraint after anchoring');
  P := NewParent(400, 300);
  C := Add(P, 16, 16, 200, 23, [akLeft, akTop, akRight]);
  C.Constraints.MaxWidth := 250;
  Resize(P, 500, 300);
  CheckEqS('(16, 16, 250, 23)', BoundsStr(C), 'X');
  P.Free;

  Section('right anchor with constraint keeps Left (UIF-231)');
  P := NewParent(400, 300);
  C := Add(P, 300, 10, 50, 20, [akRight]);
  C.Constraints.MaxWidth := 50;
  Resize(P, 450, 300);
  CheckEqS('(350, 10, 50, 20)', BoundsStr(C), 'moved by dx');
  P.Free;

  Section('E23 explicit change re-captures the baseline');
  P := NewParent(400, 300);
  C := Add(P, 300, 260, 75, 25, [akRight, akBottom]);
  Resize(P, 500, 300);
  CheckEqS('(400, 260, 75, 25)', BoundsStr(C), 'after step 1');
  C.Left := 380;
  CheckEqS('(380, 260, 75, 25)', BoundsStr(C), 'after step 2');
  Resize(P, 600, 300);
  CheckEqS('(480, 260, 75, 25)', BoundsStr(C), 'after step 3');
  P.Free;

  Section('E38 vertical centring rounding');
  P := NewParent(400, 300);
  C := Add(P, 10, 250, 80, 30, [akLeft]);
  Resize(P, 400, 200);
  CheckEqS('(10, 162, 80, 30)', BoundsStr(C), 'A');
  P.Free;

  Section('Anchors change re-captures (UIF-232 case 3)');
  P := NewParent(400, 300);
  C := Add(P, 300, 10, 50, 20, [akLeft, akTop]);
  Resize(P, 500, 300);
  CheckEqS('(300, 10, 50, 20)', BoundsStr(C), 'left anchored stays');
  C.Anchors := [akRight, akTop];
  Resize(P, 600, 300);
  CheckEqS('(400, 10, 50, 20)', BoundsStr(C), 'baseline taken at parent 500');
  P.Free;

  Section('Parent assigned captures (UIF-232 case 2)');
  P := NewParent(400, 300);
  C := TTestControl.Create(P);
  C.SetBounds(300, 10, 50, 20);
  C.Anchors := [akRight, akTop];
  Resize(P, 500, 300);
  C.Parent := P;
  CheckEqS('(300, 10, 50, 20)', BoundsStr(C), 'not moved on insertion');
  Resize(P, 520, 300);
  CheckEqS('(320, 10, 50, 20)', BoundsStr(C), 'baseline taken at insertion');
  P.Free;

  Section('centring is stable at an unchanged parent size');
  P := NewParent(400, 300);
  C := TTestControl.Create(P);
  C.Anchors := [];
  C.SetBounds(40, 30, 20, 10);
  P.DisableAlign;
  C.Parent := P;
  P.EnableAlign;
  CheckEqS('(40, 30, 20, 10)', BoundsStr(C), 'centring with a baseline from insertion');
  P.Free;

  Section('loading (UIF-232 case 4)');
  P := NewParent(400, 300);
  C := Add(P, 300, 10, 50, 20, [akRight]);
  C.StartLoading;
  C.SetBounds(100, 10, 50, 20);
  Resize(P, 500, 300);
  CheckEqS('(400, 10, 50, 20)', BoundsStr(C), 'loading change does not capture');
  C.SetBounds(100, 10, 50, 20);
  C.EndLoading;
  Resize(P, 520, 300);
  CheckEqS('(120, 10, 50, 20)', BoundsStr(C), 'Loaded captures the stored bounds');
  P.Free;
end;

begin
  CheckMulDiv;
  Examples;
  Done;
end.
