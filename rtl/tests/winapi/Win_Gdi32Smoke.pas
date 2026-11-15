// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-831, RTL-832, RTL-860, RTL-862
program Win_Gdi32Smoke;
{ Windows unit, gdi32 part (winapi.md section 5.4): a memory DC with a 32-bit top-down DIB section, drawing with
  pens, brushes, fonts and clip regions, and pixels read back with GetPixel. Pixel values are only checked where
  the result does not depend on the display (solid fills and lines on the DIB); text and curves are only called. }
uses Windows, Types, TestKit;

{$POINTERMATH ON}

const
  White = $00FFFFFF;

var
  FontCount: Integer;

function FirstFont(lpelfe: PLogFontW; lpntme: PTextMetricW; FontType: DWORD; Data: LPARAM): Integer; stdcall;
begin
  FontCount := FontCount + 1;
  Result := 0;
end;

function NewDib(DC: HDC; Width, Height: Integer; var Bits: Pointer): HBITMAP;
var
  BI: TBitmapInfo;
begin
  FillChar(BI, SizeOf(BI), 0);
  BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  BI.bmiHeader.biWidth := Width;
  BI.bmiHeader.biHeight := -Height;
  BI.bmiHeader.biPlanes := 1;
  BI.bmiHeader.biBitCount := 32;
  BI.bmiHeader.biCompression := BI_RGB;
  Result := CreateDIBSection(DC, @BI, DIB_RGB_COLORS, Bits, 0, 0);
end;

var
  DC, DC2: HDC;
  Dib, Dib2, OldBmp, Old2, Mono, Compat: HBITMAP;
  Bits, Bits2: Pointer;
  Pen: HPEN;
  Brush, Green, Blue, Pattern: HBRUSH;
  LB: TLogBrush;
  LF: TLogFontW;
  Font: HFONT;
  TM: TTextMetricW;
  Sz: TSize;
  BM: TBitmapRec;
  BI: TBitmapInfo;
  Rgn: HRGN;
  R: TRect;
  P: TPoint;
  Pts: array[0..2] of TPoint;
  MonoBits: array[0..7] of Word;
  Pixels: array[0..255] of Cardinal;
  Saved, I: Integer;
  II: TIconInfo;

