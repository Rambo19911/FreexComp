// covers: UIF-670, UIF-671
// needs: desktop
program Ctl_GroupBox;
{ U-059, controls-standard.md section 7: TGroupBox defaults (185 x 105, csAcceptsControls, ParentColor, ParentFont);
  window of our own class PN.TGroupBox with WS_CLIPCHILDREN / WS_EX_CONTROLPARENT; caption pushed and read back
  natively; events OnClick, OnResize, OnEnter, OnExit; align rect of layout.md UIF-218 (E26, Padding, font change
  realigns, scaled constants at the form's DPI); Paint (UIF-670) captured with WM_PRINTCLIENT: etched frame at
  CapH div 2, caption at s(8) over a background strip s(2) wider than the text, full frame without caption,
  clGrayText when disabled; the accelerator selects the first child (UIF-671); streaming of every published
  property kind; 100 instances with handles leak no GUI objects. No property recreates the window. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TFocusBox = class(TCustomControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Resized(Sender: TObject);
    procedure Entered(Sender: TObject);
    procedure Exited(Sender: TObject);
  end;

  TGroupForm = class(TForm)
  published
    Group: TGroupBox;
    Inner: TGroupBox;
    procedure GroupClick(Sender: TObject);
  end;

{$FORM Ctl_GroupBox.form}


var
  Log: TLog;
  Bmp: TBitmap;

constructor TFocusBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  TabStop := True;
  SetBounds(0, 0, 20, 20);
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TLog.Resized(Sender: TObject);
begin
  Text := Text + 'resize;';
end;

procedure TLog.Entered(Sender: TObject);
begin
  Text := Text + 'enter;';
end;

procedure TLog.Exited(Sender: TObject);
begin
  Text := Text + 'exit;';
end;

procedure TGroupForm.GroupClick(Sender: TObject);
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
  G, G2: TGroupBox;
  C, A, B: TFocusBox;
  SF: TGroupForm;
  DPI, S2, S5, S8, CapH, FrameY, TW, TH, I, Before: Integer;
  H: HWND;
  Style: NativeInt;
  T: TRect;
  Text: string;
begin
  Application.Initialize;
  Log := TLog.Create;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    G := TGroupBox.Create(F);

    Section('defaults');
    CheckEqI(185, G.Width, 'Width 185');
    CheckEqI(105, G.Height, 'Height 105');
    CheckEqS('', G.Caption, 'Caption empty');
    CheckEqB(True, G.Color = clBtnFace, 'Color clBtnFace');
    CheckEqB(True, G.ParentColor, 'ParentColor');
    CheckEqB(True, G.ParentFont, 'ParentFont');
    CheckEqI(-1, G.TabOrder, 'TabOrder -1');
    CheckEqB(False, G.TabStop, 'TabStop');
    CheckEqI(0, G.Padding.Left + G.Padding.Top + G.Padding.Right + G.Padding.Bottom, 'Padding 0');
    CheckEqB(True, csAcceptsControls in G.ControlStyle, 'csAcceptsControls');
    Check(GetClass('TGroupBox') <> nil, 'registered');
    CheckEqS('0,0,185,105', Format('%d,%d,%d,%d', [G.ClientRect.Left, G.ClientRect.Top, G.ClientRect.Right,
      G.ClientRect.Bottom]), 'client rect = bounds (UIF-215)');

    Section('handle-less state, window');
    G.Caption := 'Early';
    G.Enabled := False;
    G.Parent := F;
    H := G.Handle;
    CheckEqS('PN.TGroupBox', ClassOf(H), 'own window class');
    Style := GetWindowLongPtrW(H, GWL_STYLE);
    CheckEqB(True, (Style and WS_CHILD) <> 0, 'WS_CHILD');
    CheckEqB(True, (Style and WS_CLIPCHILDREN) <> 0, 'WS_CLIPCHILDREN');
    CheckEqB(False, (Style and WS_TABSTOP) <> 0, 'no WS_TABSTOP');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_EXSTYLE) and WS_EX_CONTROLPARENT) <> 0, 'WS_EX_CONTROLPARENT');
    CheckEqS('Early', NativeText(H), 'caption pushed to the window');
    CheckEqB(False, IsWindowEnabled(H), 'disabled pushed to the window');
    G.Enabled := True;

    Section('native -> object');
    SetWindowTextW(H, 'Native');
    CheckEqS('Native', G.Caption, 'Caption reads the window text');

    Section('events');
    G.OnClick := Log.Clicked;
    G.OnResize := Log.Resized;
    G.OnEnter := Log.Entered;
    G.OnExit := Log.Exited;
    Log.Text := '';
    SendMessageW(H, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(5, 50));
    SendMessageW(H, WM_LBUTTONUP, 0, MakeLParam(5, 50));
    CheckEqS('click;', Log.Text, 'OnClick on a click');
    Log.Text := '';
    G.Width := 190;
    CheckEqS('resize;', Log.Text, 'OnResize once on a size change');
    Log.Text := '';
    G.Left := 5;
    CheckEqS('', Log.Text, 'no OnResize on a move');
    A := TFocusBox.Create(F);
    A.Name := 'A';
    A.SetBounds(10, 30, 20, 20);
    A.Parent := G;
    B := TFocusBox.Create(F);
    B.Name := 'B';
    B.SetBounds(300, 10, 20, 20);
    B.Parent := F;
    F.Show;
    B.SetFocus;
    Log.Text := '';
    A.SetFocus;
    CheckEqS('enter;', Log.Text, 'OnEnter when a child gets the focus');
    Log.Text := '';
    B.SetFocus;
    CheckEqS('exit;', Log.Text, 'OnExit when the focus leaves');
    G.OnResize := nil;
    G.OnEnter := nil;
    G.OnExit := nil;

    Section('accelerator (UIF-671)');
    G.Caption := '&Group';
    CheckEqI(0, G.Perform(CM_DIALOGCHAR, Ord('X'), 0), 'other character: not consumed');
    CheckEqB(True, F.ActiveControl = B, 'focus unchanged');
    CheckEqI(1, G.Perform(CM_DIALOGCHAR, Ord('g'), 0), 'accelerator consumed');
    CheckEqB(True, F.ActiveControl = A, 'first child focused');
    B.SetFocus;
    PostMessageW(B.Handle, WM_SYSCHAR, Ord('g'), 1);
    Application.ProcessMessages;
    CheckEqB(True, F.ActiveControl = A, 'Alt+G through the form focuses the first child');
    A.Enabled := False;
    B.SetFocus;
    CheckEqI(0, G.Perform(CM_DIALOGCHAR, Ord('G'), 0), 'no focusable child: not consumed');
    CheckEqB(True, F.ActiveControl = B, 'focus stays');
    A.Free;

    Section('align rect (layout.md UIF-218, E26)');
    G2 := TGroupBox.Create(nil);
    try
      G2.Font.Height := -12;
      C := TFocusBox.Create(G2);
      C.Align := alClient;
      C.Parent := G2;
      CheckEqS('2,17,181,86', BoundsText(C), 'E26 without a form (96 DPI)');
      G2.Padding.Left := 3;
      G2.Padding.Bottom := 4;
      CheckEqS('5,17,178,82', BoundsText(C), 'Padding insets the align rect');
      G2.Padding.Left := 0;
      G2.Padding.Bottom := 0;
      G2.Font.Height := -20;
      CheckEqS('2,25,181,78', BoundsText(C), 'font change realigns');
      G2.Font.Height := -12;
      G2.Parent := F;
      DPI := F.PixelsPerInch;
      S2 := LayoutMulDiv(2, DPI, 96);
      S5 := LayoutMulDiv(5, DPI, 96);
      G2.Realign;
      CheckEqS(Format('%d,%d,%d,%d', [S2, 12 + S5, 185 - 2 * S2, 105 - S2 - 12 - S5]), BoundsText(C),
        'constants scaled at the form''s DPI');
    finally
      G2.Free;
    end;

    Section('Paint (UIF-670)');
    G.SetBounds(0, 0, 200, 100);
    G.Color := clRed;
    G.Font.Height := -12;
    G.Font.Color := clBlack;
    G.Caption := 'MMMM';
    DPI := F.PixelsPerInch;
    S2 := LayoutMulDiv(2, DPI, 96);
    S5 := LayoutMulDiv(5, DPI, 96);
    S8 := LayoutMulDiv(8, DPI, 96);
    CapH := 12 + S5;
    FrameY := CapH div 2;
    Bmp.Canvas.Font := G.Font;
    T := Rect(0, 0, 0, 0);
    Text := G.Caption;
    Bmp.Canvas.TextRect(T, Text, [tfSingleLine, tfCalcRect]);
    TW := T.Right;
    TH := T.Bottom;
    Capture(G);
    CheckEqB(True, Pixel(100, 60) = clRed, 'background Color');
    CheckEqB(True, Pixel(190, FrameY) = ColorToRGB(clBtnShadow), 'frame top: etched outer line');
    CheckEqB(True, Pixel(190, FrameY + 1) = ColorToRGB(clBtnHighlight), 'frame top: etched inner line');
    CheckEqB(True, Pixel(190, FrameY - 1) = clRed, 'above the frame: background');
    CheckEqB(True, Pixel(0, 60) = ColorToRGB(clBtnShadow), 'frame left edge at x 0');
    CheckEqB(True, Pixel(199, 60) = ColorToRGB(clBtnHighlight), 'frame right edge at Width - 1');
    CheckEqB(True, Pixel(100, 99) = ColorToRGB(clBtnHighlight), 'frame bottom edge at Height - 1');
    Check(Count(clBlack, S8, 0, S8 + TW, TH) > 0, 'caption drawn at s(8)');
    CheckEqI(0, Count(clBlack, 0, 0, S8, 100), 'nothing left of s(8)');
    CheckEqI(0, Count(clBlack, S8 + TW, 0, 200, 100), 'nothing right of the caption');
    CheckEqB(True, Pixel(S8 - S2, FrameY) = clRed, 'strip starts s(2) left of the text');
    CheckEqB(True, Pixel(S8 - S2 - 1, FrameY) = ColorToRGB(clBtnShadow), 'frame left of the strip');
    CheckEqB(True, Pixel(S8 + TW + S2 - 1, FrameY) = clRed, 'strip ends s(2) right of the text');
    CheckEqB(True, Pixel(S8 + TW + S2, FrameY) = ColorToRGB(clBtnShadow), 'frame right of the strip');
    G.Enabled := False;
    Capture(G);
    CheckEqI(0, Count(clBlack, 0, 0, 200, 100), 'disabled: no text in the font colour');
    Check(Count(clGrayText, S8, 0, S8 + TW, TH) > 0, 'disabled: clGrayText');
    G.Enabled := True;
    G.Caption := '';
    Capture(G);
    CheckEqB(True, Pixel(S8, FrameY) = ColorToRGB(clBtnShadow), 'empty caption: full frame, no gap');
    CheckEqB(True, Pixel(S8 - S2, FrameY) = ColorToRGB(clBtnShadow), 'empty caption: no strip');

    Section('streaming');
    Log.Text := '';
    SF := TGroupForm.Create(nil);
    try
      CheckEqS('Streamed', SF.Group.Caption, 'Caption');
      CheckEqB(True, SF.Group.Color = clYellow, 'Color');
      CheckEqB(False, SF.Group.ParentColor, 'ParentColor False');
      CheckEqB(False, SF.Group.ParentFont, 'ParentFont False');
      CheckEqI(-15, SF.Group.Font.Height, 'Font.Height');
      CheckEqI(4, SF.Group.Padding.Left, 'Padding.Left');
      CheckEqI(0, SF.Group.TabOrder, 'TabOrder (the only windowed child)');
      CheckEqB(True, SF.Group.TabStop, 'TabStop');
      CheckEqS('10,20,150,90', BoundsText(SF.Group), 'bounds');
      CheckEqB(True, SF.Inner.Parent = SF.Group, 'child group box parented');
      S2 := LayoutMulDiv(2, SF.PixelsPerInch, 96);
      S5 := LayoutMulDiv(5, SF.PixelsPerInch, 96);
      CheckEqS(Format('%d,%d,%d,%d', [S2 + 4, 15 + S5, 150 - 2 * S2 - 4, 90 - S2 - 15 - S5]), BoundsText(SF.Inner),
        'alClient child in the align rect');
      Check(Assigned(SF.Group.OnClick), 'OnClick bound');
      SF.Group.OnClick(SF.Group);
      CheckEqS('streamed click;', Log.Text, 'OnClick handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    G.Free;
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      G := TGroupBox.Create(nil);
      G.Caption := 'Leak';
      G.Parent := F;
      G.HandleNeeded;
      G.Free;
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
