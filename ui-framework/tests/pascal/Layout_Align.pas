// covers: UIF-221, UIF-222, UIF-223, UIF-224, UIF-225
program Layout_Align;
{ U-017, layout.md section 4: the five fixed phases, the sort key of each phase (evaluated on the bounds at the
  start of the pass, ties in Controls[] order), every row of the Place table, constrained sizes anchored at the
  phase's fixed edge, invisible aligned children consume no space, alNone children untouched, sizes clamp at 0
  when space runs out and an inverted rectangle is used as written, Anchors of aligned controls never modified.
  Worked examples E01-E06, E11-E15, E35, E40. Children are added between DisableAlign and EnableAlign so that one
  pass sees the design bounds (UIF-225). }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TBox = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
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

function Add(P: TWinControl; AAlign: TAlign; L, T, W, H: Integer): TControl;
begin
  Result := TControl.Create(P);
  Result.SetBounds(L, T, W, H);
  Result.Align := AAlign;
  Result.Parent := P;
end;

procedure Examples;
var
  P: TBox;
  A, B, C, T, L, R, X: TControl;
begin
  Section('E01');
  P := NewParent(400, 300);
  A := Add(P, alTop, 10, 20, 100, 30);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(A), 'A');
  P.Free;

  Section('E02 alTop key = Top');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 50, 100, 30);
  B := Add(P, alTop, 0, 10, 100, 20);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 20)', BoundsStr(B), 'B');
  CheckEqS('(0, 20, 400, 30)', BoundsStr(A), 'A');
  P.Free;

  Section('E03 tie keeps creation order');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 0, 50, 30);
  B := Add(P, alTop, 0, 0, 50, 40);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(A), 'A');
  CheckEqS('(0, 30, 400, 40)', BoundsStr(B), 'B');
  P.Free;

  Section('E04 alBottom key = largest bottom edge first');
  P := NewParent(400, 300);
  A := Add(P, alBottom, 0, 100, 10, 20);
  B := Add(P, alBottom, 0, 250, 10, 30);
  P.EnableAlign;
  CheckEqS('(0, 270, 400, 30)', BoundsStr(B), 'B');
  CheckEqS('(0, 250, 400, 20)', BoundsStr(A), 'A');
  P.Free;

  Section('E05 all five phases');
  P := NewParent(400, 300);
  T := Add(P, alTop, 0, 0, 10, 30);
  B := Add(P, alBottom, 0, 0, 10, 20);
  L := Add(P, alLeft, 0, 0, 50, 10);
  R := Add(P, alRight, 0, 0, 60, 10);
  C := Add(P, alClient, 0, 0, 10, 10);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(T), 'T');
  CheckEqS('(0, 280, 400, 20)', BoundsStr(B), 'B');
  CheckEqS('(0, 30, 50, 250)', BoundsStr(L), 'L');
  CheckEqS('(340, 30, 60, 250)', BoundsStr(R), 'R');
  CheckEqS('(50, 30, 290, 250)', BoundsStr(C), 'C');
  P.Free;

  Section('E06 phase order independent of creation order');
  P := NewParent(400, 300);
  C := Add(P, alClient, 0, 0, 10, 10);
  R := Add(P, alRight, 0, 0, 60, 10);
  L := Add(P, alLeft, 0, 0, 50, 10);
  B := Add(P, alBottom, 0, 0, 10, 20);
  T := Add(P, alTop, 0, 0, 10, 30);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(T), 'T');
  CheckEqS('(0, 280, 400, 20)', BoundsStr(B), 'B');
  CheckEqS('(0, 30, 50, 250)', BoundsStr(L), 'L');
  CheckEqS('(340, 30, 60, 250)', BoundsStr(R), 'R');
  CheckEqS('(50, 30, 290, 250)', BoundsStr(C), 'C');
  P.Free;

  Section('E11 invisible aligned child consumes no space');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 0, 100, 30);
  A.Visible := False;
  B := Add(P, alTop, 0, 40, 100, 20);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 20)', BoundsStr(B), 'T2');
  CheckEqS('(0, 0, 100, 30)', BoundsStr(A), 'T1 keeps its bounds');
  P.Free;

  Section('E12 alClient with maximum constraints');
  P := NewParent(400, 300);
  C := Add(P, alClient, 0, 0, 10, 10);
  C.Constraints.MaxWidth := 300;
  C.Constraints.MaxHeight := 200;
  P.EnableAlign;
  CheckEqS('(0, 0, 300, 200)', BoundsStr(C), 'C anchored top-left');
  P.Free;

  Section('E13 alTop with minimum height');
  P := NewParent(400, 300);
  A := Add(P, alTop, 0, 0, 100, 10);
  A.Constraints.MinHeight := 25;
  B := Add(P, alTop, 0, 50, 100, 20);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 25)', BoundsStr(A), 'A');
  CheckEqS('(0, 25, 400, 20)', BoundsStr(B), 'B');
  P.Free;

  Section('E14 alRight with maximum height');
  P := NewParent(400, 300);
  R := Add(P, alRight, 0, 0, 80, 10);
  R.Constraints.MaxHeight := 100;
  C := Add(P, alClient, 0, 0, 10, 10);
  P.EnableAlign;
  CheckEqS('(320, 0, 80, 100)', BoundsStr(R), 'R anchored top-right');
  CheckEqS('(0, 0, 320, 300)', BoundsStr(C), 'C');
  P.Free;

  Section('E15 alBottom with maximum width');
  P := NewParent(400, 300);
  B := Add(P, alBottom, 0, 0, 10, 40);
  B.Constraints.MaxWidth := 250;
  P.EnableAlign;
  CheckEqS('(0, 260, 250, 40)', BoundsStr(B), 'B anchored bottom-left');
  P.Free;

  Section('E35 space runs out');
  P := NewParent(100, 40);
  A := Add(P, alTop, 0, 0, 100, 30);
  B := Add(P, alTop, 0, 30, 100, 30);
  C := Add(P, alClient, 0, 0, 10, 10);
  P.EnableAlign;
  CheckEqS('(0, 0, 100, 30)', BoundsStr(A), 'A');
  CheckEqS('(0, 30, 100, 30)', BoundsStr(B), 'B outside the client area');
  CheckEqS('(0, 60, 100, 0)', BoundsStr(C), 'C clamps at 0');
  P.Free;

  Section('inverted rectangle used as written (UIF-223)');
  P := NewParent(100, 40);
  A := Add(P, alTop, 0, 0, 100, 30);
  B := Add(P, alTop, 0, 30, 100, 30);
  C := Add(P, alBottom, 0, 0, 100, 20);
  L := Add(P, alLeft, 0, 0, 10, 10);
  R := Add(P, alRight, 0, 0, 15, 10);
  X := Add(P, alClient, 0, 0, 10, 10);
  P.EnableAlign;
  CheckEqS('(0, 30, 100, 30)', BoundsStr(B), 'second alTop ends below the client area (R.Top 60)');
  CheckEqI(0, A.Top, 'first alTop');
  CheckEqS('(0, 20, 100, 20)', BoundsStr(C), 'alBottom against R.Bottom 40');
  CheckEqS('(0, 60, 10, 0)', BoundsStr(L), 'alLeft at R.Top 60, height 0');
  CheckEqS('(85, 60, 15, 0)', BoundsStr(R), 'alRight');
  CheckEqS('(10, 60, 75, 0)', BoundsStr(X), 'alClient');
  P.Free;

  Section('alLeft and alRight keys');
  P := NewParent(400, 300);
  A := Add(P, alLeft, 50, 0, 30, 10);
  B := Add(P, alLeft, 10, 0, 20, 10);
  C := Add(P, alRight, 300, 0, 50, 10);
  R := Add(P, alRight, 200, 0, 80, 10);
  P.EnableAlign;
  CheckEqS('(0, 0, 20, 300)', BoundsStr(B), 'smaller Left first');
  CheckEqS('(20, 0, 30, 300)', BoundsStr(A), 'then A');
  CheckEqS('(350, 0, 50, 300)', BoundsStr(C), 'largest right edge first');
  CheckEqS('(270, 0, 80, 300)', BoundsStr(R), 'then R');
  P.Free;

  Section('several alClient overlap; alNone untouched; Anchors kept (UIF-224)');
  P := NewParent(400, 300);
  A := Add(P, alClient, 0, 0, 10, 10);
  A.Anchors := [akRight];
  B := Add(P, alClient, 5, 5, 10, 10);
  X := Add(P, alNone, 7, 8, 9, 10);
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 300)', BoundsStr(A), 'A');
  CheckEqS('(0, 0, 400, 300)', BoundsStr(B), 'B overlaps A');
  CheckEqS('(7, 8, 9, 10)', BoundsStr(X), 'alNone child');
  Check(A.Anchors = [akRight], 'Anchors of an aligned control unchanged');
  A.Align := alNone;
  Check(A.Anchors = [akRight], 'changing Align does not touch Anchors');
  P.Free;

  Section('E40 explicit change re-sorts');
  P := NewParent(400, 300);
  A := Add(P, alLeft, 0, 0, 100, 300);
  B := Add(P, alLeft, 100, 0, 50, 300);
  P.EnableAlign;
  CheckEqS('(0, 0, 100, 300)', BoundsStr(A), 'A before');
  CheckEqS('(100, 0, 50, 300)', BoundsStr(B), 'B before');
  B.Left := -1;
  CheckEqS('(0, 0, 50, 300)', BoundsStr(B), 'B after');
  CheckEqS('(50, 0, 100, 300)', BoundsStr(A), 'A after');
  P.Free;

  Section('DisableAlign defers (UIF-225)');
  P := NewParent(400, 300);
  P.EnableAlign;
  CheckEqB(False, P.AlignDisabled, 'enabled');
  P.DisableAlign;
  P.DisableAlign;
  CheckEqB(True, P.AlignDisabled, 'disabled');
  A := Add(P, alTop, 0, 0, 10, 30);
  CheckEqS('(0, 0, 10, 30)', BoundsStr(A), 'no pass while disabled');
  P.EnableAlign;
  CheckEqS('(0, 0, 10, 30)', BoundsStr(A), 'still disabled at level 1');
  P.EnableAlign;
  CheckEqS('(0, 0, 400, 30)', BoundsStr(A), 'pending pass runs at level 0');
  P.Free;
end;

begin
  Examples;
  Done;
end.
