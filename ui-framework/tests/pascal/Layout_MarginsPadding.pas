// covers: UIF-215, UIF-218, UIF-220, UIF-235, UIF-236, UIF-237
// opc: -U ..\..\src
program Layout_MarginsPadding;
{ U-018, layout.md sections 3 and 6: the align rect = client rect (virtual formula) -> AdjustClientRect -> inset
  by Padding; Margins apply only with AlignWithMargins and Align <> alNone; Padding never changes the client size
  and never moves alNone children; changing Padding, Margins or AlignWithMargins runs a pass. Worked examples
  E07, E08, E09, E10, E36 and E25 (with a test container standing in for the panel's inset). }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TBox = class(TWinControl)
  public
    Inset: Integer;                                          { AdjustClientRect inset on all sides }
    Border: Integer;                                         { client-size formula: Width - 2 * Border }
    constructor Create(AOwner: TComponent); override;
    function GetClientRect: TRect; override;
    procedure AdjustClientRect(var Rect: TRect); override;
    property Padding;
  end;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

function TBox.GetClientRect: TRect;
begin
  Result := inherited GetClientRect;
  Result.Right := Result.Right - 2 * Border;
  Result.Bottom := Result.Bottom - 2 * Border;
end;

procedure TBox.AdjustClientRect(var Rect: TRect);
begin
  inherited AdjustClientRect(Rect);
  InflateRect(Rect, -Inset, -Inset);
end;

function BoundsStr(C: TControl): string;
begin
  Result := '(' + IntToStr(C.Left) + ', ' + IntToStr(C.Top) + ', ' + IntToStr(C.Width) + ', ' + IntToStr(C.Height) + ')';
end;

function NewParent(W, H: Integer): TBox;
begin
  Result := TBox.Create(nil);
  Result.SetBounds(0, 0, W, H);
  Result.DisableAlign;
end;

function Add(P: TWinControl; AAlign: TAlign; L, T, W, H: Integer; WithMargins: Boolean): TControl;
begin
  Result := TControl.Create(P);
  Result.SetBounds(L, T, W, H);
  Result.Align := AAlign;
  Result.AlignWithMargins := WithMargins;
  Result.Parent := P;
end;

procedure Examples;
var
  P: TBox;
  A, B, C, N, R: TControl;
begin
  Section('defaults');
  P := NewParent(400, 300);
  CheckEqS('0 0 0 0', IntToStr(P.Padding.Left) + ' ' + IntToStr(P.Padding.Top) + ' ' + IntToStr(P.Padding.Right) + ' '
    + IntToStr(P.Padding.Bottom), 'Padding 0');
  P.Free;

  Section('E07 AlignWithMargins');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 0, 10, 30, True);
  C := Add(P, alClient, 0, 0, 10, 10, False);
  P.EnableAlign;
  CheckEqS('(3, 3, 394, 30)', BoundsStr(A), 'A');
  CheckEqS('(0, 36, 400, 264)', BoundsStr(C), 'C');
  P.Free;

  Section('E08 individual margins');
  P := NewParent(400, 300);
  A := Add(P, alLeft, 0, 0, 100, 10, True);
  A.Margins.SetBounds(10, 5, 20, 15);
  C := Add(P, alClient, 0, 0, 10, 10, True);
  P.EnableAlign;
  CheckEqS('(10, 5, 100, 280)', BoundsStr(A), 'A');
  CheckEqS('(133, 3, 264, 294)', BoundsStr(C), 'C');
  P.Free;

  Section('E09 Padding');
  P := NewParent(400, 300);
  P.Padding.SetBounds(10, 10, 10, 10);
  C := Add(P, alClient, 0, 0, 10, 10, False);
  P.EnableAlign;
  CheckEqS('(10, 10, 380, 280)', BoundsStr(C), 'C');
  CheckEqI(400, P.ClientWidth, 'Padding does not change the client size (UIF-236)');
  P.Free;

  Section('E10 Padding ignores alNone');
  P := NewParent(400, 300);
  P.Padding.SetBounds(8, 8, 8, 8);
  A := Add(P, alTop, 0, 0, 10, 24, False);
  N := Add(P, alNone, 5, 5, 50, 20, False);
  P.EnableAlign;
  CheckEqS('(8, 8, 384, 24)', BoundsStr(A), 'T');
  CheckEqS('(5, 5, 50, 20)', BoundsStr(N), 'N unchanged');
  P.Free;

  Section('E36 margins on bottom and right');
  P := NewParent(400, 300);
  B := Add(P, alBottom, 0, 0, 10, 20, True);
  R := Add(P, alRight, 0, 0, 60, 10, True);
  C := Add(P, alClient, 0, 0, 10, 10, False);
  P.EnableAlign;
  CheckEqS('(3, 277, 394, 20)', BoundsStr(B), 'B');
  CheckEqS('(337, 3, 60, 268)', BoundsStr(R), 'R');
  CheckEqS('(0, 0, 334, 274)', BoundsStr(C), 'C');
  P.Free;

  Section('margins never affect alNone (UIF-235)');
  P := NewParent(400, 300);
  N := Add(P, alNone, 5, 6, 7, 8, True);
  P.EnableAlign;
  CheckEqS('(5, 6, 7, 8)', BoundsStr(N), 'alNone with AlignWithMargins');
  P.Free;

  Section('E25 AdjustClientRect inset');
  P := NewParent(200, 100);
  P.Inset := 1;
  C := Add(P, alClient, 0, 0, 10, 10, False);
  P.EnableAlign;
  CheckEqS('(1, 1, 198, 98)', BoundsStr(C), 'a: inset 1');
  P.Inset := 5;
  P.Realign;
  CheckEqS('(5, 5, 190, 90)', BoundsStr(C), 'b: inset 5');
  P.Free;

  Section('client rect formula (UIF-215) feeds the align rect');
  P := NewParent(200, 100);
  P.Border := 1;
  C := Add(P, alClient, 0, 0, 10, 10, False);
  P.EnableAlign;
  CheckEqI(198, P.ClientWidth, 'ClientWidth by formula');
  CheckEqS('(0, 0, 198, 98)', BoundsStr(C), 'alClient fills the formula client rect');
  P.Free;

  Section('changes trigger a pass');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 0, 10, 30, False);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(A), 'before');
  P.Padding.Top := 4;
  CheckEqS('(0, 4, 400, 30)', BoundsStr(A), 'Padding change');
  A.AlignWithMargins := True;
  CheckEqS('(3, 7, 394, 30)', BoundsStr(A), 'AlignWithMargins change');
  A.Margins.Left := 0;
  CheckEqS('(0, 7, 397, 30)', BoundsStr(A), 'Margins change');
  P.Free;
end;

begin
  Examples;
  Done;
end.
