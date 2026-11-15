// covers: UIF-546
program Gfx_BitmapDraw;
{ U-010, graphics.md section 3 and UIF-546: TCanvas.Draw / StretchDraw call TBitmap.Draw (BitBlt when the
  size is unchanged, StretchBlt otherwise; nil or empty graphics draw nothing); a transparent bitmap skips
  the pixels of its transparent colour (tmAuto = bottom-left pixel, tmFixed = TransparentColor), also when
  stretched; CopyRect uses StretchBlt with CopyMode. Pixels are read back from memory bitmaps. }
uses
  Windows, SysUtils, Classes, Graphics, TestKit;

type
  TCounter = class
    Count: Integer;
    procedure Changed(Sender: TObject);
  end;

procedure TCounter.Changed(Sender: TObject);
begin
  Inc(Count);
end;

function NewBitmap(W, H: Integer; Color: TColor): TBitmap;
begin
  Result := TBitmap.Create;
  Result.SetSize(W, H);
  Result.Canvas.Brush.Color := Color;
  Result.Canvas.FillRect(Rect(0, 0, W, H));
end;

procedure CheckPlainDraw;
var
  Dest, Src, Empty: TBitmap;
  Counter: TCounter;
begin
  Section('Draw and StretchDraw');
  Dest := NewBitmap(10, 10, clWhite);
  Src := NewBitmap(2, 2, clRed);
  Empty := TBitmap.Create;
  Counter := TCounter.Create;
  try
    Src.Canvas.Pixels[1, 0] := clBlue;
    Src.Canvas.Pixels[0, 1] := clLime;
    Src.Canvas.Pixels[1, 1] := clYellow;
    Dest.Canvas.OnChange := Counter.Changed;
    Dest.Canvas.Draw(1, 1, Src);
    CheckEqI(1, Counter.Count, 'Draw changes the canvas once');
    Dest.Canvas.OnChange := nil;
    CheckEqI(clRed, Dest.Canvas.Pixels[1, 1], 'Draw (0,0)');
    CheckEqI(clBlue, Dest.Canvas.Pixels[2, 1], 'Draw (1,0)');
    CheckEqI(clLime, Dest.Canvas.Pixels[1, 2], 'Draw (0,1)');
    CheckEqI(clYellow, Dest.Canvas.Pixels[2, 2], 'Draw (1,1)');
    CheckEqI(clWhite, Dest.Canvas.Pixels[3, 3], 'outside the drawn area');
    CheckEqI(clWhite, Dest.Canvas.Pixels[0, 0], 'before the drawn area');

    Dest.Canvas.StretchDraw(Rect(4, 4, 8, 8), Src);
    CheckEqI(clRed, Dest.Canvas.Pixels[4, 4], 'stretched (0,0) top-left');
    CheckEqI(clRed, Dest.Canvas.Pixels[5, 5], 'stretched (0,0) bottom-right');
    CheckEqI(clBlue, Dest.Canvas.Pixels[7, 4], 'stretched (1,0)');
    CheckEqI(clYellow, Dest.Canvas.Pixels[7, 7], 'stretched (1,1)');
    CheckEqI(clWhite, Dest.Canvas.Pixels[8, 8], 'stretch right/bottom excluded');

    Counter.Count := 0;
    Dest.Canvas.OnChange := Counter.Changed;
    Dest.Canvas.Draw(0, 0, nil);
    Dest.Canvas.Draw(0, 0, Empty);
    Dest.Canvas.StretchDraw(Rect(0, 0, 5, 5), Empty);
    CheckEqI(0, Counter.Count, 'nil or empty graphic draws nothing');
    Dest.Canvas.OnChange := nil;
    CheckEqI(clWhite, Dest.Canvas.Pixels[0, 0], 'still white');
  finally
    Dest.Free;
    Src.Free;
    Empty.Free;
    Counter.Free;
  end;
end;

procedure CheckTransparent;
var
  Dest, Src: TBitmap;
