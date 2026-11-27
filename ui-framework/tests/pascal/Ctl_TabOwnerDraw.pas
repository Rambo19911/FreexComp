// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
// covers: UIF-1513, UIF-1514, UIF-1515
// phase: 8
// needs: desktop
// opc: --app-type gui
program Ctl_TabOwnerDraw;
{ U-174, phase8/tab-control.md section 6: owner-drawn tabs on TTabControl and TPageControl. OwnerDraw (default
  False, published with OnDrawTab on both classes) sets TCS_OWNERDRAWFIXED through a recreation that keeps the tabs;
  the parent's WM_DRAWITEM (ODT_TAB) reaches the control as CN_DRAWITEM: DrawTab -> OnDrawTab with the visible-tab
  index, rcItem unchanged and Active from ODS_SELECTED, Canvas bound to the item DC only during the call, result 1,
  the DC's state restored (UIF-1513). Card acceptance: after InvalidateRect + UpdateWindow on a window shown with
  SW_SHOWNOACTIVATE, OnDrawTab is called exactly once per visible tab (3 tabs single-line, 6 tabs in 2 rows) with
  Active only for the selected tab and a Rect between TabRect(i) shrunk by 2 and grown by 2. A handler filling Rect
  red shows in a WM_PRINTCLIENT capture into a memory bitmap; without a handler the default content shows the
  caption in Font.Color on the control's Color and the image of the tab (UIF-1514). 100 captures keep the GDI object
  count within 2 (UIF-1515). }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, Forms, ImgList, CommCtrl, ComCtrls,
  TestKit;

const
  MaxTabs = 8;

type
  TTabAccess = class(TCustomTabControl);

  { records the WM_DRAWITEM messages the tab controls send to the form }
  TProbeForm = class(TForm)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    LastDC: HDC;
    Items: Integer;
    ItemRects: array[0..MaxTabs - 1] of TRect;
  end;

  TDrawLog = class
  public
    Form: TProbeForm;
    FillRed: Boolean;
    Calls: array[0..MaxTabs - 1] of Integer;
    Active: array[0..MaxTabs - 1] of Boolean;
    Rects: array[0..MaxTabs - 1] of TRect;
    Bad: Integer;                                            { calls with an index out of range }
    DCWrong: Integer;                                        { calls whose Canvas is not the item DC }
    procedure Reset;
    procedure DrawTab(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean);
  end;

var
  F: TProbeForm;
  Log: TDrawLog;
  Bmp: TBitmap;

procedure TProbeForm.WndProc(var Message: TMessage);
var
  D: PDrawItemStruct;
begin
  if (Message.Msg = WM_DRAWITEM) and (Message.LParam <> 0) then
  begin
    D := PDrawItemStruct(Message.LParam);
    if D^.CtlType = ODT_TAB then
    begin
      LastDC := D^.hDC;
      Inc(Items);
      if D^.itemID < MaxTabs then
        ItemRects[D^.itemID] := D^.rcItem;
    end;
  end;
  inherited WndProc(Message);
end;

procedure TDrawLog.Reset;
var
  I: Integer;
begin
  for I := 0 to MaxTabs - 1 do
  begin
    Calls[I] := 0;
    Active[I] := False;
    Rects[I] := Rect(0, 0, 0, 0);
  end;
  Bad := 0;
  DCWrong := 0;
end;

procedure TDrawLog.DrawTab(Control: TCustomTabControl; TabIndex: Integer; const Rect: TRect; Active: Boolean);
begin
  if (TabIndex < 0) or (TabIndex >= MaxTabs) then
    Inc(Bad)
  else
  begin
    Inc(Calls[TabIndex]);
    Self.Active[TabIndex] := Active;
    Rects[TabIndex] := Rect;
  end;
  if not Control.Canvas.HandleAllocated or (Control.Canvas.Handle <> Form.LastDC) then
    Inc(DCWrong);
  if FillRed then
  begin
    Control.Canvas.Brush.Color := clRed;
    Control.Canvas.FillRect(Rect);
  end;
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

function RectText(const R: TRect): string;
begin
  Result := Format('%d,%d,%d,%d', [R.Left, R.Top, R.Right, R.Bottom]);
end;

function StyleOf(T: TWinControl): NativeInt;
begin
  Result := NativeInt(GetWindowLongPtrW(T.Handle, GWL_STYLE));
end;

