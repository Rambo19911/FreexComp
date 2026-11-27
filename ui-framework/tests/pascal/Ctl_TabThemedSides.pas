// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1517, UIF-1518
// phase: 8
// needs: desktop
// opc: --app-type gui
program Ctl_TabThemedSides;
{ U-175, phase8/tab-control.md section 7a: with visual styles the framework paints bottom, left and right tabs itself
  from the themed parts of top tabs (the system has no themed look for these positions, UIF-1494). Proof independent
  of the theme (UIF-1518): a reference control with top tabs, drawn by the system, and the three other positions are
  captured with WM_PRINTCLIENT into memory bitmaps in the same process; with empty captions and no image list every
  pixel of a turned strip equals the reference pixel under the position's transform - mirrored vertically (bottom),
  rotated clockwise (right), rotated counter-clockwise with the row start kept at the start (left; for the selected
  tab, whose image is symmetric, that is the plain counter-clockwise rotation). The row of the pane's frame line
  shows the pane fill along the selected tab and the line along the others. Owner drawing lands on the themed
  background, once per tab, also through WM_PAINT; captions are upright (bottom) or rotated (left reads upwards,
  right downwards) with the image at the start of the text; the hot and the disabled state change the tab; 102
  captures keep the GDI object count.
  Without visual styles (IsAppThemed False, read after the first window exists, or no theme data for the tab
  class) the own drawing is not used: the checks named "exactly when visual styles are active" prove it, every
  pixel comparison passes without being made (same expected output) and one line on the error output says so. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ImgList, CommCtrl, ComCtrls, UxTheme, TestKit;

const
  N = 5;                                                     { tabs of every strip }
  Sel = 2;                                                   { the selected tab }
  TabLen = 50;
  Along = 289;                                               { control extent along the strip }
  Across = 193;
  Red = TColor($0000FF);
  Lime = TColor($00FF00);
  Blue = TColor($FF0000);

type
  TTabAccess = class(TCustomTabControl);

  { the owner's drawing: a red square of 4 x 4 pixels in the middle of Rect }
  TDrawLog = class
  public
    Calls: array[0..N - 1] of Integer;
    Active: array[0..N - 1] of Boolean;
    Rects: array[0..N - 1] of TRect;
    Bad: Integer;
    procedure Reset;
    procedure DrawTab(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean);
  end;

var
  F: TForm;
  Themed: Boolean;
  Log: TDrawLog;
  Ref, Bmp, Other: TBitmap;

procedure TDrawLog.Reset;
var
  I: Integer;
begin
  for I := 0 to N - 1 do
  begin
    Calls[I] := 0;
    Active[I] := False;
    Rects[I] := Rect(0, 0, 0, 0);
  end;
  Bad := 0;
end;

procedure TDrawLog.DrawTab(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean);
var
  X, Y: Integer;
begin
  if (TabIndex < 0) or (TabIndex >= N) then
  begin
    Inc(Bad);
    Exit;
  end;
  Inc(Calls[TabIndex]);
  Self.Active[TabIndex] := Active;
  Rects[TabIndex] := Rect;
  X := (Rect.Left + Rect.Right) div 2;
  Y := (Rect.Top + Rect.Bottom) div 2;
  Control.Canvas.Brush.Color := Red;
  Control.Canvas.FillRect(Types.Rect(X - 2, Y - 2, X + 2, Y + 2));
end;

{ a pixel comparison: made with visual styles, passed unseen without them }
procedure CheckT(Cond: Boolean; const Name: string);
begin
  Check(not Themed or Cond, Name);
end;

function RectText(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function Inflated(const R: TRect; D: Integer): TRect;
begin
  Result := Rect(R.Left - D, R.Top - D, R.Right + D, R.Bottom + D);
end;

function Inside(const Inner, Outer: TRect): Boolean;
begin
  Result := (Inner.Left >= Outer.Left) and (Inner.Top >= Outer.Top) and (Inner.Right <= Outer.Right)
    and (Inner.Bottom <= Outer.Bottom);
end;

function InRect(const R: TRect; X, Y: Integer): Boolean;
begin
  Result := (X >= R.Left) and (X < R.Right) and (Y >= R.Top) and (Y < R.Bottom);
end;

procedure Prepare(B: TBitmap; T: TWinControl);
begin
  B.PixelFormat := pf32bit;
  B.SetSize(T.Width, T.Height);
  B.Canvas.Brush.Color := clWhite;
  B.Canvas.FillRect(Rect(0, 0, B.Width, B.Height));
end;

procedure Capture(T: TWinControl; B: TBitmap);
begin
  Prepare(B, T);
  SendMessageW(T.Handle, WM_PRINTCLIENT, WPARAM(B.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

{ what the system control alone draws: the window procedure below the framework's }
procedure CaptureNative(T: TWinControl; B: TBitmap);
begin
  Prepare(B, T);
  CallWindowProcW(TFNWndProc(TTabAccess(T).DefWndProc), T.Handle, WM_PRINTCLIENT, WPARAM(B.Canvas.Handle),
    PRF_CLIENT or PRF_ERASEBKGND);
end;

function Pixel(B: TBitmap; X, Y: Integer): TColor;
begin
  if (X < 0) or (Y < 0) or (X >= B.Width) or (Y >= B.Height) then
    Result := TColor(-1)
  else
    Result := B.Canvas.Pixels[X, Y];
end;

{ the number of pixels of R that differ between A and B, both of one size }
function Differ(A, B: TBitmap; const R: TRect): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Pixel(A, X, Y) <> Pixel(B, X, Y) then
        Inc(Result);
end;

{ the pixel of a strip on Side that shows pixel (U, V) of the reference with top tabs: U runs along the strip, V
  is the depth from the strip-side edge }
function Turned(Side: TTabPosition; U, V: Integer): TPoint;
begin
  case Side of
    tpBottom:
      Result := Point(U, Across - 1 - V);
    tpLeft:
      Result := Point(V, U);
    tpRight:
      Result := Point(Across - 1 - V, U);
  else
    Result := Point(U, V);
  end;
end;

{ the rectangle of a strip on Side that shows the reference rectangle R }
function TurnedRect(Side: TTabPosition; const R: TRect): TRect;
begin
  case Side of
    tpBottom:
      Result := Rect(R.Left, Across - R.Bottom, R.Right, Across - R.Top);
    tpLeft:
      Result := Rect(R.Top, R.Left, R.Bottom, R.Right);
    tpRight:
      Result := Rect(Across - R.Bottom, R.Left, Across - R.Top, R.Right);
  else
    Result := R;
  end;
end;

{ the pixels of the reference region R that B, a capture of a strip on Side, does not show at their turned place;
  pixels inside Skip (strip coordinates) are not compared; a difference is described in a comment line }
function Mismatch(const Name: string; Side: TTabPosition; B: TBitmap; const R, Skip: TRect): Integer;
var
  U, V: Integer;
  P: TPoint;
begin
  Result := 0;
  for V := R.Top to R.Bottom - 1 do
    for U := R.Left to R.Right - 1 do
    begin
      P := Turned(Side, U, V);
      if InRect(Skip, P.X, P.Y) then
        Continue;
      if Pixel(B, P.X, P.Y) <> Pixel(Ref, U, V) then
      begin
        if (Result = 0) and Themed then
          WriteLn('# ', Name, ': first difference at reference ', U, ',', V, ' ', IntToHex(Pixel(Ref, U, V), 6),
            ' strip ', P.X, ',', P.Y, ' ', IntToHex(Pixel(B, P.X, P.Y), 6));
        Inc(Result);
      end;
    end;
  if (Result > 0) and Themed then
    WriteLn('# ', Name, ': ', Result, ' pixels differ in reference ', RectText(R));
end;

{ the same against the reference region rotated counter-clockwise in place (left strip): reference (U, V) is shown
  at (V, R.Left + R.Right - 1 - U) }
function MismatchRotatedLeft(B: TBitmap; const R: TRect): Integer;
var
  U, V: Integer;
begin
  Result := 0;
  for V := R.Top to R.Bottom - 1 do
    for U := R.Left to R.Right - 1 do
      if Pixel(B, V, R.Left + R.Right - 1 - U) <> Pixel(Ref, U, V) then
        Inc(Result);
end;

function NoRect: TRect;
begin
  Result := Rect(0, 0, 0, 0);
end;

function SideName(Side: TTabPosition): string;
begin
  case Side of
    tpBottom:
      Result := 'bottom';
    tpLeft:
      Result := 'left';
    tpRight:
      Result := 'right';
  else
    Result := 'top';
  end;
end;

{ how the reference is turned for Side }
function TurnName(Side: TTabPosition): string;
begin
  case Side of
    tpBottom:
      Result := 'mirrored vertically';
    tpLeft:
      Result := 'rotated counter-clockwise, row start kept';
    tpRight:
      Result := 'rotated clockwise';
  else
    Result := 'unchanged';
  end;
end;

function NewStrip(Side: TTabPosition; X, Y: Integer; const Caption: string): TTabControl;
var
  I: Integer;
begin
  Result := TTabControl.Create(F);
  Result.Font.Height := -12;
  if (Side = tpLeft) or (Side = tpRight) then
    Result.SetBounds(X, Y, Across, Along)
  else
    Result.SetBounds(X, Y, Along, Across);
  Result.TabWidth := TabLen;
  Result.TabPosition := Side;
  for I := 1 to N do
    Result.Tabs.Add(Caption);
  Result.Parent := F;
  Result.TabIndex := Sel;
  Result.HandleNeeded;
end;

function GdiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS));
end;

{ the box of the pixels of colour C inside R; False when there is none }
function ColorBox(B: TBitmap; C: TColor; const R: TRect; out Box: TRect): Boolean;
var
  X, Y: Integer;
begin
  Box := Rect(MaxInt, MaxInt, -1, -1);
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Pixel(B, X, Y) = C then
      begin
        if X < Box.Left then
          Box.Left := X;
        if X + 1 > Box.Right then
          Box.Right := X + 1;
        if Y < Box.Top then
          Box.Top := Y;
        if Y + 1 > Box.Bottom then
          Box.Bottom := Y + 1;
      end;
  Result := Box.Right > 0;
end;

function Count(B: TBitmap; C: TColor; const R: TRect): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Pixel(B, X, Y) = C then
        Inc(Result);
end;

{ UIF-1518: the strip's geometry and pixels against the reference }
procedure CheckStrip(T, Top: TTabControl);
var
  Side: TTabPosition;
  Name: string;
  I, Ok, Line, Fill, Edge: Integer;
  R, S: TRect;
  P: TPoint;
  FillColor: TColor;
begin
  Side := T.TabPosition;
  Name := SideName(Side);
  Ok := 0;
  for I := 0 to N - 1 do
  begin
    R := TurnedRect(Side, Top.TabRect(I));
    S := T.TabRect(I);
    if (R.Left = S.Left) and (R.Top = S.Top) and (R.Right = S.Right) and (R.Bottom = S.Bottom) then
      Inc(Ok)
    else
      WriteLn('# ', Name, ' tab ', I, ': TabRect ', RectText(S), ' turned reference ', RectText(R));
  end;
  CheckEqI(N, Ok, Name + ': every TabRect is the reference''s, ' + TurnName(Side));

  Capture(T, Bmp);
  CaptureNative(T, Other);
  CheckEqB(Themed, Differ(Bmp, Other, Rect(0, 0, Bmp.Width, Bmp.Height)) > 0,
    Name + ': the own drawing is used exactly when visual styles are active');

  CheckT(Mismatch(Name + ' first', Side, Bmp, Top.TabRect(0), NoRect) = 0,
    Name + ': the first tab equals the reference tab ' + TurnName(Side));
  CheckT(Mismatch(Name + ' middle', Side, Bmp, Top.TabRect(1), NoRect)
    + Mismatch(Name + ' middle', Side, Bmp, Top.TabRect(3), NoRect) = 0,
    Name + ': the middle tabs equal the reference tabs ' + TurnName(Side));
  CheckT(Mismatch(Name + ' selected', Side, Bmp, Inflated(Top.TabRect(Sel), 2), NoRect) = 0,
    Name + ': the selected tab, grown by 2, equals the reference tab ' + TurnName(Side));
  CheckT(Mismatch(Name + ' last', Side, Bmp, Top.TabRect(N - 1), NoRect) = 0,
    Name + ': the last tab equals the reference tab ' + TurnName(Side));
  if Side = tpLeft then
    CheckT(MismatchRotatedLeft(Bmp, Inflated(Top.TabRect(Sel), 2)) = 0,
      'left: the selected tab equals the reference tab rotated counter-clockwise');
  CheckT(Mismatch(Name + ' whole', Side, Bmp, Rect(0, 0, Along, Across), NoRect) = 0,
    Name + ': strip and pane equal the whole reference ' + TurnName(Side));

  { the row of the pane's frame line: reference row TabRect.Bottom }
  R := T.DisplayRect;
  FillColor := Pixel(Bmp, (R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2);
  S := Top.TabRect(Sel);
  Fill := 0;
  for I := S.Left - 1 to S.Right do
  begin
    P := Turned(Side, I, S.Bottom);
    if Pixel(Bmp, P.X, P.Y) = FillColor then
      Inc(Fill);
  end;
  CheckT(Fill = S.Right - S.Left + 2, Name + ': no frame line at the selected tab''s inner edge (pane fill)');
  S := Top.TabRect(0);
  Line := 0;
  Edge := 0;
  for I := S.Left to S.Right - 1 do
  begin
    P := Turned(Side, I, S.Bottom);
    if Pixel(Bmp, P.X, P.Y) <> FillColor then
      Inc(Line);
    P := Turned(Side, I, S.Bottom + 1);
    if Pixel(Bmp, P.X, P.Y) = FillColor then
      Inc(Edge);
  end;
  CheckT((Line = S.Right - S.Left) and (Edge = S.Right - S.Left),
    Name + ': the frame line runs along the other tabs, the pane fill behind it');
end;

{ UIF-1517: OnDrawTab once per tab into the capture, its drawing on the themed background }
procedure CheckOwnerDraw(T, Top: TTabControl);
var
  Side: TTabPosition;
  Name: string;
  I, Once, Contained, Squares, Wrong: Integer;
  R, Square: TRect;
begin
  Side := T.TabPosition;
  Name := SideName(Side);
  T.OwnerDraw := True;
  T.OnDrawTab := Log.DrawTab;
  CheckEqI(N, SendMessageW(T.Handle, TCM_GETITEMCOUNT, 0, 0), Name + ': tabs kept through the recreation');
  Log.Reset;
  Capture(T, Bmp);
  Once := 0;
  Contained := 0;
  Squares := 0;
  Wrong := 0;
  for I := 0 to N - 1 do
  begin
    if Log.Calls[I] = 1 then
      Inc(Once);
    R := T.TabRect(I);
    if Inside(Inflated(R, -2), Log.Rects[I]) and Inside(Log.Rects[I], Inflated(R, 2)) then
      Inc(Contained)
    else
      WriteLn('# ', Name, ' tab ', I, ': Rect ', RectText(Log.Rects[I]), ' TabRect ', RectText(R));
    if Log.Active[I] <> (I = Sel) then
      Inc(Wrong);
    Square := Rect((R.Left + R.Right) div 2 - 2, (R.Top + R.Bottom) div 2 - 2, (R.Left + R.Right) div 2 + 2,
      (R.Top + R.Bottom) div 2 + 2);
    if Count(Bmp, Red, Square) = 16 then
      Inc(Squares);
    if I = Sel then
      R := Inflated(Top.TabRect(I), 2)
    else
      R := Top.TabRect(I);
    CheckT(Mismatch(Name + ' owner-drawn tab ' + IntToStr(I), Side, Bmp, R, Square) = 0,
      Name + ': tab ' + IntToStr(I) + ' keeps the themed background around the owner''s drawing');
  end;
  CheckEqI(N, Once, Name + ': OnDrawTab once per tab in a capture');
  CheckEqI(0, Log.Bad, Name + ': no other tab index');
  CheckEqI(N, Contained, Name + ': Rect within TabRect -2 .. +2');
  CheckEqI(0, Wrong, Name + ': Active only for the selected tab');
  CheckT(Squares = N, Name + ': the owner''s drawing is on top in every tab');
  CheckEqB(False, T.Canvas.HandleAllocated, Name + ': Canvas not allocated after the capture');
end;

{ UIF-1517: the caption 'L' in blue and a lime image of 16 x 16 on every tab of a strip on Side. An upright L has
  its stem in the left column and its foot in the bottom row of its box; rotated counter-clockwise the stem is the
  bottom row and the foot the right column; rotated clockwise the stem is the top row and the foot the left column.
  The image stands at the start of the text. }
procedure CheckContent(Side: TTabPosition; X, Y: Integer; Images: TCustomImageList);
var
  T: TTabControl;
  Name: string;
  I, Upright, Turn, Whole, Start: Integer;
  R, L, G: TRect;
  LeftCol, RightCol, TopRow, BottomRow: Integer;
begin
  Name := SideName(Side);
  T := TTabControl.Create(F);
  T.Font.Height := -16;
  T.Font.Color := Blue;
  T.Font.Quality := fqNonAntialiased;
  if (Side = tpLeft) or (Side = tpRight) then
    T.SetBounds(X, Y, 120, 250)
  else
    T.SetBounds(X, Y, 250, 120);
  T.TabPosition := Side;
  T.Images := Images;
  for I := 1 to 3 do
    T.Tabs.Add('L');
  T.Parent := F;
  T.TabIndex := 1;
  T.HandleNeeded;
  CheckEqI(1, T.RowCount, Name + ' with captions and images: one row');
  Capture(T, Bmp);
  Upright := 0;
  Turn := 0;
  Whole := 0;
  Start := 0;
  for I := 0 to 2 do
  begin
    R := T.TabRect(I);
    if Count(Bmp, Lime, R) = 256 then
      Inc(Whole);
    if not ColorBox(Bmp, Blue, R, L) or not ColorBox(Bmp, Lime, R, G) then
      Continue;
    LeftCol := Count(Bmp, Blue, Rect(L.Left, L.Top, L.Left + 1, L.Bottom));
    RightCol := Count(Bmp, Blue, Rect(L.Right - 1, L.Top, L.Right, L.Bottom));
    TopRow := Count(Bmp, Blue, Rect(L.Left, L.Top, L.Right, L.Top + 1));
    BottomRow := Count(Bmp, Blue, Rect(L.Left, L.Bottom - 1, L.Right, L.Bottom));
    if (LeftCol > RightCol) and (BottomRow > TopRow) then
      Inc(Upright);
    case Side of
      tpLeft:
        begin
          if (BottomRow > TopRow) and (RightCol > LeftCol) then
            Inc(Turn);
          if G.Top >= L.Bottom then
            Inc(Start);
        end;
      tpRight:
        begin
          if (TopRow > BottomRow) and (LeftCol > RightCol) then
            Inc(Turn);
          if G.Bottom <= L.Top then
            Inc(Start);
        end;
    else
      if G.Right <= L.Left then
        Inc(Start);
    end;
  end;
  CheckT(Whole = 3, Name + ': the whole image inside every tab');
  case Side of
    tpLeft:
      CheckT((Turn = 3) and (Upright = 0), 'left: captions rotated counter-clockwise (read upwards)');
    tpRight:
      CheckT((Turn = 3) and (Upright = 0), 'right: captions rotated clockwise (read downwards)');
  else
    CheckT(Upright = 3, Name + ': captions upright');
  end;
  CheckT(Start = 3, Name + ': the image at the start of the text');
end;

procedure Run;
var
  Top, Bot, Lft, Rgt: TTabControl;
  IL: TImageList;
  B: TBitmap;
  I, Before, Once: Integer;
  R: TRect;
  Theme: HTHEME;
begin
  Application.Initialize;
  Log := TDrawLog.Create;
  Ref := TBitmap.Create;
  Bmp := TBitmap.Create;
  Other := TBitmap.Create;
  F := TForm.CreateNew(nil);
  try
    F.SetBounds(0, 0, 1020, 660);
    F.HandleNeeded;

    Section('the reference and the three other positions (UIF-1518)');
    Top := NewStrip(tpTop, 8, 8, '');
    Bot := NewStrip(tpBottom, 305, 8, '');
    Lft := NewStrip(tpLeft, 602, 8, '');
    Rgt := NewStrip(tpRight, 803, 8, '');
    { read after the thread's first window exists (RTL-1147); visual styles are active for the tabs when the
      program is themed and the tab class has theme data }
    Themed := False;
    if IsAppThemed then
    begin
      Theme := OpenThemeData(0, 'TAB');
      if Theme <> 0 then
      begin
        Themed := True;
        CloseThemeData(Theme);
      end;
    end;
    if not Themed then
      WriteLn(ErrOutput, 'Ctl_TabThemedSides: visual styles are off - the own drawing is not used, the pixel ',
        'comparisons were not made');
    CheckEqI(1, Top.RowCount + Bot.RowCount + Lft.RowCount + Rgt.RowCount - 3, 'one row of 5 tabs everywhere');
    Capture(Top, Ref);
    CaptureNative(Top, Other);
    CheckEqI(0, Differ(Ref, Other, Rect(0, 0, Along, Across)), 'top tabs are drawn by the system alone');
    R := Top.TabRect(Sel);
    CheckT(Pixel(Ref, R.Left + 5, R.Bottom) = Pixel(Ref, Along div 2, Across div 2),
      'reference: no frame line at the selected tab''s inner edge');
    CheckT(Pixel(Ref, 10, R.Bottom) <> Pixel(Ref, Along div 2, Across div 2),
      'reference: the frame line runs along the other tabs');

    Section('bottom tabs');
    CheckStrip(Bot, Top);
    Section('left tabs');
    CheckStrip(Lft, Top);
    Section('right tabs');
    CheckStrip(Rgt, Top);

    Section('WM_PAINT into a device context (UIF-1517)');
    Capture(Bot, Bmp);
    Prepare(Other, Bot);
    Bot.Perform(WM_PAINT, WPARAM(Other.Canvas.Handle), 0);
    CheckT(Differ(Bmp, Other, Rect(0, 0, Along, Across)) = 0, 'bottom: WM_PAINT draws what WM_PRINTCLIENT draws');
    Capture(Rgt, Bmp);
    Prepare(Other, Rgt);
    Rgt.Perform(WM_PAINT, WPARAM(Other.Canvas.Handle), 0);
    CheckT(Differ(Bmp, Other, Rect(0, 0, Across, Along)) = 0, 'right: WM_PAINT draws what WM_PRINTCLIENT draws');

    Section('hot and disabled tabs (UIF-1517)');
    Capture(Bot, Other);
    R := Bot.TabRect(0);
    Bot.Perform(WM_MOUSEMOVE, 0, MakeLParam(Word((R.Left + R.Right) div 2), Word((R.Top + R.Bottom) div 2)));
    Capture(Bot, Bmp);
    CheckT(Differ(Bmp, Other, R) > 0, 'bottom: the tab under the mouse is drawn hot');
    CheckT(Differ(Bmp, Other, Bot.TabRect(3)) + Differ(Bmp, Other, Inflated(Bot.TabRect(Sel), 2)) = 0,
      'bottom: the other tabs are unchanged');
    Bot.Perform(WM_MOUSELEAVE, 0, 0);
    Capture(Bot, Bmp);
    CheckEqI(0, Differ(Bmp, Other, Rect(0, 0, Along, Across)), 'bottom: back to normal when the mouse leaves');
    R := Bot.TabRect(Sel);
    Bot.Perform(WM_MOUSEMOVE, 0, MakeLParam(Word((R.Left + R.Right) div 2), Word((R.Top + R.Bottom) div 2)));
    Capture(Bot, Bmp);
    CheckEqI(0, Differ(Bmp, Other, Rect(0, 0, Along, Across)),
      'bottom: the selected tab stays selected under the mouse');
    Bot.Perform(WM_MOUSELEAVE, 0, 0);
    Bot.Enabled := False;
    Capture(Bot, Bmp);
    CheckT(Differ(Bmp, Other, Bot.TabRect(0)) > 0, 'bottom: tabs of a disabled control are drawn disabled');
    CheckT(Differ(Bmp, Other, Inflated(Bot.TabRect(Sel), 2)) = 0, 'bottom: its selected tab stays selected');
    Bot.Enabled := True;
    Capture(Bot, Bmp);
    CheckEqI(0, Differ(Bmp, Other, Rect(0, 0, Along, Across)), 'bottom: back to normal when enabled');

    Section('owner drawing on the themed background (UIF-1517)');
    CheckOwnerDraw(Bot, Top);
    CheckOwnerDraw(Lft, Top);

    Section('owner drawing through WM_PAINT (UIF-1517)');
    ShowWindow(F.Handle, SW_SHOWNOACTIVATE);
    UpdateWindow(F.Handle);
    Log.Reset;
    InvalidateRect(Bot.Handle, nil, True);
    UpdateWindow(Bot.Handle);
    Once := 0;
    for I := 0 to N - 1 do
      if Log.Calls[I] = 1 then
        Inc(Once);
    CheckEqI(N, Once, 'bottom: OnDrawTab once per tab in a repaint');
    Log.Reset;
    InvalidateRect(Lft.Handle, nil, True);
    UpdateWindow(Lft.Handle);
    Once := 0;
    for I := 0 to N - 1 do
      if Log.Calls[I] = 1 then
        Inc(Once);
    CheckEqI(N, Once, 'left: OnDrawTab once per tab in a repaint');
    CheckEqB(False, Bot.Canvas.HandleAllocated or Lft.Canvas.HandleAllocated,
      'Canvas not allocated after the repaints');
    ShowWindow(F.Handle, SW_HIDE);

    Section('captions and images (UIF-1517)');
    IL := TImageList.Create(F);
    B := TBitmap.Create;
    try
      B.SetSize(16, 16);
      B.Canvas.Brush.Color := Lime;
      B.Canvas.FillRect(Rect(0, 0, 16, 16));
      for I := 1 to 3 do
        IL.Add(B, nil);
    finally
      B.Free;
    end;
    CheckContent(tpBottom, 8, 310, IL);
    CheckContent(tpLeft, 270, 310, IL);
    CheckContent(tpRight, 400, 310, IL);

    Section('no GDI growth');
    Capture(Bot, Bmp);
    Capture(Lft, Bmp);
    Capture(Rgt, Bmp);
    Before := GdiObjects;
    for I := 1 to 34 do
    begin
      Capture(Bot, Bmp);
      Capture(Lft, Bmp);
      Capture(Rgt, Bmp);
    end;
    Check(Abs(GdiObjects - Before) <= 2, '102 captures: GDI objects within 2');
  finally
    F.Free;
    Other.Free;
    Bmp.Free;
    Ref.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