begin
  Section('DC and DIB section');
  DC := CreateCompatibleDC(0);
  Check(DC <> 0, 'CreateCompatibleDC');
  Dib := NewDib(DC, 16, 16, Bits);
  Check((Dib <> 0) and (Bits <> nil), 'CreateDIBSection');
  OldBmp := HBITMAP(SelectObject(DC, Dib));
  Check(OldBmp <> 0, 'SelectObject bitmap');
  CheckEqI(Int64(Dib), Int64(GetCurrentObject(DC, OBJ_BITMAP)), 'GetCurrentObject');
  CheckEqI(OBJ_MEMDC, GetObjectType(DC), 'GetObjectType memory DC');
  CheckEqI(OBJ_BITMAP, GetObjectType(Dib), 'GetObjectType bitmap');
  CheckEqI(0, GetObjectType(0), 'GetObjectType of no object');
  CheckEqI(SizeOf(BM), GetObjectW(Dib, SizeOf(BM), @BM), 'GetObjectW');
  CheckEqI(16, BM.bmWidth, 'TBitmapRec.bmWidth');
  CheckEqI(32, BM.bmBitsPixel, 'TBitmapRec.bmBitsPixel');
  Check(GetObject(Dib, SizeOf(BM), @BM) > 0, 'GetObject alias');
  Check(PatBlt(DC, 0, 0, 16, 16, WHITENESS), 'PatBlt WHITENESS');
  CheckEqI(White, GetPixel(DC, 3, 3), 'GetPixel white');
  CheckEqI(White, PCardinal(Bits)[0] and White, 'DIB bits white');
  CheckEqI(RGB(1, 2, 3), SetPixel(DC, 2, 2, RGB(1, 2, 3)), 'SetPixel');
  CheckEqI(RGB(1, 2, 3), GetPixel(DC, 2, 2), 'GetPixel after SetPixel');
  Check(GetDeviceCaps(DC, BITSPIXEL) > 0, 'GetDeviceCaps BITSPIXEL');
  Check(GetDeviceCaps(DC, LOGPIXELSX) > 0, 'GetDeviceCaps LOGPIXELSX');

  Section('state');
  Saved := SaveDC(DC);
  Check(Saved > 0, 'SaveDC');
  SetTextColor(DC, RGB(0, 0, 0));
  CheckEqI(RGB(0, 0, 0), SetTextColor(DC, RGB(10, 20, 30)), 'SetTextColor previous');
  CheckEqI(RGB(10, 20, 30), GetTextColor(DC), 'GetTextColor');
  SetBkColor(DC, White);
  CheckEqI(White, SetBkColor(DC, RGB(40, 50, 60)), 'SetBkColor previous');
  CheckEqI(RGB(40, 50, 60), GetBkColor(DC), 'GetBkColor');
  CheckEqI(OPAQUE, SetBkMode(DC, TRANSPARENT), 'SetBkMode default OPAQUE');
  CheckEqI(TRANSPARENT, GetBkMode(DC), 'GetBkMode');
  CheckEqI(TA_LEFT or TA_TOP, SetTextAlign(DC, TA_CENTER), 'SetTextAlign default');
  CheckEqI(R2_COPYPEN, SetROP2(DC, R2_XORPEN), 'SetROP2 default');
  CheckEqI(R2_XORPEN, GetROP2(DC), 'GetROP2');
  CheckEqI(BLACKONWHITE, SetStretchBltMode(DC, COLORONCOLOR), 'SetStretchBltMode default');
  Check(SetBrushOrgEx(DC, 1, 1, @P), 'SetBrushOrgEx');
  CheckEqI(0, P.X, 'SetBrushOrgEx previous');
  Check(RestoreDC(DC, Saved), 'RestoreDC');
  CheckEqI(RGB(0, 0, 0), GetTextColor(DC), 'RestoreDC text colour');
  CheckEqI(R2_COPYPEN, SetROP2(DC, R2_COPYPEN), 'RestoreDC ROP2');

  Section('pens and brushes');
  Green := CreateSolidBrush(RGB(0, 255, 0));
  Check(Green <> 0, 'CreateSolidBrush');
  Brush := HBRUSH(SelectObject(DC, Green));
  Check(PatBlt(DC, 0, 0, 4, 4, PATCOPY), 'PatBlt PATCOPY');
  CheckEqI(RGB(0, 255, 0), GetPixel(DC, 1, 1), 'solid brush pixel');
  LB.lbStyle := BS_SOLID;
  LB.lbColor := RGB(0, 0, 255);
  LB.lbHatch := 0;
  Blue := CreateBrushIndirect(LB);
  Check(Blue <> 0, 'CreateBrushIndirect');
  SelectObject(DC, Blue);
  Check(PatBlt(DC, 4, 0, 4, 4, PATCOPY), 'PatBlt blue');
  CheckEqI(RGB(0, 0, 255), GetPixel(DC, 5, 1), 'CreateBrushIndirect pixel');
  for I := 0 to 7 do
    MonoBits[I] := $AAAA;
  Mono := CreateBitmap(8, 8, 1, 1, @MonoBits[0]);
  Check(Mono <> 0, 'CreateBitmap');
  Pattern := CreatePatternBrush(Mono);
  Check(Pattern <> 0, 'CreatePatternBrush');
  SelectObject(DC, GetStockObject(WHITE_BRUSH));
  Check(GetStockObject(BLACK_PEN) <> 0, 'GetStockObject');
  Pen := CreatePen(PS_SOLID, 1, RGB(0, 0, 0));
  Check(Pen <> 0, 'CreatePen');
  SelectObject(DC, Pen);
  Check(MoveToEx(DC, 0, 8, @P), 'MoveToEx');
  Check(LineTo(DC, 16, 8), 'LineTo');
  CheckEqI(0, GetPixel(DC, 9, 8), 'LineTo pixel');
  Check(GetCurrentPositionEx(DC, @P), 'GetCurrentPositionEx');
  CheckEqI(16, P.X, 'GetCurrentPositionEx X after LineTo');
  CheckEqI(8, P.Y, 'GetCurrentPositionEx Y after LineTo');
  CheckEqI(White, GetPixel(DC, 9, 9), 'below the line');
  Check(Rectangle(DC, 8, 10, 16, 16), 'Rectangle');
  CheckEqI(0, GetPixel(DC, 8, 12), 'Rectangle border');
  CheckEqI(White, GetPixel(DC, 11, 12), 'Rectangle interior (white brush)');
  Check(RoundRect(DC, 0, 10, 8, 16, 2, 2), 'RoundRect');
  Check(Ellipse(DC, 0, 10, 8, 16), 'Ellipse');
  Check(Arc(DC, 0, 0, 8, 8, 0, 4, 8, 4), 'Arc');
  Check(Pie(DC, 0, 0, 8, 8, 0, 4, 8, 4), 'Pie');
  Check(Chord(DC, 0, 0, 8, 8, 0, 4, 8, 4), 'Chord');
  Pts[0] := Point(1, 1);
  Pts[1] := Point(6, 1);
  Pts[2] := Point(3, 6);
  Check(Polygon(DC, @Pts[0], 3), 'Polygon');
  Check(Polyline(DC, @Pts[0], 3), 'Polyline');
  SelectObject(DC, GetStockObject(BLACK_PEN));
  SelectObject(DC, Brush);
  Check(DeleteObject(Pen), 'DeleteObject pen');
  Check(DeleteObject(Green), 'DeleteObject brush');
  Check(DeleteObject(Blue), 'DeleteObject indirect brush');
  Check(DeleteObject(Pattern), 'DeleteObject pattern brush');
  Check(DeleteObject(Mono), 'DeleteObject bitmap');

  Section('fonts and text');
  FillChar(LF, SizeOf(LF), 0);
  LF.lfHeight := -12;
  LF.lfWeight := FW_NORMAL;
  LF.lfCharSet := DEFAULT_CHARSET;
  Font := CreateFontIndirectW(LF);
  Check(Font <> 0, 'CreateFontIndirectW');
  SelectObject(DC, Font);
  Check(GetTextMetricsW(DC, TM), 'GetTextMetricsW');
  Check(TM.tmHeight > 0, 'TTextMetricW.tmHeight');
  Check(GetTextExtentPoint32W(DC, 'Abc', 3, Sz), 'GetTextExtentPoint32W');
  Check((Sz.cx > 0) and (Sz.cy > 0), 'GetTextExtentPoint32W size');
  Check(TextOutW(DC, 0, 0, 'Abc', 3), 'TextOutW');
  R := Rect(0, 0, 16, 8);
  Check(ExtTextOutW(DC, 0, 0, ETO_OPAQUE or ETO_CLIPPED, @R, 'Abc', 3, nil), 'ExtTextOutW');
  FontCount := 0;
  FillChar(LF, SizeOf(LF), 0);
  LF.lfCharSet := DEFAULT_CHARSET;
  CheckEqI(0, EnumFontFamiliesExW(DC, LF, @FirstFont, 0, 0), 'EnumFontFamiliesExW stopped by the callback');
  CheckEqI(1, FontCount, 'EnumFontFamiliesExW callback');
  SelectObject(DC, GetStockObject(DEFAULT_GUI_FONT));
  Check(DeleteObject(Font), 'DeleteObject font');

  Section('clipping and viewport');
  Rgn := CreateRectRgn(0, 0, 8, 8);
  Check(Rgn <> 0, 'CreateRectRgn');
  CheckEqI(SIMPLEREGION, SelectClipRgn(DC, Rgn), 'SelectClipRgn');
  CheckEqI(SIMPLEREGION, GetClipBox(DC, R), 'GetClipBox');
  CheckEqI(8, R.Right, 'GetClipBox Right');
  { the region kind these two report is not checked: the OS may call a rectangular result complex }
  Check(IntersectClipRect(DC, 0, 0, 4, 8) <> RGN_ERROR, 'IntersectClipRect');
  Check(GetClipBox(DC, R) <> RGN_ERROR, 'GetClipBox after IntersectClipRect');
  CheckEqI(4, R.Right, 'IntersectClipRect box Right');
  Check(ExcludeClipRect(DC, 1, 1, 2, 2) <> RGN_ERROR, 'ExcludeClipRect');
  Check(SelectClipRgn(DC, 0) <> RGN_ERROR, 'SelectClipRgn none');
  Check(DeleteObject(Rgn), 'DeleteObject region');
  Check(SetViewportOrgEx(DC, 2, 3, @P), 'SetViewportOrgEx');
  CheckEqI(0, P.X, 'SetViewportOrgEx previous');
  Check(OffsetViewportOrgEx(DC, 1, 1, nil), 'OffsetViewportOrgEx');
  Check(GetViewportOrgEx(DC, P), 'GetViewportOrgEx');
  CheckEqI(3, P.X, 'viewport X');
  CheckEqI(4, P.Y, 'viewport Y');
  SetViewportOrgEx(DC, 0, 0, nil);

  Section('blits and DIB bits');
  Check(PatBlt(DC, 0, 0, 16, 16, BLACKNESS), 'PatBlt BLACKNESS');
  SetPixel(DC, 1, 0, RGB(0, 255, 0));
  SetPixel(DC, 1, 1, RGB(0, 255, 0));
  DC2 := CreateCompatibleDC(DC);
  Check(DC2 <> 0, 'CreateCompatibleDC of a DC');
  Dib2 := NewDib(DC2, 16, 16, Bits2);
  Old2 := HBITMAP(SelectObject(DC2, Dib2));
  Check(BitBlt(DC2, 0, 0, 16, 16, DC, 0, 0, SRCCOPY), 'BitBlt');
  CheckEqI(RGB(0, 255, 0), GetPixel(DC2, 1, 1), 'BitBlt pixel');
  CheckEqI(0, GetPixel(DC2, 2, 1), 'BitBlt black pixel');
  { 8 x 8 -> 16 x 16: destination pixels 2 and 3 of a row come from source pixel 1 }
  Check(StretchBlt(DC2, 0, 0, 16, 16, DC, 0, 0, 8, 8, SRCCOPY), 'StretchBlt');
  CheckEqI(RGB(0, 255, 0), GetPixel(DC2, 3, 3), 'StretchBlt pixel');
  Check(GdiFlush, 'GdiFlush');
  SelectObject(DC2, Old2);
  FillChar(BI, SizeOf(BI), 0);
  BI.bmiHeader.biSize := SizeOf(TBitmapInfoHeader);
  BI.bmiHeader.biWidth := 16;
  BI.bmiHeader.biHeight := -16;
  BI.bmiHeader.biPlanes := 1;
  BI.bmiHeader.biBitCount := 32;
  BI.bmiHeader.biCompression := BI_RGB;
  CheckEqI(16, GetDIBits(DC2, Dib2, 0, 16, @Pixels[0], @BI, DIB_RGB_COLORS), 'GetDIBits');
  CheckEqI(RGB(0, 255, 0), Pixels[2] and White, 'GetDIBits pixel');
  CheckEqI(0, Pixels[16 * 15 + 15] and White, 'GetDIBits last pixel');
  Pixels[1] := $00102030;
  CheckEqI(16, SetDIBits(DC2, Dib2, 0, 16, @Pixels[0], @BI, DIB_RGB_COLORS), 'SetDIBits');
  CheckEqI($00102030, PCardinal(Bits2)[1] and White, 'SetDIBits pixel');
  Compat := CreateCompatibleBitmap(DC, 4, 4);
  Check(Compat <> 0, 'CreateCompatibleBitmap');
  Check(DeleteObject(Compat), 'DeleteObject compatible bitmap');
  Check(DeleteDC(DC2), 'DeleteDC second');
  Check(DeleteObject(Dib2), 'DeleteObject second DIB');

  Section('icon bitmaps');
  Check(GetIconInfo(LoadIconW(0, IDI_APPLICATION), II), 'GetIconInfo');
  Check(DeleteObject(II.hbmMask), 'DeleteObject icon mask');
  Check(DeleteObject(II.hbmColor), 'DeleteObject icon colour');

  Section('cleanup');
  SelectObject(DC, OldBmp);
  Check(DeleteDC(DC), 'DeleteDC');
  Check(DeleteObject(Dib), 'DeleteObject DIB');
  Done;
end.
