// covers: UIF-810
// needs: desktop
program Ctl_ProgressTrack;
{ U-085, controls-standard.md sections 23 and 24. TProgressBar: defaults (150 x 17), the sub-classed
  msctls_progress32 window with PBS_SMOOTH / PBS_VERTICAL / PBS_MARQUEE re-creating the window and keeping the state,
  range / step / position pushed after creation, the range error, the position clamp, StepIt wrapping to Min and
  StepBy clamped, mouse events, a stored range applied after loading. TTrackBar: defaults (150 x 45, a tab stop),
  the msctls_trackbar32 window with the orientation and tick styles, every property pushed after creation, the
  range error, OnChange from the user's scrolling (CN_HSCROLL / CN_VSCROLL, UIF-810) only when the position
  changed, and from programmatic changes of the position (also by a range clamp); streaming; leak checks.
  The user is simulated with key messages sent to the track bar's window. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, CommCtrl, ComCtrls,
  UIConsts, TestKit, KeyStateKit;

type
  TWinAccess = class(TWinControl);

  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
    procedure Clicked(Sender: TObject);
  end;

  TBarsForm = class(TForm)
  published
    Progress: TProgressBar;
    Odd: TProgressBar;
    Track: TTrackBar;
    procedure TrackChange(Sender: TObject);
  end;

{$FORM Ctl_ProgressTrack.form}

var
  Log: TLog;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change:' + IntToStr(TTrackBar(Sender).Position) + ';';
end;

procedure TLog.Clicked(Sender: TObject);
begin
  Text := Text + 'click;';
end;

procedure TBarsForm.TrackChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed change;';
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

function HasStyle(C: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(C.Handle, GWL_STYLE) and Bits) = Bits;
end;

function PBState(P: TProgressBar): string;
begin
  Result := Format('%d..%d pos %d step %d', [
    Integer(SendMessageW(P.Handle, PBM_GETRANGE, 1, 0)), Integer(SendMessageW(P.Handle, PBM_GETRANGE, 0, 0)),
    Integer(SendMessageW(P.Handle, PBM_GETPOS, 0, 0)), Integer(SendMessageW(P.Handle, PBM_GETSTEP, 0, 0))]);
end;

function TBState(T: TTrackBar): string;
begin
  Result := Format('%d..%d pos %d line %d page %d', [
    Integer(SendMessageW(T.Handle, TBM_GETRANGEMIN, 0, 0)), Integer(SendMessageW(T.Handle, TBM_GETRANGEMAX, 0, 0)),
    Integer(SendMessageW(T.Handle, TBM_GETPOS, 0, 0)), Integer(SendMessageW(T.Handle, TBM_GETLINESIZE, 0, 0)),
    Integer(SendMessageW(T.Handle, TBM_GETPAGESIZE, 0, 0))]);
end;

function NumTics(T: TTrackBar): Integer;
begin
  Result := Integer(SendMessageW(T.Handle, TBM_GETNUMTICS, 0, 0));
end;

procedure Key(C: TWinControl; K: Word);
begin
  SendKeyMessage(C.Handle, WM_KEYDOWN, K, 1);
  SendKeyMessage(C.Handle, WM_KEYUP, K, LPARAM($C0000001));
end;

function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure ProgressBar(F: TForm);
var
  P: TProgressBar;
  H: HWND;
  Msg: string;
begin
  Section('progress bar: defaults');
  P := TProgressBar.Create(F);
  CheckEqI(150, P.Width, 'Width 150');
  CheckEqI(17, P.Height, 'Height 17');
  CheckEqI(100, P.Max, 'Max 100');
  CheckEqI(0, P.Min, 'Min 0');
  CheckEqB(True, P.Orientation = pbHorizontal, 'Orientation pbHorizontal');
  CheckEqI(0, P.Position, 'Position 0');
  CheckEqB(False, P.Smooth, 'Smooth');
  CheckEqI(10, P.Step, 'Step 10');
  CheckEqB(True, P.Style = pbstNormal, 'Style pbstNormal');
  CheckEqB(False, TWinAccess(P).TabStop, 'not a tab stop');
  Check(GetClass('TProgressBar') <> nil, 'TProgressBar registered');

  Section('progress bar: handle-less state');
  P.Min := 10;
  P.Max := 50;
  P.Position := 30;
  P.Step := 5;
  P.Parent := F;
  H := P.Handle;
  CheckEqS('msctls_progress32', ClassOf(H), 'window class msctls_progress32');
  CheckEqS('10..50 pos 30 step 5', PBState(P), 'range, position and step pushed');
  CheckEqB(False, HasStyle(P, PBS_SMOOTH), 'no PBS_SMOOTH');
  CheckEqB(False, HasStyle(P, PBS_VERTICAL), 'no PBS_VERTICAL');
  CheckEqB(False, HasStyle(P, PBS_MARQUEE), 'no PBS_MARQUEE');
  CheckEqB(False, HasStyle(P, WS_TABSTOP), 'no WS_TABSTOP');

  Section('progress bar: recreating styles');
  P.Smooth := True;
  CheckEqB(True, P.Handle <> H, 'Smooth recreates the window');
  Check(HasStyle(P, PBS_SMOOTH), 'PBS_SMOOTH');
  CheckEqS('10..50 pos 30 step 5', PBState(P), 'state kept');
  H := P.Handle;
  P.Orientation := pbVertical;
  CheckEqB(True, P.Handle <> H, 'Orientation recreates the window');
  Check(HasStyle(P, PBS_VERTICAL), 'PBS_VERTICAL');
  CheckEqS('10..50 pos 30 step 5', PBState(P), 'state kept after Orientation');
  H := P.Handle;
  P.Style := pbstMarquee;
  CheckEqB(True, P.Handle <> H, 'Style recreates the window');
  Check(HasStyle(P, PBS_MARQUEE), 'PBS_MARQUEE');
  P.Style := pbstNormal;
  CheckEqB(False, HasStyle(P, PBS_MARQUEE), 'pbstNormal: no PBS_MARQUEE');
  P.Orientation := pbHorizontal;
  P.Smooth := False;

  Section('progress bar: range and position');
  Msg := '';
  try
    P.Max := 5;
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS(SProgressRange, Msg, 'Max < Min raises');
  CheckEqS('Progress bar range is invalid', Msg, 'the message');
  CheckEqI(50, P.Max, 'Max unchanged');
  Msg := '';
  try
    P.Min := 60;
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS(SProgressRange, Msg, 'Min > Max raises');
  CheckEqI(10, P.Min, 'Min unchanged');
  P.Position := 70;
  CheckEqI(50, P.Position, 'clamped to Max');
  CheckEqS('10..50 pos 50 step 5', PBState(P), 'PBM_SETPOS');
  P.Position := -3;
  CheckEqI(10, P.Position, 'clamped to Min');
  P.Position := 30;
  P.Max := 20;
  CheckEqI(20, P.Position, 'a smaller Max clamps the position');
  CheckEqS('10..20 pos 20 step 5', PBState(P), 'pushed');
  P.Max := 50;
  P.Step := 7;
  CheckEqS('10..50 pos 20 step 7', PBState(P), 'PBM_SETSTEP');
  P.Step := 5;

  Section('progress bar: StepIt and StepBy');
  P.Position := 40;
  P.StepIt;
  CheckEqI(45, P.Position, 'StepIt adds Step');
  P.StepIt;
  CheckEqI(50, P.Position, 'StepIt up to Max');
  P.StepIt;
  CheckEqI(10, P.Position, 'StepIt beyond Max wraps to Min');
  CheckEqS('10..50 pos 10 step 5', PBState(P), 'pushed');
  P.StepBy(15);
  CheckEqI(25, P.Position, 'StepBy');
  P.StepBy(100);
  CheckEqI(50, P.Position, 'StepBy clamped to Max');
  P.StepBy(-100);
  CheckEqI(10, P.Position, 'StepBy clamped to Min');

  Section('progress bar: events');
  P.OnClick := Log.Clicked;
  Log.Text := '';
  SendMessageW(P.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(5, 5));
  SendMessageW(P.Handle, WM_LBUTTONUP, 0, MakeLParam(5, 5));
  CheckEqS('click;', Log.Text, 'OnClick');
  P.Free;
end;

procedure TrackBar(F: TForm);
var
  T: TTrackBar;
  H: HWND;
  Msg: string;
begin
  Section('track bar: defaults');
  T := TTrackBar.Create(F);
  CheckEqI(150, T.Width, 'Width 150');
  CheckEqI(45, T.Height, 'Height 45');
  CheckEqI(1, T.Frequency, 'Frequency 1');
  CheckEqI(1, T.LineSize, 'LineSize 1');
  CheckEqI(10, T.Max, 'Max 10');
  CheckEqI(0, T.Min, 'Min 0');
  CheckEqB(True, T.Orientation = trHorizontal, 'Orientation trHorizontal');
  CheckEqI(2, T.PageSize, 'PageSize 2');
  CheckEqI(0, T.Position, 'Position 0');
  CheckEqI(-1, T.TabOrder, 'TabOrder -1');
  CheckEqB(True, T.TabStop, 'TabStop');
  CheckEqB(True, T.TickMarks = tmBottomRight, 'TickMarks tmBottomRight');
  CheckEqB(True, T.TickStyle = tsAuto, 'TickStyle tsAuto');
  Check(GetClass('TTrackBar') <> nil, 'TTrackBar registered');

  Section('track bar: handle-less state');
  T.OnChange := Log.Changed;
  Log.Text := '';
  T.Min := 2;
  T.Max := 20;
  T.Position := 7;
  T.LineSize := 2;
  T.PageSize := 5;
  T.Frequency := 3;
  CheckEqS('change:2;change:7;', Log.Text, 'OnChange without a window: the clamp by Min, then Position');
  T.Parent := F;
  Log.Text := '';
  H := T.Handle;
  CheckEqS('', Log.Text, 'no OnChange while the window is created');
  CheckEqS('msctls_trackbar32', ClassOf(H), 'window class msctls_trackbar32');
  CheckEqS('2..20 pos 7 line 2 page 5', TBState(T), 'range, position, line and page size pushed');
  CheckEqI(7, NumTics(T), 'Frequency 3 over 2..20: ticks at 2, 5, ..., 20');
  Check(HasStyle(T, TBS_AUTOTICKS), 'tsAuto: TBS_AUTOTICKS');
  CheckEqB(False, HasStyle(T, TBS_VERT), 'horizontal');
  CheckEqB(False, HasStyle(T, TBS_TOP), 'tmBottomRight: no TBS_TOP');
  CheckEqB(False, HasStyle(T, TBS_BOTH), 'tmBottomRight: no TBS_BOTH');
  CheckEqB(False, HasStyle(T, TBS_NOTICKS), 'no TBS_NOTICKS');
  Check(HasStyle(T, WS_TABSTOP), 'WS_TABSTOP');

  Section('track bar: recreating styles');
  T.Orientation := trVertical;
  CheckEqB(True, T.Handle <> H, 'Orientation recreates the window');
  Check(HasStyle(T, TBS_VERT), 'TBS_VERT');
  CheckEqS('2..20 pos 7 line 2 page 5', TBState(T), 'state kept');
  CheckEqS('', Log.Text, 'recreating fires no OnChange');
  H := T.Handle;
  T.TickMarks := tmTopLeft;
  CheckEqB(True, T.Handle <> H, 'TickMarks recreates the window');
  Check(HasStyle(T, TBS_LEFT), 'tmTopLeft vertical: TBS_LEFT');
  T.TickMarks := tmBoth;
  Check(HasStyle(T, TBS_BOTH), 'tmBoth: TBS_BOTH');
  H := T.Handle;
  T.TickStyle := tsNone;
  CheckEqB(True, T.Handle <> H, 'TickStyle recreates the window');
  Check(HasStyle(T, TBS_NOTICKS), 'tsNone: TBS_NOTICKS');
  CheckEqB(False, HasStyle(T, TBS_AUTOTICKS), 'tsNone: no TBS_AUTOTICKS');
  T.TickStyle := tsManual;
  CheckEqB(False, HasStyle(T, TBS_NOTICKS), 'tsManual: no TBS_NOTICKS');
  CheckEqB(False, HasStyle(T, TBS_AUTOTICKS), 'tsManual: no TBS_AUTOTICKS');
  CheckEqI(2, NumTics(T), 'tsManual: only the end ticks');
  T.TickStyle := tsAuto;
  T.TickMarks := tmBottomRight;

  Section('track bar: the user scrolls (UIF-810)');
  Log.Text := '';
  Key(T, VK_DOWN);
  CheckEqS('change:9;', Log.Text, 'vertical: Down moves by LineSize, CN_VSCROLL, one OnChange');
  CheckEqI(9, T.Position, 'Position follows the window');
  Log.Text := '';
  Key(T, VK_NEXT);
  CheckEqS('change:14;', Log.Text, 'Page Down moves by PageSize');
  Log.Text := '';
  Key(T, VK_END);
  CheckEqS('change:20;', Log.Text, 'End: Max');
  Log.Text := '';
  Key(T, VK_END);
  CheckEqS('', Log.Text, 'no change: no OnChange');
  T.Orientation := trHorizontal;
  Log.Text := '';
  Key(T, VK_HOME);
  CheckEqS('change:2;', Log.Text, 'horizontal: Home, CN_HSCROLL');
  Log.Text := '';
  Key(T, VK_RIGHT);
  CheckEqS('change:4;', Log.Text, 'horizontal: Right');

  Section('track bar: programmatic changes');
  Log.Text := '';
  T.Position := 10;
  CheckEqS('change:10;', Log.Text, 'Position := fires OnChange');
  CheckEqS('2..20 pos 10 line 2 page 5', TBState(T), 'TBM_SETPOS');
  Log.Text := '';
  T.Position := 10;
  CheckEqS('', Log.Text, 'same value: nothing');
  T.Position := 99;
  CheckEqS('change:20;', Log.Text, 'clamped to Max');
  Log.Text := '';
  T.Max := 15;
  CheckEqS('change:15;', Log.Text, 'a smaller Max clamps the position and fires OnChange');
  CheckEqS('2..15 pos 15 line 2 page 5', TBState(T), 'range pushed');
  Log.Text := '';
  T.Max := 30;
  CheckEqS('', Log.Text, 'a range change keeping the position: nothing');
  T.Frequency := 7;
  CheckEqI(5, NumTics(T), 'TBM_SETTICFREQ: ticks at 2, 9, 16, 23, 30');
  T.LineSize := 4;
  T.PageSize := 6;
  CheckEqS('2..30 pos 15 line 4 page 6', TBState(T), 'line and page size pushed');
  Msg := '';
  try
    T.Max := 1;
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS(STrackBarRange, Msg, 'Max < Min raises');
  CheckEqS('Track bar range is invalid', Msg, 'the message');
  CheckEqI(30, T.Max, 'Max unchanged');
  Msg := '';
  try
    T.Min := 31;
  except
    on E: EInvalidOperation do
      Msg := E.Message;
  end;
  CheckEqS(STrackBarRange, Msg, 'Min > Max raises');
  T.Free;
end;

procedure Run;
var
  F: TForm;
  SF: TBarsForm;
  P: TProgressBar;
  T: TTrackBar;
  I, Before: Integer;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    ProgressBar(F);
    TrackBar(F);

    Section('streaming');
    Log.Text := '';
    SF := TBarsForm.Create(nil);
    try
      CheckEqI(200, SF.Progress.Max, 'progress Max');
      CheckEqI(20, SF.Progress.Min, 'progress Min');
      CheckEqI(150, SF.Progress.Position, 'progress Position');
      CheckEqB(True, SF.Progress.Smooth, 'progress Smooth');
      CheckEqI(7, SF.Progress.Step, 'progress Step');
      CheckEqB(True, SF.Progress.Orientation = pbVertical, 'progress Orientation');
      CheckEqS('20..200 pos 150 step 7', PBState(SF.Progress), 'progress window state');
      CheckEqI(10, SF.Odd.Min, 'a stored Max below Min: Min kept');
      CheckEqI(10, SF.Odd.Max, 'Max becomes Min, no error');
      CheckEqI(10, SF.Odd.Position, 'position clamped');
      CheckEqB(True, SF.Odd.Style = pbstMarquee, 'progress Style');
      CheckEqI(5, SF.Track.Frequency, 'track Frequency');
      CheckEqI(3, SF.Track.LineSize, 'track LineSize');
      CheckEqI(50, SF.Track.Max, 'track Max');
      CheckEqI(-10, SF.Track.Min, 'track Min');
      CheckEqB(True, SF.Track.Orientation = trVertical, 'track Orientation');
      CheckEqI(10, SF.Track.PageSize, 'track PageSize');
      CheckEqI(25, SF.Track.Position, 'track Position');
      CheckEqB(True, SF.Track.TickMarks = tmBoth, 'track TickMarks');
      CheckEqB(True, SF.Track.TickStyle = tsManual, 'track TickStyle');
      CheckEqB(False, SF.Track.TabStop, 'track TabStop');
      CheckEqS('-10..50 pos 25 line 3 page 10', TBState(SF.Track), 'track window state');
      Check(Assigned(SF.Track.OnChange), 'OnChange bound');
      CheckEqS('', Log.Text, 'no events while loading');
    finally
      SF.Free;
    end;

    Section('leak');
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      P := TProgressBar.Create(nil);
      P.Position := 30;
      P.Parent := F;
      P.HandleNeeded;
      P.Free;
      T := TTrackBar.Create(nil);
      T.Position := 3;
      T.Parent := F;
      T.HandleNeeded;
      T.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances of each with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
