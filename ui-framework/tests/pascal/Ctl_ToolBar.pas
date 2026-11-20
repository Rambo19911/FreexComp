// covers: UIF-261, UIF-800, UIF-801, UIF-802, UIF-803, UIF-804
// needs: desktop
program Ctl_ToolBar;
{ U-084, controls-standard.md section 22: TToolBar / TToolButton defaults (300 x 29, Align alTop; 23 x 22, a
  separator 8 wide); the bar is an own-class window that accepts children and is no tab stop (UIF-804); the flow
  layout from Indent and s(2): buttons ButtonWidth x ButtonHeight, separators and dividers their own Width, other
  controls their own size, invisible children skipped, Align/Anchors of children ignored, re-flowed on every
  relevant change, scaled ButtonWidth/ButtonHeight/Indent (UIF-800; E28 with a status bar); painting captured with
  WM_PRINTCLIENT: plain, hot (frame + tint), pressed and Down (sunken frame), the divider's etched line, the
  separator's gap, images centred / with captions below / List, disabled images (UIF-801); clicks: inside an enabled
  button only, check buttons toggling before OnClick, grouped check buttons as a radio group (UIF-802); action
  links (Caption, Hint, Checked -> Down, ImageIndex, Enabled, execution); the hint text of a button (UIF-803);
  streaming with Width stored only for separators; 100 bars with buttons leak no GUI objects.
  The mouse is simulated with messages sent to the bar's window. }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, Forms, StdCtrls, ExtCtrls, ImgList,
  ActnList, ComCtrls, TestKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Exec(Sender: TObject);
  end;

  TBarForm = class(TForm)
  published
    Bar: TToolBar;
    New: TToolButton;
    Sep: TToolButton;
    Opt: TToolButton;
    Box: TPanel;
    procedure NewClick(Sender: TObject);
  end;

{$FORM Ctl_ToolBar.form}

var
  Log: TLog;
  Bmp: TBitmap;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name;
  if TToolButton(Sender).Down then
    Text := Text + '(down)';
  Text := Text + ';';
end;

procedure TLog.Exec(Sender: TObject);
begin
  Text := Text + 'exec:' + TComponent(Sender).Name + ';';
end;

procedure TBarForm.NewClick(Sender: TObject);
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

function BoundsText(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function NewButton(Bar: TToolBar; const AName: string; AStyle: TToolButtonStyle): TToolButton;
begin
  Result := TToolButton.Create(Bar.Owner);
  Result.Name := AName;
  Result.Style := AStyle;
  Result.OnClick := Log.Clicked;
  Result.Parent := Bar;
end;

procedure Capture(Bar: TToolBar);
begin
  Bmp.Canvas.Brush.Color := clBlack;
  Bmp.Canvas.FillRect(Rect(0, 0, Bmp.Width, Bmp.Height));
  SendMessageW(Bar.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

function Pixel(X, Y: Integer): TColor;
begin
  Result := Bmp.Canvas.Pixels[X, Y];
end;

function IsColor(X, Y: Integer; C: TColor): Boolean;
begin
  Result := Pixel(X, Y) = ColorToRGB(C);
end;

{ pixels of colour C inside the rectangle }
function Count(C: TColor; const R: TRect): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
        Inc(Result);
end;

{ the bounding box of colour C inside the rectangle, relative to its top-left, as 'l,t,r,b' (r, b exclusive) }
function BoxOf(C: TColor; const R: TRect): string;
var
  X, Y, L, T, Rt, B: Integer;
begin
  C := ColorToRGB(C);
  L := MaxInt;
  T := MaxInt;
  Rt := -1;
  B := -1;
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
      begin
        if X < L then
          L := X;
        if Y < T then
          T := Y;
        if X > Rt then
          Rt := X;
        if Y > B then
          B := Y;
      end;
  if Rt < 0 then
    Result := 'none'
  else
    Result := Format('%d,%d,%d,%d', [L - R.Left, T - R.Top, Rt + 1 - R.Left, B + 1 - R.Top]);
end;

{ BoxOf as a rectangle; Left = -1 when there is no such pixel }
function BoxRect(C: TColor; const R: TRect): TRect;
var
  X, Y: Integer;
begin
  Result := Rect(-1, -1, -1, -1);
  C := ColorToRGB(C);
  for Y := R.Top to R.Bottom - 1 do
    for X := R.Left to R.Right - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
      begin
        if (Result.Left < 0) or (X - R.Left < Result.Left) then
          Result.Left := X - R.Left;
        if (Result.Top < 0) or (Y - R.Top < Result.Top) then
          Result.Top := Y - R.Top;
        if X + 1 - R.Left > Result.Right then
          Result.Right := X + 1 - R.Left;
        if Y + 1 - R.Top > Result.Bottom then
          Result.Bottom := Y + 1 - R.Top;
      end;
end;

{ pixels that are neither the bar's red nor the image's lime: text }
function Others(const R: TRect): Integer;
begin
  Result := (R.Right - R.Left) * (R.Bottom - R.Top) - Count(clRed, R) - Count(clLime, R);
end;

procedure MouseMove(Bar: TToolBar; X, Y: Integer; Buttons: WPARAM);
begin
  SendMessageW(Bar.Handle, WM_MOUSEMOVE, Buttons, MakeLParam(X, Y));
end;

procedure ClickAt(Bar: TToolBar; X, Y: Integer);
begin
  Log.Text := '';
  SendMessageW(Bar.Handle, WM_MOUSEMOVE, 0, MakeLParam(X, Y));
  SendMessageW(Bar.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(X, Y));
  SendMessageW(Bar.Handle, WM_LBUTTONUP, 0, MakeLParam(X, Y));
end;

procedure ClickOn(B: TToolButton);
begin
  ClickAt(TToolBar(B.Parent), B.Left + B.Width div 2, B.Top + B.Height div 2);
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Defaults;
var
  T: TToolBar;
  B: TToolButton;
begin
  Section('defaults');
  T := TToolBar.Create(nil);
  B := TToolButton.Create(nil);
  try
    CheckEqI(300, T.Width, 'Width 300');
    CheckEqI(29, T.Height, 'Height 29');
    CheckEqB(True, T.Align = alTop, 'Align alTop');
    CheckEqI(22, T.ButtonHeight, 'ButtonHeight 22');
    CheckEqI(23, T.ButtonWidth, 'ButtonWidth 23');
    CheckEqB(True, T.Color = clBtnFace, 'Color clBtnFace');
    CheckEqB(True, T.Images = nil, 'Images nil');
    CheckEqI(0, T.Indent, 'Indent 0');
    CheckEqB(False, T.List, 'List');
    CheckEqB(True, T.ParentColor, 'ParentColor');
    CheckEqB(True, T.ParentFont, 'ParentFont');
    CheckEqB(False, T.ShowCaptions, 'ShowCaptions');
    CheckEqI(-1, T.TabOrder, 'TabOrder -1');
    CheckEqB(False, T.TabStop, 'TabStop False');
    CheckEqB(True, csAcceptsControls in T.ControlStyle, 'accepts controls');
    CheckEqB(True, T is TCustomControl, 'a TCustomControl');
    Check(GetClass('TToolBar') <> nil, 'TToolBar registered');
    Check(GetClass('TToolButton') <> nil, 'TToolButton registered');
    CheckEqI(23, B.Width, 'button Width 23');
    CheckEqI(22, B.Height, 'button Height 22');
    CheckEqB(True, B.Action = nil, 'button Action nil');
    CheckEqS('', B.Caption, 'button Caption');
    CheckEqB(False, B.Down, 'button Down');
    CheckEqB(True, B.Enabled, 'button Enabled');
    CheckEqB(False, B.Grouped, 'button Grouped');
    CheckEqS('', B.Hint, 'button Hint');
    CheckEqI(-1, B.ImageIndex, 'button ImageIndex -1');
    CheckEqB(True, B.Style = tbsButton, 'button Style tbsButton');
    CheckEqB(True, B.Visible, 'button Visible');
    CheckEqB(True, B is TGraphicControl, 'a TGraphicControl');
    CheckEqB(False, IsStoredProp(B, 'Width'), 'a button does not store Width');
    CheckEqB(False, IsStoredProp(B, 'Left'), 'Left not stored');
    CheckEqB(False, IsStoredProp(B, 'Top'), 'Top not stored');
    CheckEqB(False, IsStoredProp(B, 'Height'), 'Height not stored');
    B.Style := tbsSeparator;
    CheckEqI(8, B.Width, 'a separator is 8 wide');
    CheckEqB(True, IsStoredProp(B, 'Width'), 'a separator stores Width');
    B.Width := 12;
    B.Style := tbsDivider;
    CheckEqI(12, B.Width, 'separator to divider keeps the width');
    CheckEqB(True, IsStoredProp(B, 'Width'), 'a divider stores Width');
    B.Style := tbsCheck;
    CheckEqB(False, IsStoredProp(B, 'Width'), 'a check button does not store Width');
  finally
    B.Free;
    T.Free;
  end;
end;

procedure Flow(F: TForm);
var
  T: TToolBar;
  B1, B2, S, Dv, Hid, B3: TToolButton;
  P: TPanel;
  Y: Integer;
begin
  Section('flow layout (UIF-800)');
  T := TToolBar.Create(F);
  T.Parent := F;
  Y := LayoutMulDiv(2, F.PixelsPerInch, 96);
  T.Indent := 5;
  B1 := NewButton(T, 'B1', tbsButton);
  CheckEqS(Format('5,%d,23,22', [Y]), BoundsText(B1), 'first button at (Indent, s(2))');
  B2 := NewButton(T, 'B2', tbsCheck);
  B2.Width := 50;
  CheckEqS(Format('28,%d,23,22', [Y]), BoundsText(B2), 'a button always gets ButtonWidth');
  S := NewButton(T, 'S', tbsSeparator);
  Dv := NewButton(T, 'Dv', tbsDivider);
  Dv.Width := 10;
  P := TPanel.Create(F);
  P.SetBounds(0, 0, 40, 15);
  P.Parent := T;
  Hid := NewButton(T, 'Hid', tbsButton);
  Hid.Visible := False;
  B3 := NewButton(T, 'B3', tbsButton);
  CheckEqS(Format('51,%d,8,22', [Y]), BoundsText(S), 'separator: its own Width 8');
  CheckEqS(Format('59,%d,10,22', [Y]), BoundsText(Dv), 'divider: its own Width');
  CheckEqS(Format('69,%d,40,15', [Y]), BoundsText(P), 'another control: its own size');
  CheckEqS(Format('109,%d,23,22', [Y]), BoundsText(B3), 'an invisible button is skipped');
  Hid.Visible := True;
  CheckEqS(Format('109,%d,23,22', [Y]), BoundsText(Hid), 'shown again: flowed');
  CheckEqS(Format('132,%d,23,22', [Y]), BoundsText(B3), 'the next one moves');
  Hid.Visible := False;
  CheckEqS(Format('109,%d,23,22', [Y]), BoundsText(B3), 'hidden again');
  T.ButtonWidth := 30;
  CheckEqS(Format('35,%d,30,22', [Y]), BoundsText(B2), 'ButtonWidth re-flows');
  T.ButtonHeight := 25;
  CheckEqS(Format('35,%d,30,25', [Y]), BoundsText(B2), 'ButtonHeight re-flows');
  CheckEqS(Format('65,%d,8,25', [Y]), BoundsText(S), 'separator height = ButtonHeight');
  T.Indent := 0;
  CheckEqS(Format('0,%d,30,25', [Y]), BoundsText(B1), 'Indent re-flows');
  S.Style := tbsButton;
  CheckEqS(Format('60,%d,30,25', [Y]), BoundsText(S), 'Style change re-flows');
  S.Style := tbsSeparator;
  S.Width := 8;
  P.Align := alClient;
  P.Anchors := [akLeft, akTop, akRight, akBottom];
  T.Realign;
  CheckEqS(Format('78,%d,40,15', [Y]), BoundsText(P), 'Align and Anchors of children are ignored');
  B1.SetBounds(200, 10, 5, 5);
  CheckEqS(Format('0,%d,30,25', [Y]), BoundsText(B1), 'a button''s own bounds are replaced');
  T.ScaleBy(2, 1);
  CheckEqI(60, T.ButtonWidth, 'ButtonWidth scaled');
  CheckEqI(50, T.ButtonHeight, 'ButtonHeight scaled');
  T.Indent := 4;
  T.ScaleBy(3, 2);
  CheckEqI(6, T.Indent, 'Indent scaled');
  T.Free;
  T := TToolBar.Create(nil);
  try
    B1 := TToolButton.Create(T);
    B1.Parent := T;
    CheckEqS('0,2,23,22', BoundsText(B1), 'without a form: s(2) at 96 DPI');
  finally
    T.Free;
  end;
end;

procedure Run;
var
  F, F2: TForm;
  T: TToolBar;
  B, S, Dv, C, G1, G2, G3, G4, G5, Off: TToolButton;
  SB: TStatusBar;
  M: TMemo;
  IL: TImageList;
  Img: TBitmap;
  AL: TActionList;
  A: TAction;
  R: TRect;
  BF: TBarForm;
  I, Before, X, Y, M3: Integer;
  Box: TRect;
begin
  Application.Initialize;
  Log := TLog.Create;
  Bmp := TBitmap.Create;
  Bmp.SetSize(300, 60);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    Defaults;
    Flow(F);

    Section('E28: tool bar and status bar (layout.md UIF-261)');
    F2 := TForm.CreateNew(nil);
    try
      F2.ClientWidth := 640;
      F2.ClientHeight := 400;
      T := TToolBar.Create(F2);
      T.Parent := F2;
      SB := TStatusBar.Create(F2);
      SB.Parent := F2;
      M := TMemo.Create(F2);
      M.Align := alClient;
      M.Parent := F2;
      CheckEqS('0,0,640,29', BoundsText(T), 'ToolBar');
      CheckEqS('0,381,640,19', BoundsText(SB), 'StatusBar');
      CheckEqS('0,29,640,352', BoundsText(M), 'Memo');
      T.Font.Height := -30;
      CheckEqI(29, T.Height, 'no auto-size from the font');
    finally
      F2.Free;
    end;

    Section('window (UIF-804)');
    T := TToolBar.Create(F);
    T.Name := 'T';
    T.Parent := F;
    CheckEqS('PN.TToolBar', ClassOf(T.Handle), 'own window class');
    CheckEqB(False, (GetWindowLongPtrW(T.Handle, GWL_STYLE) and WS_TABSTOP) <> 0, 'no WS_TABSTOP');
    T.Color := clRed;
    B := NewButton(T, 'B', tbsButton);
    S := NewButton(T, 'S', tbsSeparator);
    Dv := NewButton(T, 'Dv', tbsDivider);
    C := NewButton(T, 'C', tbsCheck);
    Off := NewButton(T, 'Off', tbsButton);
    Off.Enabled := False;
    F.Show;
    CheckEqB(False, T.CanFocus and T.TabStop, 'the bar is not a tab stop');
    { B 0..23, S 23..31, Dv 31..39, C 39..62, Off 62..85 }

    Section('painting (UIF-801)');
    Capture(T);
    CheckEqI(B.Width * B.Height, Count(clRed, B.BoundsRect), 'a plain button: background only');
    CheckEqI(S.Width * S.Height, Count(clRed, S.BoundsRect), 'separator: an empty gap');
    X := Dv.Left + Dv.Width div 2 - 1;
    CheckEqB(True, IsColor(X, Dv.Top + 3, clBtnShadow), 'divider: shadow line left of the centre');
    CheckEqB(True, IsColor(X + 1, Dv.Top + 3, clBtnHighlight), 'divider: highlight line at the centre');
    CheckEqI(2 * Dv.Height, Dv.Width * Dv.Height - Count(clRed, Dv.BoundsRect), 'divider: one etched line, full height');
    MouseMove(T, B.Left + 5, B.Top + 5, 0);
    Capture(T);
    CheckEqB(True, IsColor(B.Left, B.Top, clBtnShadow), 'hot: frame top-left');
    CheckEqB(True, IsColor(B.Left + B.Width - 1, B.Top + B.Height - 1, clBtnShadow), 'hot: frame bottom-right');
    CheckEqB(True, IsColor(B.Left + 5, B.Top + 5, clBtnHighlight), 'hot: highlight tint inside');
    CheckEqI(2 * (B.Width + B.Height) - 4, Count(clBtnShadow, B.BoundsRect), 'hot: a 1-px frame');
    MouseMove(T, S.Left + 2, S.Top + 2, 0);
    Capture(T);
    CheckEqI(B.Width * B.Height, Count(clRed, B.BoundsRect), 'mouse gone: plain again');
    MouseMove(T, B.Left + 5, B.Top + 5, 0);
    SendMessageW(T.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(B.Left + 5, B.Top + 5));
    Capture(T);
    CheckEqB(True, IsColor(B.Left, B.Top, clBtnShadow), 'pressed: sunken frame, shadow top-left');
    CheckEqB(True, IsColor(B.Left + B.Width - 1, B.Top + B.Height - 1, clBtnHighlight),
      'pressed: sunken frame, highlight bottom-right');
    MouseMove(T, B.Left + 100, B.Top + 5, MK_LBUTTON);
    Capture(T);
    CheckEqI(B.Width * B.Height, Count(clRed, B.BoundsRect), 'dragged outside: not pressed');
    MouseMove(T, B.Left + 5, B.Top + 5, MK_LBUTTON);
    Capture(T);
    CheckEqB(True, IsColor(B.Left + B.Width - 1, B.Top + B.Height - 1, clBtnHighlight), 'back inside: pressed');
    SendMessageW(T.Handle, WM_LBUTTONUP, 0, MakeLParam(B.Left + 100, B.Top + 5));
    MouseMove(T, S.Left + 2, S.Top + 2, 0);
    C.Down := True;
    Capture(T);
    CheckEqB(True, IsColor(C.Left, C.Top, clBtnShadow), 'Down: sunken frame, shadow top-left');
    CheckEqB(True, IsColor(C.Left + C.Width - 1, C.Top + C.Height - 1, clBtnHighlight),
      'Down: sunken frame, highlight bottom-right');
    C.Down := False;
    MouseMove(T, Off.Left + 5, Off.Top + 5, 0);
    Capture(T);
    CheckEqI(Off.Width * Off.Height, Count(clRed, Off.BoundsRect), 'a disabled button is never hot');
    MouseMove(T, S.Left + 2, S.Top + 2, 0);

    Section('images (UIF-801)');
    IL := TImageList.Create(F);
    Img := TBitmap.Create;
    try
      Img.SetSize(16, 16);
      Img.Canvas.Brush.Color := clLime;
      Img.Canvas.FillRect(Rect(0, 0, 16, 16));
      IL.AddMasked(Img, clFuchsia);
    finally
      Img.Free;
    end;
    B.ImageIndex := 0;
    Off.ImageIndex := 0;
    T.Images := IL;
    Capture(T);
    CheckEqS('3,3,19,19', BoxOf(clLime, B.BoundsRect), 'image centred in the button');
    Check(Count(clLime, Off.BoundsRect) < 16 * 16, 'disabled: blended (UIF-566), not drawn plain');
    Check(Count(clRed, Off.BoundsRect) < Off.Width * Off.Height, 'disabled: the image is still drawn');
    B.Caption := 'Go';
    Capture(T);
    CheckEqS('3,3,19,19', BoxOf(clLime, B.BoundsRect), 'a caption without ShowCaptions is not drawn');
    T.ButtonHeight := 40;
    T.ButtonWidth := 40;
    Capture(T);
    R := B.BoundsRect;
    CheckEqS('12,12,28,28', BoxOf(clLime, R), 'a bigger button: image centred');
    T.ShowCaptions := True;
    Capture(T);
    Box := BoxRect(clLime, R);
    CheckEqI(12, Box.Left, 'ShowCaptions: image centred horizontally');
    Check(Box.Top < 12, 'ShowCaptions: image above the centre');
    Check(Others(Rect(R.Left, R.Top + Box.Bottom, R.Right, R.Bottom)) > 0, 'caption text below the image');
    CheckEqI(0, Others(Rect(R.Left, R.Top, R.Right, R.Top + Box.Top)), 'nothing above the image');
    T.List := True;
    Capture(T);
    Box := BoxRect(clLime, R);
    M3 := LayoutMulDiv(3, F.PixelsPerInch, 96);
    CheckEqI(M3, Box.Left, 'List: image at the left margin s(3)');
    CheckEqI(12, Box.Top, 'List: image centred vertically');
    Check(Others(Rect(R.Left + Box.Right, R.Top, R.Right, R.Bottom)) > 0, 'List: caption right of the image');
    T.Images := nil;
    Capture(T);
    CheckEqS('none', BoxOf(clLime, R), 'Images nil: no image');
    T.Images := IL;
    IL.Free;
    CheckEqB(True, T.Images = nil, 'Images cleared when the image list is freed');
    T.List := False;
    T.ShowCaptions := False;
    T.ButtonHeight := 22;
    T.ButtonWidth := 23;

    Section('clicks (UIF-802)');
    B.OnClick := Log.Clicked;
    ClickOn(B);
    CheckEqS('B;', Log.Text, 'click on a button');
    ClickOn(S);
    CheckEqS('', Log.Text, 'separator: no click');
    ClickOn(Dv);
    CheckEqS('', Log.Text, 'divider: no click');
    ClickOn(Off);
    CheckEqS('', Log.Text, 'disabled button: no click');
    Log.Text := '';
    SendMessageW(T.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(B.Left + 5, B.Top + 5));
    SendMessageW(T.Handle, WM_LBUTTONUP, 0, MakeLParam(B.Left + 5, B.Top + 100));
    CheckEqS('', Log.Text, 'released outside: no click');
    ClickOn(C);
    CheckEqS('C(down);', Log.Text, 'check button: Down toggled before OnClick');
    ClickOn(C);
    CheckEqS('C;', Log.Text, 'check button: released by the next click');
    G1 := NewButton(T, 'G1', tbsCheck);
    G2 := NewButton(T, 'G2', tbsCheck);
    G3 := NewButton(T, 'G3', tbsCheck);
    G4 := NewButton(T, 'G4', tbsCheck);
    G1.Grouped := True;
    G2.Grouped := True;
    G3.Grouped := True;
    G4.Grouped := True;
    ClickOn(G2);
    CheckEqS('G2(down);', Log.Text, 'grouped: click sets the button down');
    ClickOn(G1);
    CheckEqS('G1(down);', Log.Text, 'grouped: another one');
    CheckEqB(False, G2.Down, 'grouped: the other one is released');
    ClickOn(G1);
    CheckEqS('G1(down);', Log.Text, 'grouped: a down button stays down');
    CheckEqB(True, G1.Down, 'still down');
    G3.Down := True;
    CheckEqB(False, G1.Down, 'Down := True in code releases the group');
    G3.Down := False;
    CheckEqB(False, G3.Down, 'Down := False in code is allowed');
    G1.Down := True;
    G4.Grouped := False;
    G4.Down := True;
    CheckEqB(True, G1.Down, 'a button that is not grouped is not part of the group');
    G5 := NewButton(T, 'G5', tbsCheck);
    G5.Grouped := True;
    G5.Down := True;
    CheckEqB(True, G1.Down, 'a group ends at a button that is not grouped');
    G4.Down := False;
    G4.Grouped := True;
    G4.Down := True;
    CheckEqB(False, G1.Down, 'G4 grouped again: one group G1 ... G5, G1 released');
    CheckEqB(False, G5.Down, 'G5 released');

    Section('actions');
    AL := TActionList.Create(F);
    A := TAction.Create(F);
    A.Name := 'A';
    A.ActionList := AL;
    A.Caption := 'Act';
    A.Hint := 'Short|Long';
    A.Checked := True;
    A.ImageIndex := 3;
    A.OnExecute := Log.Exec;
    C.OnClick := nil;
    C.Down := False;
    C.Action := A;
    CheckEqS('Act', C.Caption, 'Caption from the action');
    CheckEqS('Short|Long', C.Hint, 'Hint from the action');
    CheckEqB(True, C.Down, 'Checked -> Down');
    CheckEqI(3, C.ImageIndex, 'ImageIndex from the action');
    A.Checked := False;
    CheckEqB(False, C.Down, 'a later Checked change reaches Down');
    A.ImageIndex := 4;
    CheckEqI(4, C.ImageIndex, 'a later ImageIndex change');
    A.Enabled := False;
    CheckEqB(False, C.Enabled, 'Enabled from the action');
    A.Enabled := True;
    C.Style := tbsButton;
    ClickOn(C);
    CheckEqS('exec:A;', Log.Text, 'click executes the action');
    A.Free;
    CheckEqB(True, C.Action = nil, 'freed action: link cleared');
    CheckEqS('Act', C.Caption, 'copied values stay');

    Section('hints (UIF-803)');
    C.Hint := 'Open|Opens a file';
    CheckEqS('Open', GetShortHint(C.Hint), 'short hint of a tool button');
    T.ShowHint := True;
    CheckEqB(True, C.ShowHint, 'ParentShowHint: the bar''s ShowHint');

    Section('streaming');
    Log.Text := '';
    BF := TBarForm.Create(nil);
    try
      Y := LayoutMulDiv(2, BF.PixelsPerInch, 96);
      CheckEqI(30, BF.Bar.ButtonWidth, 'ButtonWidth');
      CheckEqI(26, BF.Bar.ButtonHeight, 'ButtonHeight');
      CheckEqI(4, BF.Bar.Indent, 'Indent');
      CheckEqI(36, BF.Bar.Height, 'Height');
      CheckEqB(True, BF.Bar.ShowCaptions, 'ShowCaptions');
      CheckEqB(True, BF.Bar.List, 'List');
      CheckEqS('&New', BF.New.Caption, 'Caption');
      CheckEqI(2, BF.New.ImageIndex, 'ImageIndex');
      CheckEqS('Creates', BF.New.Hint, 'Hint');
      CheckEqB(True, BF.Sep.Style = tbsSeparator, 'separator');
      CheckEqB(True, BF.Opt.Style = tbsCheck, 'check');
      CheckEqB(True, BF.Opt.Down, 'Down');
      CheckEqB(True, BF.Opt.Grouped, 'Grouped');
      CheckEqS(Format('4,%d,30,26', [Y]), BoundsText(BF.New), 'flowed: New');
      CheckEqS(Format('34,%d,6,26', [Y]), BoundsText(BF.Sep), 'flowed: separator with its stored Width');
      CheckEqS(Format('40,%d,30,26', [Y]), BoundsText(BF.Opt), 'flowed: Opt');
      CheckEqS(Format('70,%d,50,20', [Y]), BoundsText(BF.Box), 'flowed: a panel with its own size');
      Check(Assigned(BF.New.OnClick), 'OnClick bound');
      CheckEqS('', Log.Text, 'no events while loading');
    finally
      BF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      T := TToolBar.Create(nil);
      B := TToolButton.Create(T);
      B.Parent := T;
      B := TToolButton.Create(T);
      B.Style := tbsDivider;
      B.Parent := T;
      T.Parent := F;
      T.HandleNeeded;
      Capture(T);
      T.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 bars with handles');
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
