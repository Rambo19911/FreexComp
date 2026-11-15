// covers: UIF-032, UIF-060, UIF-061
// needs: desktop
program Core_Subclass;
{ U-022, core.md 4.1 step 2c/2f and 7.1: a control with Params.SubclassOf = 'EDIT' is created from the system
  class (no own class registered), its window procedure is replaced by ours, and the system procedure stays the
  default handler: EM_SETSEL/EM_GETSEL and the native text work, our WndProc sees every message sent to the window,
  a handler that does not call inherited keeps the message from the native control, WM_SETFONT carries Font.Handle
  to the system control (WM_GETFONT answers it), the text survives DestroyHandle and re-creation, and without a
  handle DefaultHandler serves the text from the field. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, TestKit;

type
  TTestWin = class(TWinControl)
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TTopWin = class(TTestWin)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  end;

  TTestEdit = class(TWinControl)
  protected
    procedure CreateParams(var Params: TCreateParams); override;
  public
    SelSeen: Integer;
    SetTextSeen: Integer;
    FontSeen: Integer;
    Swallow: Cardinal;
    procedure WndProc(var Message: TMessage); override;
    function SystemProc: Pointer;
    property Text;
    property Font;
  end;

constructor TTestWin.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  ControlStyle := ControlStyle + [csAcceptsControls];
end;

procedure TTopWin.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.Style := WS_OVERLAPPEDWINDOW or WS_CLIPCHILDREN;
  Params.WndParent := 0;
end;

procedure TTestEdit.CreateParams(var Params: TCreateParams);
begin
  inherited CreateParams(Params);
  Params.SubclassOf := 'EDIT';
  Params.Style := Params.Style or $0080;                     { ES_AUTOHSCROLL }
end;

procedure TTestEdit.WndProc(var Message: TMessage);
begin
  case Message.Msg of
    EM_SETSEL: SelSeen := SelSeen + 1;
    WM_SETTEXT: SetTextSeen := SetTextSeen + 1;
    WM_SETFONT: FontSeen := FontSeen + 1;
  end;
  if (Swallow <> 0) and (Message.Msg = Swallow) then
  begin
    Message.Result := 0;
    Exit;
  end;
  inherited WndProc(Message);
end;

function TTestEdit.SystemProc: Pointer;
begin
  Result := DefWndProc;
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

procedure Run;
var
  Root: TTopWin;
  E: TTestEdit;
  H: HWND;
  SelStart, SelEnd: DWORD;
  Info: TWndClassExW;
begin
  Root := TTopWin.Create(nil);
  try
    Root.SetBounds(100, 100, 300, 200);
    E := TTestEdit.Create(Root);
    E.Name := 'E';
    E.SetBounds(10, 10, 150, 24);
    E.Text := 'hello world';

    Section('without a handle (UIF-060 DefaultHandler)');
    CheckEqS('hello world', E.Text, 'text from the field');
    CheckEqB(False, E.HandleAllocated, 'no handle yet');
    CheckEqI(1, E.SetTextSeen, 'WM_SETTEXT went through WndProc without a handle');

    Section('system class (UIF-032 step 2c)');
    E.Parent := Root;
    H := E.Handle;
    Check(H <> 0, 'handle created');
    CheckEqS('Edit', ClassOf(H), 'instantiated from the system class');
    FillChar(Info, SizeOf(Info), 0);
    Info.cbSize := SizeOf(Info);
    CheckEqB(False, GetClassInfoExW(HINST(HInstance), 'PN.TTestEdit', Info), 'no own class registered');
    CheckEqB(True, Pointer(GetWindowLongPtrW(H, GWLP_WNDPROC)) <> E.SystemProc, 'window procedure replaced');
    CheckEqB(True, E.SystemProc <> nil, 'system procedure kept as default');
    CheckEqB(True, FindControl(H) = E, 'object attached');
    CheckEqS('hello world', NativeText(H), 'caption given at creation');

    Section('native behaviour kept');
    SendMessageW(H, EM_SETSEL, 2, 5);
    SelStart := 0;
    SelEnd := 0;
    SendMessageW(H, EM_GETSEL, WPARAM(@SelStart), LPARAM(@SelEnd));
    CheckEqI(2, SelStart, 'EM_SETSEL start');
    CheckEqI(5, SelEnd, 'EM_SETSEL end');
    CheckEqI(1, E.SelSeen, 'our WndProc saw EM_SETSEL');
    E.Perform(EM_SETSEL, 0, 3);
    SendMessageW(H, EM_GETSEL, WPARAM(@SelStart), LPARAM(@SelEnd));
    CheckEqI(3, SelEnd, 'Perform reaches the native control');
    SendMessageW(H, WM_SETTEXT, 0, LPARAM(PChar('native')));
    CheckEqS('native', E.Text, 'text read from the native control');
    E.Text := 'from field';
    CheckEqS('from field', NativeText(H), 'text written to the native control');

    Section('font push (UIF-032 step 2f)');
    CheckEqI(1, E.FontSeen, 'WM_SETFONT through our WndProc at creation');
    CheckEqB(True, HFONT(SendMessageW(H, WM_GETFONT, 0, 0)) = E.Font.Handle, 'system control uses Font.Handle');

    Section('consumed without inherited (UIF-061)');
    E.Swallow := WM_SETTEXT;
    SendMessageW(H, WM_SETTEXT, 0, LPARAM(PChar('blocked')));
    CheckEqS('from field', NativeText(H), 'native control never saw the message');
    E.Swallow := 0;

    Section('re-creation');
    E.DestroyHandle;
    CheckEqB(False, IsWindow(H), 'window destroyed');
    CheckEqS('from field', E.Text, 'native text saved into the field');
    H := E.Handle;
    CheckEqS('from field', NativeText(H), 'text rebuilt');
    CheckEqS('Edit', ClassOf(H), 'system class again');
    CheckEqB(True, FindControl(H) = E, 'attached again');
  finally
    Root.Free;
  end;
end;

begin
  Run;
  Done;
end.
