// covers: UIF-030, UIF-031, UIF-032, UIF-033, UIF-034, UIF-035, UIF-036, UIF-038
// needs: desktop
program Core_HandleLifecycle;
{ U-021, core.md section 4: handles are created lazily and top-down (reading a child's Handle creates the root
  and every windowed descendant, hidden ones without WS_VISIBLE), our window class 'PN.<ClassName>' with
  StdWndProc, the CreateParams style bits, the object attached through the window property (FindControl),
  WM_SETFONT pushed at creation, text kept natively while the handle exists and saved back on destruction,
  DestroyHandle destroys the subtree and leaves the objects alive, an externally destroyed window only clears
  HandleAllocated, a child inserted under a parent with a handle gets its handle at once, removal destroys it,
  a child without a parent chain raises. The root stands in for a form (top-level, never shown). }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    FontPushed: WPARAM;
    procedure StartLoading;
    procedure EndLoading;
    procedure WndProc(var Message: TMessage); override;
    property Caption;
    property Font;
    property TabStop;
  end;

  { a hidden top-level window in place of a form }
  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

var
  Enumerated: Integer;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.StartLoading;
begin
  Loading;
end;

procedure TTestWin.EndLoading;
begin
  Loaded;
end;

procedure TTestWin.WndProc(var Message: TMessage);
begin
  if Message.Msg = WM_SETFONT then
    FontPushed := Message.WParam;
  inherited WndProc(Message);
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

function CountChild(Wnd: HWND; Data: LPARAM): BOOL; stdcall;
begin
  Enumerated := Enumerated + 1;
  Result := True;
end;

function ChildWindows(H: HWND): Integer;
begin
  Enumerated := 0;
  EnumChildWindows(H, @CountChild, 0);
  Result := Enumerated;
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

function NativeText(H: HWND): string;
var
  Buf: array[0..127] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 128, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

function StyleOf(H: HWND): NativeInt;
begin
  Result := GetWindowLongPtrW(H, GWL_STYLE);
end;

procedure Run;
var
  Root: TTopWin;
  A, B, Inner, Late, Orphan: TTestWin;
  G: TControl;
  HRoot, HA, HB, HInner, Old: HWND;
  S: NativeInt;
