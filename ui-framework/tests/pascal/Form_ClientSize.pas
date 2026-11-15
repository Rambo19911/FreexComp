// covers: UIF-271, UIF-272, UIF-273
// opc: -U ..\..\src
// needs: desktop
program Form_ClientSize;
{ U-037, layout.md 10: a form keeps its client size; outer size = client size + the frame of
  AdjustWindowRectExForDpi with the form's current style properties and PixelsPerInch. Without a handle Width/Height
  are computed, Width := V sets ClientWidth := V - frame width; a BorderStyle change keeps the client size; the window
  is created with the outer size and its client area equals the stored client size; after the window is created or
  resized by the system the real client size is adopted and a layout pass runs. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

const
  Clip = WS_CLIPCHILDREN or WS_CLIPSIBLINGS;
  Sizeable = WS_CAPTION or WS_THICKFRAME or WS_SYSMENU or WS_MINIMIZEBOX or WS_MAXIMIZEBOX or Clip;
  Dialog = WS_POPUP or WS_CAPTION or WS_SYSMENU or Clip;

type
  TPanelLike = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

constructor TPanelLike.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

var
  FrameW, FrameH: Integer;

procedure Frame(Style, ExStyle: Cardinal; DPI: Integer);
var
  R: TRect;
begin
  R := Rect(0, 0, 0, 0);
  AdjustWindowRectExForDpi(R, Style, False, ExStyle, DPI);
  FrameW := R.Right - R.Left;
  FrameH := R.Bottom - R.Top;
end;

procedure Run;
var
  F: TForm;
  P: TPanelLike;
  R: TRect;
begin
  F := TForm.CreateNew(nil);
  try
    Section('without a handle (UIF-273)');
    Frame(Sizeable, WS_EX_CONTROLPARENT, F.PixelsPerInch);
    Check(FrameW > 0, 'a sizeable form has a frame');
    CheckEqI(320, F.ClientWidth, 'ClientWidth');
    CheckEqI(240, F.ClientHeight, 'ClientHeight');
    CheckEqI(320 + FrameW, F.Width, 'Width = client + frame');
    CheckEqI(240 + FrameH, F.Height, 'Height = client + frame');
    F.Width := 500;
    CheckEqI(500 - FrameW, F.ClientWidth, 'Width := V sets ClientWidth := V - frame');
    F.Height := 400;
    CheckEqI(400 - FrameH, F.ClientHeight, 'Height := V sets ClientHeight := V - frame');
    F.ClientWidth := 300;
    F.ClientHeight := 200;
    CheckEqI(300 + FrameW, F.Width, 'ClientWidth := V: Width follows');
    F.BorderStyle := bsNone;
    CheckEqI(300, F.ClientWidth, 'BorderStyle change keeps ClientWidth');
    CheckEqI(200, F.ClientHeight, 'BorderStyle change keeps ClientHeight');
    CheckEqI(300, F.Width, 'bsNone: no frame');
    F.BorderStyle := bsDialog;
    Frame(Dialog, WS_EX_DLGMODALFRAME or WS_EX_CONTROLPARENT, F.PixelsPerInch);
    CheckEqI(300 + FrameW, F.Width, 'bsDialog frame');
    CheckEqI(200 + FrameH, F.Height, 'bsDialog frame height');
    F.BorderStyle := bsSizeable;
    Frame(Sizeable, WS_EX_CONTROLPARENT, F.PixelsPerInch);

    Section('the window (UIF-272)');
    P := TPanelLike.Create(F);
    P.Parent := F;
    P.Align := alClient;
    CheckEqI(300, P.Width, 'alClient child: client width');
    F.HandleNeeded;
    CheckEqI(GetDpiForWindow(F.Handle), F.PixelsPerInch, 'PixelsPerInch: the window DPI');
    Windows.GetClientRect(F.Handle, R);
    CheckEqI(300, R.Right, 'window client width = ClientWidth');
    CheckEqI(200, R.Bottom, 'window client height = ClientHeight');
    GetWindowRect(F.Handle, R);
    CheckEqI(F.Width, R.Right - R.Left, 'window width = Width');
    CheckEqI(F.Height, R.Bottom - R.Top, 'window height = Height');
    F.ClientWidth := 350;
    Windows.GetClientRect(F.Handle, R);
    CheckEqI(350, R.Right, 'ClientWidth with a handle resizes the window');
    CheckEqI(350, P.Width, 'layout follows');

    Section('real client size adopted');
    SetWindowPos(F.Handle, 0, 0, 0, 420 + FrameW, 330 + FrameH, SWP_NOMOVE or SWP_NOZORDER or SWP_NOACTIVATE);
    Windows.GetClientRect(F.Handle, R);
    CheckEqI(R.Right, F.ClientWidth, 'external resize: real client width');
    CheckEqI(R.Bottom, F.ClientHeight, 'external resize: real client height');
    CheckEqI(R.Right, P.Width, 'layout pass: child width');
    CheckEqI(R.Bottom, P.Height, 'layout pass: child height');
    GetWindowRect(F.Handle, R);
    CheckEqI(R.Right - R.Left, F.Width, 'outer width follows the window');
  finally
    F.Free;
  end;

  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 10;
    F.ClientHeight := 10;
    F.HandleNeeded;
    Windows.GetClientRect(F.Handle, R);
    CheckEqI(R.Right, F.ClientWidth, 'tiny form: ClientWidth is the real client width');
    CheckEqI(R.Bottom, F.ClientHeight, 'tiny form: ClientHeight is the real client height');
    GetWindowRect(F.Handle, R);
    CheckEqI(R.Right - R.Left, F.Width, 'tiny form: Width is the real width');
  finally
    F.Free;
  end;
end;

begin
  Run;
  Done;
end.
