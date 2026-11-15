// covers: UIF-226, UIF-227
program Layout_Idempotent;
{ U-020, layout.md UIF-226: running a second layout pass immediately after a pass changes nothing. Applied to
  trees built in code from the worked examples (all five phases, margins, padding, constraints, space running
  out, anchors after a parent resize, nested containers). The shared corpus tests/layout-corpus/ is not present
  yet; LayoutCases_Runtime (U-051) runs the corpus forms. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TBox = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    property Padding;
  end;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

function Snapshot(P: TWinControl): string;
var
  I: Integer;
  C: TControl;
begin
  Result := '[' + IntToStr(P.Left) + ',' + IntToStr(P.Top) + ',' + IntToStr(P.Width) + ',' + IntToStr(P.Height);
  for I := 0 to P.ControlCount - 1 do
  begin
    C := P.Controls[I];
    if C is TWinControl then
      Result := Result + Snapshot(TWinControl(C))
    else
      Result := Result + '(' + IntToStr(C.Left) + ',' + IntToStr(C.Top) + ',' + IntToStr(C.Width) + ','
        + IntToStr(C.Height) + ')';
  end;
  Result := Result + ']';
end;

procedure RealignAll(P: TWinControl);
var
  I: Integer;
begin
  P.Realign;
  for I := 0 to P.ControlCount - 1 do
    if P.Controls[I] is TWinControl then
      RealignAll(TWinControl(P.Controls[I]));
end;

function Add(P: TWinControl; AAlign: TAlign; L, T, W, H: Integer): TControl;
begin
  Result := TControl.Create(P);
  Result.SetBounds(L, T, W, H);
  Result.Align := AAlign;
  Result.Parent := P;
end;

function AddBox(P: TWinControl; AAlign: TAlign; L, T, W, H: Integer): TBox;
begin
  Result := TBox.Create(P);
  Result.SetBounds(L, T, W, H);
  Result.Align := AAlign;
  Result.Parent := P;
end;

procedure CheckTree(Root: TBox; const Name: string);
var
  First: string;
begin
  Root.EnableAlign;
  First := Snapshot(Root);
  RealignAll(Root);
  CheckEqS(First, Snapshot(Root), Name);
  Root.Free;
end;

function NewRoot(W, H: Integer): TBox;
begin
  Result := TBox.Create(nil);
  Result.SetBounds(0, 0, W, H);
  Result.DisableAlign;
end;

procedure Run;
var
  R, Panel: TBox;
  C: TControl;
begin
  Section('second pass changes nothing (UIF-226)');
  R := NewRoot(400, 300);
  Add(R, alClient, 0, 0, 10, 10);
  Add(R, alRight, 0, 0, 60, 10);
  Add(R, alLeft, 0, 0, 50, 10);
  Add(R, alBottom, 0, 0, 10, 20);
  Add(R, alTop, 0, 0, 10, 30);
  CheckTree(R, 'E06 five phases');

  R := NewRoot(400, 300);
  C := Add(R, alLeft, 0, 0, 100, 10);
  C.AlignWithMargins := True;
  C.Margins.SetBounds(10, 5, 20, 15);
  C := Add(R, alClient, 0, 0, 10, 10);
  C.AlignWithMargins := True;
  CheckTree(R, 'E08 margins');

  R := NewRoot(400, 300);
  R.Padding.SetBounds(8, 8, 8, 8);
  Add(R, alTop, 0, 0, 10, 24);
  Add(R, alNone, 5, 5, 50, 20);
  CheckTree(R, 'E10 padding');

  R := NewRoot(400, 300);
  C := Add(R, alTop, 0, 0, 100, 10);
  C.Constraints.MinHeight := 25;
  Add(R, alTop, 0, 50, 100, 20);
  C := Add(R, alRight, 0, 0, 80, 10);
  C.Constraints.MaxHeight := 100;
  C := Add(R, alBottom, 0, 0, 10, 40);
  C.Constraints.MaxWidth := 250;
  CheckTree(R, 'E13 E14 E15 constraints');

  R := NewRoot(100, 40);
  Add(R, alTop, 0, 0, 100, 30);
  Add(R, alTop, 0, 30, 100, 30);
  Add(R, alBottom, 0, 0, 100, 20);
  Add(R, alLeft, 0, 0, 10, 10);
  Add(R, alClient, 0, 0, 10, 10);
  CheckTree(R, 'E35 space runs out, inverted rectangle');

  R := NewRoot(400, 300);
  C := Add(R, alNone, 300, 260, 75, 25);
  C.Anchors := [akRight, akBottom];
  C := Add(R, alNone, 150, 100, 100, 50);
  C.Anchors := [];
  C := Add(R, alNone, 8, 48, 384, 200);
  C.Anchors := [akLeft, akTop, akRight, akBottom];
  R.EnableAlign;
  R.SetBounds(0, 0, 517, 233);
  R.DisableAlign;
  CheckTree(R, 'anchors after a resize');

  R := NewRoot(640, 480);
  Panel := AddBox(R, alClient, 0, 0, 400, 300);
  Panel.Padding.SetBounds(4, 4, 4, 4);
  C := Add(Panel, alNone, 300, 260, 75, 25);
  C.Anchors := [akRight, akBottom];
  Add(Panel, alBottom, 0, 0, 10, 19);
  AddBox(Panel, alLeft, 0, 0, 150, 10);
  Add(R, alTop, 0, 0, 10, 29);
  CheckTree(R, 'E24 E28 nested containers');
end;

begin
  Run;
  Done;
end.
