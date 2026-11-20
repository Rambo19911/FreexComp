// covers: UIF-261, UIF-780, UIF-781, UIF-782
// needs: desktop
program Ctl_StatusBar;
{ U-083, controls-standard.md section 21: TStatusBar / TStatusPanel defaults (300 x 19, Align alBottom); the
  sub-classed msctls_statusbar32 window with CCS_NORESIZE, CCS_NOPARENTALIGN, CCS_NODIVIDER (layout.md UIF-261) and
  SBARS_SIZEGRIP only for SizeGrip on a sizeable form with Align alBottom, SizeGrip re-creating the window keeping the
  panels; parts with cumulative right edges, the last one -1, a single empty part without panels (UIF-780); panel
  texts with the bevel flags and the alignment tab prefixes; every change in Panels re-sent (UIF-781); SimplePanel /
  SimpleText (SB_SIMPLE); the stored Height kept by the native control (UIF-782); panel widths scaled; mouse events;
  streaming of the Panels collection; 100 instances with handles leak no GUI objects. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, CommCtrl, ComCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Clicked(Sender: TObject);
    procedure Resized(Sender: TObject);
  end;

  TWinAccess = class(TWinControl);

  TStatusForm = class(TForm)
  published
    Status: TStatusBar;
    Simple: TStatusBar;
    procedure StatusClick(Sender: TObject);
  end;

{$FORM Ctl_StatusBar.form}

var
  Log: TLog;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TLog.Resized(Sender: TObject);
begin
  Text := Text + 'resize;';
end;

procedure TStatusForm.StatusClick(Sender: TObject);
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

function HasStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bits) = Bits;
end;

{ the native parts as 'e1,e2,...' }
function Parts(SB: TStatusBar): string;
var
  Edges: array[0..15] of Integer;
  I, N: Integer;
begin
  N := Integer(SendMessageW(SB.Handle, SB_GETPARTS, 16, LPARAM(@Edges[0])));
  Result := '';
  for I := 0 to N - 1 do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + IntToStr(Edges[I]);
  end;
end;

{ the native text of part Index, tabs shown as '\t'; in simple mode part 0 answers the simple text }
function PartText(SB: TStatusBar; Index: Integer): string;
var
  Buf: array[0..255] of WideChar;
  I: Integer;
begin
  FillChar(Buf, SizeOf(Buf), 0);
  SendMessageW(SB.Handle, SB_GETTEXTW, WPARAM(Index), LPARAM(@Buf[0]));
  Result := '';
  I := 0;
  while Buf[I] <> #0 do
  begin
    if Buf[I] = #9 then
      Result := Result + '\t'
    else
      Result := Result + Buf[I];
    Inc(I);
  end;
end;

{ the drawing flags of part Index (HIWORD of SB_GETTEXTLENGTHW) }
function PartFlags(SB: TStatusBar; Index: Integer): Integer;
begin
  Result := HiWord(Cardinal(SendMessageW(SB.Handle, SB_GETTEXTLENGTHW, WPARAM(Index), 0)));
end;

function WindowHeight(C: TWinControl): Integer;
var
  R: TRect;
begin
  GetWindowRect(C.Handle, R);
  Result := R.Bottom - R.Top;
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Defaults;
var
  SB: TStatusBar;
  P: TStatusPanel;
begin
  Section('defaults');
  SB := TStatusBar.Create(nil);
  try
    CheckEqI(300, SB.Width, 'Width 300');
    CheckEqI(19, SB.Height, 'Height 19');
    CheckEqB(True, SB.Align = alBottom, 'Align alBottom');
    CheckEqI(0, SB.Panels.Count, 'Panels empty');
    CheckEqB(True, SB.ParentFont, 'ParentFont');
    CheckEqB(False, SB.SimplePanel, 'SimplePanel');
    CheckEqS('', SB.SimpleText, 'SimpleText');
    CheckEqB(True, SB.SizeGrip, 'SizeGrip');
    CheckEqB(False, TWinAccess(SB).TabStop, 'TabStop False (not published)');
    CheckEqB(False, csAcceptsControls in SB.ControlStyle, 'does not accept children');
    Check(GetClass('TStatusBar') <> nil, 'TStatusBar registered');
    P := SB.Panels.Add;
    CheckEqB(True, P is TStatusPanel, 'Add returns a TStatusPanel');
    CheckEqB(True, P.Alignment = taLeftJustify, 'panel Alignment taLeftJustify');
    CheckEqB(True, P.Bevel = pbLowered, 'panel Bevel pbLowered');
    CheckEqS('', P.Text, 'panel Text');
    CheckEqI(50, P.Width, 'panel Width 50');
    CheckEqB(True, SB.Panels[0] = P, 'Panels[0]');
    CheckEqB(True, SB.Panels.Owner = SB, 'the collection belongs to the status bar');
  finally
    SB.Free;
  end;
end;