begin
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 300, 200);
    A := TTestWin.Create(Root);
    A.Name := 'A';
    A.SetBounds(10, 20, 100, 50);
    A.Caption := 'alpha';
    A.Parent := Root;
    G := TControl.Create(Root);
    G.Parent := Root;
    B := TTestWin.Create(Root);
    B.Name := 'B';
    B.Visible := False;
    B.Enabled := False;
    B.TabStop := True;
    B.Parent := Root;
    Inner := TTestWin.Create(Root);
    Inner.Name := 'Inner';
    Inner.Parent := A;

    Section('lazy creation (UIF-030, UIF-031)');
    CheckEqB(False, Root.HandleAllocated or A.HandleAllocated or B.HandleAllocated or Inner.HandleAllocated,
      'no handle before first use');
    HInner := Inner.Handle;
    Check(HInner <> 0, 'reading a grandchild handle');
    CheckEqB(True, Root.HandleAllocated and A.HandleAllocated and B.HandleAllocated, 'root and every child created');
    HRoot := Root.Handle;
    HA := A.Handle;
    HB := B.Handle;
    CheckEqI(3, ChildWindows(HRoot), 'three windowed descendants, no window for the graphic child');
    CheckEqB(True, GetParent(HA) = HRoot, 'A parented by the root window');
    CheckEqB(True, GetParent(HInner) = HA, 'Inner parented by A');
    CheckEqB(True, GetWindow(HRoot, GW_CHILD) = HB, 'later sibling is on top (Z order = list order)');

    Section('window class and styles (UIF-032, core.md 4.2)');
    CheckEqS('PN.TTestWin', ClassOf(HA), 'own class name');
    CheckEqS('PN.TTopWin', ClassOf(HRoot), 'class per ClassName');
    S := StyleOf(HA);
    CheckEqB(True, (S and WS_CHILD) <> 0, 'WS_CHILD');
    CheckEqB(True, (S and WS_CLIPSIBLINGS) <> 0, 'WS_CLIPSIBLINGS');
    CheckEqB(True, (S and WS_CLIPCHILDREN) <> 0, 'WS_CLIPCHILDREN for a container');
    CheckEqB(True, (S and WS_VISIBLE) <> 0, 'WS_VISIBLE when Visible');
    CheckEqB(False, (S and WS_DISABLED) <> 0, 'enabled');
    CheckEqB(False, (S and WS_TABSTOP) <> 0, 'no WS_TABSTOP');
    CheckEqB(True, (GetWindowLongPtrW(HA, GWL_EXSTYLE) and WS_EX_CONTROLPARENT) <> 0, 'WS_EX_CONTROLPARENT');
    S := StyleOf(HB);
    CheckEqB(False, (S and WS_VISIBLE) <> 0, 'hidden child created without WS_VISIBLE');
    CheckEqB(True, (S and WS_DISABLED) <> 0, 'WS_DISABLED');
    CheckEqB(True, (S and WS_TABSTOP) <> 0, 'WS_TABSTOP');
    CheckEqB(True, GetClassLongPtrW(HA, GCLP_WNDPROC) <> 0, 'class has a window procedure');
    CheckEqI(CS_DBLCLKS, GetClassLongPtrW(HA, GCL_STYLE) and CS_DBLCLKS, 'CS_DBLCLKS');

    Section('object attached (UIF-035)');
    CheckEqB(True, FindControl(HA) = A, 'FindControl');
    CheckEqB(True, FindControl(0) = nil, 'FindControl(0)');
    CheckEqB(True, FindControl(GetDesktopWindow) = nil, 'foreign window');
    CheckEqB(True, A.FontPushed = WPARAM(A.Font.Handle), 'WM_SETFONT with Font.Handle reached the control at creation');

    Section('text (UIF-033, UIF-034)');
    CheckEqS('alpha', NativeText(HA), 'field text used at creation');
    A.Caption := 'beta';
    CheckEqS('beta', NativeText(HA), 'text set natively');
    SendMessageW(HA, WM_SETTEXT, 0, LPARAM(PChar('gamma')));
    CheckEqS('gamma', A.Caption, 'text read from the window');

    Section('DestroyHandle (UIF-036)');
    Root.DestroyHandle;
    CheckEqB(False, Root.HandleAllocated or A.HandleAllocated or B.HandleAllocated or Inner.HandleAllocated,
      'subtree has no handles');
    CheckEqB(False, IsWindow(HRoot) or IsWindow(HA) or IsWindow(HInner), 'windows destroyed');
    CheckEqS('gamma', A.Caption, 'native text saved into the field');
    CheckEqB(True, Inner.Parent = A, 'objects keep their tree');
    Check(A.Handle <> 0, 'handle re-created on demand');
    CheckEqS('gamma', NativeText(A.Handle), 'state rebuilt from fields');

    Section('external destruction (UIF-038)');
    Old := Inner.Handle;
    DestroyWindow(Old);
    CheckEqB(False, Inner.HandleAllocated, 'HandleAllocated False');
    CheckEqB(True, Inner.Parent = A, 'object alive and still parented');
    Check(Inner.Handle <> 0, 'new handle on demand');

    Section('insertion and removal (UIF-030, UIF-047)');
    Late := TTestWin.Create(Root);
    Late.Parent := Root;
    CheckEqB(True, Late.HandleAllocated, 'inserted under a parent with a handle: created at once');
    Old := Late.Handle;
    Late.Parent := nil;
    CheckEqB(False, Late.HandleAllocated or IsWindow(Old), 'removal destroys the handle');
    Late.StartLoading;
    Late.Parent := Root;
    CheckEqB(False, Late.HandleAllocated, 'loading: not created on insertion');
    Late.EndLoading;
    CheckEqB(True, Late.HandleAllocated, 'created at the end of its Loaded');

    Section('no parent chain');
    Orphan := TTestWin.Create(Root);
    Orphan.Name := 'Orphan';
    Expect('EInvalidOperation', 'Control "Orphan" has no parent window');
    try
      if Orphan.Handle = 0 then
        Check(False, 'handle is never 0');
      Check(False, 'orphan Handle raises');
    except
      on E: Exception do
        CheckRaises('orphan Handle raises', E.ClassName, E.Message);
    end;
    HRoot := Root.Handle;
    HA := A.Handle;
  finally
    Root.Free;
  end;
  CheckEqB(False, IsWindow(HRoot) or IsWindow(HA), 'freeing the root destroys its windows');
end;

begin
  Run;
  Done;
end.
