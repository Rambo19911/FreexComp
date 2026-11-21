// covers: UIF-255, UIF-256, UIF-260, UIF-263
// needs: desktop
program Layout_AutoSize;
{ U-065, layout.md 8.2: measured auto-size of TLabel and TStaticText. The expected sizes are measured here through
  a memory-bitmap canvas (DrawTextW with DT_CALCRECT, the same rule as UIF-255), so no font or DPI value of the
  host is assumed. Covered: caption, multi-line caption, empty caption (0 x tmHeight), ShowAccelChar, Font,
  WordWrap (width stays), AutoSize off and on again, the right-justified position rule, an alTop label (width from
  the parent, height measured, UIF-256), static text borders (+2 / +4 px per side), no measurement while scaling,
  never in dump mode (UIF-263 b), and stored bounds winning while loading (UIF-260), also once the form has
  windows. U-117: in dump mode also no static-text border/font measuring, no image sizing from a picture, and no
  measuring after a form was loaded in dump mode (what an OnCreate handler or a corpus set step does). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ExtCtrls, TestKit;

type
  TAutoForm = class(TForm)
  published
    Stored: TLabel;
    Right: TLabel;
    Boxed: TStaticText;
  end;

{$FORM Layout_AutoSize.form}

var
  Bmp: TBitmap;

{ the size UIF-255 prescribes for Text in AFont; WordWrap measures at the given width and keeps it }
function Measure(AFont: TFont; const Text: string; WordWrap, Prefix: Boolean; Width: Integer): TSize;
var
  R: TRect;
  S: string;
  Fmt: TTextFormat;
begin
  Bmp.Canvas.Font := AFont;
  if Text = '' then
  begin
    Result := Bmp.Canvas.TextExtent('');
    if WordWrap then
      Result.cx := Width;
    Exit;
  end;
  Fmt := [tfCalcRect, tfExpandTabs];
  if WordWrap then
  begin
    Include(Fmt, tfWordBreak);
    R := Rect(0, 0, Width, 0);
  end
  else
    R := Rect(0, 0, 0, 0);
  if not Prefix then
    Include(Fmt, tfNoPrefix);
  S := Text;
  Bmp.Canvas.TextRect(R, S, Fmt);
  Result.cx := R.Right - R.Left;
  if WordWrap then
    Result.cx := Width;
  Result.cy := R.Bottom - R.Top;
end;

function SizeStr(W, H: Integer): string;
begin
  Result := Format('%dx%d', [W, H]);
end;

function CtlSize(C: TControl): string;
begin
  Result := SizeStr(C.Width, C.Height);
end;

function MeasureStr(C: TControl; AFont: TFont; const Text: string; WordWrap, Prefix: Boolean; Extra: Integer)
  : string;
var
  S: TSize;
begin
  S := Measure(AFont, Text, WordWrap, Prefix, C.Width);
  if WordWrap then
    Result := SizeStr(S.cx, S.cy + 2 * Extra)
  else
    Result := SizeStr(S.cx + 2 * Extra, S.cy + 2 * Extra);
end;

function BoundsStr(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

procedure Run;
var
  F: TForm;
  L, T: TLabel;
  S: TStaticText;
  Img: TImage;
  AF: TAutoForm;
  One: TSize;
  Before: string;
  RightEdge, W, H: Integer;
begin
  Application.Initialize;
  Bmp := TBitmap.Create;
  Bmp.SetSize(8, 8);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    L := TLabel.Create(F);

    Section('label');
    CheckEqS('34x15', CtlSize(L), 'a new label keeps the default size');
    L.SetBounds(10, 10, 34, 15);
    L.Parent := F;
    L.Caption := 'Hello world';
    CheckEqS(MeasureStr(L, L.Font, 'Hello world', False, True, 0), CtlSize(L), 'Caption: measured size');
    CheckEqS('10,10', Format('%d,%d', [L.Left, L.Top]), 'left-justified: position stays');
    One := Measure(L.Font, 'One', False, True, 0);
    L.Caption := 'One'#13#10'Two';
    CheckEqS(MeasureStr(L, L.Font, 'One'#13#10'Two', False, True, 0), CtlSize(L), 'line break: several lines');
    CheckEqI(2 * One.cy, L.Height, 'two lines are twice as high');
    L.Caption := '';
    CheckEqS(SizeStr(0, Bmp.Canvas.TextExtent('').cy), CtlSize(L), 'empty caption: 0 x tmHeight');
    L.Caption := 'A'#9'B';
    CheckEqS(MeasureStr(L, L.Font, 'A'#9'B', False, True, 0), CtlSize(L), 'tabs expanded');
    L.Caption := '&&Save';
    W := L.Width;
    CheckEqS(MeasureStr(L, L.Font, '&&Save', False, True, 0), CtlSize(L), 'accelerator prefix processed');
    L.ShowAccelChar := False;
    CheckEqS(MeasureStr(L, L.Font, '&&Save', False, False, 0), CtlSize(L), 'ShowAccelChar False: DT_NOPREFIX');
    Check(L.Width > W, 'ShowAccelChar False: both ampersands measured');
    L.ShowAccelChar := True;
    L.Caption := 'Hello world';
    H := L.Height;
    L.Font.Height := -30;
    CheckEqS(MeasureStr(L, L.Font, 'Hello world', False, True, 0), CtlSize(L), 'Font change: re-measured');
    Check(L.Height > H, 'larger font: higher');
    L.ParentFont := True;
    CheckEqS(MeasureStr(L, F.Font, 'Hello world', False, True, 0), CtlSize(L), 'ParentFont: the form font');
    F.Font.Height := -24;
    CheckEqS(MeasureStr(L, F.Font, 'Hello world', False, True, 0), CtlSize(L), 'parent font change: re-measured');

    Section('WordWrap');
    L.Caption := 'alpha beta gamma delta epsilon';
    L.Width := 70;
    CheckEqI(70, L.Width, 'explicit width accepted');
    L.WordWrap := True;
    CheckEqI(70, L.Width, 'WordWrap: width stays');
    CheckEqS(MeasureStr(L, L.Font, 'alpha beta gamma delta epsilon', True, True, 0), CtlSize(L),
      'WordWrap: height at the current width');
    Check(L.Height > Measure(L.Font, 'alpha', False, True, 0).cy, 'WordWrap: several lines');
    L.Caption := '';
    CheckEqS(SizeStr(70, Bmp.Canvas.TextExtent('').cy), CtlSize(L), 'WordWrap, empty caption: tmHeight');
    L.WordWrap := False;

    Section('AutoSize');
    L.Caption := 'Measured';
    L.AutoSize := False;
    L.SetBounds(10, 10, 5, 50);
    L.Caption := 'Not measured any more';
    CheckEqS('10,10,5,50', BoundsStr(L), 'AutoSize False: caption change keeps the bounds');
    L.Font.Height := -12;
    CheckEqS('10,10,5,50', BoundsStr(L), 'AutoSize False: font change keeps the bounds');
    L.AutoSize := True;
    CheckEqS(MeasureStr(L, L.Font, 'Not measured any more', False, True, 0), CtlSize(L), 'AutoSize True: measured');

    Section('position rule');
    L.Caption := 'Wide caption text';
    L.Alignment := taRightJustify;
    RightEdge := L.Left + L.Width;
    W := L.Width;
    L.Caption := 'x';
    Check(L.Width < W, 'shorter caption: narrower');
    CheckEqI(RightEdge, L.Left + L.Width, 'taRightJustify keeps the right edge');
    L.Caption := 'A much wider caption than before';
    CheckEqI(RightEdge, L.Left + L.Width, 'taRightJustify keeps the right edge when growing');
    L.Alignment := taCenter;
    W := L.Left;
    L.Caption := 'x';
    CheckEqI(W, L.Left, 'taCenter: Left stays');
    L.Alignment := taLeftJustify;
    L.Left := 10;

    Section('aligned label (UIF-256)');
    T := TLabel.Create(F);
    T.Align := alTop;
    T.Parent := F;
    T.Caption := 'Top';
    CheckEqI(F.ClientWidth, T.Width, 'alTop: width from the parent');
    CheckEqI(Measure(T.Font, 'Top', False, True, 0).cy, T.Height, 'alTop: height measured');
    T.Font.Height := -40;
    CheckEqI(F.ClientWidth, T.Width, 'alTop after a font change: width from the parent');
    CheckEqI(Measure(T.Font, 'Top', False, True, 0).cy, T.Height, 'alTop after a font change: height measured');
    T.Free;

    Section('static text');
    S := TStaticText.Create(F);
    CheckEqS('65x17', CtlSize(S), 'a new static text keeps the default size');
    S.Left := 10;
    S.Top := 200;
    S.Parent := F;
    S.Caption := 'Static';
    CheckEqS(MeasureStr(S, S.Font, 'Static', False, True, 0), CtlSize(S), 'sbsNone: measured caption');
    S.BorderStyle := sbsSingle;
    CheckEqS(MeasureStr(S, S.Font, 'Static', False, True, 2), CtlSize(S), 'sbsSingle: +2 px per side');
    S.BorderStyle := sbsSunken;
    CheckEqS(MeasureStr(S, S.Font, 'Static', False, True, 4), CtlSize(S), 'sbsSunken: +4 px per side');
    S.BorderStyle := sbsNone;
    S.HandleNeeded;
    S.Caption := '&&Two words';
    CheckEqS(MeasureStr(S, S.Font, '&&Two words', False, True, 0), CtlSize(S), 'caption with a window');
    S.ShowAccelChar := False;
    CheckEqS(MeasureStr(S, S.Font, '&&Two words', False, False, 0), CtlSize(S), 'ShowAccelChar False');
    S.ShowAccelChar := True;
    S.Font.Height := -28;
    CheckEqS(MeasureStr(S, S.Font, '&&Two words', False, True, 0), CtlSize(S), 'Font change');
    S.Alignment := taRightJustify;
    RightEdge := S.Left + S.Width;
    S.Caption := 'x';
    CheckEqI(RightEdge, S.Left + S.Width, 'taRightJustify keeps the right edge');
    S.Alignment := taLeftJustify;
    S.AutoSize := False;
    S.SetBounds(10, 200, 7, 7);
    S.Caption := 'Not measured';
    CheckEqS('10,200,7,7', BoundsStr(S), 'AutoSize False: bounds kept');
    S.AutoSize := True;
    CheckEqS(MeasureStr(S, S.Font, 'Not measured', False, True, 0), CtlSize(S), 'AutoSize True: measured');

    Section('no measurement while scaling');
    L.Caption := 'Scale me';
    L.Font.Height := -13;
    S.Caption := 'Scale me';
    Before := Format('%d,%d,%d,%d', [LayoutMulDiv(L.Left, 3, 2), LayoutMulDiv(L.Top, 3, 2),
      LayoutMulDiv(L.Left + L.Width, 3, 2) - LayoutMulDiv(L.Left, 3, 2),
      LayoutMulDiv(L.Top + L.Height, 3, 2) - LayoutMulDiv(L.Top, 3, 2)]);
    F.ScaleBy(3, 2);
    CheckEqI(LayoutMulDiv(-13, 3, 2), L.Font.Height, 'own font scaled');
    CheckEqS(Before, BoundsStr(L), 'label: the bounds of the scaling rule, not measured');
    Before := Format('%d,%d', [LayoutMulDiv(S.Left + S.Width, 2, 3) - LayoutMulDiv(S.Left, 2, 3),
      LayoutMulDiv(S.Top + S.Height, 2, 3) - LayoutMulDiv(S.Top, 2, 3)]);
    F.ScaleBy(2, 3);
    CheckEqS(Before, Format('%d,%d', [S.Width, S.Height]), 'static text: the bounds of the scaling rule');

    Section('dump mode (UIF-263 b)');
    Img := TImage.Create(F);
    Img.SetBounds(300, 10, 5, 5);
    Img.Parent := F;
    Img.AutoSize := True;
    CheckEqS('300,10,5,5', BoundsStr(Img), 'image: an empty picture keeps the bounds');
    Before := BoundsStr(L);
    LayoutDumpMode := True;
    try
      L.Caption := 'A caption change in dump mode';
      CheckEqS(Before, BoundsStr(L), 'dump mode: caption change does not measure');
      L.Font.Height := -40;
      CheckEqS(Before, BoundsStr(L), 'dump mode: font change does not measure');
      L.AutoSize := False;
      L.AutoSize := True;
      CheckEqS(Before, BoundsStr(L), 'dump mode: AutoSize True does not measure');
      L.WordWrap := True;
      L.WordWrap := False;
      CheckEqS(Before, BoundsStr(L), 'dump mode: WordWrap does not measure');
      Before := BoundsStr(S);
      S.Caption := 'Static text in dump mode';
      CheckEqS(Before, BoundsStr(S), 'dump mode: static text does not measure');
      S.BorderStyle := sbsSunken;
      CheckEqS(Before, BoundsStr(S), 'dump mode: static text BorderStyle does not measure');
      S.Font.Height := -33;
      CheckEqS(Before, BoundsStr(S), 'dump mode: static text font change does not measure');
      Img.Picture.Bitmap.SetSize(40, 30);
      CheckEqS('300,10,5,5', BoundsStr(Img), 'dump mode: a picture change does not size the image');
    finally
      LayoutDumpMode := False;
    end;
    Img.Picture.Bitmap.SetSize(41, 31);
    CheckEqS('300,10,41,31', BoundsStr(Img), 'image: measuring again afterwards');
    L.Caption := 'After dump mode';
    CheckEqS(MeasureStr(L, L.Font, 'After dump mode', False, True, 0), CtlSize(L), 'measuring again afterwards');
  finally
    F.Free;
  end;

  Section('stored bounds win (UIF-260)');
  AF := TAutoForm.Create(nil);
  try
    CheckEqS('8,8,20,40', BoundsStr(AF.Stored), 'label: stored bounds');
    CheckEqB(True, AF.Stored.AutoSize, 'label: AutoSize True');
    CheckEqI(-20, AF.Stored.Font.Height, 'label: own font loaded');
    CheckEqS('100,60,50,30', BoundsStr(AF.Right), 'right-justified word-wrapped label: stored bounds');
    CheckEqS('8,120,30,50', BoundsStr(AF.Boxed), 'static text: stored bounds');
    CheckEqB(True, AF.Boxed.AutoSize, 'static text: AutoSize True');
    AF.HandleNeeded;
    AF.Boxed.HandleNeeded;
    Application.ProcessMessages;
    CheckEqS('8,8,20,40', BoundsStr(AF.Stored), 'label: stored bounds with windows');
    CheckEqS('8,120,30,50', BoundsStr(AF.Boxed), 'static text: stored bounds with windows');
    AF.Stored.Caption := 'Now';
    CheckEqS(MeasureStr(AF.Stored, AF.Stored.Font, 'Now', False, True, 0), CtlSize(AF.Stored),
      'a change after loading measures');
    AF.Right.Caption := 'Wrapped again';
    CheckEqI(50, AF.Right.Width, 'word-wrapped: width stays after loading');
    CheckEqI(150, AF.Right.Left + AF.Right.Width, 'word-wrapped: right edge');
    CheckEqS(MeasureStr(AF.Right, AF.Right.Font, 'Wrapped again', True, True, 0), CtlSize(AF.Right),
      'word-wrapped: height measured');
    AF.Boxed.Caption := 'Box';
    CheckEqS(MeasureStr(AF.Boxed, AF.Boxed.Font, 'Box', False, True, 2), CtlSize(AF.Boxed),
      'static text: a change after loading measures');
  finally
    AF.Free;
  end;

  Section('dump mode after loading (UIF-263 b: an OnCreate handler or a corpus set step)');
  LayoutDumpMode := True;
  AF := nil;
  try
    AF := TAutoForm.Create(nil);
    CheckEqS('8,8,20,40', BoundsStr(AF.Stored), 'loaded in dump mode: label stored bounds');
    AF.Stored.Caption := 'Now';
    CheckEqS('8,8,20,40', BoundsStr(AF.Stored), 'loaded in dump mode: a caption change keeps the label bounds');
    AF.Right.Caption := 'Wrapped again';
    CheckEqS('100,60,50,30', BoundsStr(AF.Right), 'loaded in dump mode: word-wrapped label keeps its bounds');
    AF.Boxed.Caption := 'Box';
    CheckEqS('8,120,30,50', BoundsStr(AF.Boxed), 'loaded in dump mode: static text keeps its bounds');
  finally
    LayoutDumpMode := False;
    AF.Free;
    Bmp.Free;
  end;
end;

begin
  Run;
  Done;
end.