begin
  Section('transparency (UIF-546)');
  Dest := NewBitmap(8, 8, clLime);
  Src := NewBitmap(3, 3, clRed);
  try
    Src.Canvas.Pixels[0, 2] := clFuchsia;
    Src.Canvas.Pixels[1, 1] := clFuchsia;
    Src.Canvas.Pixels[2, 0] := clBlue;
    Check(Src.TransparentMode = tmAuto, 'TransparentMode default tmAuto');
    CheckEqI(clFuchsia, Src.TransparentColor, 'tmAuto: bottom-left pixel');
    Src.Transparent := True;
    Dest.Canvas.Draw(0, 0, Src);
    CheckEqI(clLime, Dest.Canvas.Pixels[0, 2], 'transparent pixel skipped (bottom-left)');
    CheckEqI(clLime, Dest.Canvas.Pixels[1, 1], 'transparent pixel skipped (centre)');
    CheckEqI(clRed, Dest.Canvas.Pixels[0, 0], 'opaque pixel drawn');
    CheckEqI(clBlue, Dest.Canvas.Pixels[2, 0], 'opaque blue drawn');
    CheckEqI(clRed, Dest.Canvas.Pixels[2, 2], 'opaque corner drawn');
    CheckEqI(clLime, Dest.Canvas.Pixels[3, 3], 'outside untouched');

    Dest.Canvas.Brush.Color := clLime;
    Dest.Canvas.FillRect(Rect(0, 0, 8, 8));
    Src.TransparentColor := clRed;
    Check(Src.TransparentMode = tmFixed, 'assigning TransparentColor selects tmFixed');
    CheckEqI(clRed, Src.TransparentColor, 'tmFixed colour');
    Dest.Canvas.Draw(0, 0, Src);
    CheckEqI(clLime, Dest.Canvas.Pixels[0, 0], 'tmFixed: red skipped');
    CheckEqI(clFuchsia, Dest.Canvas.Pixels[1, 1], 'tmFixed: fuchsia drawn');
    CheckEqI(clBlue, Dest.Canvas.Pixels[2, 0], 'tmFixed: blue drawn');

    Dest.Canvas.FillRect(Rect(0, 0, 8, 8));
    Src.TransparentMode := tmAuto;
    Dest.Canvas.StretchDraw(Rect(0, 0, 6, 6), Src);
    CheckEqI(clLime, Dest.Canvas.Pixels[0, 4], 'stretched: transparent block skipped');
    CheckEqI(clLime, Dest.Canvas.Pixels[3, 3], 'stretched: transparent centre skipped');
    CheckEqI(clRed, Dest.Canvas.Pixels[1, 1], 'stretched: opaque block drawn');
    CheckEqI(clBlue, Dest.Canvas.Pixels[5, 0], 'stretched: blue block drawn');

    Dest.Canvas.FillRect(Rect(0, 0, 8, 8));
    Src.Transparent := False;
    Dest.Canvas.Draw(0, 0, Src);
    CheckEqI(clFuchsia, Dest.Canvas.Pixels[0, 2], 'not transparent: every pixel drawn');
    CheckEqI(clLime, Dest.Canvas.Brush.Color, 'destination brush untouched');
    Dest.Canvas.FillRect(Rect(0, 0, 1, 1));
    CheckEqI(clLime, Dest.Canvas.Pixels[0, 0], 'destination canvas still draws with its brush');
  finally
    Dest.Free;
    Src.Free;
  end;
end;

procedure CheckCopyRect;
var
  Dest, Src: TBitmap;
begin
  Section('CopyRect');
  Dest := NewBitmap(6, 6, clWhite);
  Src := NewBitmap(4, 4, clBlue);
  try
    Src.Canvas.Pixels[3, 3] := clRed;
    Dest.Canvas.CopyRect(Rect(0, 0, 2, 2), Src.Canvas, Rect(2, 2, 4, 4));
    CheckEqI(clBlue, Dest.Canvas.Pixels[0, 0], 'source (2,2) copied');
    CheckEqI(clRed, Dest.Canvas.Pixels[1, 1], 'source (3,3) copied');
    CheckEqI(clWhite, Dest.Canvas.Pixels[2, 2], 'outside the destination');
    Dest.Canvas.CopyRect(Rect(2, 2, 6, 6), Src.Canvas, Rect(2, 2, 4, 4));
    CheckEqI(clRed, Dest.Canvas.Pixels[5, 5], 'stretched copy');
    CheckEqI(clBlue, Dest.Canvas.Pixels[2, 2], 'stretched copy top-left');
    Dest.Canvas.CopyMode := cmNotSrcCopy;
    Dest.Canvas.CopyRect(Rect(0, 0, 1, 1), Src.Canvas, Rect(0, 0, 1, 1));
    CheckEqI(clYellow, Dest.Canvas.Pixels[0, 0], 'CopyMode cmNotSrcCopy inverts blue');
    Dest.Canvas.CopyMode := cmSrcCopy;
  finally
    Dest.Free;
    Src.Free;
  end;
end;

begin
  CheckPlainDraw;
  CheckTransparent;
  CheckCopyRect;
  Done;
end.
