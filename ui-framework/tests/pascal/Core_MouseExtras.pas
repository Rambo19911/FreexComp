// covers: UIF-068, UIF-075, UIF-077
// needs: desktop
program Core_MouseExtras;
{ U-030, core.md 7.4, 7.6, 7.7: the control under the mouse is tracked over WM_MOUSEMOVE (after graphic routing):
  a change sends CM_MOUSELEAVE to the old one and CM_MOUSEENTER to the new one (OnMouseEnter before OnMouseMove);
  WM_MOUSELEAVE on a window leaves only when the tracked control is that window's control or one of its graphic
  children; destroying the tracked control clears it. WM_MOUSEWHEEL fires OnMouseWheel, then OnMouseWheelUp/Down,
  and bubbles to the parent windows until one handles it. WM_CONTEXTMENU fires OnContextPopup at the client
  position (keyboard origin: the client centre), graphic children first; unhandled it bubbles to the parent. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TLog = class
  public
    Text: string;
    WheelStop: TObject;                                     { the control whose OnMouseWheel handles }
    DownStop: TObject;                                      { the control whose OnMouseWheelDown handles }
    PopupStop: TObject;                                     { the control whose OnContextPopup handles }
    procedure Add(Sender: TObject; const S: string);
    procedure Enter(Sender: TObject);
    procedure Leave(Sender: TObject);
    procedure Move(Sender: TObject; Shift: TShiftState; X, Y: Integer);
    procedure Wheel(Sender: TObject; Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint; var Handled: Boolean);
    procedure WheelUp(Sender: TObject; Shift: TShiftState; MousePos: TPoint; var Handled: Boolean);
    procedure WheelDown(Sender: TObject; Shift: TShiftState; MousePos: TPoint; var Handled: Boolean);
    procedure Popup(Sender: TObject; MousePos: TPoint; var Handled: Boolean);
  end;

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

var
  Log: TLog;

procedure TLog.Add(Sender: TObject; const S: string);
begin
  Text := Text + TComponent(Sender).Name + '.' + S + ';';
end;

procedure TLog.Enter(Sender: TObject);
begin
  Add(Sender, 'Enter');
end;

procedure TLog.Leave(Sender: TObject);
begin
  Add(Sender, 'Leave');
end;

procedure TLog.Move(Sender: TObject; Shift: TShiftState; X, Y: Integer);
begin
  Add(Sender, 'Move(' + IntToStr(X) + ',' + IntToStr(Y) + ')');
end;

procedure TLog.Wheel(Sender: TObject; Shift: TShiftState; WheelDelta: Integer; MousePos: TPoint; var Handled: Boolean);
var
  S: string;
begin
  S := '';
  if ssShift in Shift then
    S := 's';
  Add(Sender, 'Wheel(' + S + ',' + IntToStr(WheelDelta) + ',' + IntToStr(MousePos.X) + ',' + IntToStr(MousePos.Y) + ')');
  Handled := Sender = WheelStop;
end;

procedure TLog.WheelUp(Sender: TObject; Shift: TShiftState; MousePos: TPoint; var Handled: Boolean);
begin
  Add(Sender, 'Up');
end;

procedure TLog.WheelDown(Sender: TObject; Shift: TShiftState; MousePos: TPoint; var Handled: Boolean);
begin
  Add(Sender, 'Down');
  Handled := Sender = DownStop;
end;

procedure TLog.Popup(Sender: TObject; MousePos: TPoint; var Handled: Boolean);
begin
  Add(Sender, 'Popup(' + IntToStr(MousePos.X) + ',' + IntToStr(MousePos.Y) + ')');
  Handled := Sender = PopupStop;
end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
  OnMouseEnter := Log.Enter;
  OnMouseLeave := Log.Leave;
  OnMouseMove := Log.Move;
  OnMouseWheel := Log.Wheel;
  OnMouseWheelUp := Log.WheelUp;
  OnMouseWheelDown := Log.WheelDown;
  OnContextPopup := Log.Popup;
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
  OnMouseEnter := Log.Enter;
  OnMouseLeave := Log.Leave;
  OnMouseMove := Log.Move;
  OnContextPopup := Log.Popup;
end;

function Send(C: TWinControl; Msg: Cardinal; WParam: WPARAM; LParam: LPARAM): string;
begin
  Log.Text := '';
  SendMessageW(C.Handle, Msg, WParam, LParam);
  Result := Log.Text;
end;

function MoveTo(C: TWinControl; X, Y: Integer): string;
begin
  Result := Send(C, WM_MOUSEMOVE, 0, MakeLParam(Word(X), Word(Y)));
end;

function ScreenOf(C: TWinControl; X, Y: Integer): LPARAM;
var
  P: TPoint;
begin
  P := Point(X, Y);
  ClientToScreen(C.Handle, P);
  Result := MakeLParam(Word(P.X), Word(P.Y));
end;

procedure Run;
var
  Root: TTopWin;
  W, Mid, Inner: TTestWin;
  G, T: TTestGraphic;
  R: LRESULT;
begin
  Log := TLog.Create;
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 300, 200);
    W := TTestWin.Create(Root);
    W.Name := 'W';
    W.SetBounds(100, 10, 50, 40);
    W.Parent := Root;
    G := TTestGraphic.Create(Root);
    G.Name := 'G';
    G.SetBounds(20, 20, 30, 30);
    G.Parent := Root;
    Mid := TTestWin.Create(Root);
    Mid.Name := 'Mid';
    Mid.SetBounds(10, 100, 120, 80);
    Mid.Parent := Root;
    Inner := TTestWin.Create(Root);
    Inner.Name := 'Inner';
    Inner.SetBounds(10, 10, 60, 40);
    Inner.Parent := Mid;
    Root.HandleNeeded;

    Section('enter and leave (UIF-068)');
    CheckEqS('Root.Enter;Root.Move(5,5);', MoveTo(Root, 5, 5), 'first move: OnMouseEnter before OnMouseMove');
    CheckEqS('Root.Move(6,6);', MoveTo(Root, 6, 6), 'same control: no enter');
    CheckEqS('Root.Leave;G.Enter;G.Move(5,10);', MoveTo(Root, 25, 30), 'onto a graphic child');
    CheckEqS('G.Leave;W.Enter;W.Move(3,3);', MoveTo(W, 3, 3), 'onto another window');
    CheckEqS('', Send(Root, WM_MOUSELEAVE, 0, 0), 'WM_MOUSELEAVE of another window: nothing');
    CheckEqS('W.Leave;', Send(W, WM_MOUSELEAVE, 0, 0), 'WM_MOUSELEAVE of its window');
    CheckEqS('', Send(W, WM_MOUSELEAVE, 0, 0), 'nothing tracked: nothing');
    CheckEqS('G.Enter;G.Move(5,10);', MoveTo(Root, 25, 30), 'graphic child again');
    CheckEqS('G.Leave;', Send(Root, WM_MOUSELEAVE, 0, 0), 'WM_MOUSELEAVE of the parent window leaves the graphic child');
    T := TTestGraphic.Create(Root);
    T.Name := 'T';
    T.SetBounds(200, 100, 20, 20);
    T.Parent := Root;
    CheckEqS('T.Enter;T.Move(5,5);', MoveTo(Root, 205, 105), 'onto a temporary child');
    T.Free;
    CheckEqS('Root.Enter;Root.Move(205,105);', MoveTo(Root, 205, 105), 'destroyed control: reference cleared');
    Send(Root, WM_MOUSELEAVE, 0, 0);

    Section('wheel (UIF-075)');
    Log.WheelStop := Mid;
    CheckEqS('Inner.Wheel(s,120,10,20);Inner.Up;Mid.Wheel(s,120,10,20);',
      Send(Inner, WM_MOUSEWHEEL, MakeWParam(MK_SHIFT, Word(120)), MakeLParam(10, 20)), 'bubbles until handled');
    Log.WheelStop := nil;
    Log.DownStop := Inner;
    CheckEqS('Inner.Wheel(,-120,10,20);Inner.Down;',
      Send(Inner, WM_MOUSEWHEEL, MakeWParam(0, Word(-120)), MakeLParam(10, 20)), 'OnMouseWheelDown handles');
    Log.DownStop := nil;
    CheckEqS('Inner.Wheel(,-240,1,2);Inner.Down;Mid.Wheel(,-240,1,2);Mid.Down;Root.Wheel(,-240,1,2);Root.Down;',
      Send(Inner, WM_MOUSEWHEEL, MakeWParam(0, Word(-240)), MakeLParam(1, 2)), 'unhandled: up to the top window');

    Section('context popup (UIF-077)');
    Log.PopupStop := W;
    Log.Text := '';
    R := SendMessageW(W.Handle, WM_CONTEXTMENU, W.Handle, ScreenOf(W, 5, 6));
    CheckEqS('W.Popup(5,6);', Log.Text, 'OnContextPopup at the client position');
    CheckEqB(True, R = 1, 'handled: result 1');
    CheckEqS('W.Popup(25,20);', Send(W, WM_CONTEXTMENU, W.Handle, LPARAM(-1)), 'keyboard origin: the client centre');
    Log.PopupStop := nil;
    CheckEqS('W.Popup(5,6);Root.Popup(105,16);', Send(W, WM_CONTEXTMENU, W.Handle, ScreenOf(W, 5, 6)),
      'unhandled: bubbles to the parent');
    Log.PopupStop := G;
    CheckEqS('G.Popup(5,10);', Send(Root, WM_CONTEXTMENU, Root.Handle, ScreenOf(Root, 25, 30)),
      'graphic child first');
    Log.PopupStop := nil;
    CheckEqS('G.Popup(5,10);Root.Popup(25,30);', Send(Root, WM_CONTEXTMENU, Root.Handle, ScreenOf(Root, 25, 30)),
      'graphic child declines: the window');
  finally
    Root.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
