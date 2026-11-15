// covers: UIF-090, UIF-091, UIF-092, UIF-093, UIF-094, UIF-110, UIF-111, UIF-132, UIF-137
// needs: desktop
program Form_Focus;
{ U-043, core.md 8.1 and 8.3: CanFocus needs a visible, enabled chain ending in a visible, enabled form; SetFocus
  raises otherwise; SetFocusedControl fires OnExit for the old control and its ancestors that do not contain the new
  one (innermost first), then OnEnter for the new control's ancestors that do not contain the old one (outermost
  first) and the new control, then CM_FOCUSCHANGED; an OnExit handler that moves the focus abandons the change;
  WM_SETFOCUS runs the same path; disabling or hiding the focused control (or an ancestor) moves the focus to the
  next tab stop, or to the form; destroying it clears ActiveControl silently; activation focuses ActiveControl or
  the first tab stop; ActiveControl on a hidden form is only recorded; re-activation fires no OnEnter/OnExit.
  Win32 focus checks depend on whether the form became the active window (an unattended host may refuse). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, TestKit;

type
  TBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    LastChanged: string;
    constructor Create(AOwner: TComponent); override;
  published
    property TabStop;
    property OnEnter;
    property OnExit;
  end;

  TLog = class
  public
    Text: string;
    MoveTo: TWinControl;
    procedure Entered(Sender: TObject);
    procedure Exited(Sender: TObject);
    procedure ExitMoves(Sender: TObject);
  end;

var
  Log: TLog;

constructor TBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(0, 0, 40, 20);
  OnEnter := Log.Entered;
  OnExit := Log.Exited;
end;

procedure TBox.WndProc(var Message: TMessage);
begin
  if (Message.Msg = CM_FOCUSCHANGED) and (Message.LParam <> 0) then
    LastChanged := TComponent(Pointer(Message.LParam)).Name;
  inherited WndProc(Message);
end;

procedure TLog.Entered(Sender: TObject);
begin
  Text := Text + 'enter:' + TComponent(Sender).Name + ';';
end;

procedure TLog.Exited(Sender: TObject);
begin
  Text := Text + 'exit:' + TComponent(Sender).Name + ';';
end;

procedure TLog.ExitMoves(Sender: TObject);
var
  Target: TWinControl;
begin
  Exited(Sender);
  Target := MoveTo;
  MoveTo := nil;
  if Target <> nil then
    Target.SetFocus;
end;

function Box(const Name: string; Parent: TWinControl; Tab: Boolean): TBox;
begin
  Result := TBox.Create(Parent);
  Result.Name := Name;
  Result.TabStop := Tab;
  Result.Parent := Parent;
end;

procedure CheckSetFocusRaises(C: TWinControl; const Name: string);
begin
  Expect('EInvalidOperation', 'Cannot focus a disabled or invisible window');
  try
    C.SetFocus;
    CheckRaises(Name, '', '');
  except
    on E: Exception do
      CheckRaises(Name, E.ClassName, E.Message);
  end;
end;

procedure Activate(F: TForm);
begin
  F.Perform(WM_ACTIVATE, WA_ACTIVE, 0);
end;

