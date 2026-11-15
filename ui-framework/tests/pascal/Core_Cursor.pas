// covers: UIF-021, UIF-022
// opc: -U ..\..\src
// needs: desktop
program Core_Cursor;
{ U-035, core.md 7.10 (WM_SETCURSOR), UIF-021, CM_CURSORCHANGED, application-forms.md 3 (Screen.Cursor):
  WM_SETCURSOR with HTCLIENT finds the control under the mouse (graphic child or the window's control), walks up
  the parent chain while Cursor = crDefault (finally the arrow), lets a non-default Screen.Cursor win, calls
  SetCursor and answers 1; other hit codes get default processing. A Cursor change of the control under the mouse
  applies at once; so does a Screen.Cursor change. The mouse is never moved: the window is placed under it. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestGraphic = class(TGraphicControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

constructor TTestGraphic.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
end;

var
  Top: TTopWin;
  W: TTestWin;
  G: TTestGraphic;

{ the cursor last set by this thread (SetCursor answers the previous one; it is put back) }
function Current: HCURSOR;
begin
  Result := SetCursor(0);
  SetCursor(Result);
end;

function Stock(Id: PWideChar): HCURSOR;
begin
  Result := LoadCursorW(0, Id);
end;

{ places Top so that the mouse is at client point (X, Y) }
procedure MouseAt(X, Y: Integer);
var
  P: TPoint;
begin
  GetCursorPos(P);
  Top.SetBounds(P.X - X, P.Y - Y, 200, 100);
end;

function SetCursorMsg(Wnd: HWND; HitTest: Word): LRESULT;
begin
  SetCursor(0);
  Result := SendMessageW(Wnd, WM_SETCURSOR, WPARAM(Wnd), MakeLParam(HitTest, WM_MOUSEMOVE));
end;

procedure Run;
begin
  Top := TTopWin.Create(nil);
  try
    Top.Name := 'Top';
    W := TTestWin.Create(Top);
    W.Name := 'W';
    W.Parent := Top;
    W.SetBounds(100, 10, 50, 30);
    G := TTestGraphic.Create(Top);
    G.Name := 'G';
    G.Parent := Top;
    G.SetBounds(10, 10, 50, 30);
    MouseAt(20, 20);
    Top.HandleNeeded;
    Top.Cursor := crCross;
    G.Cursor := crHandPoint;

    Section('WM_SETCURSOR in the client area');
    CheckEqI(1, SetCursorMsg(Top.Handle, HTCLIENT), 'handled: result 1');
    CheckEqB(True, Current = Stock(IDC_HAND), 'graphic child under the mouse: its cursor');
    MouseAt(180, 80);
    SetCursorMsg(Top.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_CROSS), 'no child under the mouse: the window control''s cursor');
    MouseAt(110, 20);
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_CROSS), 'crDefault child: the parent''s cursor');
    MouseAt(20, 20);
    G.Enabled := False;
    SetCursorMsg(Top.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_CROSS), 'disabled graphic child: not under the mouse');
    G.Enabled := True;
    Top.Cursor := crDefault;
    MouseAt(110, 20);
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_ARROW), 'crDefault up the whole chain: arrow');
    Top.Cursor := crNone;
    SetCursor(Stock(IDC_ARROW));
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = 0, 'crNone: no cursor');

    Section('UIF-021 mapping');
    Top.Cursor := crIBeam;
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_IBEAM), 'crIBeam');
    Top.Cursor := crHourGlass;
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_WAIT), 'crHourGlass');
    Top.Cursor := crHSplit;
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_SIZENS), 'crHSplit');
    Top.Cursor := crNoDrop;
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_NO), 'crNoDrop');
    Top.Cursor := crDrag;
    SetCursorMsg(W.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_ARROW), 'crDrag');
    Top.Cursor := crCross;

    Section('non-client hit codes');
    SetCursorMsg(Top.Handle, HTCAPTION);
    CheckEqB(False, Current = Stock(IDC_CROSS), 'HTCAPTION: default processing');

    Section('Screen.Cursor');
    CheckEqI(crDefault, Screen.Cursor, 'default crDefault');
    SetCursor(Stock(IDC_ARROW));
    Screen.Cursor := crHourGlass;
    CheckEqB(True, Current = Stock(IDC_WAIT), 'applied at once');
    MouseAt(20, 20);
    SetCursorMsg(Top.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_WAIT), 'wins over the control''s cursor');
    Screen.Cursor := crDefault;
    SetCursorMsg(Top.Handle, HTCLIENT);
    CheckEqB(True, Current = Stock(IDC_HAND), 'reset: the control''s cursor again');

    Section('CM_CURSORCHANGED');
    SendMessageW(Top.Handle, WM_MOUSEMOVE, 0, MakeLParam(20, 20));
    SetCursor(Stock(IDC_ARROW));
    G.Cursor := crIBeam;
    CheckEqB(True, Current = Stock(IDC_IBEAM), 'the control under the mouse: applied at once');
    SetCursor(Stock(IDC_ARROW));
    W.Cursor := crSizeAll;
    CheckEqB(True, Current = Stock(IDC_ARROW), 'another control: nothing happens');
    G.Cursor := crDefault;
    CheckEqB(True, Current = Stock(IDC_CROSS), 'back to crDefault: the inherited cursor');
  finally
    Top.Free;
  end;
end;

begin
  Run;
  Done;
end.
