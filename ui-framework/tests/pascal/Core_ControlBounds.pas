// covers: UIF-024, UIF-200, UIF-210, UIF-212, UIF-217
// opc: -U ..\..\src
program Core_ControlBounds;
{ U-014, core.md section 2 and layout.md UIF-210 steps 1-4 and 8: TControl defaults after Create, SetBounds
  clamps negative sizes, an unchanged call has no side effect at all, OnResize fires only when the size changed
  and never while loading, the Left/Top/Width/Height/BoundsRect/ClientWidth/ClientHeight setters, text storage
  through WM_SETTEXT / WM_GETTEXT / WM_GETTEXTLENGTH performed on the control (CM_TEXTCHANGED once per real change),
  Perform reaching a replaced WindowProc without a window handle, ClientToParent / ParentToClient arithmetic. }
uses
  Windows, Messages, Types, SysUtils, Classes, TypInfo, Graphics, Controls, TestKit;

type
  { exposes protected members of TControl to the test }
  TTestControl = class(TControl)
  public
    Log: string;
    SavedProc: TWndMethod;
    procedure StartLoading;
    procedure EndLoading;
    procedure Resized(Sender: TObject);
    procedure Spy(var Message: TMessage);
    procedure WndProc(var Message: TMessage); override;
  published
    property Caption;
    property Text;
    property Color;
    property Font;
    property ParentColor;
    property ParentFont;
    property AutoSize;
    property OnResize;
  end;

procedure TTestControl.StartLoading;
begin
  Loading;
end;

procedure TTestControl.EndLoading;
begin
  Loaded;
end;

procedure TTestControl.Resized(Sender: TObject);
begin
  Log := Log + 'resize(' + IntToStr(Width) + 'x' + IntToStr(Height) + ');';
end;

procedure TTestControl.Spy(var Message: TMessage);
begin
  if Message.Msg = WM_APP + 7 then
  begin
    Log := Log + 'spy;';
    Message.Result := 42;
  end
  else
    SavedProc(Message);
end;

procedure TTestControl.WndProc(var Message: TMessage);
begin
  if Message.Msg = CM_TEXTCHANGED then
    Log := Log + 'textchanged;';
  inherited WndProc(Message);
end;

function BoundsStr(C: TControl): string;
begin
  Result := '(' + IntToStr(C.Left) + ', ' + IntToStr(C.Top) + ', ' + IntToStr(C.Width) + ', ' + IntToStr(C.Height) + ')';
end;

function RectStr(const R: TRect): string;
begin
  Result := '(' + IntToStr(R.Left) + ', ' + IntToStr(R.Top) + ', ' + IntToStr(R.Right) + ', ' + IntToStr(R.Bottom) + ')';
end;

procedure CheckDefaults;
var
  C: TTestControl;
begin
  Section('defaults (core.md 2.1)');
  C := TTestControl.Create(nil);
  try
    CheckEqS('(0, 0, 0, 0)', BoundsStr(C), 'bounds');
    Check(C.Align = alNone, 'Align');
    Check(C.Anchors = [akLeft, akTop], 'Anchors');
    CheckEqB(False, C.AlignWithMargins, 'AlignWithMargins');
    CheckEqS('3 3 3 3', IntToStr(C.Margins.Left) + ' ' + IntToStr(C.Margins.Top) + ' ' + IntToStr(C.Margins.Right) + ' '
      + IntToStr(C.Margins.Bottom), 'Margins');
    CheckEqI(0, C.Constraints.MaxHeight + C.Constraints.MaxWidth + C.Constraints.MinHeight + C.Constraints.MinWidth,
      'Constraints');
    CheckEqI(crDefault, C.Cursor, 'Cursor');
    CheckEqB(True, C.Enabled, 'Enabled');
    CheckEqS('', C.Hint, 'Hint');
    CheckEqB(True, C.ParentShowHint, 'ParentShowHint');
    CheckEqB(False, C.ShowHint, 'ShowHint');
    CheckEqB(True, C.Visible, 'Visible');
    CheckEqB(True, C.Parent = nil, 'Parent');
    Check(C.ControlStyle = [csCaptureMouse, csClickEvents, csSetCaption, csDoubleClicks], 'ControlStyle');
    Check(C.ControlState = [], 'ControlState');
    CheckEqS('', C.Caption, 'Caption');
    CheckEqI(clWindow, C.Color, 'Color');
    CheckEqB(True, C.ParentColor, 'ParentColor');
    CheckEqB(True, C.ParentFont, 'ParentFont');
    CheckEqI(-12, C.Font.Height, 'Font.Height');
    CheckEqS('Segoe UI', C.Font.Name, 'Font.Name');
    CheckEqB(False, C.AutoSize, 'AutoSize');
    CheckEqB(False, C.Showing, 'not Showing without a parent');

    Section('stored rules (UIF-024)');
    CheckEqB(True, IsStoredProp(C, 'Left') and IsStoredProp(C, 'Top') and IsStoredProp(C, 'Width')
      and IsStoredProp(C, 'Height'), 'bounds are stored');
    CheckEqB(True, GetPropInfo(C, 'Width')^.Default = Int64($8000000000000000), 'Width has no default');
    CheckEqB(False, IsStoredProp(C, 'ShowHint'), 'ShowHint not stored while ParentShowHint');
    C.ShowHint := True;
    CheckEqB(False, C.ParentShowHint, 'ShowHint clears ParentShowHint');
    CheckEqB(True, IsStoredProp(C, 'ShowHint'), 'ShowHint stored');
    CheckEqB(False, IsStoredProp(C, 'Color'), 'Color not stored while ParentColor');
    C.Color := clRed;
    CheckEqB(False, C.ParentColor, 'Color clears ParentColor');
    CheckEqB(True, IsStoredProp(C, 'Color'), 'Color stored');
    CheckEqB(False, IsStoredProp(C, 'Font'), 'Font not stored while ParentFont');
    C.Font.Height := -16;
    CheckEqB(False, C.ParentFont, 'Font change clears ParentFont');
    CheckEqB(True, IsStoredProp(C, 'Font'), 'Font stored');
  finally
    C.Free;
  end;
