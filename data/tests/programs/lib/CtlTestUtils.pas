{ CtlTestUtils - helpers shared by the test programs of the data-aware controls: a shown form, simulated
  keyboard input sent straight to a window, native state probes and a counter in place of the rejection beep. }
unit CtlTestUtils;

interface

uses
  Windows, Messages, SysUtils, Classes, Controls, Forms, StdCtrls, DB, DBCtrls;

{ A shown form at a fixed place. Exit code 3 when no window can be created (no interactive window station). }
function NewForm: TForm;
{ The rejection beep of the controls is counted, not sounded. }
procedure HookBeeps;
{ The beeps since the last call. }
function TakeBeeps: Integer;
{ Each character as WM_CHAR, as the keyboard would deliver it. }
procedure TypeText(H: HWND; const S: string);
{ WM_KEYDOWN, then (unless swallowed by the control: it does not matter here) WM_KEYUP. }
procedure PressKey(H: HWND; Key: Word);
{ The click notification of a button-class control, as its parent window reflects it to the control when the
  user clicks it or presses Space (a simulated mouse click needs the active window, which a test cannot rely on). }
procedure ClickControl(C: TWinControl);
{ The window text as the system reports it. }
function WinText(H: HWND): string;
{ The native read-only state of an edit window. }
function NativeReadOnly(H: HWND): Boolean;
{ Class and message of the exception SetFocus raises; '' when none. }
function FocusError(C: TWinControl): string;
{ The text with line breaks shown as \r and \n. }
function Visible(const S: string): string;

implementation

var
  Beeps: Integer;

procedure CountBeep;
begin
  Inc(Beeps);
end;

procedure HookBeeps;
begin
  RejectBeepHook := CountBeep;
end;

function TakeBeeps: Integer;
begin
  Result := Beeps;
  Beeps := 0;
end;

function NewForm: TForm;
begin
  Result := TForm.CreateNew(nil);
  try
    Result.SetBounds(40, 40, 420, 320);
    Result.Show;
    Result.HandleNeeded;
  except
    on E: Exception do
    begin
      WriteLn('cannot create window: ', E.Message);
      Halt(3);
    end;
  end;
end;

procedure TypeText(H: HWND; const S: string);
var
  I: Integer;
begin
  for I := 1 to Length(S) do
    SendMessageW(H, WM_CHAR, WPARAM(Ord(S[I])), 1);
end;

procedure PressKey(H: HWND; Key: Word);
begin
  SendMessageW(H, WM_KEYDOWN, WPARAM(Key), 1);
  SendMessageW(H, WM_KEYUP, WPARAM(Key), LPARAM($C0000001));
end;

procedure ClickControl(C: TWinControl);
begin
  C.Perform(CN_COMMAND, WPARAM(BN_CLICKED shl 16), LPARAM(C.Handle));
end;

function WinText(H: HWND): string;
var
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXTLENGTH, 0, 0));
  SetLength(Result, N);
  if N > 0 then
    SetLength(Result, Integer(SendMessageW(H, WM_GETTEXT, WPARAM(N + 1), LPARAM(PChar(Result)))));
end;

function NativeReadOnly(H: HWND): Boolean;
begin
  Result := (GetWindowLongPtrW(H, GWL_STYLE) and ES_READONLY) <> 0;
end;

function FocusError(C: TWinControl): string;
begin
  Result := '';
  try
    C.SetFocus;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

function Visible(const S: string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 1 to Length(S) do
    if S[I] = #13 then
      Result := Result + '\r'
    else if S[I] = #10 then
      Result := Result + '\n'
    else
      Result := Result + S[I];
end;

end.
