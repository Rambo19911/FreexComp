// covers: UIF-530, UIF-531, UIF-572
// opc: -U ..\..\src
program Gfx_CanvasShapes;
{ U-006, graphics.md section 3: a TCanvas attached to a memory DC holding a 32-bit DIB section. Pixel
  assertions: rectangle border and interior with the right/bottom edge excluded, line end point excluded,
  FillRect, FrameRect, ellipse/polygon interiors, Pixels read/write, ClipRect and reads outside the clip (-1).
  Object state tracking: objects are selected on demand, an OnChange deselects (the DC's original object is
  selected back), detaching the DC restores the objects saved at attach time; Pen.Mode reaches SetROP2 and the
  brush style decides the background mode; OnChanging/OnChange around drawing calls; a plain canvas without
  a DC raises EInvalidOperation. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TCounter = class
    Log: string;
    procedure Changing(Sender: TObject);
    procedure Changed(Sender: TObject);
  end;

const
  Size = 20;

var
  DC: HDC;
  Bmp, OldBmp: HBITMAP;
  OrigPen, OrigBrush, OrigFont: HGDIOBJ;
  C: TCanvas;

procedure TCounter.Changing(Sender: TObject);
begin
  Log := Log + '<';
end;

procedure TCounter.Changed(Sender: TObject);
begin
  Log := Log + '>';
end;

procedure MakeSurface;
var
  BI: TBitmapInfo;
  Bits: Pointer;
begin
  FillChar(BI, SizeOf(BI), 0);
  BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  BI.bmiHeader.biWidth := Size;
  BI.bmiHeader.biHeight := -Size;
  BI.bmiHeader.biPlanes := 1;
  BI.bmiHeader.biBitCount := 32;
  BI.bmiHeader.biCompression := BI_RGB;
  DC := CreateCompatibleDC(0);
  Bmp := CreateDIBSection(DC, @BI, DIB_RGB_COLORS, Bits, 0, 0);
  OldBmp := SelectObject(DC, Bmp);
  OrigPen := GetCurrentObject(DC, OBJ_PEN);
  OrigBrush := GetCurrentObject(DC, OBJ_BRUSH);
  OrigFont := GetCurrentObject(DC, OBJ_FONT);
end;

procedure FreeSurface;
begin
  SelectObject(DC, OldBmp);
  DeleteObject(Bmp);
  DeleteDC(DC);
end;

{ paints the surface white and keeps the brush the caller had set }
procedure Clear;
var
  Color: TColor;
  Style: TBrushStyle;
begin
  Color := C.Brush.Color;
  Style := C.Brush.Style;
  C.Brush.Style := bsSolid;
  C.Brush.Color := clWhite;
  C.FillRect(Rect(0, 0, Size, Size));
  C.Brush.Color := Color;
  C.Brush.Style := Style;
end;

procedure CheckPixel(X, Y: Integer; Expected: TColor; const Name: string);
begin
  CheckEqI(Expected, GetPixel(DC, X, Y), Name);
end;

procedure CheckNoHandle;
var
  Plain: TCanvas;
  H: HDC;
begin
  Section('plain canvas without DC');
  Plain := TCanvas.Create;
  try
    CheckEqB(False, Plain.HandleAllocated, 'HandleAllocated');
    Expect('EInvalidOperation', 'Canvas does not allow drawing');
    try
      H := Plain.Handle;
      CheckEqI(0, H, 'no DC');
      Check(False, 'reading Handle raises');
    except
      on E: Exception do
        CheckRaises('reading Handle raises', E.ClassName, E.Message);
    end;
    Expect('EInvalidOperation', 'Canvas does not allow drawing');
    try
      Plain.LineTo(1, 1);
      Check(False, 'drawing raises');
    except
      on E: Exception do
        CheckRaises('drawing raises', E.ClassName, E.Message);
    end;
  finally
    Plain.Free;
  end;
end;

procedure CheckShapes;
begin
  Section('FillRect');
  Clear;
  CheckPixel(0, 0, clWhite, 'top-left white');
  CheckPixel(Size - 1, Size - 1, clWhite, 'bottom-right white');
  C.Brush.Color := clBlue;
  C.FillRect(Rect(1, 1, 3, 3));
  CheckPixel(1, 1, clBlue, 'FillRect inside');
  CheckPixel(2, 2, clBlue, 'FillRect last pixel');
  CheckPixel(3, 3, clWhite, 'FillRect right/bottom excluded');

  Section('Rectangle');
  Clear;
  C.Pen.Color := clRed;
  C.Brush.Color := clBlue;
  C.Rectangle(2, 2, 10, 10);
  CheckPixel(2, 2, clRed, 'border top-left');
  CheckPixel(9, 9, clRed, 'border bottom-right inside the rectangle');
  CheckPixel(9, 5, clRed, 'border right');
  CheckPixel(5, 5, clBlue, 'interior');
  CheckPixel(3, 3, clBlue, 'interior next to border');
  CheckPixel(10, 10, clWhite, 'right/bottom edge excluded');
  CheckPixel(1, 1, clWhite, 'outside');
  Clear;
  C.Rectangle(Rect(0, 0, 4, 4));
  CheckPixel(3, 0, clRed, 'TRect overload border');
  CheckPixel(1, 1, clBlue, 'TRect overload interior');
  CheckPixel(4, 4, clWhite, 'TRect overload excluded edge');

  Section('lines');
  Clear;
  C.MoveTo(0, 15);
  CheckEqI(0, C.PenPos.X, 'PenPos after MoveTo X');
  CheckEqI(15, C.PenPos.Y, 'PenPos after MoveTo Y');
  C.LineTo(5, 15);
  CheckPixel(0, 15, clRed, 'line start');
  CheckPixel(4, 15, clRed, 'line last pixel');
  CheckPixel(5, 15, clWhite, 'line end point excluded');
  CheckEqI(5, C.PenPos.X, 'PenPos after LineTo X');
  CheckEqI(15, C.PenPos.Y, 'PenPos after LineTo Y');
  C.PenPos := Point(3, 1);
  C.LineTo(3, 4);
  CheckPixel(3, 1, clRed, 'PenPos assignment moves');
  CheckPixel(3, 4, clWhite, 'vertical end excluded');
  Clear;
  C.Polyline([Point(0, 0), Point(4, 0), Point(4, 4)]);
  CheckPixel(4, 0, clRed, 'Polyline corner');
  CheckPixel(4, 3, clRed, 'Polyline second segment');
  CheckPixel(4, 4, clWhite, 'Polyline end excluded');

  Section('FrameRect and DrawFocusRect');
  Clear;
  C.Brush.Color := clGreen;
  C.FrameRect(Rect(12, 0, 18, 6));
  CheckPixel(12, 0, clGreen, 'frame top-left');
  CheckPixel(17, 5, clGreen, 'frame bottom-right');
  CheckPixel(14, 2, clWhite, 'frame interior untouched');
  CheckPixel(18, 6, clWhite, 'frame excluded edge');
  Clear;
  C.DrawFocusRect(Rect(0, 0, 6, 6));
  Check((GetPixel(DC, 0, 0) <> clWhite) or (GetPixel(DC, 1, 0) <> clWhite), 'DrawFocusRect inverts the dotted frame');
  CheckPixel(3, 3, clWhite, 'DrawFocusRect interior untouched');

  Section('ellipse, round rect, polygon, pie, chord, arc');
  Clear;
  C.Pen.Color := clRed;
  C.Brush.Color := clBlue;
  C.Ellipse(0, 0, 11, 11);
  CheckPixel(5, 5, clBlue, 'ellipse interior');
  CheckPixel(0, 0, clWhite, 'ellipse corner outside');
  CheckPixel(5, 0, clRed, 'ellipse top border');
  Clear;
  C.Ellipse(Rect(0, 0, 11, 11));
  CheckPixel(5, 5, clBlue, 'ellipse TRect overload');
  Clear;
  C.RoundRect(0, 0, 12, 12, 4, 4);
  CheckPixel(6, 6, clBlue, 'round rect interior');
  CheckPixel(6, 0, clRed, 'round rect top border');
  CheckPixel(0, 0, clWhite, 'round rect corner cut');
  Clear;
  C.Polygon([Point(0, 0), Point(12, 0), Point(0, 12)]);
  CheckPixel(2, 2, clBlue, 'polygon interior');
  CheckPixel(11, 11, clWhite, 'polygon outside');
  CheckPixel(0, 5, clRed, 'polygon border');
  Clear;
  C.Pie(0, 0, 12, 12, 12, 6, 6, 0);
  CheckPixel(8, 3, clBlue, 'pie interior (first quadrant)');
  CheckPixel(3, 8, clWhite, 'pie outside (third quadrant)');
  Clear;
  C.Chord(0, 0, 12, 12, 12, 6, 0, 6);
  CheckPixel(6, 2, clBlue, 'chord interior (upper half)');
  CheckPixel(6, 9, clWhite, 'chord outside (lower half)');
  Clear;
  C.Arc(0, 0, 12, 12, 12, 6, 0, 6);
  CheckPixel(6, 0, clRed, 'arc top');
  CheckPixel(6, 3, clWhite, 'arc not filled');
  CheckPixel(6, 11, clWhite, 'arc lower half not drawn');
end;

procedure CheckPixelsAndClip;
var
  R: TRect;
begin
  Section('Pixels and ClipRect');
  Clear;
  C.Pixels[19, 19] := clLime;
  CheckEqI(clLime, C.Pixels[19, 19], 'Pixels write and read');
  C.Pixels[0, 19] := clBtnFace;
  CheckEqI(ColorToRGB(clBtnFace), C.Pixels[0, 19], 'system colour resolved on write');
  R := C.ClipRect;
  CheckEqI(0, R.Left, 'ClipRect.Left');
  CheckEqI(0, R.Top, 'ClipRect.Top');
  CheckEqI(Size, R.Right, 'ClipRect.Right');
  CheckEqI(Size, R.Bottom, 'ClipRect.Bottom');
  IntersectClipRect(DC, 0, 0, 10, 8);
  R := C.ClipRect;
  CheckEqI(10, R.Right, 'clipped ClipRect.Right');
  CheckEqI(8, R.Bottom, 'clipped ClipRect.Bottom');
  CheckEqI(-1, C.Pixels[15, 15], 'outside the clip reads -1');
  C.Pixels[15, 15] := clRed;
  SelectClipRgn(DC, 0);
  CheckPixel(15, 15, clWhite, 'write outside the clip has no effect');
end;

procedure CheckStateTracking;
var
  Counter: TCounter;
  Prev: Integer;
begin
  Section('object state tracking (UIF-530)');
  Clear;
  C.Pen.Color := clRed;
  C.LineTo(1, 1);
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = C.Pen.Handle, 'pen selected for a line');
  CheckEqB(True, GetCurrentObject(DC, OBJ_BRUSH) = C.Brush.Handle, 'brush selected by the earlier FillRect');
  C.Pen.Color := clGreen;
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = OrigPen, 'pen change deselects (original selected back)');
  C.LineTo(2, 2);
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = C.Pen.Handle, 'new pen selected on next drawing');
  C.Brush.Color := clYellow;
  CheckEqB(True, GetCurrentObject(DC, OBJ_BRUSH) = OrigBrush, 'brush change deselects');
  C.Pen.Mode := pmXor;
  C.LineTo(3, 3);
  CheckEqI(R2_XORPEN, SetROP2(DC, R2_COPYPEN), 'Pen.Mode applied with SetROP2');
  C.Pen.Mode := pmCopy;
  C.LineTo(4, 4);
  CheckEqI(R2_COPYPEN, SetROP2(DC, R2_COPYPEN), 'pmCopy applied');
  C.Brush.Style := bsClear;
  C.Rectangle(0, 0, 2, 2);
  CheckEqI(TRANSPARENT, SetBkMode(DC, OPAQUE), 'bsClear: TRANSPARENT background');
  C.Brush.Color := clYellow;
  C.Rectangle(0, 0, 2, 2);
  CheckEqI(OPAQUE, SetBkMode(DC, TRANSPARENT), 'bsSolid: OPAQUE background');
  C.Brush.Color := clAqua;
  C.Rectangle(0, 0, 2, 2);
  CheckEqI(ColorToRGB(clAqua), SetBkColor(DC, 0), 'SetBkColor follows the brush');
  C.Font.Color := clMaroon;
  C.Refresh;
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = OrigPen, 'Refresh deselects the pen');
  CheckEqB(True, GetCurrentObject(DC, OBJ_BRUSH) = OrigBrush, 'Refresh deselects the brush');
  C.LineTo(5, 5);
  CheckEqB(True, GetCurrentObject(DC, OBJ_FONT) = OrigFont, 'a line does not select the font');

  Section('OnChanging / OnChange');
  Counter := TCounter.Create;
  try
    C.OnChanging := Counter.Changing;
    C.OnChange := Counter.Changed;
    C.LineTo(6, 6);
    C.FillRect(Rect(0, 0, 1, 1));
    C.Pixels[0, 0] := clRed;
    Prev := C.Pixels[0, 0];
    CheckEqS('<><><>', Counter.Log, 'one pair per modifying call, none for reads');
    CheckEqI(clRed, Prev, 'read back');
  finally
    C.OnChanging := nil;
    C.OnChange := nil;
    Counter.Free;
  end;

  Section('Lock, CopyMode');
  CheckEqB(True, C.TryLock, 'TryLock');
  C.Lock;
  CheckEqI(0, C.LockCount, 'LockCount always 0');
  C.Unlock;
  C.Unlock;
  CheckEqI(SRCCOPY, C.CopyMode, 'CopyMode default');
  CheckEqI(SRCCOPY, cmSrcCopy, 'cmSrcCopy');
  CheckEqI(BLACKNESS, cmBlackness, 'cmBlackness');
  CheckEqI(WHITENESS, cmWhiteness, 'cmWhiteness');

  Section('detach (UIF-531)');
  C.LineTo(7, 7);
  C.FillRect(Rect(0, 0, 1, 1));
  C.Handle := 0;
  CheckEqB(False, C.HandleAllocated, 'detached');
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = OrigPen, 'original pen back');
  CheckEqB(True, GetCurrentObject(DC, OBJ_BRUSH) = OrigBrush, 'original brush back');
  CheckEqB(True, GetCurrentObject(DC, OBJ_FONT) = OrigFont, 'original font back');
  C.Handle := DC;
  C.LineTo(8, 8);
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = C.Pen.Handle, 'reattached canvas selects again');
end;

begin
  CheckNoHandle;
  MakeSurface;
  C := TCanvas.Create;
  try
    C.Handle := DC;
    CheckEqB(True, C.HandleAllocated, 'attached');
    CheckEqB(True, C.Handle = DC, 'Handle is the attached DC');
    CheckShapes;
    CheckPixelsAndClip;
    CheckStateTracking;
  finally
    C.Free;
  end;
  CheckEqB(True, GetCurrentObject(DC, OBJ_PEN) = OrigPen, 'freeing the canvas leaves no object selected');
  FreeSurface;
  Done;
end.
