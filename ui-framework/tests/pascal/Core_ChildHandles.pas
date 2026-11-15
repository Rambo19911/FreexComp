// covers: UIF-030, UIF-039, UIF-147
// needs: desktop
program Core_ChildHandles;
{ U-023, core.md 4.1, 4.5 and UIF-147: the Win32 Z order of windowed siblings (walked with GW_CHILD/GW_HWNDNEXT,
  top first) equals the reverse of Controls[] order - after creation with the parent, after a child is inserted
  later, and after a child's handle is re-created on demand at a middle position; RecreateWnd keeps the order,
  the natively changed text, the focus and the bounds, re-creates the windowed children, and does nothing
  without a handle. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
    procedure Recreate;
    property Caption;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

var
  Root: TTopWin;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTestWin.Recreate;
begin
  RecreateWnd;
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

{ names of the child windows of H, top of the Z order first }
function ZOrder(H: HWND): string;
var
  W: HWND;
  C: TWinControl;
begin
  Result := '';
  W := GetWindow(H, GW_CHILD);
  while W <> 0 do
  begin
    C := FindControl(W);
    if C = nil then
      Result := Result + '?'
    else
      Result := Result + C.Name;
    W := GetWindow(W, GW_HWNDNEXT);
    if W <> 0 then
      Result := Result + ',';
  end;
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

function NewChild(const AName: string; AParent: TWinControl): TTestWin;
begin
  Result := TTestWin.Create(Root);
  Result.Name := AName;
  Result.SetBounds(10, 10, 50, 20);
  Result.Parent := AParent;
end;

procedure Run;
var
  A, B, C, D, Inner, Loose: TTestWin;
  G: TControl;
  H, Old, OldInner: HWND;
  R: TRect;
begin
  Root := TTopWin.Create(nil);
  try
    Root.Name := 'Root';
    Root.SetBounds(100, 100, 300, 200);
    A := NewChild('A', Root);
    G := TControl.Create(Root);
    G.Parent := Root;
    B := NewChild('B', Root);
    C := NewChild('C', Root);
    Inner := NewChild('Inner', A);

    Section('created with the parent (UIF-030, UIF-147)');
    H := Root.Handle;
    CheckEqS('C,B,A', ZOrder(H), 'top first = reverse list order');

    Section('inserted later');
    D := NewChild('D', Root);
    CheckEqS('D,C,B,A', ZOrder(H), 'new sibling on top');

    Section('re-created on demand in the middle');
    Old := B.Handle;
    DestroyWindow(Old);
    CheckEqS('D,C,A', ZOrder(H), 'window gone');
    B.HandleNeeded;
    CheckEqS('D,C,B,A', ZOrder(H), 'back at its list position, not on top');

    Section('RecreateWnd (UIF-039)');
    SendMessageW(C.Handle, WM_SETTEXT, 0, LPARAM(PChar('typed')));
    Old := C.Handle;
    C.SetBounds(20, 30, 60, 25);
    C.Recreate;
    Check(C.HandleAllocated, 'handle exists again');
    CheckEqB(False, IsWindow(Old), 'old window destroyed');
    CheckEqS('D,C,B,A', ZOrder(H), 'order kept');
    CheckEqS('typed', NativeText(C.Handle), 'native text kept');
    GetWindowRect(C.Handle, R);
    MapWindowPoints(0, H, @R, 2);
    CheckEqI(20, R.Left, 'left kept');
    CheckEqI(30, R.Top, 'top kept');
    CheckEqI(60, R.Right - R.Left, 'width kept');
    CheckEqI(25, R.Bottom - R.Top, 'height kept');
    CheckEqI(60, C.Width, 'Width field unchanged');

    OldInner := Inner.Handle;
    A.Recreate;
    CheckEqS('D,C,B,A', ZOrder(H), 'bottom sibling stays at the bottom');
    CheckEqB(False, IsWindow(OldInner), 'child window destroyed with it');
    CheckEqB(True, Inner.HandleAllocated and (GetParent(Inner.Handle) = A.Handle), 'child re-created inside');

    Section('focus kept');
    Windows.SetFocus(B.Handle);
    CheckEqB(True, GetFocus = B.Handle, 'B focused before');
    B.Recreate;
    CheckEqB(True, GetFocus = B.Handle, 'new window of B focused');
    Windows.SetFocus(Inner.Handle);
    A.Recreate;
    CheckEqB(True, GetFocus = Inner.Handle, 'focused descendant refocused');

    Section('without a handle');
    Loose := TTestWin.Create(Root);
    Loose.Recreate;
    CheckEqB(False, Loose.HandleAllocated, 'nothing created');
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
