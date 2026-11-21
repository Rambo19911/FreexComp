// covers: UIF-080, UIF-560, UIF-571, UIF-573
// needs: desktop
program Core_DoubleBuffer;
{ U-095, graphics.md 7 UIF-560: an own window class with DoubleBuffered = True paints WM_PAINT into a memory bitmap
  of the update rectangle's size, whose origin is offset to the update rectangle: the erase logic, PaintWindow and
  the graphic children all draw into the memory DC, which is then copied to the window. Without the property the
  window DC is used. Native (subclassed) classes ignore it. A WM_PAINT that brings its own DC is painted directly.
  The DC each step receives is recorded (GetObjectType: OBJ_MEMDC or OBJ_DC); the blit is checked by reading the
  window back with GetPixel. Repeated buffered paints leak no GDI object. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
  end;

  { an own class that is not opaque: the erase logic fills it with Color }
  TPaintBox = class(TCustomControl)
  protected
    procedure Paint; override;
    procedure WMEraseBkgnd(var Message: TWMEraseBkgnd); override;
  public
    MarkColor: TColor;
    property DoubleBuffered;
  end;

  TDot = class(TGraphicControl)
  protected
    procedure Paint; override;
  end;

  TWinAccess = class(TWinControl);

var
  Log: TLog;

function DCKind(DC: HDC): string;
var
  Org: TPoint;
begin
  case GetObjectType(DC) of
    OBJ_MEMDC:
      Result := 'mem';
    OBJ_DC:
      Result := 'dc';
  else
    Result := '?';
  end;
  GetViewportOrgEx(DC, Org);
  Result := Result + Format('@%d,%d', [Org.X, Org.Y]);
end;

function BitmapSize(DC: HDC): string;
var
  B: TBitmapRec;
begin
  Result := '-';
  if GetObjectType(DC) <> OBJ_MEMDC then
    Exit;
  FillChar(B, SizeOf(B), 0);
  GetObjectW(GetCurrentObject(DC, OBJ_BITMAP), SizeOf(B), @B);
  Result := Format('%dx%d', [B.bmWidth, B.bmHeight]);
end;

procedure TPaintBox.Paint;
begin
  Log.Text := Log.Text + 'paint ' + DCKind(Canvas.Handle) + ' ' + BitmapSize(Canvas.Handle) + ';';
  Canvas.Brush.Color := MarkColor;
  Canvas.FillRect(Rect(10, 10, 30, 30));
end;

{ logs the erases into a memory DC and whether the erase logic drew (a marker pixel was overwritten) }
procedure TPaintBox.WMEraseBkgnd(var Message: TWMEraseBkgnd);
var
  Mem: Boolean;
begin
  Mem := GetObjectType(Message.DC) = OBJ_MEMDC;
  if Mem then
    SetPixel(Message.DC, 100, 50, $123456);
  inherited WMEraseBkgnd(Message);
  if Mem then
  begin
    Log.Text := Log.Text + 'erase ' + DCKind(Message.DC);
    if GetPixel(Message.DC, 100, 50) <> $123456 then
      Log.Text := Log.Text + ' filled';
    Log.Text := Log.Text + ';';
  end;
end;

procedure TDot.Paint;
begin
  Log.Text := Log.Text + 'dot ' + DCKind(Canvas.Handle) + ';';
  Canvas.Brush.Color := clLime;
  Canvas.FillRect(Rect(0, 0, Width, Height));
end;

{ paints the given client rectangle of C now (it is the only invalid part) }
procedure Repaint(C: TWinControl; const R: TRect);
begin
  ValidateRect(C.Handle, nil);
  InvalidateRect(C.Handle, @R, True);
  Log.Text := '';
  UpdateWindow(C.Handle);
end;

function PixelAt(C: TWinControl; X, Y: Integer): TColor;
var
  DC: HDC;
begin
  DC := GetDC(C.Handle);
  try
    Result := TColor(GetPixel(DC, X, Y));
  finally
    ReleaseDC(C.Handle, DC);
  end;
end;

function GuiObjects: Cardinal;
begin
  Result := GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS) + GetGuiResources(GetCurrentProcess, GR_USEROBJECTS);
