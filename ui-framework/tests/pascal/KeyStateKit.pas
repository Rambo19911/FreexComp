unit KeyStateKit;
{ U-141: the Shift / Ctrl state of the calling thread for programs that feed key messages to controls (shared unit,
  not a program). The key state of a thread is not private to it: the system brings it in line with the real
  keyboard when the thread retrieves messages, so a modifier held on the real desktop while a program runs (another
  test or a person typing) shows up in GetKeyState and in the character translation of the message loop. A real Alt
  next to the Ctrl of a test turns Ctrl+Q into a key without a character, for example.
  SetKeyState therefore writes the whole table (every key up except the two asked for; the lock toggles are kept)
  instead of changing one entry of whatever was there, and it writes the table again for every key message that
  Application retrieves, after the retrieval and before the shortcut search, the translation and the dispatch
  (Application.OnMessage). No real input is produced and no other thread is affected. }

interface

uses
  Windows, Messages, Forms;

{ Shift and Ctrl down or up, every other key up; stays in force for later key messages until the next call }
procedure SetKeyState(Shift, Ctrl: Boolean);

{ SendMessageW / PostMessageW for a key or character message under the state of the last SetKeyState (no modifier
  when it was never called): a sent message is not retrieved, so the table is written just before the call }
function SendKeyMessage(Wnd: HWND; Msg: UINT; W: WPARAM; L: LPARAM): LRESULT;
function PostKeyMessage(Wnd: HWND; Msg: UINT; W: WPARAM; L: LPARAM): BOOL;

implementation

type
  TKeyStateGuard = class
  public
    procedure AppMessage(var Msg: TMsg; var Handled: Boolean);
  end;

var
  Guard: TKeyStateGuard;
  WantShift, WantCtrl: Boolean;

procedure Apply;
var
  Old, State: TKeyboardState;
begin
  GetKeyboardState(Old);
  FillChar(State, SizeOf(State), 0);
  State[VK_CAPITAL] := Old[VK_CAPITAL] and 1;
  State[VK_NUMLOCK] := Old[VK_NUMLOCK] and 1;
  State[VK_SCROLL] := Old[VK_SCROLL] and 1;
  if WantShift then
    State[VK_SHIFT] := $80;
  if WantCtrl then
    State[VK_CONTROL] := $80;
  SetKeyboardState(State);
end;

procedure TKeyStateGuard.AppMessage(var Msg: TMsg; var Handled: Boolean);
begin
  if (Msg.message >= WM_KEYFIRST) and (Msg.message <= WM_KEYLAST) then
    Apply;
end;

procedure SetKeyState(Shift, Ctrl: Boolean);
begin
  WantShift := Shift;
  WantCtrl := Ctrl;
  if Guard = nil then
  begin
    Guard := TKeyStateGuard.Create;
    Application.OnMessage := Guard.AppMessage;
  end;
  Apply;
end;

function SendKeyMessage(Wnd: HWND; Msg: UINT; W: WPARAM; L: LPARAM): LRESULT;
begin
  SetKeyState(WantShift, WantCtrl);
  Result := SendMessageW(Wnd, Msg, W, L);
end;

function PostKeyMessage(Wnd: HWND; Msg: UINT; W: WPARAM; L: LPARAM): BOOL;
begin
  SetKeyState(WantShift, WantCtrl);
  Result := PostMessageW(Wnd, Msg, W, L);
end;

initialization

finalization
  if Guard <> nil then
  begin
    Application.OnMessage := nil;
    Guard.Free;
    Guard := nil;
  end;
end.
