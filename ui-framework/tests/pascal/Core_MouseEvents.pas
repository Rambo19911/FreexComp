// covers: UIF-064, UIF-065, UIF-066, UIF-067
// opc: -U ..\..\src
// needs: desktop
program Core_MouseEvents;
{ U-029, core.md 7.3: mouse messages become events in the exact order OnMouseDown, OnClick, OnMouseUp; the left
  button captures the mouse (a graphic control through its parent's window, GetCaptureControl names it) and the
  release outside the client area gives no click; a double click fires OnDblClick and OnMouseDown with ssDouble
  (without csDoubleClicks it counts as a second press); the right button clicks nothing; Shift comes from the key
  flags; messages over a graphic child are re-sent to it in its own coordinates, a disabled one is skipped (the
  parent gets the event); csNoStdEvents suppresses every event. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Down(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
    procedure Up(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
    procedure Move(Sender: TObject; Shift: TShiftState; X, Y: Integer);
    procedure Click(Sender: TObject);
    procedure DblClick(Sender: TObject);
  end;

  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    property OnMouseDown;
    property OnMouseUp;
    property OnMouseMove;
    property OnClick;
    property OnDblClick;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestGraphic = class(TGraphicControl)
  public
    property OnMouseDown;
    property OnMouseUp;
    property OnMouseMove;
    property OnClick;
  end;

var
  Log: TLog;

function ButtonName(Button: TMouseButton): string;
begin
  case Button of
    mbLeft: Result := 'L';
    mbRight: Result := 'R';
  else
    Result := 'M';
  end;
end;

function ShiftText(Shift: TShiftState): string;
begin
  Result := '';
  if ssShift in Shift then
    Result := Result + 's';
  if ssCtrl in Shift then
    Result := Result + 'c';
  if ssLeft in Shift then
    Result := Result + 'l';
  if ssDouble in Shift then
    Result := Result + 'd';
end;

procedure TLog.Down(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  Text := Text + TComponent(Sender).Name + '.Down(' + ButtonName(Button) + ShiftText(Shift) + ',' + IntToStr(X) + ','
    + IntToStr(Y) + ');';
end;

procedure TLog.Up(Sender: TObject; Button: TMouseButton; Shift: TShiftState; X, Y: Integer);
begin
  Text := Text + TComponent(Sender).Name + '.Up(' + ButtonName(Button) + ',' + IntToStr(X) + ',' + IntToStr(Y) + ');';
end;

procedure TLog.Move(Sender: TObject; Shift: TShiftState; X, Y: Integer);
begin
  Text := Text + TComponent(Sender).Name + '.Move(' + IntToStr(X) + ',' + IntToStr(Y) + ');';
end;

procedure TLog.Click(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + '.Click;';
end;

procedure TLog.DblClick(Sender: TObject);
begin
  Text := Text + TComponent(Sender).Name + '.Dbl;';
end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  OnMouseDown := Log.Down;
  OnMouseUp := Log.Up;
  OnMouseMove := Log.Move;
  OnClick := Log.Click;
  OnDblClick := Log.DblClick;
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_POPUP or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

function Mouse(C: TWinControl; Msg: Cardinal; Keys: WPARAM; X, Y: Integer): string;
begin
  Log.Text := '';
  SendMessageW(C.Handle, Msg, Keys, MakeLParam(Word(X), Word(Y)));
  Result := Log.Text;
end;

procedure Run;
var
  Root: TTopWin;
  W, Quiet: TTestWin;
  G: TTestGraphic;
begin
  Log := TLog.Create;
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 200, 150);
    W := TTestWin.Create(Root);
    W.Name := 'W';
    W.SetBounds(100, 10, 50, 40);
    W.Parent := Root;
    G := TTestGraphic.Create(Root);
    G.Name := 'G';
    G.SetBounds(20, 20, 30, 30);
    G.OnMouseDown := Log.Down;
    G.OnMouseUp := Log.Up;
    G.OnMouseMove := Log.Move;
    G.OnClick := Log.Click;
    G.Parent := Root;
    Quiet := TTestWin.Create(Root);
    Quiet.Name := 'Quiet';
    Quiet.ControlStyle := Quiet.ControlStyle + [csNoStdEvents];
    Quiet.SetBounds(10, 100, 30, 30);
    Quiet.Parent := Root;
    Root.HandleNeeded;

    Section('press and release (UIF-065)');
    CheckEqS('W.Down(Ll,5,6);', Mouse(W, WM_LBUTTONDOWN, MK_LBUTTON, 5, 6), 'OnMouseDown');
    CheckEqB(True, GetCapture = W.Handle, 'mouse captured');
    CheckEqB(True, GetCaptureControl = W, 'GetCaptureControl');
    CheckEqS('W.Click;W.Up(L,7,8);', Mouse(W, WM_LBUTTONUP, 0, 7, 8), 'OnClick before OnMouseUp');
    CheckEqB(True, GetCapture = 0, 'capture released');
    CheckEqB(True, GetCaptureControl = nil, 'no capture control');
    Mouse(W, WM_LBUTTONDOWN, MK_LBUTTON, 5, 6);
    CheckEqS('W.Up(L,60,6);', Mouse(W, WM_LBUTTONUP, 0, 60, 6), 'released outside: no click');
    CheckEqS('W.Move(3,4);', Mouse(W, WM_MOUSEMOVE, 0, 3, 4), 'OnMouseMove');
    CheckEqS('W.Down(Lscl,1,1);', Mouse(W, WM_LBUTTONDOWN, MK_LBUTTON or MK_SHIFT or MK_CONTROL, 1, 1),
      'Shift from the key flags');
    Mouse(W, WM_LBUTTONUP, 0, 1, 1);

    Section('other buttons');
    CheckEqS('W.Down(R,2,2);', Mouse(W, WM_RBUTTONDOWN, 0, 2, 2), 'right press');
    CheckEqS('W.Up(R,2,2);', Mouse(W, WM_RBUTTONUP, 0, 2, 2), 'right release: no click');
    CheckEqS('W.Down(M,2,2);', Mouse(W, WM_MBUTTONDOWN, 0, 2, 2), 'middle press');
    CheckEqS('W.Up(M,2,2);', Mouse(W, WM_MBUTTONUP, 0, 2, 2), 'middle release');
    CheckEqS('W.Down(Rd,2,2);', Mouse(W, WM_RBUTTONDBLCLK, 0, 2, 2), 'right double click');

    Section('double click');
    Mouse(W, WM_LBUTTONDOWN, MK_LBUTTON, 4, 4);
    Mouse(W, WM_LBUTTONUP, 0, 4, 4);
    CheckEqS('W.Dbl;W.Down(Lld,4,4);', Mouse(W, WM_LBUTTONDBLCLK, MK_LBUTTON, 4, 4), 'OnDblClick then OnMouseDown');
    CheckEqS('W.Up(L,4,4);', Mouse(W, WM_LBUTTONUP, 0, 4, 4), 'no second click');
    W.ControlStyle := W.ControlStyle - [csDoubleClicks];
    CheckEqS('W.Down(Ll,4,4);', Mouse(W, WM_LBUTTONDBLCLK, MK_LBUTTON, 4, 4), 'without csDoubleClicks: a press');
    CheckEqS('W.Click;W.Up(L,4,4);', Mouse(W, WM_LBUTTONUP, 0, 4, 4), 'and a click');

    Section('graphic child (UIF-064)');
    CheckEqS('G.Down(Ll,5,10);', Mouse(Root, WM_LBUTTONDOWN, MK_LBUTTON, 25, 30), 'routed in child coordinates');
    CheckEqB(True, GetCapture = Root.Handle, 'captured through the parent window');
    CheckEqB(True, GetCaptureControl = G, 'GetCaptureControl names the graphic control');
    CheckEqS('G.Move(80,80);', Mouse(Root, WM_MOUSEMOVE, MK_LBUTTON, 100, 100), 'captured: moves outside go to it');
    CheckEqS('G.Up(L,80,80);', Mouse(Root, WM_LBUTTONUP, 0, 100, 100), 'released outside: no click');
    CheckEqB(True, GetCaptureControl = nil, 'capture released');
    Mouse(Root, WM_LBUTTONDOWN, MK_LBUTTON, 25, 30);
    CheckEqS('G.Click;G.Up(L,1,1);', Mouse(Root, WM_LBUTTONUP, 0, 21, 21), 'click inside');
    CheckEqS('Root.Move(5,5);', Mouse(Root, WM_MOUSEMOVE, 0, 5, 5), 'outside the child: the parent');

    Section('disabled graphic child (UIF-066)');
    G.Enabled := False;
    CheckEqS('Root.Down(Ll,25,30);', Mouse(Root, WM_LBUTTONDOWN, MK_LBUTTON, 25, 30), 'skipped: the parent gets it');
    Mouse(Root, WM_LBUTTONUP, 0, 25, 30);
    G.Enabled := True;

    Section('csNoStdEvents (UIF-067)');
    CheckEqS('', Mouse(Quiet, WM_LBUTTONDOWN, MK_LBUTTON, 5, 5), 'no OnMouseDown');
    CheckEqS('', Mouse(Quiet, WM_LBUTTONUP, 0, 5, 5), 'no OnClick, no OnMouseUp');
    CheckEqB(True, GetCapture = 0, 'no capture');
  finally
    Root.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
