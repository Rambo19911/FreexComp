// covers: UIF-210, UIF-225, UIF-226, UIF-227
program Layout_Triggers;
{ U-020, layout.md sections 4.3-4.5: each trigger of the UIF-227 table runs exactly one layout pass of the parent
  (counted by overriding AlignControls), an unchanged value runs none, a pass requested while loading,
  destroying or align-disabled only sets the flag (one pass at EnableAlign / end of Loaded), a request made
  during the running pass of the same parent is ignored, a child's own explicit change inside a pass does not
  start a second pass, and layout is top-down (E24: a resized child container runs its own pass). }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TBox = class(TWinControl)
  public
    Passes: Integer;
    constructor Create(AOwner: TComponent); override;
    procedure AlignControls(AControl: TControl; var Rect: TRect); override;
    procedure StartLoading;
    procedure EndLoading;
    property Padding;
  end;

  TMover = class(TControl)
  public
    Other: TControl;
    procedure Resize; override;
  end;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TBox.AlignControls(AControl: TControl; var Rect: TRect);
begin
  Inc(Passes);
  inherited AlignControls(AControl, Rect);
end;

procedure TBox.StartLoading;
begin
  Loading;
end;

procedure TBox.EndLoading;
begin
  Loaded;
end;

procedure TMover.Resize;
begin
  inherited Resize;
  if Other <> nil then
    Other.Left := Other.Left + 1;                            { an explicit change requested inside the pass }
end;

function BoundsStr(C: TControl): string;
begin
  Result := '(' + IntToStr(C.Left) + ', ' + IntToStr(C.Top) + ', ' + IntToStr(C.Width) + ', ' + IntToStr(C.Height) + ')';
end;

procedure Expect1(P: TBox; const Name: string);
begin
  CheckEqI(1, P.Passes, Name);
  P.Passes := 0;
end;

procedure Expect0(P: TBox; const Name: string);
begin
  CheckEqI(0, P.Passes, Name);
  P.Passes := 0;
end;

procedure Run;
var
  P, Inner: TBox;
  A, B: TControl;
  M: TMover;
begin
  P := TBox.Create(nil);
  try
    P.SetBounds(0, 0, 400, 300);
    Section('UIF-227 rows, one pass each');
    P.Passes := 0;
    P.SetBounds(0, 0, 400, 300);
    Expect0(P, 'unchanged size: no pass');
    P.SetBounds(5, 5, 400, 300);
    Expect0(P, 'a move of P: no pass of P');
    P.SetBounds(0, 0, 500, 300);
    Expect1(P, 'client size changed (UIF-210 step 7)');
    A := TControl.Create(P);
    A.Parent := P;
    Expect1(P, 'child inserted');
    B := TControl.Create(P);
    B.Parent := P;
    P.Passes := 0;
    A.Align := alTop;
    Expect1(P, 'child Align changed');
    A.Align := alTop;
    Expect0(P, 'same Align: no pass');
    A.Visible := False;
    Expect1(P, 'child Visible changed');
    A.Visible := True;
    P.Passes := 0;
    A.AlignWithMargins := True;
    Expect1(P, 'child AlignWithMargins changed');
    A.Margins.Top := 7;
    Expect1(P, 'child Margins changed');
    A.Margins.SetBounds(1, 2, 3, 4);
    Expect1(P, 'Margins.SetBounds: one pass');
    B.SetBounds(10, 10, 20, 20);
    Expect1(P, 'child bounds changed explicitly');
    B.SetBounds(10, 10, 20, 20);
    Expect0(P, 'unchanged child bounds: no pass');
    B.BringToFront;
    Expect0(P, 'already at the front: no pass');
    B.SendToBack;
    Expect1(P, 'child z-order changed');
    P.Padding.Left := 3;
    Expect1(P, 'Padding changed');
    P.Padding.SetBounds(1, 1, 1, 1);
    Expect1(P, 'Padding.SetBounds: one pass');
    B.Parent := nil;
    Expect1(P, 'child removed');
    B.Free;

    Section('deferral (UIF-225)');
    P.DisableAlign;
    A.Align := alBottom;
    A.Height := 40;
    P.SetBounds(0, 0, 600, 300);
    P.Padding.Top := 9;
    Expect0(P, 'no pass while disabled');
    P.EnableAlign;
    Expect1(P, 'one pass at EnableAlign');
    P.DisableAlign;
    P.EnableAlign;
    Expect0(P, 'nothing pending: no pass');
    P.StartLoading;
    A.Align := alTop;
    P.SetBounds(0, 0, 610, 300);
    Expect0(P, 'no pass while loading');
    P.EndLoading;
    Expect1(P, 'one pass at the end of Loaded');

    Section('re-entrancy (UIF-226)');
    M := TMover.Create(P);
    M.Align := alClient;
    B := TControl.Create(P);
    B.SetBounds(10, 10, 20, 20);
    P.DisableAlign;
    M.Parent := P;
    B.Parent := P;
    M.Other := B;
    P.EnableAlign;
    Expect1(P, 'explicit change inside the pass is ignored, not a second pass');
    CheckEqI(11, B.Left, 'the change itself happened');
    M.Other := nil;
    P.Realign;
    Expect1(P, 'Realign: one pass');
  finally
    P.Free;
  end;

  Section('top-down (E24)');
  P := TBox.Create(nil);
  try
    P.SetBounds(0, 0, 400, 300);
    Inner := TBox.Create(P);
    Inner.SetBounds(0, 0, 400, 300);
    Inner.Align := alClient;
    Inner.Parent := P;
    A := TControl.Create(P);
    A.SetBounds(300, 260, 75, 25);
    A.Anchors := [akRight, akBottom];
    A.Parent := Inner;
    P.Passes := 0;
    Inner.Passes := 0;
    P.SetBounds(0, 0, 640, 480);
    CheckEqS('(0, 0, 640, 480)', BoundsStr(Inner), 'Panel');
    CheckEqS('(540, 440, 75, 25)', BoundsStr(A), 'Btn');
    Expect1(P, 'one pass of the root');
    Expect1(Inner, 'one pass of the resized child container');
  finally
    P.Free;
  end;
end;

begin
  Run;
  Done;
end.
