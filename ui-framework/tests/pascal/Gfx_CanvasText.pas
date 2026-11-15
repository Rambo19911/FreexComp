// covers: UIF-530
// opc: -U ..\..\src
program Gfx_CanvasText;
{ U-007, graphics.md section 3: text on a TCanvas over a 32-bit DIB section. TextWidth('') = 0, TextHeight =
  tmHeight of the selected font, TextExtent = GetTextExtentPoint32W; TextOut draws in Font.Color, advances
  PenPos and paints the cell background only when the brush is solid (opaque vs transparent pixel);
  TextRect(R, X, Y, Text) clips to R and fills R with a solid brush; TextRect(R, Text, Format) maps the
  format set to DrawTextW (tfCalcRect measures without drawing; alignment flags place the text). Fonts are
  non-antialiased so that text pixels have exactly the font colour. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

const
  W = 80;
  H = 30;

var
  DC: HDC;
  Bmp, OldBmp: HBITMAP;
  C: TCanvas;

procedure MakeSurface;
var
  BI: TBitmapInfo;
  Bits: Pointer;
begin
  FillChar(BI, SizeOf(BI), 0);
  BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  BI.bmiHeader.biWidth := W;
  BI.bmiHeader.biHeight := -H;
  BI.bmiHeader.biPlanes := 1;
  BI.bmiHeader.biBitCount := 32;
  BI.bmiHeader.biCompression := BI_RGB;
  DC := CreateCompatibleDC(0);
  Bmp := CreateDIBSection(DC, @BI, DIB_RGB_COLORS, Bits, 0, 0);
  OldBmp := SelectObject(DC, Bmp);
end;

procedure Clear;
var
  Color: TColor;
  Style: TBrushStyle;
begin
  Color := C.Brush.Color;
  Style := C.Brush.Style;
  C.Brush.Style := bsSolid;
  C.Brush.Color := clWhite;
  C.FillRect(Rect(0, 0, W, H));
  C.Brush.Color := Color;
  C.Brush.Style := Style;
end;

{ number of pixels of colour Color in the rectangle X1..X2-1, Y1..Y2-1 }
function CountColor(X1, Y1, X2, Y2: Integer; Color: TColor): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if TColor(GetPixel(DC, X, Y)) = Color then
        Inc(Result);
end;

procedure CheckExtents;
var
  TM: TTextMetricW;
  Sz, Ext: TSize;
  S: string;
begin
  Section('extents');
  CheckEqI(0, C.TextWidth(''), 'TextWidth of empty text');
  FillChar(TM, SizeOf(TM), 0);
  SelectObject(DC, C.Font.Handle);
  GetTextMetricsW(DC, TM);
  Check(TM.tmHeight > 0, 'tmHeight positive');
  CheckEqI(TM.tmHeight, C.TextHeight(''), 'TextHeight of empty text = tmHeight');
  CheckEqI(TM.tmHeight, C.TextHeight('Wg'), 'TextHeight = tmHeight');
  S := 'Hello';
  GetTextExtentPoint32W(DC, PWideChar(S), Length(S), Sz);
  C.Refresh;
  Ext := C.TextExtent(S);
  CheckEqI(Sz.cx, Ext.cx, 'TextExtent.cx');
  CheckEqI(Sz.cy, Ext.cy, 'TextExtent.cy');
  CheckEqI(Sz.cx, C.TextWidth(S), 'TextWidth');
  Check(C.TextWidth('WWWW') > C.TextWidth('W'), 'longer text is wider');
  Ext := C.TextExtent('');
  CheckEqI(0, Ext.cx, 'TextExtent empty cx');
  CheckEqI(TM.tmHeight, Ext.cy, 'TextExtent empty cy');
  C.Font.Height := -24;
  Check(C.TextHeight('W') > TM.tmHeight, 'font change reselects the font');
  C.Font.Height := -12;
end;

procedure CheckTextOut;
var
  TW, TH: Integer;
begin
  Section('TextOut');
  TW := C.TextWidth('WW');
  TH := C.TextHeight('WW');
  Clear;
  C.Brush.Style := bsSolid;
  C.Brush.Color := clYellow;
  C.Font.Color := clBlack;
  C.TextOut(2, 2, 'WW');
  Check(CountColor(2, 2, 2 + TW, 2 + TH, clBlack) > 0, 'glyph pixels in Font.Color');
  CheckEqI(0, CountColor(0, 0, W, H, clYellow) - (TW * TH - CountColor(2, 2, 2 + TW, 2 + TH, clBlack)),
    'opaque: the whole text cell and nothing else is in the brush colour');
  CheckEqI(clWhite, TColor(GetPixel(DC, 1, 1)), 'outside the cell untouched');
  CheckEqI(2 + TW, C.PenPos.X, 'PenPos.X advanced to the end of the text');
  CheckEqI(2, C.PenPos.Y, 'PenPos.Y unchanged');

  Clear;
  C.Brush.Style := bsClear;
  C.Font.Color := clRed;
  C.TextOut(2, 2, 'WW');
  Check(CountColor(2, 2, 2 + TW, 2 + TH, clRed) > 0, 'transparent: glyph pixels drawn');
  CheckEqI(TW * TH, CountColor(2, 2, 2 + TW, 2 + TH, clRed) + CountColor(2, 2, 2 + TW, 2 + TH, clWhite),
    'transparent: background of the cell untouched');
  C.Brush.Style := bsSolid;
end;

procedure CheckTextRect;
var
  R: TRect;
  S: string;
  TW, TH: Integer;
begin
  Section('TextRect(R, X, Y, Text)');
  Clear;
  C.Brush.Color := clAqua;
  C.Font.Color := clBlack;
  C.TextRect(Rect(0, 0, 10, 20), 0, 0, 'WWWWWW');
  CheckEqI(0, CountColor(10, 0, W, H, clBlack), 'no glyph pixel outside R');
  CheckEqI(0, CountColor(0, 20, W, H, clAqua) + CountColor(10, 0, W, 20, clAqua), 'no fill outside R');
  CheckEqI(10 * 20, CountColor(0, 0, 10, 20, clAqua) + CountColor(0, 0, 10, 20, clBlack), 'R filled opaque');
  Clear;
  C.Brush.Style := bsClear;
  C.TextRect(Rect(0, 0, 10, 20), 0, 0, 'WWWWWW');
  CheckEqI(0, CountColor(0, 0, W, H, clAqua), 'non-solid brush: R not filled');
  Check(CountColor(0, 0, 10, 20, clBlack) > 0, 'non-solid brush: glyphs drawn');
  CheckEqI(0, CountColor(10, 0, W, H, clBlack), 'non-solid brush: clipped');
  C.Brush.Style := bsSolid;

  Section('TextRect(R, Text, Format)');
  S := 'Wide';
  TW := C.TextWidth(S);
  TH := C.TextHeight(S);
  Clear;
  R := Rect(5, 5, 5, 5);
  C.TextRect(R, S, [tfCalcRect, tfSingleLine]);
  CheckEqI(5, R.Left, 'tfCalcRect keeps Left');
  CheckEqI(5 + TW, R.Right, 'tfCalcRect Right = Left + text width');
  CheckEqI(5 + TH, R.Bottom, 'tfCalcRect Bottom = Top + text height');
  CheckEqI(W * H, CountColor(0, 0, W, H, clWhite), 'tfCalcRect draws nothing');
  CheckEqS('Wide', S, 'text unchanged');
  Clear;
  C.Brush.Style := bsClear;
  R := Rect(0, 0, W, H);
  C.TextRect(R, S, [tfRight, tfBottom, tfSingleLine]);
  CheckEqI(0, CountColor(0, 0, W - TW, H, clBlack), 'tfRight: nothing left of the text');
  Check(CountColor(W - TW, H - TH, W, H, clBlack) > 0, 'tfRight/tfBottom: glyphs at the bottom-right');
  Clear;
  R := Rect(0, 0, W, H);
  C.TextRect(R, S, [tfCenter, tfVerticalCenter, tfSingleLine]);
  CheckEqI(0, CountColor(0, 0, (W - TW) div 2 - 1, H, clBlack), 'tfCenter: left margin empty');
  CheckEqI(0, CountColor((W + TW) div 2 + 1, 0, W, H, clBlack), 'tfCenter: right margin empty');
  Check(CountColor(0, 0, W, H, clBlack) > 0, 'tfCenter: glyphs drawn');
  Clear;
  R := Rect(0, 0, W, H);
  C.TextRect(R, S, [tfLeft, tfTop, tfSingleLine]);
  CheckEqI(0, CountColor(TW + 1, 0, W, H, clBlack), 'tfLeft: nothing right of the text');
  CheckEqI(0, CountColor(0, TH + 1, W, H, clBlack), 'tfTop: nothing below the text');
  C.Brush.Style := bsSolid;
end;

begin
  MakeSurface;
  C := TCanvas.Create;
  try
    C.Handle := DC;
    C.Font.Quality := fqNonAntialiased;
    CheckExtents;
    CheckTextOut;
    CheckTextRect;
  finally
    C.Free;
  end;
  SelectObject(DC, OldBmp);
  DeleteObject(Bmp);
  DeleteDC(DC);
  Done;
end.
