// covers: UIF-680, UIF-681
// needs: desktop
program Ctl_Panel;
{ U-060, controls-standard.md section 8: TPanel defaults (185 x 41, raised outer bevel, centred caption,
  ParentColor False); window of our own class PN.TPanel, WS_BORDER for BorderStyle = bsSingle (recreated, caption
  kept) with the formula client size of layout.md UIF-215 equal to Win32's; align rect of UIF-218 (E25 a and b,
  bvSpace, BevelWidth, Padding); bevel and border changes realign (UIF-681); BorderWidth scaled, BevelWidth not;
  Paint (UIF-680) captured with WM_PRINTCLIENT: bevel colours and thickness, the BorderWidth gap, caption placement
  by Alignment / VerticalAlignment, ShowCaption, clGrayText when disabled; events OnClick and OnResize; streaming of
  every published property kind; 100 instances with handles leak no GUI objects. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit;

type
  TBox = class(TCustomControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Resized(Sender: TObject);
  end;

  TPanelForm = class(TForm)
  published
    Outer: TPanel;
    Body: TPanel;
    Plain: TPanel;
    procedure OuterClick(Sender: TObject);
  end;


{$FORM Ctl_Panel.form}

var
  Log: TLog;
  Bmp: TBitmap;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 10, 10);
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TLog.Resized(Sender: TObject);
begin
  Text := Text + 'resize;';
end;