end;

procedure CheckSetBounds;
var
  C: TTestControl;
begin
  Section('SetBounds (UIF-210)');
  C := TTestControl.Create(nil);
  try
    C.OnResize := C.Resized;
    C.SetBounds(10, 20, 100, 50);
    CheckEqS('(10, 20, 100, 50)', BoundsStr(C), 'stored');
    CheckEqS('resize(100x50);', C.Log, 'size change fires OnResize');
    C.Log := '';
    C.SetBounds(10, 20, 100, 50);
    CheckEqS('', C.Log, 'unchanged call fires nothing');
    C.SetBounds(30, 40, 100, 50);
    CheckEqS('(30, 40, 100, 50)', BoundsStr(C), 'moved');
    CheckEqS('', C.Log, 'a move does not fire OnResize');
    C.SetBounds(-5, -6, -1, -100);
    CheckEqS('(-5, -6, 0, 0)', BoundsStr(C), 'negative size clamped, negative position kept');
    CheckEqS('resize(0x0);', C.Log, 'clamped size change');
    C.Log := '';
    C.SetBounds(-5, -6, -7, 0);
    CheckEqS('', C.Log, 'clamping to the current size is unchanged');

    Section('setters (UIF-212, UIF-217)');
    C.Left := 1;
    C.Top := 2;
    C.Width := 3;
    C.Height := 4;
    CheckEqS('(1, 2, 3, 4)', BoundsStr(C), 'Left/Top/Width/Height');
    C.Width := -3;
    CheckEqI(0, C.Width, 'Width never negative');
    C.BoundsRect := Rect(5, 6, 25, 46);
    CheckEqS('(5, 6, 20, 40)', BoundsStr(C), 'BoundsRect setter');
    CheckEqS('(5, 6, 25, 46)', RectStr(C.BoundsRect), 'BoundsRect getter');
    CheckEqS('(0, 0, 20, 40)', RectStr(C.ClientRect), 'ClientRect');
    C.ClientWidth := 30;
    C.ClientHeight := 10;
    CheckEqS('(5, 6, 30, 10)', BoundsStr(C), 'ClientWidth/ClientHeight adjust the size');

    Section('loading (UIF-210 step 8)');
    C.Log := '';
    C.StartLoading;
    CheckEqB(True, csLoading in C.ComponentState, 'csLoading set');
    C.SetBounds(0, 0, 77, 88);
    CheckEqS('(0, 0, 77, 88)', BoundsStr(C), 'stored while loading');
    CheckEqS('', C.Log, 'no OnResize while loading');
    C.EndLoading;
    CheckEqB(False, csLoading in C.ComponentState, 'csLoading cleared');
    C.Width := 78;
    CheckEqS('resize(78x88);', C.Log, 'OnResize after loading');
  finally
    C.Free;
  end;
end;

procedure CheckText;
var
  C: TTestControl;
  Buf: array[0..15] of Char;
  N: Integer;
begin
  Section('text storage');
  C := TTestControl.Create(nil);
  try
    C.Caption := 'Hello';
    CheckEqS('textchanged;', C.Log, 'CM_TEXTCHANGED on change');
    CheckEqS('Hello', C.Text, 'Text and Caption are one value');
    C.Log := '';
    C.Text := 'Hello';
    CheckEqS('', C.Log, 'unchanged text sends nothing');
    CheckEqI(5, C.GetTextLen, 'GetTextLen');
    CheckEqI(5, C.Perform(WM_GETTEXTLENGTH, 0, 0), 'WM_GETTEXTLENGTH');
    N := C.GetTextBuf(@Buf[0], 4);
    CheckEqI(3, N, 'GetTextBuf truncates to BufSize - 1');
    CheckEqS('Hel', string(PChar(@Buf[0])), 'truncated text is terminated');
    C.SetTextBuf('World');
    CheckEqS('World', C.Caption, 'SetTextBuf');
    CheckEqI(1, C.Perform(WM_SETTEXT, 0, LPARAM(PChar('Abc'))), 'WM_SETTEXT result');
    CheckEqS('Abc', C.Text, 'WM_SETTEXT stores');
    C.Caption := '';
    CheckEqI(0, C.GetTextLen, 'empty');

    Section('Perform and WindowProc (core.md 2.2)');
    CheckEqB(True, TMethod(C.WindowProc).Code <> nil, 'WindowProc assigned');
    C.SavedProc := C.WindowProc;
    C.WindowProc := C.Spy;
    C.Log := '';
    CheckEqI(42, C.Perform(WM_APP + 7, 1, 2), 'Perform returns the result');
    CheckEqS('spy;', C.Log, 'replaced WindowProc sees the message');
    C.Caption := 'X';
    CheckEqS('X', C.Caption, 'other messages pass through');
    C.WindowProc := C.SavedProc;
  finally
    C.Free;
  end;
end;

begin
  CheckDefaults;
  CheckSetBounds;
  CheckText;
  Done;
end.