procedure Run;
var
  F, D: TForm;
  SB, SB2: TStatusBar;
  P0, P1, P2: TStatusPanel;
  H: HWND;
  SF: TStatusForm;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 640;
    F.ClientHeight := 400;
    Defaults;

    Section('handle-less state');
    SB := TStatusBar.Create(F);
    P0 := SB.Panels.Add;
    P0.Width := 100;
    P0.Text := 'one';
    P1 := SB.Panels.Add;
    P1.Width := 60;
    P1.Text := 'two';
    P1.Bevel := pbNone;
    P2 := SB.Panels.Add;
    P2.Text := 'three';
    P2.Bevel := pbRaised;
    SB.Parent := F;
    H := SB.Handle;
    CheckEqS('msctls_statusbar32', ClassOf(H), 'window class msctls_statusbar32');
    CheckEqS('100,160,-1', Parts(SB), 'parts: cumulative edges, the last one -1 (UIF-780)');
    CheckEqS('one', PartText(SB, 0), 'text of part 0');
    CheckEqS('two', PartText(SB, 1), 'text of part 1');
    CheckEqS('three', PartText(SB, 2), 'text of part 2');
    CheckEqI(0, PartFlags(SB, 0), 'pbLowered: no flag');
    CheckEqI(SBT_NOBORDERS, PartFlags(SB, 1), 'pbNone: SBT_NOBORDERS');
    CheckEqI(SBT_POPOUT, PartFlags(SB, 2), 'pbRaised: SBT_POPOUT');

    Section('window styles (UIF-261)');
    Check(HasStyle(SB, CCS_NORESIZE), 'CCS_NORESIZE');
    Check(HasStyle(SB, CCS_NOPARENTALIGN), 'CCS_NOPARENTALIGN');
    Check(HasStyle(SB, CCS_NODIVIDER), 'CCS_NODIVIDER');
    CheckEqB(False, HasStyle(SB, WS_TABSTOP), 'no WS_TABSTOP');
    Check(HasStyle(SB, SBARS_SIZEGRIP), 'sizeable form, alBottom: SBARS_SIZEGRIP');
    SB.SizeGrip := False;
    CheckEqB(True, SB.Handle <> H, 'SizeGrip recreates the window');
    CheckEqB(False, HasStyle(SB, SBARS_SIZEGRIP), 'SizeGrip False: no grip');
    CheckEqS('100,160,-1', Parts(SB), 'parts kept');
    CheckEqS('three', PartText(SB, 2), 'texts kept');
    SB.Align := alTop;
    SB.SizeGrip := True;
    CheckEqB(False, HasStyle(SB, SBARS_SIZEGRIP), 'Align alTop: no grip');
    SB.SizeGrip := False;
    SB.Align := alBottom;
    SB.SizeGrip := True;
    Check(HasStyle(SB, SBARS_SIZEGRIP), 'Align alBottom again: grip');
    D := TForm.CreateNew(nil);
    try
      D.BorderStyle := bsDialog;
      SB2 := TStatusBar.Create(D);
      SB2.Parent := D;
      CheckEqB(False, HasStyle(SB2, SBARS_SIZEGRIP), 'dialog form: no grip');
      D.BorderStyle := bsSizeToolWin;
      SB2.SizeGrip := False;
      SB2.SizeGrip := True;
      Check(HasStyle(SB2, SBARS_SIZEGRIP), 'bsSizeToolWin form: grip');
    finally
      D.Free;
    end;

    Section('geometry (UIF-782, E28)');
    CheckEqS('0,381,640,19', BoundsText(SB), 'alBottom on a 640 x 400 form');
    CheckEqI(19, WindowHeight(SB), 'the native window keeps the stored height');
    SB.Font.Height := -30;
    CheckEqI(19, SB.Height, 'a font change does not resize');
    CheckEqI(19, WindowHeight(SB), 'native height after the font change');
    SB.ParentFont := True;
    SB.Height := 25;
    CheckEqS('0,375,640,25', BoundsText(SB), 'a stored Height of 25');
    CheckEqI(25, WindowHeight(SB), 'native height 25');
    SB.Height := 19;

    Section('panel changes (UIF-781)');
    P1.Width := 40;
    CheckEqS('100,140,-1', Parts(SB), 'a width change re-sends the parts');
    P1.Text := 'zwei';
    CheckEqS('zwei', PartText(SB, 1), 'a text change re-sends the text');
    P1.Bevel := pbRaised;
    CheckEqI(SBT_POPOUT, PartFlags(SB, 1), 'a bevel change re-sends the flags');
    P0.Alignment := taCenter;
    CheckEqS('\tone', PartText(SB, 0), 'taCenter: one tab before the text');
    P0.Alignment := taRightJustify;
    CheckEqS('\t\tone', PartText(SB, 0), 'taRightJustify: two tabs before the text');
    P0.Alignment := taLeftJustify;
    CheckEqS('one', PartText(SB, 0), 'taLeftJustify: no prefix');
    SB.Panels.Add.Text := 'four';
    CheckEqS('100,140,190,-1', Parts(SB), 'Add: a new last part');
    CheckEqS('four', PartText(SB, 3), 'its text');
    SB.Panels.Delete(1);
    CheckEqS('100,150,-1', Parts(SB), 'Delete: the parts follow');
    CheckEqS('three', PartText(SB, 1), 'texts follow');
    SB.Panels.BeginUpdate;
    try
      SB.Panels[0].Width := 10;
      CheckEqS('100,150,-1', Parts(SB), 'nothing sent inside BeginUpdate');
    finally
      SB.Panels.EndUpdate;
    end;
    CheckEqS('10,60,-1', Parts(SB), 'sent at EndUpdate');
    SB.Panels.Clear;
    CheckEqS('-1', Parts(SB), 'no panels: a single part');
    CheckEqS('', PartText(SB, 0), 'the single part is empty');

    Section('simple panel');
    SB.Panels.Add.Text := 'A';
    SB.Panels.Add.Text := 'B';
    SB.SimpleText := 'busy';
    CheckEqB(False, SendMessageW(SB.Handle, SB_ISSIMPLE, 0, 0) <> 0, 'not simple');
    SB.SimplePanel := True;
    CheckEqB(True, SendMessageW(SB.Handle, SB_ISSIMPLE, 0, 0) <> 0, 'SimplePanel: SB_SIMPLE');
    CheckEqS('busy', PartText(SB, 0), 'the simple text');
    SB.SimpleText := 'done';
    CheckEqS('done', PartText(SB, 0), 'SimpleText change re-sent');
    H := SB.Handle;
    SB.SizeGrip := False;
    CheckEqB(True, SB.Handle <> H, 'recreated');
    CheckEqB(True, SendMessageW(SB.Handle, SB_ISSIMPLE, 0, 0) <> 0, 'simple mode kept');
    CheckEqS('done', PartText(SB, 0), 'simple text kept');
    SB.SimplePanel := False;
    CheckEqB(False, SendMessageW(SB.Handle, SB_ISSIMPLE, 0, 0) <> 0, 'SimplePanel False: parts again');
    CheckEqS('50,-1', Parts(SB), 'the panels are back');
    CheckEqS('A', PartText(SB, 0), 'their texts: part 0');
    CheckEqS('B', PartText(SB, 1), 'their texts: part 1');

    Section('scaling');
    SB.Panels[0].Width := 100;
    SB.ScaleBy(2, 1);
    CheckEqI(200, SB.Panels[0].Width, 'panel Width scaled');
    CheckEqI(38, SB.Height, 'Height scaled');
    CheckEqS('200,-1', Parts(SB), 'scaled parts sent');
    SB.ScaleBy(1, 2);

    Section('events');
    SB.OnClick := Log.Clicked;
    SB.OnResize := Log.Resized;
    Log.Text := '';
    SendMessageW(SB.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(10, 5));
    SendMessageW(SB.Handle, WM_LBUTTONUP, 0, MakeLParam(10, 5));
    CheckEqS('click;', Log.Text, 'OnClick');
    Log.Text := '';
    F.ClientWidth := 500;
    CheckEqS('resize;', Log.Text, 'OnResize when the form width changes');
    CheckEqS('0,381,500,19', BoundsText(SB), 'still at the bottom');

    Section('streaming');
    Log.Text := '';
    SF := TStatusForm.Create(nil);
    try
      CheckEqI(3, SF.Status.Panels.Count, 'three panels');
      CheckEqI(120, SF.Status.Panels[0].Width, 'panel 0 Width');
      CheckEqS('Ready', SF.Status.Panels[0].Text, 'panel 0 Text');
      CheckEqB(True, SF.Status.Panels[1].Alignment = taCenter, 'panel 1 Alignment');
      CheckEqB(True, SF.Status.Panels[1].Bevel = pbNone, 'panel 1 Bevel');
      CheckEqB(True, SF.Status.Panels[2].Bevel = pbRaised, 'panel 2 Bevel');
      CheckEqI(50, SF.Status.Panels[2].Width, 'panel 2 default Width');
      CheckEqB(False, SF.Status.SizeGrip, 'SizeGrip False');
      CheckEqS('0,276,400,24', BoundsText(SF.Status), 'stored Height 24 at the bottom');
      CheckEqS('120,200,-1', Parts(SF.Status), 'native parts');
      CheckEqS('\tMiddle', PartText(SF.Status, 1), 'native text of panel 1');
      CheckEqB(False, HasStyle(SF.Status, SBARS_SIZEGRIP), 'no grip');
      CheckEqB(True, SF.Simple.Align = alTop, 'Align alTop');
      CheckEqB(True, SF.Simple.SimplePanel, 'SimplePanel');
      CheckEqS('Loading', SF.Simple.SimpleText, 'SimpleText');
      CheckEqS('Loading', PartText(SF.Simple, 0), 'native simple text');
      CheckEqB(False, SF.Simple.ParentFont, 'ParentFont False');
      CheckEqI(-14, SF.Simple.Font.Height, 'Font.Height');
      Check(Assigned(SF.Status.OnClick), 'OnClick bound');
      CheckEqS('', Log.Text, 'no events while loading');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      SB2 := TStatusBar.Create(nil);
      SB2.Panels.Add.Text := 'x';
      SB2.Panels.Add.Text := 'y';
      SB2.Parent := F;
      SB2.HandleNeeded;
      SB2.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
