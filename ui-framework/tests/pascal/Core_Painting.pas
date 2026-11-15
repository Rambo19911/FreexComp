// covers: UIF-055, UIF-057, UIF-080, UIF-081, UIF-082
// needs: desktop
program Core_Painting;
{ U-028, core.md 6 and 7.8: WM_PRINTCLIENT into a memory bitmap erases with the brush (only when asked),
  then paints the graphic children back to front with the origin at their top-left and the clip at their bounds;
  their Canvas carries the control's Font and Color during Paint only; hidden ones are skipped; a custom control
  paints through its Canvas; Invalidate marks exactly the control's rectangle (never creating a handle); Update
  paints synchronously through WM_PAINT; the control canvas of a custom control gets a window DC on demand and
  gives it back when the handle goes. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, TestKit;

const
  PRF_CLIENT = $4;
  PRF_ERASEBKGND = $8;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    property Color;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestGraphic = class(TGraphicControl)
  protected
    procedure Paint; override;
  public
    Paints: Integer;
    BrushAtPaint: TColor;
    FontSizeAtPaint: Integer;
    function CanvasHandle: HDC;
    property Color;
    property Font;
  end;

  TTestCustom = class(TCustomControl)
  protected
    procedure Paint; override;
  public
    function TheCanvas: TCanvas;
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TTestGraphic.Paint;
begin
  Paints := Paints + 1;
  BrushAtPaint := Canvas.Brush.Color;
  FontSizeAtPaint := Canvas.Font.Size;
  Canvas.FillRect(Rect(-10, -10, 100, 100));                 { larger than the control: the clip must cut it }
end;

function TTestGraphic.CanvasHandle: HDC;
begin
  Result := 0;
  if Canvas.HandleAllocated then
    Result := Canvas.Handle;
end;

procedure TTestCustom.Paint;
begin
  Canvas.Brush.Color := clBlue;
  Canvas.FillRect(ClientRect);
end;

function TTestCustom.TheCanvas: TCanvas;
begin
  Result := Canvas;
end;

function NewGraphic(AParent: TWinControl; L, T, W, H: Integer; AColor: TColor): TTestGraphic;
begin
  Result := TTestGraphic.Create(AParent);
  Result.SetBounds(L, T, W, H);
  Result.Color := AColor;
  Result.Parent := AParent;
end;

function UpdateRectOf(H: HWND): string;
var
  R: TRect;
begin
  if GetUpdateRect(H, @R, False) then
    Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom])
  else
    Result := 'empty';
end;

procedure Run;
var
  Root: TTopWin;
  G, Hidden, Above: TTestGraphic;
  CC: TTestCustom;
  Bmp: TBitmap;
  DC: HDC;
begin
  Bmp := TBitmap.Create;
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 120, 80);
    Root.Color := clWhite;
    G := NewGraphic(Root, 10, 10, 20, 20, clRed);
    G.Font.Size := 13;
    Hidden := NewGraphic(Root, 40, 40, 10, 10, clLime);
    Hidden.Visible := False;
    Above := NewGraphic(Root, 20, 20, 20, 20, clBlue);
    CC := TTestCustom.Create(Root);
    CC.SetBounds(60, 10, 30, 30);
    CC.Parent := Root;

    Section('nothing without a parent handle (UIF-057)');
    G.Invalidate;
    CheckEqB(False, Root.HandleAllocated, 'Invalidate creates no handle');

    Section('WM_PRINTCLIENT with erase (UIF-080, UIF-081)');
    Bmp.SetSize(120, 80);
    DC := Bmp.Canvas.Handle;
    PatBlt(DC, 0, 0, 120, 80, BLACKNESS);
    SendMessageW(Root.Handle, WM_PRINTCLIENT, WPARAM(DC), PRF_CLIENT or PRF_ERASEBKGND);
    CheckEqB(True, Bmp.Canvas.Pixels[5, 5] = clWhite, 'background erased with the brush');
    CheckEqB(True, Bmp.Canvas.Pixels[15, 15] = clRed, 'graphic child painted at its position');
    CheckEqB(True, Bmp.Canvas.Pixels[9, 15] = clWhite, 'clipped on the left');
    CheckEqB(True, Bmp.Canvas.Pixels[15, 9] = clWhite, 'clipped at the top');
    CheckEqB(True, Bmp.Canvas.Pixels[25, 15] = clRed, 'right edge inside');
    CheckEqB(True, Bmp.Canvas.Pixels[35, 15] = clWhite, 'clipped on the right');
    CheckEqB(True, Bmp.Canvas.Pixels[25, 25] = clBlue, 'later sibling painted above');
    CheckEqB(True, Bmp.Canvas.Pixels[45, 45] = clWhite, 'hidden child not painted');
    CheckEqI(0, Hidden.Paints, 'hidden child Paint not called');
    CheckEqB(True, Bmp.Canvas.Pixels[70, 20] = clWhite, 'windowed child is not painted by the parent (UIF-082)');

    Section('Canvas during Paint (UIF-055)');
    CheckEqB(True, G.BrushAtPaint = clRed, 'brush colour = Color');
    CheckEqI(13, G.FontSizeAtPaint, 'font = Font');
    CheckEqB(True, G.CanvasHandle = 0, 'no DC outside Paint');

    Section('WM_PRINTCLIENT without erase');
    PatBlt(DC, 0, 0, 120, 80, BLACKNESS);
    SendMessageW(Root.Handle, WM_PRINTCLIENT, WPARAM(DC), PRF_CLIENT);
    CheckEqB(True, Bmp.Canvas.Pixels[5, 5] = clBlack, 'background left alone');
    CheckEqB(True, Bmp.Canvas.Pixels[15, 15] = clRed, 'children painted');

    Section('custom control (core.md 6)');
    PatBlt(DC, 0, 0, 120, 80, BLACKNESS);
    SendMessageW(CC.Handle, WM_PRINTCLIENT, WPARAM(DC), PRF_CLIENT);
    CheckEqB(True, Bmp.Canvas.Pixels[29, 29] = clBlue, 'Paint drew through Canvas');
    CheckEqB(True, Bmp.Canvas.Pixels[31, 31] = clBlack, 'nothing outside its client area');
    CheckEqB(False, CC.TheCanvas.HandleAllocated, 'paint DC detached afterwards');
    Check(CC.TheCanvas.Handle <> 0, 'window DC obtained on demand');
    CC.DestroyHandle;
    CheckEqB(False, CC.TheCanvas.HandleAllocated, 'window DC given back with the handle');

    Section('Invalidate and Update');
    ShowWindow(Root.Handle, SW_SHOWNOACTIVATE);
    UpdateWindow(Root.Handle);
    ValidateRect(Root.Handle, nil);
    CheckEqS('empty', UpdateRectOf(Root.Handle), 'nothing pending');
    G.Invalidate;
    CheckEqS('10,10,30,30', UpdateRectOf(Root.Handle), 'exactly the graphic control''s rectangle');
    G.Paints := 0;
    G.Update;
    CheckEqI(1, G.Paints, 'Update painted through WM_PAINT');
    CheckEqS('empty', UpdateRectOf(Root.Handle), 'update region validated');
    Root.Invalidate;
    CheckEqS('0,0,120,80', UpdateRectOf(Root.Handle), 'windowed control: whole client area');
    Root.Update;
    CheckEqS('empty', UpdateRectOf(Root.Handle), 'painted');
  finally
    Root.Free;
    Bmp.Free;
  end;
end;

begin
  Run;
  Done;
end.
