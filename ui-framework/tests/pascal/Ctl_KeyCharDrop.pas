// covers: UIF-085
// needs: desktop
program Ctl_KeyCharDrop;
{ U-123, core.md 7.9 UIF-085 step 2: Key := 0 in OnKeyDown drops the WM_CHAR translated from that key. The mark
  that drops it lives only until the key's own WM_KEYUP (or the next WM_KEYDOWN): a consumed key that produces no
  character (an arrow key) must not drop a later bare WM_CHAR (one sent without its own key-down, as a test or
  another program sends it). A consumed letter key still drops its own character, which arrives before its key-up.
  This control facet only: the Ctl_* sections of testing.md 3.5 (defaults, streaming, leak) do not apply to a
  message-path fix. }
uses
  Windows, Messages, Types, SysUtils, Classes, Controls, Forms, StdCtrls, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
    procedure KeyPress(Sender: TObject; var Key: Char);
  end;

var
  Log: TLog;

{ consumes the arrow keys and Q }
procedure TLog.KeyDown(Sender: TObject; var Key: Word; Shift: TShiftState);
begin
  Text := Text + 'down ' + IntToStr(Key) + ';';
  if (Key = VK_UP) or (Key = VK_DOWN) or (Key = Ord('Q')) then
    Key := 0;
end;

procedure TLog.KeyPress(Sender: TObject; var Key: Char);
begin
  Text := Text + 'press ' + Key + ';';
end;

procedure KeyDownMsg(C: TWinControl; VK: Word);
begin
  SendMessageW(C.Handle, WM_KEYDOWN, VK, 1);
end;

procedure KeyUpMsg(C: TWinControl; VK: Word);
begin
  SendMessageW(C.Handle, WM_KEYUP, VK, LPARAM($C0000001));
end;

procedure CharMsg(C: TWinControl; Ch: Char);
begin
  SendMessageW(C.Handle, WM_CHAR, Ord(Ch), 1);
end;

var
  F: TForm;
  E: TEdit;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    E := TEdit.Create(F);
    E.Parent := F;
    E.OnKeyDown := Log.KeyDown;
    E.OnKeyPress := Log.KeyPress;
    E.HandleNeeded;

    Section('a consumed key without a character (U-123)');
    KeyDownMsg(E, VK_UP);
    KeyUpMsg(E, VK_UP);
    Log.Text := '';
    CharMsg(E, 'z');
    CheckEqS('press z;', Log.Text, 'after a consumed arrow key and its key-up a bare WM_CHAR reaches OnKeyPress');
    CheckEqS('z', E.Text, 'and is typed');
    KeyDownMsg(E, VK_DOWN);
    KeyDownMsg(E, Ord('A'));
    Log.Text := '';
    CharMsg(E, 'a');
    CheckEqS('press a;', Log.Text, 'a key-down that is not consumed clears the mark');

    Section('a consumed key with a character (UIF-085 unchanged)');
    Log.Text := '';
    KeyDownMsg(E, Ord('Q'));
    CharMsg(E, 'q');
    KeyUpMsg(E, Ord('Q'));
    CheckEqS('down 81;', Log.Text, 'its own WM_CHAR, before the key-up, is dropped');
    CheckEqS('za', E.Text, 'nothing typed');
    Log.Text := '';
    CharMsg(E, 'w');
    CheckEqS('press w;', Log.Text, 'the next character is not');
    Log.Text := '';
    KeyDownMsg(E, VK_UP);
    CharMsg(E, 'r');
    CheckEqS('down 38;', Log.Text, 'without a key-up in between, the next WM_CHAR is still dropped');
  finally
    F.Free;
    Log.Free;
  end;
  Done;
end.