procedure Capture(T: TWinControl);
begin
  Bmp.SetSize(T.Width, T.Height);
  Bmp.Canvas.Brush.Color := clWhite;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(T.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

function Pixel(X, Y: Integer): TColor;
begin
  Result := Bmp.Canvas.Pixels[X, Y];
end;

{ the number of pixels of colour C inside R }
function Count(C: TColor; const R: TRect): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Pixel(X, Y) = C then
        Inc(Result);
end;

function GdiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS));
end;

{ the card's acceptance check: one repaint, OnDrawTab once per visible tab }
procedure CheckRepaint(const Name: string; T: TCustomTabControl; N: Integer);
var
  I, Once, Selected, Contained: Integer;
  R: TRect;
begin
  Log.Reset;
  InvalidateRect(T.Handle, nil, True);
  UpdateWindow(T.Handle);
  Selected := TTabAccess(T).TabIndex;
  Once := 0;
  Contained := 0;
  for I := 0 to N - 1 do
  begin
    if Log.Calls[I] = 1 then
      Inc(Once);
    R := T.TabRect(I);
    if Inside(Inflated(R, -2), Log.Rects[I]) and Inside(Log.Rects[I], Inflated(R, 2)) then
      Inc(Contained)
    else
      WriteLn('# ', Name, ' tab ', I, ': Rect ', RectText(Log.Rects[I]), ' TabRect ', RectText(R));
    CheckEqB(I = Selected, Log.Active[I], Name + ': Active of tab ' + IntToStr(I));
  end;
  for I := N to MaxTabs - 1 do
    Inc(Log.Bad, Log.Calls[I]);
  CheckEqI(N, Once, Name + ': every visible tab drawn exactly once');
  CheckEqI(0, Log.Bad, Name + ': no other tab index');
  CheckEqI(N, Contained, Name + ': Rect within TabRect -2 .. +2');
  CheckEqI(0, Log.DCWrong, Name + ': Canvas.Handle is the item DC inside the call');
  CheckEqB(False, T.Canvas.HandleAllocated, Name + ': Canvas not allocated after the paint');
end;

procedure CheckRedCapture(const Name: string; T: TCustomTabControl; N: Integer);
var
  I, Red: Integer;
  R: TRect;
begin
  Log.Reset;
  Log.FillRed := True;
  Capture(T);
  Log.FillRed := False;
  Red := 0;
  for I := 0 to N - 1 do
  begin
    R := T.TabRect(I);
    if Pixel((R.Left + R.Right) div 2, (R.Top + R.Bottom) div 2) = clRed then
      Inc(Red);
  end;
  CheckEqI(N, Red, Name + ': the red fill of every tab in the WM_PRINTCLIENT capture');
  CheckEqI(0, Log.DCWrong, Name + ': Canvas.Handle is the capture''s item DC');
  CheckEqB(False, T.Canvas.HandleAllocated, Name + ': Canvas not allocated after the capture');
end;

procedure Run;
var
  T, D: TTabControl;
  PC, PI: TPageControl;
  S: TTabSheet;
  IL: TImageList;
  B: TBitmap;
  I, N, Before, Px, Ok, Back, Text, Image: Integer;
  Old: HWND;
  DIS: TDrawItemStruct;
  DC: HDC;
  OldFont, OldBrush: HGDIOBJ;
  OldText: COLORREF;
  OldMode: Integer;
  R: TRect;
begin
  Application.Initialize;
  Log := TDrawLog.Create;
  Bmp := TBitmap.Create;
  F := TProbeForm.CreateNew(nil);
  Log.Form := F;
  try
    F.SetBounds(0, 0, 640, 480);
    F.HandleNeeded;

    Section('defaults and the native style (UIF-1513)');
    T := TTabControl.Create(F);
    T.Font.Height := -12;
    T.SetBounds(8, 8, 289, 193);
    T.Tabs.Add('One');
    T.Tabs.Add('Two');
    T.Tabs.Add('Three');
    CheckEqB(False, T.OwnerDraw, 'TTabControl.OwnerDraw default False');
    CheckEqB(True, IsPublishedProp(TTabControl, 'OwnerDraw'), 'TTabControl: OwnerDraw published');
    CheckEqB(True, IsPublishedProp(TTabControl, 'OnDrawTab'), 'TTabControl: OnDrawTab published');
    CheckEqB(True, IsPublishedProp(TPageControl, 'OwnerDraw'), 'TPageControl: OwnerDraw published');
    CheckEqB(True, IsPublishedProp(TPageControl, 'OnDrawTab'), 'TPageControl: OnDrawTab published');
    CheckEqI(0, GetPropInfo(TTabControl, 'OwnerDraw')^.Default, 'OwnerDraw: default False (not written)');
    Check(T.Canvas <> nil, 'Canvas exists');
    CheckEqB(False, T.Canvas.HandleAllocated, 'Canvas has no handle');
    T.Parent := F;
    T.HandleNeeded;
    CheckEqB(False, (StyleOf(T) and TCS_OWNERDRAWFIXED) <> 0, 'no TCS_OWNERDRAWFIXED by default');
    Old := T.Handle;
    T.OwnerDraw := True;
    CheckEqB(True, (StyleOf(T) and TCS_OWNERDRAWFIXED) <> 0, 'OwnerDraw = True: TCS_OWNERDRAWFIXED');
    Check(T.Handle <> Old, 'the window was recreated');
    CheckEqI(3, SendMessageW(T.Handle, TCM_GETITEMCOUNT, 0, 0), 'tabs kept through the recreation');
    T.OwnerDraw := False;
    CheckEqB(False, (StyleOf(T) and TCS_OWNERDRAWFIXED) <> 0, 'OwnerDraw = False: style removed');
    T.OwnerDraw := True;
    T.TabIndex := 1;
    T.OnDrawTab := Log.DrawTab;

    Section('the drawing message (UIF-1513)');
    DC := CreateCompatibleDC(0);
    try
      OldFont := GetCurrentObject(DC, OBJ_FONT);
      OldBrush := GetCurrentObject(DC, OBJ_BRUSH);
      OldText := GetTextColor(DC);
      OldMode := GetBkMode(DC);
      FillChar(DIS, SizeOf(DIS), 0);
      DIS.CtlType := ODT_TAB;
      DIS.CtlID := UINT(GetDlgCtrlID(T.Handle));
      DIS.itemID := 2;
      DIS.itemState := ODS_SELECTED;
      DIS.hwndItem := T.Handle;
      DIS.hDC := DC;
      DIS.rcItem := Rect(10, 3, 70, 24);
      Log.Reset;
      F.LastDC := DC;
      CheckEqI(1, F.Perform(WM_DRAWITEM, WPARAM(DIS.CtlID), LPARAM(@DIS)), 'WM_DRAWITEM (ODT_TAB) answered 1');
      CheckEqI(1, Log.Calls[2], 'OnDrawTab called for itemID 2');
      CheckEqB(True, Log.Active[2], 'ODS_SELECTED -> Active');
      CheckEqS('10,3,70,24', RectText(Log.Rects[2]), 'Rect is rcItem unchanged');
      CheckEqI(0, Log.DCWrong, 'Canvas.Handle is the DRAWITEMSTRUCT DC inside the call');
      CheckEqB(False, T.Canvas.HandleAllocated, 'Canvas.Handle = 0 after the call');
      DIS.itemID := 0;
      DIS.itemState := 0;
      Log.Reset;
      Log.FillRed := True;
      F.Perform(WM_DRAWITEM, WPARAM(DIS.CtlID), LPARAM(@DIS));
      Log.FillRed := False;
      CheckEqB(False, Log.Active[0], 'no ODS_SELECTED -> not Active');
      Check(GetCurrentObject(DC, OBJ_FONT) = OldFont, 'the DC''s font restored');
      Check(GetCurrentObject(DC, OBJ_BRUSH) = OldBrush, 'the DC''s brush restored');
      CheckEqI(OldText, GetTextColor(DC), 'the DC''s text colour restored');
      CheckEqI(OldMode, GetBkMode(DC), 'the DC''s background mode restored');
    finally
      DeleteDC(DC);
    end;

    ShowWindow(F.Handle, SW_SHOWNOACTIVATE);
    UpdateWindow(F.Handle);

    Section('card acceptance: once per visible tab (UIF-1513)');
    CheckRepaint('3 tabs single-line', T, 3);
    PC := TPageControl.Create(F);
    PC.Font.Height := -12;
    PC.SetBounds(300, 8, 289, 193);
    PC.MultiLine := True;
    PC.TabWidth := 60;
    for I := 0 to 5 do
    begin
      S := TTabSheet.Create(F);
      S.Caption := 'P' + IntToStr(I);
      S.PageControl := PC;
    end;
    PC.Parent := F;
    PC.OwnerDraw := True;
    PC.OnDrawTab := Log.DrawTab;
    PC.ActivePageIndex := 4;
    PC.HandleNeeded;
    CheckEqI(2, PC.RowCount, 'page control: 6 tabs in 2 rows');
    CheckRepaint('page control 6 tabs, 2 rows', PC, 6);

    Section('a handler''s drawing in a capture (UIF-1513)');
    CheckRedCapture('tab control', T, 3);
    CheckRedCapture('page control', PC, 6);

    Section('default content without a handler (UIF-1514)');
    D := TTabControl.Create(F);
    D.Font.Height := -12;
    D.Font.Color := clBlue;
    D.Font.Quality := fqNonAntialiased;
    D.SetBounds(8, 220, 289, 100);
    D.Tabs.Add('WWW');
    D.Tabs.Add('MMM');
    D.Tabs.Add('HHH');
    D.Parent := F;
    D.OwnerDraw := True;
    D.HandleNeeded;
    CheckEqB(True, ColorToRGB(TTabAccess(D).Color) = ColorToRGB(clBtnFace), 'the control''s Color is clBtnFace');
    for I := 0 to MaxTabs - 1 do
      F.ItemRects[I] := Rect(0, 0, 0, 0);
    F.Items := 0;
    Capture(D);
    CheckEqI(3, F.Items, 'one WM_DRAWITEM per tab in the capture');
    Ok := 0;
    Back := 0;
    for I := 0 to 2 do
    begin
      R := F.ItemRects[I];
      if Count(clBlue, R) > 0 then
        Inc(Ok);
      { measured: with visual styles the native control clips the item to the tab's shape, so the outermost
        corner pixels of Rect show what lies below; 2 pixels inside every corner are ours }
      if (Pixel(R.Left + 2, R.Top + 2) = ColorToRGB(clBtnFace))
        and (Pixel(R.Right - 3, R.Top + 2) = ColorToRGB(clBtnFace))
        and (Pixel(R.Left + 2, R.Bottom - 3) = ColorToRGB(clBtnFace))
        and (Pixel(R.Right - 3, R.Bottom - 3) = ColorToRGB(clBtnFace)) then
        Inc(Back);
    end;
    CheckEqI(3, Ok, 'the caption in Font.Color inside every tab');
    CheckEqI(3, Back, 'Rect filled with the control''s Color (2 pixels inside its corners)');

    IL := TImageList.Create(F);
    B := TBitmap.Create;
    try
      B.SetSize(16, 16);
      B.Canvas.Brush.Color := clLime;
      B.Canvas.FillRect(Rect(0, 0, 16, 16));
      IL.Add(B, nil);
    finally
      B.Free;
    end;
    PI := TPageControl.Create(F);
    PI.Font.Height := -12;
    PI.Font.Color := clBlue;
    PI.Font.Quality := fqNonAntialiased;
    PI.SetBounds(300, 220, 289, 100);
    PI.Images := IL;
    for I := 0 to 1 do
    begin
      S := TTabSheet.Create(F);
      S.Caption := 'WWW';
      S.ImageIndex := 0;
      S.PageControl := PI;
    end;
    PI.Parent := F;
    PI.OwnerDraw := True;
    PI.HandleNeeded;
    Px := LayoutMulDiv(6, F.PixelsPerInch, 96);
    F.Items := 0;
    Capture(PI);
    CheckEqI(2, F.Items, 'page control: one WM_DRAWITEM per tab');
    Image := 0;
    Text := 0;
    for I := 0 to 1 do
    begin
      R := F.ItemRects[I];
      if Pixel(R.Left + Px + 8, (R.Top + R.Bottom) div 2) = clLime then
        Inc(Image);
      if Count(clBlue, Rect(R.Left + Px + 16, R.Top, R.Right, R.Bottom)) > 0 then
        Inc(Text);
    end;
    CheckEqI(2, Image, 'the tab''s image at Rect.Left + Px, vertically centred');
    CheckEqI(2, Text, 'the caption right of the image');

    Section('no GDI growth (UIF-1515)');
    Log.FillRed := True;
    Capture(T);
    Capture(PC);
    Capture(D);
    Capture(PI);
    Before := GdiObjects;
    for N := 1 to 25 do
    begin
      Capture(T);
      Capture(PC);
      Capture(D);
      Capture(PI);
    end;
    Log.FillRed := False;
    Check(Abs(GdiObjects - Before) <= 2, '100 captures: GDI objects within 2');
    CheckEqB(False, T.Canvas.HandleAllocated, 'no Canvas handle after the captures');
  finally
    F.Free;
    Bmp.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
