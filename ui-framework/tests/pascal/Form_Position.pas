// covers: UIF-436
// opc: -U ..\..\src
// needs: desktop
program Form_Position;
{ U-039, application-forms.md UIF-436 and 4.2 MakeFullyVisible: Position is applied once, at the first show:
  poDesigned keeps Left/Top unless the window shows less than 32 x 32 px of any work area (then MakeFullyVisible on
  the primary monitor); poDefault / poDefaultPosOnly / poDefaultSizeOnly let Windows choose position and/or size
  (the bounds and the client size are then taken from the window); poScreenCenter centres in the work area of the
  monitor under the mouse, poDesktopCenter in the virtual screen, poMainFormCenter over the main form (screen centre
  without one), poOwnerFormCenter over the owner form (else as poMainFormCenter). MakeFullyVisible moves, never
  resizes, the window into the work area. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TMainForm = class(TForm)
  public
    constructor Create(AOwner: TComponent); override;
  end;

var
  MainForm: TMainForm;

constructor TMainForm.Create(AOwner: TComponent);
begin
  CreateNew(AOwner);
  Name := 'Main';
end;

function NewForm(AOwner: TComponent; APosition: TPosition): TForm;
begin
  Result := TForm.CreateNew(AOwner);
  Result.BorderStyle := bsSingle;
  Result.ClientWidth := 300;
  Result.ClientHeight := 200;
  Result.Left := 150;
  Result.Top := 140;
  Result.Position := APosition;
end;

function CursorWorkArea: TRect;
var
  P: TPoint;
  Info: TMonitorInfo;
begin
  GetCursorPos(P);
  FillChar(Info, SizeOf(Info), 0);
  Info.cbSize := SizeOf(Info);
  GetMonitorInfoW(MonitorFromPoint(P, MONITOR_DEFAULTTONEAREST), Info);
  Result := Info.rcWork;
end;

procedure CheckWindow(F: TForm; const Name: string);
var
  R: TRect;
begin
  GetWindowRect(F.Handle, R);
  CheckEqB(True, (R.Left = F.Left) and (R.Top = F.Top) and (R.Right - R.Left = F.Width)
    and (R.Bottom - R.Top = F.Height), Name + ': the window has the bounds');
end;

procedure Run;
var
  F: TForm;
  WA, D, R: TRect;
  W, H, I: Integer;
  Primary: TMonitor;
begin
  Application.Initialize;                                    { per-monitor DPI awareness before any metric }
  WA := Screen.WorkAreaRect;
  Primary := nil;
  for I := 0 to Screen.MonitorCount - 1 do
    if Screen.Monitors[I].Primary then
      Primary := Screen.Monitors[I];
  Application.CreateForm(TMainForm, MainForm);
  try
    Section('poDesigned');
    F := NewForm(nil, poDesigned);
    try
      F.Show;
      CheckEqI(150, F.Left, 'Left kept');
      CheckEqI(140, F.Top, 'Top kept');
      CheckWindow(F, 'poDesigned');
    finally
      F.Free;
    end;
    F := NewForm(nil, poDesigned);
    try
      F.Left := -20000;
      F.Top := -20000;
      F.Show;
      CheckEqI(WA.Left, F.Left, 'off screen: moved into the primary work area (left)');
      CheckEqI(WA.Top, F.Top, 'off screen: moved into the primary work area (top)');
      CheckEqI(300, F.ClientWidth, 'never resized');
    finally
      F.Free;
    end;
    F := NewForm(nil, poDesigned);
    try
      F.Left := WA.Right - 40;
      F.Top := WA.Bottom - 40;
      F.Show;
      CheckEqI(WA.Right - 40, F.Left, '40 x 40 px visible: kept (left)');
      CheckEqI(WA.Bottom - 40, F.Top, '40 x 40 px visible: kept (top)');
    finally
      F.Free;
    end;

    Section('centred');
    F := NewForm(nil, poScreenCenter);
    try
      F.Show;
      R := CursorWorkArea;
      CheckEqI(R.Left + (R.Right - R.Left - F.Width) div 2, F.Left, 'poScreenCenter: Left');
      CheckEqI(R.Top + (R.Bottom - R.Top - F.Height) div 2, F.Top, 'poScreenCenter: Top');
      CheckWindow(F, 'poScreenCenter');
    finally
      F.Free;
    end;
    F := NewForm(nil, poDesktopCenter);
    try
      F.Show;
      D := Screen.DesktopRect;
      CheckEqI(D.Left + (D.Right - D.Left - F.Width) div 2, F.Left, 'poDesktopCenter: Left');
      CheckEqI(D.Top + (D.Bottom - D.Top - F.Height) div 2, F.Top, 'poDesktopCenter: Top');
    finally
      F.Free;
    end;
    F := NewForm(nil, poMainFormCenter);
    try
      F.Show;
      R := CursorWorkArea;
      CheckEqI(R.Left + (R.Right - R.Left - F.Width) div 2, F.Left, 'poMainFormCenter, no main form window: screen');
    finally
      F.Free;
    end;
    MainForm.BorderStyle := bsSingle;
    MainForm.ClientWidth := 600;
    MainForm.ClientHeight := 400;
    MainForm.Left := WA.Left + 60;
    MainForm.Top := WA.Top + 50;
    MainForm.Show;
    F := NewForm(nil, poMainFormCenter);
    try
      F.Show;
      CheckEqI(MainForm.Left + (MainForm.Width - F.Width) div 2, F.Left, 'poMainFormCenter: Left');
      CheckEqI(MainForm.Top + (MainForm.Height - F.Height) div 2, F.Top, 'poMainFormCenter: Top');
    finally
      F.Free;
    end;
    F := NewForm(MainForm, poOwnerFormCenter);
    try
      F.Show;
      CheckEqI(MainForm.Left + (MainForm.Width - F.Width) div 2, F.Left, 'poOwnerFormCenter: Left');
      CheckEqI(MainForm.Top + (MainForm.Height - F.Height) div 2, F.Top, 'poOwnerFormCenter: Top');
    finally
      F.Free;
    end;
    F := NewForm(nil, poOwnerFormCenter);
    try
      F.Show;
      CheckEqI(MainForm.Left + (MainForm.Width - F.Width) div 2, F.Left, 'poOwnerFormCenter without owner: main form');
    finally
      F.Free;
    end;
    MainForm.Left := WA.Right - 200;
    F := NewForm(nil, poMainFormCenter);
    try
      F.Show;
      R := F.Monitor.WorkareaRect;
      Check((F.Left >= R.Left) and (F.Left + F.Width <= R.Right), 'poMainFormCenter: then MakeFullyVisible');
    finally
      F.Free;
    end;

    Section('applied once');
    F := NewForm(nil, poDesktopCenter);
    try
      F.Show;
      F.Hide;
      F.Left := WA.Left + 10;
      F.Show;
      CheckEqI(WA.Left + 10, F.Left, 'second show: Left is free');
    finally
      F.Free;
    end;

    Section('chosen by Windows');
    F := NewForm(nil, poDefaultPosOnly);
    try
      F.Show;
      CheckEqI(300, F.ClientWidth, 'poDefaultPosOnly: size as designed');
      CheckWindow(F, 'poDefaultPosOnly');
    finally
      F.Free;
    end;
    F := NewForm(nil, poDefaultSizeOnly);
    try
      F.Show;
      CheckEqI(150, F.Left, 'poDefaultSizeOnly: Left as designed');
      CheckEqI(140, F.Top, 'poDefaultSizeOnly: Top as designed');
      CheckWindow(F, 'poDefaultSizeOnly');
      GetClientRect(F.Handle, R);
      CheckEqI(R.Right, F.ClientWidth, 'poDefaultSizeOnly: client size from the window');
    finally
      F.Free;
    end;
    F := NewForm(nil, poDefault);
    try
      F.Show;
      CheckWindow(F, 'poDefault');
      GetClientRect(F.Handle, R);
      CheckEqI(R.Right, F.ClientWidth, 'poDefault: client width from the window');
      CheckEqI(R.Bottom, F.ClientHeight, 'poDefault: client height from the window');
    finally
      F.Free;
    end;

    Section('MakeFullyVisible');
    F := NewForm(nil, poDesigned);
    try
      F.Show;
      W := F.Width;
      H := F.Height;
      F.Left := WA.Right - 50;
      F.Top := WA.Bottom - 30;
      F.MakeFullyVisible(Primary);
      CheckEqI(WA.Right - W, F.Left, 'moved left into the work area');
      CheckEqI(WA.Bottom - H, F.Top, 'moved up into the work area');
      CheckEqI(W, F.Width, 'not resized');
      CheckEqI(H, F.Height, 'not resized (height)');
      CheckWindow(F, 'MakeFullyVisible');
      F.Left := WA.Left - 100;
      F.MakeFullyVisible;
      R := F.Monitor.WorkareaRect;
      Check((F.Left >= R.Left) and (F.Left + F.Width <= R.Right) and (F.Top >= R.Top)
        and (F.Top + F.Height <= R.Bottom), 'nil: inside the form''s monitor');
    finally
      F.Free;
    end;
  finally
    MainForm.Free;
  end;
end;

begin
  Run;
  Done;
end.
