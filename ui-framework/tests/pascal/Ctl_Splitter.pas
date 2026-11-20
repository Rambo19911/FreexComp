// covers: UIF-760, UIF-761, UIF-762, UIF-763, UIF-764
// needs: desktop
program Ctl_Splitter;
{ U-068, controls-standard.md section 19: TSplitter defaults (3 x 100, Align alLeft redeclared, AutoSnap, MinSize
  30, rsUpdate, ParentColor); the effective cursor by alignment (UIF-760); the target control (UIF-761) for alLeft,
  alRight and alTop splitters; dragging with mouse messages sent to the parent window (UIF-762): live resize of the
  target, the maximum S0 + Free0 - MinSize, AutoSnap to 0 or MinSize, OnCanResize changing or refusing the size,
  OnMoved on button-up, rsNone inert; Escape restores S0 (UIF-763); the etched line of Beveled (UIF-764); MinSize
  scaled; streaming with the default Align not stored. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, ExtCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    Accept: Boolean;
    Replace: Integer;
    procedure CanResize(Sender: TObject; var NewSize: Integer; var Accept_: Boolean);
    procedure Moved(Sender: TObject);
  end;

  TSplitForm = class(TForm)
  published
    Side: TPanel;
    Bar: TSplitter;
    Body: TPanel;
    procedure BarMoved(Sender: TObject);
  end;

{$FORM Ctl_Splitter.form}

var
  Log: TLog;
  F: TForm;
  Bmp: TBitmap;

procedure TLog.CanResize(Sender: TObject; var NewSize: Integer; var Accept_: Boolean);
begin
  Text := Text + Format('can %d;', [NewSize]);
  Accept_ := Accept;
  if Replace > 0 then
    NewSize := Replace;
end;

procedure TLog.Moved(Sender: TObject);
begin
  Text := Text + 'moved;';
end;

procedure TSplitForm.BarMoved(Sender: TObject);
begin
end;

procedure Down(X, Y: Integer);
begin
  SendMessageW(F.Handle, WM_LBUTTONDOWN, MK_LBUTTON, MakeLParam(X, Y));
end;

procedure Move(X, Y: Integer);
begin
  SendMessageW(F.Handle, WM_MOUSEMOVE, MK_LBUTTON, MakeLParam(X, Y));
end;

procedure Up(X, Y: Integer);
begin
  SendMessageW(F.Handle, WM_LBUTTONUP, 0, MakeLParam(X, Y));
end;

function BoundsStr(C: TControl): string;
begin
  Result := Format('%d,%d,%d,%d', [C.Left, C.Top, C.Width, C.Height]);
end;

function Count(C: TColor; X1, Y1, X2, Y2: Integer): Integer;
var
  X, Y: Integer;
begin
  Result := 0;
  C := ColorToRGB(C);
  for Y := Y1 to Y2 - 1 do
    for X := X1 to X2 - 1 do
      if Bmp.Canvas.Pixels[X, Y] = C then
        Inc(Result);
end;

procedure Capture(AForm: TForm);
begin
  SendMessageW(AForm.Handle, WM_PRINTCLIENT, WPARAM(Bmp.Canvas.Handle), PRF_CLIENT or PRF_ERASEBKGND);
end;

procedure Run;
var
  L, R, T, C: TPanel;
  S, SR, ST: TSplitter;
  SF: TSplitForm;
begin
  Application.Initialize;
  Log := TLog.Create;
  Bmp := TBitmap.Create;
  Bmp.SetSize(400, 200);
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 200;
    S := TSplitter.Create(F);

    Section('defaults');
    CheckEqS('3x100', Format('%dx%d', [S.Width, S.Height]), 'size 3 x 100');
    CheckEqB(True, S.Align = alLeft, 'Align alLeft');
    CheckEqB(True, S.AutoSnap, 'AutoSnap True');
    CheckEqB(False, S.Beveled, 'Beveled False');
    CheckEqB(True, S.Color = clBtnFace, 'Color clBtnFace');
    CheckEqI(30, S.MinSize, 'MinSize 30');
    CheckEqB(True, S.ParentColor, 'ParentColor True');
    CheckEqB(True, S.ResizeStyle = rsUpdate, 'ResizeStyle rsUpdate');
    CheckEqB(True, S.Cursor = crDefault, 'Cursor crDefault');
    Check(GetClass('TSplitter') <> nil, 'registered');

    Section('cursor (UIF-760)');
    CheckEqI(crVSplit, EffectiveCursor(S), 'alLeft: crVSplit');
    S.Align := alRight;
    CheckEqI(crVSplit, EffectiveCursor(S), 'alRight: crVSplit');
    S.Align := alTop;
    CheckEqI(crHSplit, EffectiveCursor(S), 'alTop: crHSplit');
    S.Align := alBottom;
    CheckEqI(crHSplit, EffectiveCursor(S), 'alBottom: crHSplit');
    S.Align := alNone;
    S.Parent := F;
    F.Cursor := crHandPoint;
    CheckEqI(crHandPoint, EffectiveCursor(S), 'alNone: the parent''s cursor');
    F.Cursor := crDefault;
    S.Align := alLeft;
    S.Cursor := crCross;
    CheckEqI(crCross, EffectiveCursor(S), 'own Cursor wins');
    S.Cursor := crDefault;

    Section('target (UIF-761)');
    S.Free;
    L := TPanel.Create(F);
    L.Align := alLeft;
    L.Width := 100;
    L.Parent := F;
    S := TSplitter.Create(F);
    S.Left := 101;
    S.Parent := F;
    C := TPanel.Create(F);
    C.Align := alClient;
    C.Parent := F;
    CheckEqS('100,0,3,200', BoundsStr(S), 'splitter placed after the panel');
    Check(S.FindTarget = L, 'alLeft: the preceding alLeft sibling');
    L.Left := 200;
    Check(S.FindTarget = nil, 'splitter first in its phase: no target');
    CheckEqS('3,0,100,200', BoundsStr(L), 'panel now after the splitter');
    S.Left := 200;
    Check(S.FindTarget = L, 'moved back after the panel');
    L.Visible := False;
    Check(S.FindTarget = nil, 'invisible sibling is no target');
    L.Visible := True;
    S.Left := 101;
    CheckEqS('100,0,3,200', BoundsStr(S), 'splitter after the panel again');

    Section('drag (UIF-762)');
    S.OnCanResize := Log.CanResize;
    S.OnMoved := Log.Moved;
    Log.Accept := True;
    Log.Text := '';
    Down(101, 50);
    Move(151, 50);
    CheckEqI(150, L.Width, 'live resize: S0 + d');
    CheckEqS('150,0,3,200', BoundsStr(S), 'the splitter follows the target');
    Move(201, 50);
    CheckEqI(200, L.Width, 'second move from the new splitter position');
    Move(1000, 50);
    CheckEqI(100 + 297 - 30, L.Width, 'maximum S0 + Free0 - MinSize');
    Move(121, 50);
    CheckEqI(120, L.Width, 'back');
    Move(25, 50);
    CheckEqI(0, L.Width, 'below MinSize with AutoSnap: 0');
    Up(25, 50);
    CheckEqS('can 150;can 200;can 367;can 120;can 0;moved;', Log.Text, 'OnCanResize per change, OnMoved on up');
    Log.Text := '';
    Move(80, 50);
    CheckEqI(0, L.Width, 'no drag after button-up');
    CheckEqS('', Log.Text, 'no events after button-up');
    L.Width := 100;
    S.AutoSnap := False;
    Down(101, 50);
    Move(21, 50);
    CheckEqI(30, L.Width, 'below MinSize without AutoSnap: MinSize');
    Up(21, 50);
    Log.Text := '';
    Log.Accept := False;
    Down(31, 50);
    Move(81, 50);
    CheckEqI(30, L.Width, 'OnCanResize refuses');
    Log.Accept := True;
    Log.Replace := 77;
    Move(91, 50);
    CheckEqI(77, L.Width, 'OnCanResize replaces NewSize');
    Log.Replace := 0;
    Up(91, 50);
    CheckEqS('can 80;can 90;moved;', Log.Text, 'OnCanResize log');

    Section('Escape (UIF-763)');
    Log.Text := '';
    Down(78, 50);
    Move(128, 50);
    CheckEqI(127, L.Width, 'dragged');
    PostMessageW(F.Handle, WM_KEYDOWN, VK_ESCAPE, 1);
    Application.ProcessMessages;
    CheckEqI(77, L.Width, 'Escape restores S0');
    CheckEqB(True, GetCaptureControl = nil, 'capture released');
    Move(200, 50);
    CheckEqI(77, L.Width, 'no drag after Escape');
    Up(200, 50);
    CheckEqS('can 127;', Log.Text, 'no OnMoved after Escape');

    Section('rsNone');
    S.ResizeStyle := rsNone;
    Down(78, 50);
    Move(128, 50);
    Up(128, 50);
    CheckEqI(77, L.Width, 'rsNone: dragging disabled');
    S.ResizeStyle := rsUpdate;

    Section('alRight and alTop');
    R := TPanel.Create(F);
    R.Align := alRight;
    R.Width := 80;
    R.Parent := F;
    SR := TSplitter.Create(F);
    SR.Align := alRight;
    SR.Left := 300;
    SR.Parent := F;
    CheckEqS('320,0,80,200', BoundsStr(R), 'right panel');
    CheckEqS('317,0,3,200', BoundsStr(SR), 'right splitter before it');
    Check(SR.FindTarget = R, 'alRight: the sibling with the larger right edge');
    Down(318, 50);
    Move(298, 50);
    CheckEqI(100, R.Width, 'alRight: S0 - d');
    CheckEqS('297,0,3,200', BoundsStr(SR), 'right splitter follows');
    Up(298, 50);
    T := TPanel.Create(F);
    T.Align := alTop;
    T.Height := 50;
    T.Parent := F;
    ST := TSplitter.Create(F);
    ST.Align := alTop;
    ST.Height := 4;
    ST.Top := 60;
    ST.Parent := F;
    CheckEqS('0,50,400,4', BoundsStr(ST), 'top splitter below the top panel');
    Check(ST.FindTarget = T, 'alTop: the preceding alTop sibling');
    Down(200, 51);
    Move(200, 71);
    CheckEqI(70, T.Height, 'alTop: S0 + d');
    Move(200, 500);
    CheckEqI(50 + (200 - 50 - 4) - 30, T.Height, 'alTop maximum');
    Up(200, 500);

    Section('Beveled (UIF-764)');
    S.Color := clYellow;
    S.Width := 6;
    F.HandleNeeded;
    Capture(F);
    CheckEqI(0, Count(clBtnShadow, S.Left, S.Top, S.Left + 6, S.Top + S.Height), 'not beveled: no line');
    CheckEqI(6 * S.Height, Count(clYellow, S.Left, S.Top, S.Left + 6, S.Top + S.Height), 'filled with Color');
    S.Beveled := True;
    Capture(F);
    CheckEqI(S.Height, Count(clBtnShadow, S.Left + 2, S.Top, S.Left + 3, S.Top + S.Height),
      'etched: shadow column in the middle');
    CheckEqI(S.Height, Count(clBtnHighlight, S.Left + 3, S.Top, S.Left + 4, S.Top + S.Height),
      'etched: highlight column next to it');
    CheckEqI(4 * S.Height, Count(clYellow, S.Left, S.Top, S.Left + 6, S.Top + S.Height), 'rest filled with Color');
    ST.Color := clYellow;
    ST.Beveled := True;
    Capture(F);
    CheckEqI(ST.Width, Count(clBtnShadow, 0, ST.Top + 1, ST.Width, ST.Top + 2), 'horizontal: shadow row');
    CheckEqI(ST.Width, Count(clBtnHighlight, 0, ST.Top + 2, ST.Width, ST.Top + 3), 'horizontal: highlight row');

    Section('scaling');
    S.MinSize := 30;
    F.ScaleBy(2, 1);
    CheckEqI(60, S.MinSize, 'MinSize scaled');
  finally
    F.Free;
  end;

  Section('streaming');
  SF := TSplitForm.Create(nil);
  try
    CheckEqB(True, SF.Bar.Align = alLeft, 'default Align alLeft (not stored)');
    CheckEqS('80,0,5,200', BoundsStr(SF.Bar), 'bounds');
    CheckEqB(False, SF.Bar.AutoSnap, 'AutoSnap');
    CheckEqB(True, SF.Bar.Beveled, 'Beveled');
    CheckEqI(40, SF.Bar.MinSize, 'MinSize');
    CheckEqB(True, SF.Bar.ResizeStyle = rsNone, 'ResizeStyle');
    Check(Assigned(SF.Bar.OnMoved), 'OnMoved bound');
    Check(SF.Bar.FindTarget = SF.Side, 'target of the loaded splitter');
  finally
    SF.Free;
    Bmp.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
