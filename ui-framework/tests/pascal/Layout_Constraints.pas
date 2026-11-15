// covers: UIF-240, UIF-241, UIF-242
// opc: -U ..\..\src
program Layout_Constraints;
{ U-016, layout.md section 7: ConstrainSize inside SetBounds (maximum, minimum, minimum wins over a contradictory
  maximum, 0 = none), the ConstrainedResize hook (OnConstrainedResize may change the limits), constraint changes
  re-apply SetBounds(Left, Top, Width, Height) as an explicit change, constraints never move a control, the
  intrinsic-size hook runs before the constraints, and constraints also apply while loading. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestControl = class(TControl)
  public
    Log: string;
    Intrinsic: Integer;                                      { > 0: forced height of the intrinsic hook }
    procedure StartLoading;
    procedure EndLoading;
    procedure Resized(Sender: TObject);
    procedure Limits(Sender: TObject; var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer);
    procedure IntrinsicSize(var AWidth, AHeight: Integer); override;
    property OnResize;
    property OnConstrainedResize;
  end;

procedure TTestControl.StartLoading;
begin
  Loading;
end;

procedure TTestControl.EndLoading;
begin
  Loaded;
end;

procedure TTestControl.Resized(Sender: TObject);
begin
  Log := Log + 'resize(' + IntToStr(Width) + 'x' + IntToStr(Height) + ');';
end;

procedure TTestControl.Limits(Sender: TObject; var MinWidth, MinHeight, MaxWidth, MaxHeight: Integer);
begin
  Log := Log + 'limits(' + IntToStr(MinWidth) + ',' + IntToStr(MinHeight) + ',' + IntToStr(MaxWidth) + ','
    + IntToStr(MaxHeight) + ');';
  MaxWidth := 60;
end;

procedure TTestControl.IntrinsicSize(var AWidth, AHeight: Integer);
begin
  if Intrinsic > 0 then
    AHeight := Intrinsic;
end;

function BoundsStr(C: TControl): string;
begin
  Result := '(' + IntToStr(C.Left) + ', ' + IntToStr(C.Top) + ', ' + IntToStr(C.Width) + ', ' + IntToStr(C.Height) + ')';
end;

procedure Run;
var
  C: TTestControl;
  W, H: Integer;
begin
  C := TTestControl.Create(nil);
  try
    Section('ConstrainSize (UIF-240)');
    C.SetBounds(10, 20, 100, 50);
    C.Constraints.MaxWidth := 80;
    CheckEqS('(10, 20, 80, 50)', BoundsStr(C), 'constraint change re-applies the bounds (UIF-241)');
    C.SetBounds(10, 20, 200, 50);
    CheckEqS('(10, 20, 80, 50)', BoundsStr(C), 'maximum width');
    C.Constraints.MinHeight := 30;
    C.SetBounds(10, 20, 5, 5);
    CheckEqS('(10, 20, 5, 30)', BoundsStr(C), 'minimum height');
    C.Constraints.MinWidth := 90;
    CheckEqS('(10, 20, 90, 30)', BoundsStr(C), 'minimum wins over a contradictory maximum');
    C.SetBounds(10, 20, 500, 500);
    CheckEqS('(10, 20, 90, 500)', BoundsStr(C), 'no maximum height = none');
    W := 1000;
    H := 1;
    C.ConstrainSize(W, H);
    CheckEqS('90x30', IntToStr(W) + 'x' + IntToStr(H), 'ConstrainSize as a function');
    C.Constraints.MinWidth := 0;
    C.Constraints.MaxWidth := 0;
    C.Constraints.MinHeight := 0;
    C.SetBounds(10, 20, 1000, 1);
    CheckEqS('(10, 20, 1000, 1)', BoundsStr(C), '0 means none');

    Section('re-application is an explicit change (UIF-241, UIF-242)');
    C.OnResize := C.Resized;
    C.Log := '';
    C.Constraints.MaxHeight := 0;
    CheckEqS('', C.Log, 'no change, no event');
    C.Constraints.MinHeight := 40;
    CheckEqS('resize(1000x40);', C.Log, 'OnResize fires');
    CheckEqI(10, C.Left, 'Left unchanged');
    CheckEqI(20, C.Top, 'Top unchanged');
    C.Constraints.MinHeight := 0;
    CheckEqS('(10, 20, 1000, 40)', BoundsStr(C), 'relaxing a constraint does not restore the old size');

    Section('ConstrainedResize hook');
    C.Log := '';
    C.OnResize := nil;
    C.Constraints.MinHeight := 7;
    C.OnConstrainedResize := C.Limits;
    C.SetBounds(10, 20, 300, 40);
    CheckEqS('limits(0,7,0,0);', C.Log, 'handler receives the property values');
    CheckEqS('(10, 20, 60, 40)', BoundsStr(C), 'limits changed by the handler apply');
    CheckEqI(0, C.Constraints.MaxWidth, 'property not changed by the handler');
    C.OnConstrainedResize := nil;
    C.Constraints.MinHeight := 0;

    Section('intrinsic rules run first (UIF-240 step 1)');
    C.Intrinsic := 23;
    C.Constraints.MaxHeight := 20;
    C.SetBounds(0, 0, 50, 100);
    CheckEqS('(0, 0, 50, 20)', BoundsStr(C), 'constraint applied after the intrinsic height');
    C.Constraints.MaxHeight := 0;
    C.SetBounds(0, 0, 50, 100);
    CheckEqS('(0, 0, 50, 23)', BoundsStr(C), 'intrinsic height');
    C.Intrinsic := 0;

    Section('loading');
    C.StartLoading;
    C.Constraints.MaxWidth := 40;
    C.SetBounds(1, 2, 400, 30);
    CheckEqS('(1, 2, 40, 30)', BoundsStr(C), 'constraints apply while loading');
    C.EndLoading;
  finally
    C.Free;
  end;
end;

begin
  Run;
  Done;
end.