procedure Run;
var
  F, G, K: TForm;
  P1, P2, P3, E1, E2, E3, E4, E5, Lone, Top, Inner, A, B, C, X, Y: TBox;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.Name := 'F';
    P1 := Box('P1', F, False);
    E1 := Box('E1', P1, True);
    E2 := Box('E2', P1, True);
    P2 := Box('P2', F, False);
    E3 := Box('E3', P2, True);
    E4 := Box('E4', P2, True);
    P3 := Box('P3', P2, False);
    E5 := Box('E5', P3, True);

    Section('CanFocus (UIF-090)');
    CheckEqB(False, E1.CanFocus, 'hidden form: False');
    Lone := TBox.Create(nil);
    Top := TBox.Create(nil);
    Inner := Box('Inner', Top, True);
    try
      Top.Visible := True;
      CheckEqB(False, Lone.CanFocus, 'no parent form: False');
      CheckEqB(False, Inner.CanFocus, 'chain not ending in a form: False');
    finally
      Top.Free;
      Lone.Free;
    end;
    Log.Text := '';
    F.Show;
    Activate(F);                                             { whether or not Show activated the window }
    CheckEqS('enter:P1;enter:E1;', Log.Text, 'activation focuses the first tab stop: ancestors outermost first');
    CheckEqB(True, F.ActiveControl = E1, 'ActiveControl');
    CheckEqB(True, E5.CanFocus, 'visible enabled chain: True');
    E5.Visible := False;
    CheckEqB(False, E5.CanFocus, 'invisible control: False');
    E5.Visible := True;
    P3.Enabled := False;
    CheckEqB(False, E5.CanFocus, 'disabled parent: False');
    P3.Enabled := True;
    F.Enabled := False;
    CheckEqB(False, E5.CanFocus, 'disabled form: False');
    F.Enabled := True;
    CheckEqB(True, GetParentForm(E5) = F, 'GetParentForm');

    Section('SetFocus (UIF-091)');
    E5.Enabled := False;
    CheckSetFocusRaises(E5, 'disabled control raises');
    E5.Enabled := True;

    Section('enter and exit order (UIF-110)');
    Log.Text := '';
    E2.SetFocus;
    CheckEqS('exit:E1;enter:E2;', Log.Text, 'sibling: the common parent neither exits nor enters');
    CheckEqB(F.Active, E2.Focused, 'Win32 focus when the form is active');
    Log.Text := '';
    E5.SetFocus;
    CheckEqS('exit:E2;exit:P1;enter:P2;enter:P3;enter:E5;', Log.Text, 'across panels');
    Log.Text := '';
    E3.SetFocus;
    CheckEqS('exit:E5;exit:P3;enter:E3;', Log.Text, 'outwards: innermost exits first');
    CheckEqS('E3', E5.LastChanged, 'CM_FOCUSCHANGED reaches nested controls');
    CheckEqS('E3', P1.LastChanged, 'CM_FOCUSCHANGED reaches every branch');
    Log.Text := '';
    E3.SetFocus;
    CheckEqS('', Log.Text, 'the same control again: no events');

    Section('OnExit moves the focus');
    E3.OnExit := Log.ExitMoves;
    Log.MoveTo := E4;
    Log.Text := '';
    E1.SetFocus;
    CheckEqS('exit:E3;enter:E4;', Log.Text, 'the original change is abandoned');
    CheckEqB(True, F.ActiveControl = E4, 'focus where the handler put it');
    E3.OnExit := Log.Exited;

    Section('WM_SETFOCUS path (core.md 7.2)');
    Log.Text := '';
    E1.Perform(WM_SETFOCUS, 0, 0);
    CheckEqS('exit:E4;exit:P2;enter:P1;enter:E1;', Log.Text, 'WM_SETFOCUS runs SetFocusedControl');
    CheckEqB(True, F.ActiveControl = E1, 'ActiveControl follows WM_SETFOCUS');

    Section('focused control disabled or hidden (UIF-093, UIF-132, UIF-137)');
    Log.Text := '';
    E1.Enabled := False;
    CheckEqS('exit:E1;enter:E2;', Log.Text, 'disabled: next tab stop');
    E1.Enabled := True;
    Log.Text := '';
    P1.Visible := False;
    CheckEqS('exit:E2;exit:P1;enter:P2;enter:E3;', Log.Text, 'ancestor hidden: next focusable tab stop');
    CheckEqB(True, F.ActiveControl = E3, 'ActiveControl moved');
    P1.Visible := True;

    Section('destroyed (UIF-037)');
    Log.Text := '';
    E3.Free;
    CheckEqB(True, F.ActiveControl = nil, 'ActiveControl cleared');
    CheckEqS('', Log.Text, 'silently');
  finally
    F.Free;
  end;

  G := TForm.CreateNew(nil);
  try
    A := Box('A', G, True);
    G.Show;
    A.SetFocus;
    Log.Text := '';
    A.Enabled := False;
    CheckEqS('exit:A;', Log.Text, 'no other candidate: to the form');
    CheckEqB(True, G.ActiveControl = nil, 'ActiveControl nil');
  finally
    G.Free;
  end;

  Section('activation (UIF-092, UIF-111)');
  G := TForm.CreateNew(nil);
  try
    A := Box('A', G, False);
    B := Box('B', G, True);
    C := Box('C', G, True);
    Log.Text := '';
    G.Show;
    Activate(G);
    CheckEqS('enter:B;', Log.Text, 'no ActiveControl: the first tab stop');
    CheckEqB(True, G.ActiveControl = B, 'ActiveControl is the first tab stop');
  finally
    G.Free;
  end;
  K := TForm.CreateNew(nil);
  try
    X := Box('X', K, True);
    Y := Box('Y', K, True);
    Log.Text := '';
    K.ActiveControl := Y;
    CheckEqS('', Log.Text, 'hidden form: ActiveControl only recorded');
    CheckEqB(True, K.ActiveControl = Y, 'recorded');
    K.FocusControl(X);
    CheckEqB(True, K.ActiveControl = X, 'FocusControl sets ActiveControl (UIF-094)');
    K.ActiveControl := Y;
    K.Show;
    Activate(K);
    CheckEqS('enter:Y;', Log.Text, 'activation focuses the recorded ActiveControl');
    Log.Text := '';
    K.Perform(WM_ACTIVATE, WA_INACTIVE, 0);
    Activate(K);
    CheckEqS('', Log.Text, 'deactivation and re-activation: no OnExit/OnEnter');
    CheckEqB(True, K.ActiveControl = Y, 'ActiveControl kept');
  finally
    K.Free;
  end;
  Log.Free;
end;

begin
  Run;
  Done;
end.
