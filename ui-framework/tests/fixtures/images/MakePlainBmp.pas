// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
program MakePlainBmp;
{ One-off generator of plain.bmp (see README.md): 16 x 12, 24 bits per pixel, four opaque blocks (red, green,
  blue, yellow), written by the framework's own TBitmap.SaveToFile into the file named by the first parameter.
  Run by hand, never by a test. }
uses
  Types, Graphics;

var
  B: TBitmap;
begin
  B := TBitmap.Create;
  try
    B.PixelFormat := pf24bit;
    B.SetSize(16, 12);
    B.Canvas.Brush.Color := clRed;
    B.Canvas.FillRect(Rect(0, 0, 8, 6));
    B.Canvas.Brush.Color := clLime;
    B.Canvas.FillRect(Rect(8, 0, 16, 6));
    B.Canvas.Brush.Color := clBlue;
    B.Canvas.FillRect(Rect(0, 6, 8, 12));
    B.Canvas.Brush.Color := clYellow;
    B.Canvas.FillRect(Rect(8, 6, 16, 12));
    B.SaveToFile(ParamStr(1));
  finally
    B.Free;
  end;
end.