procedure TPanelForm.OuterClick(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed click;';
end;

function ClassOf(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := GetClassNameW(H, @Buf[0], 128);
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function NativeText(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 128, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function HasBorder(H: HWND): Boolean;
begin
  Result := (GetWindowLongPtrW(H, GWL_STYLE) and WS_BORDER) <> 0;
end;

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

procedure Capture(C: TWinControl);
begin
  Bmp.Canvas.Brush.Color := clWhite;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(C.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

function Pixel(X, Y: Integer): TColor;
begin
  Result := Bmp.Canvas.Pixels[X, Y];
end;

function At(X, Y: Integer; C: TColor): Boolean;
begin
  Result := Pixel(X, Y) = ColorToRGB(C);
end;

{ the number of pixels of colour C inside (X1, Y1) .. (X2 - 1, Y2 - 1) }
function Count(C: TColor; X1, Y1, X2, Y2: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if Pixel(X, Y) = C then
        Inc(Result);
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Run;
var
  F: TForm;
  P, P2: TPanel;
  C: TBox;
  SF: TPanelForm;
  H, H2: HWND;
  R: TRect;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    F.Color := clWhite;
    P := TPanel.Create(F);

    Section('defaults');
    CheckEqI(185, P.Width, 'Width 185');
    CheckEqI(41, P.Height, 'Height 41');
    CheckEqB(True, P.Alignment = taCenter, 'Alignment taCenter');
    CheckEqB(True, P.BevelInner = bvNone, 'BevelInner bvNone');
    CheckEqB(True, P.BevelOuter = bvRaised, 'BevelOuter bvRaised');
    CheckEqI(1, P.BevelWidth, 'BevelWidth 1');
    CheckEqB(True, P.BorderStyle = bsNone, 'BorderStyle bsNone');
    CheckEqI(0, P.BorderWidth, 'BorderWidth 0');
    CheckEqS('', P.Caption, 'Caption empty');
    CheckEqB(True, P.Color = clBtnFace, 'Color clBtnFace');
    CheckEqB(False, P.ParentColor, 'ParentColor False');
    CheckEqB(True, P.ParentFont, 'ParentFont');
    CheckEqB(True, P.ShowCaption, 'ShowCaption');
    CheckEqI(-1, P.TabOrder, 'TabOrder -1');
    CheckEqB(False, P.TabStop, 'TabStop');
    CheckEqB(True, P.VerticalAlignment = taVerticalCenter, 'VerticalAlignment taVerticalCenter');
    CheckEqI(0, P.Padding.Left + P.Padding.Top + P.Padding.Right + P.Padding.Bottom, 'Padding 0');
    CheckEqB(True, csAcceptsControls in P.ControlStyle, 'csAcceptsControls');
    Check(GetClass('TPanel') <> nil, 'registered');
    P.Parent := F;
    CheckEqB(True, P.Color = clBtnFace, 'parenting keeps Color');

    Section('handle-less state, window');
    P.Caption := 'Early';
    P.Enabled := False;
    P.BorderStyle := bsSingle;
    H := P.Handle;
    CheckEqS('PN.TPanel', ClassOf(H), 'own window class');
    CheckEqB(True, HasBorder(H), 'bsSingle before the handle: WS_BORDER');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_STYLE) and WS_CLIPCHILDREN) <> 0, 'WS_CLIPCHILDREN');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_EXSTYLE) and WS_EX_CONTROLPARENT) <> 0, 'WS_EX_CONTROLPARENT');
    CheckEqS('Early', NativeText(H), 'caption pushed to the window');
    CheckEqB(False, IsWindowEnabled(H), 'disabled pushed to the window');
    P.Enabled := True;
    P.BorderStyle := bsNone;
    H2 := P.Handle;
    Check(H2 <> H, 'BorderStyle recreates the window');
    CheckEqB(False, HasBorder(H2), 'bsNone: no WS_BORDER');
    CheckEqS('Early', NativeText(H2), 'caption kept across the re-creation');
    P.BorderStyle := bsSingle;
    Check(P.Handle <> H2, 'bsSingle recreates the window');
    CheckEqB(True, HasBorder(P.Handle), 'bsSingle: WS_BORDER');

    Section('client size (layout.md UIF-215, UIF-216)');
    CheckEqI(183, P.ClientWidth, 'bsSingle: ClientWidth = Width - 2');
    CheckEqI(39, P.ClientHeight, 'bsSingle: ClientHeight = Height - 2');
    Windows.GetClientRect(P.Handle, R);
    CheckEqI(P.ClientWidth, R.Right, 'formula client width = Win32');
    CheckEqI(P.ClientHeight, R.Bottom, 'formula client height = Win32');
    P.SetBounds(0, 0, 1, 1);
    CheckEqI(0, P.ClientWidth, 'client width clamped to 0');
    CheckEqI(0, P.ClientHeight, 'client height clamped to 0');
    P.BorderStyle := bsNone;
    P.SetBounds(0, 0, 185, 41);
    CheckEqI(185, P.ClientWidth, 'bsNone: ClientWidth = Width');
    Windows.GetClientRect(P.Handle, R);
    CheckEqI(185, R.Right, 'bsNone: Win32 agrees');

    Section('native -> object');
    SetWindowTextW(P.Handle, 'Native');
    CheckEqS('Native', P.Caption, 'Caption reads the window text');

    Section('events');
    P.OnClick := Log.Clicked;
    P.OnResize := Log.Resized;
    Log.Text := '';
    SendMessageW(P.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(5, 5));
    SendMessageW(P.Handle, WM_LBUTTONUP, 0, MakeLParam(5, 5));
    CheckEqS('click;', Log.Text, 'OnClick on a click');
    Log.Text := '';
    P.Height := 50;
    CheckEqS('resize;', Log.Text, 'OnResize once on a size change');
    Log.Text := '';
    P.BevelInner := bvLowered;
    CheckEqS('', Log.Text, 'no OnResize on a bevel change');
    P.BevelInner := bvNone;
    P.OnResize := nil;

    Section('align rect (layout.md UIF-218, E25, UIF-681)');
    P2 := TPanel.Create(nil);
    try
      P2.SetBounds(0, 0, 200, 100);
      C := TBox.Create(P2);
      C.Align := alClient;
      C.Parent := P2;
      CheckEqS('1,1,198,98', BoundsText(C), 'E25 a: raised outer bevel');
      P2.BevelInner := bvLowered;
      CheckEqS('2,2,196,96', BoundsText(C), 'BevelInner change realigns');
      P2.BorderWidth := 3;
      CheckEqS('5,5,190,90', BoundsText(C), 'E25 b: inset 5');
      P2.BevelWidth := 2;
      CheckEqS('7,7,186,86', BoundsText(C), 'BevelWidth counts per bevel');
      P2.BevelOuter := bvNone;
      CheckEqS('5,5,190,90', BoundsText(C), 'bvNone: no thickness');
      P2.BevelInner := bvSpace;
      CheckEqS('5,5,190,90', BoundsText(C), 'bvSpace: same thickness');
      P2.BevelInner := bvNone;
      P2.BevelWidth := 1;
      P2.BorderWidth := 0;
      CheckEqS('0,0,200,100', BoundsText(C), 'no bevels: whole client');
      P2.Padding.Left := 4;
      P2.Padding.Top := 6;
      CheckEqS('4,6,196,94', BoundsText(C), 'Padding insets the align rect');
      P2.Padding.Left := 0;
      P2.Padding.Top := 0;
      P2.BevelOuter := bvRaised;
      P2.BorderStyle := bsSingle;
      CheckEqS('1,1,196,96', BoundsText(C), 'bsSingle: client 198 x 98, then the bevel');
      P2.BorderStyle := bsNone;
      P2.BorderWidth := 3;
      P2.ScaleBy(2, 1);
      CheckEqI(6, P2.BorderWidth, 'BorderWidth scaled');
      CheckEqI(1, P2.BevelWidth, 'BevelWidth not scaled');
      CheckEqS('7,7,386,186', BoundsText(C), 'scaled panel realigned');
    finally
      P2.Free;
    end;

    Section('Paint (UIF-680)');
    P.Caption := '';
    P.SetBounds(0, 0, 100, 60);
    P.Color := clRed;
    Capture(P);
    CheckEqB(True, At(50, 0, clBtnHighlight), 'raised: top clBtnHighlight');
    CheckEqB(True, At(0, 30, clBtnHighlight), 'raised: left clBtnHighlight');
    CheckEqB(True, At(50, 59, clBtnShadow), 'raised: bottom clBtnShadow');
    CheckEqB(True, At(99, 30, clBtnShadow), 'raised: right clBtnShadow');
    CheckEqB(True, At(50, 1, clRed), 'one pixel thick, filled with Color');
    CheckEqB(True, At(50, 30, clRed), 'interior Color');
    P.BevelOuter := bvLowered;
    Capture(P);
    CheckEqB(True, At(50, 0, clBtnShadow), 'lowered: top clBtnShadow');
    CheckEqB(True, At(50, 59, clBtnHighlight), 'lowered: bottom clBtnHighlight');
    P.BevelOuter := bvSpace;
    Capture(P);
    CheckEqB(True, At(50, 0, clRed), 'bvSpace: drawn in Color');
    P.BevelOuter := bvNone;
    Capture(P);
    CheckEqB(True, At(50, 0, clRed), 'bvNone: nothing drawn');
    P.BevelOuter := bvRaised;
    P.BevelWidth := 2;
    Capture(P);
    CheckEqB(True, At(50, 1, clBtnHighlight), 'BevelWidth 2: two lines');
    CheckEqB(True, At(50, 2, clRed), 'BevelWidth 2: then Color');
    P.BevelWidth := 1;
    P.BorderWidth := 3;
    P.BevelInner := bvLowered;
    Capture(P);
    CheckEqB(True, At(50, 0, clBtnHighlight), 'outer bevel first');
    CheckEqB(True, At(50, 1, clRed) and At(50, 3, clRed), 'BorderWidth gap in Color');
    CheckEqB(True, At(50, 4, clBtnShadow), 'inner lowered bevel: top clBtnShadow');
    CheckEqB(True, At(50, 55, clBtnHighlight), 'inner lowered bevel: bottom clBtnHighlight');
    CheckEqB(True, At(50, 5, clRed), 'inside the inner bevel: Color');
    P.BorderWidth := 0;
    P.BevelInner := bvNone;
    P.ParentFont := False;
    P.Font.Height := -12;
    P.Font.Color := clBlack;
    P.Caption := 'MM';
    Capture(P);
    Check(Count(clBlack, 0, 0, 100, 60) > 0, 'caption drawn');
    CheckEqI(0, Count(clBlack, 0, 0, 25, 60) + Count(clBlack, 75, 0, 100, 60), 'taCenter: left and right quarters empty');
    CheckEqI(0, Count(clBlack, 0, 0, 100, 15) + Count(clBlack, 0, 45, 100, 60), 'taVerticalCenter: top and bottom empty');
    P.Alignment := taLeftJustify;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 1, 60), 'taLeftJustify: not over the bevel');
    CheckEqI(0, Count(clBlack, 50, 0, 100, 60), 'taLeftJustify: right half empty');
    Check(Count(clBlack, 1, 0, 50, 60) > 0, 'taLeftJustify: text in the left half');
    P.Alignment := taRightJustify;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 50, 60), 'taRightJustify: left half empty');
    P.Alignment := taCenter;
    P.VerticalAlignment := taAlignTop;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 30, 100, 60), 'taAlignTop: lower half empty');
    P.VerticalAlignment := taAlignBottom;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 100, 30), 'taAlignBottom: upper half empty');
    P.VerticalAlignment := taVerticalCenter;
    P.Padding.Left := 60;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 60, 60), 'caption inside the align rect (Padding)');
    P.Padding.Left := 0;
    P.ShowCaption := False;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 100, 60), 'ShowCaption False: no caption');
    P.ShowCaption := True;
    P.Enabled := False;
    Capture(P);
    CheckEqI(0, Count(clBlack, 0, 0, 100, 60), 'disabled: no text in the font colour');
    Check(Count(clGrayText, 1, 1, 99, 59) > 0, 'disabled: clGrayText');
    P.Enabled := True;

    Section('streaming');
    Log.Text := '';
    SF := TPanelForm.Create(nil);
    try
      CheckEqB(True, SF.Outer.Alignment = taLeftJustify, 'Alignment');
      CheckEqB(True, SF.Outer.BevelInner = bvLowered, 'BevelInner');
      CheckEqB(True, SF.Outer.BevelOuter = bvNone, 'BevelOuter');
      CheckEqI(2, SF.Outer.BevelWidth, 'BevelWidth');
      CheckEqB(True, SF.Outer.BorderStyle = bsSingle, 'BorderStyle');
      CheckEqI(4, SF.Outer.BorderWidth, 'BorderWidth');
      CheckEqS('Streamed', SF.Outer.Caption, 'Caption');
      CheckEqB(True, SF.Outer.Color = clLime, 'Color');
      CheckEqB(False, SF.Outer.ParentFont, 'ParentFont False');
      CheckEqI(-15, SF.Outer.Font.Height, 'Font.Height');
      CheckEqI(3, SF.Outer.Padding.Top, 'Padding.Top');
      CheckEqB(False, SF.Outer.ShowCaption, 'ShowCaption');
      CheckEqB(True, SF.Outer.TabStop, 'TabStop');
      CheckEqB(True, SF.Outer.VerticalAlignment = taAlignBottom, 'VerticalAlignment');
      CheckEqS('10,20,200,100', BoundsText(SF.Outer), 'bounds');
      CheckEqB(True, SF.Body.Parent = SF.Outer, 'child panel parented');
      CheckEqS('6,9,186,83', BoundsText(SF.Body), 'alClient child: border, bevel, BorderWidth, Padding');
      CheckEqB(True, SF.Plain.ParentColor, 'ParentColor True');
      CheckEqB(True, SF.Plain.Color = SF.Color, 'ParentColor: the form''s Color');
      Check(Assigned(SF.Outer.OnClick), 'OnClick bound');
      SF.Outer.OnClick(SF.Outer);
      CheckEqS('streamed click;', Log.Text, 'OnClick handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    P.Free;
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      P := TPanel.Create(nil);
      P.Caption := 'Leak';
      P.Parent := F;
      P.HandleNeeded;
      P.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
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
