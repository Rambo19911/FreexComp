// covers: UIF-130, UIF-131, UIF-135, UIF-136, UIF-139
// opc: -U ..\..\src
// needs: desktop
program Core_VisibleEnabled;
{ U-026, core.md sections 9 and 10: Enabled and Visible changes send CM_ENABLEDCHANGED / CM_VISIBLECHANGED
  (WParam = Ord(Visible)) once per real change, reach the window (EnableWindow, show/hide without activation),
  hiding an aligned control frees its space, a change while loading only sets the field, disabling a parent
  leaves the children's Enabled alone, an external EnableWindow syncs the field, and Showing is derived from
  Visible up the parent chain to a visible top-level window. Graphic controls get the messages without a
  window. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    Log: string;
    constructor Create(AOwner: TComponent); override;
    procedure WndProc(var Message: TMessage); override;
    procedure StartLoading;
    procedure EndLoading;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestGraphic = class(TControl)
  public
    Log: string;
    procedure WndProc(var Message: TMessage); override;
  end;

procedure AddLog(var Log: string; const Message: TMessage);
begin
  case Message.Msg of
    CM_ENABLEDCHANGED: Log := Log + 'E;';
    CM_VISIBLECHANGED: Log := Log + 'V' + IntToStr(Integer(Message.WParam)) + ';';
  end;
end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.WndProc(var Message: TMessage);
begin
  AddLog(Log, Message);
  inherited WndProc(Message);
end;

procedure TTestWin.StartLoading;
begin
  Loading;
end;

procedure TTestWin.EndLoading;
begin
  Loaded;
end;

procedure TTestGraphic.WndProc(var Message: TMessage);
begin
  AddLog(Log, Message);
  inherited WndProc(Message);
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure Run;
var
  Root: TTopWin;
  A, Inner, Bar, Fill, L: TTestWin;
  G: TTestGraphic;
begin
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 300, 200);
    A := TTestWin.Create(Root);
    A.SetBounds(10, 10, 100, 80);
    A.Parent := Root;
    Inner := TTestWin.Create(Root);
    Inner.SetBounds(5, 5, 20, 20);
    Inner.Parent := A;
    G := TTestGraphic.Create(Root);
    G.Parent := A;
    Root.HandleNeeded;

    Section('Enabled (UIF-130)');
    A.Enabled := False;
    CheckEqS('E;', A.Log, 'CM_ENABLEDCHANGED once');
    CheckEqB(False, IsWindowEnabled(A.Handle), 'window disabled');
    A.Enabled := False;
    CheckEqS('E;', A.Log, 'no message without a change');
    A.Enabled := True;
    CheckEqB(True, IsWindowEnabled(A.Handle), 'window enabled again');
    G.Enabled := False;
    CheckEqS('E;', G.Log, 'graphic control gets CM_ENABLEDCHANGED');
    G.Enabled := True;

    Section('parent disabled (UIF-131)');
    A.Enabled := False;
    CheckEqB(True, Inner.Enabled, 'child property unchanged');
    CheckEqB(True, IsWindowEnabled(Inner.Handle), 'child window not disabled');
    A.Enabled := True;

    Section('external EnableWindow (core.md 7.10 WM_ENABLE)');
    EnableWindow(Inner.Handle, False);
    CheckEqB(False, Inner.Enabled, 'field synced');
    EnableWindow(Inner.Handle, True);
    CheckEqB(True, Inner.Enabled, 'field synced back');

    Section('Visible (UIF-135, UIF-136)');
    A.Log := '';
    A.Visible := False;
    CheckEqS('V0;', A.Log, 'CM_VISIBLECHANGED with WParam 0');
    CheckEqB(False, (GetWindowLongPtrW(A.Handle, GWL_STYLE) and WS_VISIBLE) <> 0, 'window hidden');
    A.Visible := False;
    CheckEqS('V0;', A.Log, 'no message without a change');
    A.Visible := True;
    CheckEqS('V0;V1;', A.Log, 'WParam 1');
    CheckEqB(True, (GetWindowLongPtrW(A.Handle, GWL_STYLE) and WS_VISIBLE) <> 0, 'window shown');
    G.Log := '';
    G.Visible := False;
    CheckEqS('V0;', G.Log, 'graphic control gets CM_VISIBLECHANGED');
    G.Visible := True;

    Section('hidden aligned control frees its space (UIF-135)');
    Bar := TTestWin.Create(Root);
    Bar.Height := 30;
    Bar.Align := alTop;
    Bar.Parent := Root;
    Fill := TTestWin.Create(Root);
    Fill.Align := alClient;
    Fill.Parent := Root;
    CheckEqI(30, Fill.Top, 'below the bar');
    Bar.Visible := False;
    CheckEqI(0, Fill.Top, 'space freed');
    Bar.Visible := True;
    CheckEqI(30, Fill.Top, 'space taken again');

    Section('loading: field only (UIF-135)');
    L := TTestWin.Create(Root);
    L.StartLoading;
    L.Parent := Root;
    L.Visible := False;
    CheckEqS('', L.Log, 'no message while loading');
    CheckEqB(False, L.Visible, 'field set');
    L.EndLoading;
    CheckEqB(True, L.HandleAllocated, 'handle created at the end of Loaded');
    CheckEqB(False, (GetWindowLongPtrW(L.Handle, GWL_STYLE) and WS_VISIBLE) <> 0, 'created hidden');

    Section('Showing (UIF-139)');
    CheckEqB(False, Root.Showing, 'top-level window not shown yet');
    CheckEqB(False, A.Showing, 'child of a hidden top-level window');
    ShowWindow(Root.Handle, SW_SHOWNOACTIVATE);
    CheckEqB(True, Root.Showing, 'top-level window shown');
    CheckEqB(True, A.Showing and Inner.Showing and G.Showing, 'visible chain');
    A.Visible := False;
    CheckEqB(False, A.Showing, 'hidden control');
    CheckEqB(False, Inner.Showing or G.Showing, 'children of a hidden control');
    CheckEqB(True, Inner.Visible and G.Visible, 'their Visible unchanged');
    A.Visible := True;
    CheckEqB(True, Inner.Showing, 'shown again');
    ShowWindow(Root.Handle, SW_HIDE);
    CheckEqB(False, Inner.Showing, 'top-level hidden');
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