end;

var
  F: TForm;
  P: TPaintBox;
  D: TDot;
  G: TButton;
  D2: TDot;
  I: Integer;
  Before: Cardinal;
  DC: HDC;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.Scaled := False;
    F.Position := poDesigned;
    F.SetBounds(100, 100, 400, 300);
    P := TPaintBox.Create(F);
    P.SetBounds(0, 0, 200, 100);
    P.Color := clRed;
    P.MarkColor := clBlue;
    P.Parent := F;
    D := TDot.Create(F);
    D.SetBounds(40, 20, 10, 10);
    D.Parent := P;
    G := TButton.Create(F);
    G.SetBounds(0, 120, 200, 100);
    G.Parent := F;
    D2 := TDot.Create(F);
    D2.SetBounds(20, 30, 10, 10);
    D2.Parent := G;
    F.Show;
    UpdateWindow(F.Handle);

    Section('defaults (UIF-560)');
    CheckEqB(False, P.DoubleBuffered, 'DoubleBuffered False by default');

    Section('not buffered: the window DC');
    Repaint(P, Rect(0, 0, 200, 100));
    CheckEqS('paint dc@0,0 -;dot dc@40,20;', Log.Text, 'Paint and the graphic child get the window DC');

    Section('buffered (UIF-560)');
    P.DoubleBuffered := True;
    Repaint(P, Rect(0, 0, 200, 100));
    CheckEqS('erase mem@0,0 filled;paint mem@0,0 200x100;dot mem@40,20;', Log.Text,
      'erase logic, Paint and the graphic child draw into one memory bitmap of the update size');
    CheckEqI(clRed, PixelAt(P, 5, 5), 'the erase fill reached the window');
    CheckEqI(clBlue, PixelAt(P, 15, 15), 'Paint reached the window');
    CheckEqI(clLime, PixelAt(P, 45, 25), 'the graphic child reached the window');
    Repaint(P, Rect(70, 30, 120, 60));
    CheckEqS('erase mem@-70,-30 filled;paint mem@-70,-30 50x30;dot mem@-30,-10;', Log.Text,
      'a partial update: bitmap of the update rectangle, origin offset to it');
    CheckEqI(clRed, PixelAt(P, 100, 50), 'drawn at the right place');

    Section('opaque control: no erase into the buffer (UIF-080)');
    P.ControlStyle := P.ControlStyle + [csOpaque];
    Repaint(P, Rect(0, 0, 200, 100));
    CheckEqS('erase mem@0,0;paint mem@0,0 200x100;dot mem@40,20;', Log.Text,
      'csOpaque with DoubleBuffered: the erase logic draws nothing');
    P.ControlStyle := P.ControlStyle - [csOpaque];

    Section('a WM_PAINT with its own DC is painted directly');
    DC := GetDC(P.Handle);
    try
      Log.Text := '';
      P.Perform(WM_PAINT, WPARAM(DC), 0);
      CheckEqS('paint dc@0,0 -;dot dc@40,20;', Log.Text, 'the given DC, no buffer');
    finally
      ReleaseDC(P.Handle, DC);
    end;

    Section('native classes ignore the property (UIF-560)');
    TWinAccess(G).DoubleBuffered := True;
    Repaint(G, Rect(0, 0, 200, 100));
    CheckEqS('dot dc@20,30;', Log.Text, 'a subclassed system class paints its graphic child on the window DC');

    Section('leak (UIF-571, UIF-573)');
    Repaint(P, Rect(0, 0, 200, 100));
    Before := GuiObjects;
    for I := 1 to 200 do
      Repaint(P, Rect(0, 0, 200, 100));
    Check(Abs(Integer(GuiObjects) - Integer(Before)) <= 2, 'GUI objects after 200 buffered paints');
  finally
    F.Free;
    Log.Free;
  end;
  Done;
end.
