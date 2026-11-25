// covers: UIF-1331, UIF-1332, UIF-1333, UIF-1334, UIF-1335, UIF-1336
// needs: desktop
// phase: 8
program Ctl_RichEditSearchLinks;
{ U-146, rich-edit.md section 6: FindText offsets in engine positions - plain, whole word, match case, a range, not
  found, empty - without moving the selection (UIF-1335, card acceptance 3a); AutoURLDetect marks a URL in text
  that is set and in existing text (UIF-1331); a left click on the link (button down and up messages at the
  position EM_POSFROMCHAR gives) fires OnLinkClick once with the URL, a right click and a click beside the link
  fire nothing (UIF-1332, card acceptance 3b); OnSelectionChange, OnProtectChange refused without a handler,
  refused and allowed by one, never for the control's own content operations, and none of the events during the
  CreateWnd push (UIF-1333); Zoom range, read-back and persistence (UIF-1334); Undo, Redo, CanRedo, ClearUndo
  (UIF-1336). }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ComCtrls, RichEdit, TestKit;

type
  TLog = class
  public
    Text: string;
    Allow: Boolean;
    procedure Changed(Sender: TObject);
    procedure Link(Sender: TObject; const URL: string);
    procedure Selection(Sender: TObject);
    procedure Protect(Sender: TObject; StartPos, EndPos: Integer; var AllowChange: Boolean);
  end;

const
  CRLF = #13#10;
  Words = 'one two One twofold TWO two.';
  Linked = 'see https://example.org/page now';

var
  Log: TLog;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

procedure TLog.Link(Sender: TObject; const URL: string);
begin
  Text := Text + 'link:' + URL + ';';
end;

procedure TLog.Selection(Sender: TObject);
begin
  Text := Text + Format('sel:%d+%d;', [TRichEdit(Sender).SelStart, TRichEdit(Sender).SelLength]);
end;

procedure TLog.Protect(Sender: TObject; StartPos, EndPos: Integer; var AllowChange: Boolean);
begin
  Text := Text + Format('protect:%d..%d:%s;', [StartPos, EndPos, BoolToStr(AllowChange, True)]);
  AllowChange := Allow;
end;

procedure NativeSelect(H: HWND; First, Last: Integer);
var
  R: TCharRange;
begin
  R.cpMin := First;
  R.cpMax := Last;
  SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@R));
end;

function NativeZoom(H: HWND): string;
var
  N, D: Integer;
begin
  N := -1;
  D := -1;
  SendMessageW(H, EM_GETZOOM, WPARAM(@N), LPARAM(@D));
  Result := Format('%d/%d', [N, D]);
end;

{ True when the character at Pos is marked as a link by the engine }
function IsLink(R: TRichEdit; Pos: Integer): Boolean;
var
  CF: TCharFormat2W;
begin
  NativeSelect(R.Handle, Pos, Pos + 1);
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  SendMessageW(R.Handle, EM_GETCHARFORMAT, SCF_SELECTION, LPARAM(@CF));
  Result := (CF.dwEffects and CFE_LINK) <> 0;
end;

{ a click as the system delivers it: button down and up messages at the place of the character at Pos }
procedure Click(R: TRichEdit; Pos: Integer; Down, Up: Cardinal; Keys: WPARAM);
var
  P: TPoint;
  Where: LPARAM;
begin
  P.X := 0;
  P.Y := 0;
  SendMessageW(R.Handle, EM_POSFROMCHAR, WPARAM(@P), LPARAM(Pos));
  Where := LPARAM(((P.Y + 4) shl 16) or (P.X + 2));
  SendMessageW(R.Handle, Down, Keys, Where);
  SendMessageW(R.Handle, Up, 0, Where);
end;

procedure Run;
var
  F: TForm;
  R, N: TRichEdit;
  M: TMemoryStream;
  H: HWND;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    R := TRichEdit.Create(F);
    R.Width := 380;
    R.Parent := F;
    R.HandleNeeded;

    Section('FindText (UIF-1335)');
    R.Text := Words;
    R.SelStart := 2;
    R.SelLength := 3;
    CheckEqI(4, R.FindText('two', 0, -1, []), 'plain: the first match');
    CheckEqI(4, R.FindText('TWO', 0, -1, []), 'plain: letter case is ignored');
    CheckEqI(20, R.FindText('TWO', 0, -1, [stMatchCase]), 'match case');
    CheckEqI(12, R.FindText('two', 5, -1, []), 'from a start position: inside a longer word');
    CheckEqI(20, R.FindText('two', 5, -1, [stWholeWord]), 'whole word');
    CheckEqI(24, R.FindText('two', 5, -1, [stWholeWord, stMatchCase]), 'whole word and match case');
    CheckEqI(4, R.FindText('two', 0, 7, []), 'a range that contains the match');
    CheckEqI(-1, R.FindText('two', 0, 6, []), 'a range that ends inside the match');
    CheckEqI(-1, R.FindText('two', 5, 5, []), 'a range without a match');
    CheckEqI(24, R.FindText('two', 21, 1000, []), 'a range past the end means to the end of the text');
    CheckEqI(-1, R.FindText('two', 25, -1, []), 'not found after the last match');
    CheckEqI(-1, R.FindText('three', 0, -1, []), 'not found');
    CheckEqI(-1, R.FindText('', 0, -1, []), 'an empty search string');
    CheckEqI(27, R.FindText('.', 0, -1, []), 'the last character');
    CheckEqS('2+3', Format('%d+%d', [R.SelStart, R.SelLength]), 'the selection is not changed');
    R.Text := 'ab' + CRLF + 'cd' + CRLF + 'ab';
    CheckEqI(3, R.FindText('cd', 0, -1, []), 'engine positions: a paragraph break counts once');
    CheckEqI(6, R.FindText('ab', 1, -1, []), 'the match in the third paragraph');
    N := TRichEdit.Create(nil);
    try
      N.Text := Words;
      CheckEqI(-1, N.FindText('two', 0, -1, []), 'without a handle: -1');
    finally
      N.Free;
    end;

    Section('AutoURLDetect (UIF-1331)');
    R.OnLinkClick := Log.Link;
    CheckEqB(True, R.AutoURLDetect, 'on by default');
    R.AutoURLDetect := False;
    CheckEqB(False, SendMessageW(R.Handle, EM_GETAUTOURLDETECT, 0, 0) <> 0, 'switched off: the engine does not detect');
    R.Text := Linked;
    CheckEqB(False, IsLink(R, 6), 'off: nothing is marked');
    Log.Text := '';
    Click(R, 8, WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON);
    CheckEqS('', Log.Text, 'no link, no OnLinkClick');
    R.AutoURLDetect := True;
    CheckEqB(True, SendMessageW(R.Handle, EM_GETAUTOURLDETECT, 0, 0) <> 0, 'the engine detects URLs');
    CheckEqB(True, IsLink(R, 6), 'existing text is scanned when the property becomes True');
    CheckEqB(False, IsLink(R, 1), 'the text before the URL is no link');
    R.Text := 'go to ' + Linked + '!';
    CheckEqB(True, IsLink(R, 12), 'text that is set is scanned');
    CheckEqB(False, IsLink(R, 3), 'the text before the URL');
    CheckEqB(False, IsLink(R, 35), 'the text after the URL');
    R.Text := Linked;

    Section('OnLinkClick (UIF-1332)');
    Log.Text := '';
    Click(R, 8, WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON);
    CheckEqS('link:https://example.org/page;', Log.Text, 'a left click on the link fires once with the URL');
    Log.Text := '';
    Click(R, 8, WM_RBUTTONDOWN, WM_RBUTTONUP, MK_RBUTTON);
    CheckEqS('', Log.Text, 'a right click fires nothing');
    Click(R, 8, WM_MBUTTONDOWN, WM_MBUTTONUP, MK_MBUTTON);
    CheckEqS('', Log.Text, 'a middle click fires nothing');
    Click(R, 1, WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON);
    CheckEqS('', Log.Text, 'a left click beside the link fires nothing');
    Click(R, 27, WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON);
    CheckEqS('link:https://example.org/page;', Log.Text, 'a click on the last character of the link');
    R.OnLinkClick := nil;
    Click(R, 8, WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON);
    Check(True, 'a click without a handler does nothing');
    R.AutoURLDetect := False;
    CheckEqB(False, SendMessageW(R.Handle, EM_GETAUTOURLDETECT, 0, 0) <> 0, 'detection switched off');

    Section('OnSelectionChange (UIF-1333)');
    R.Text := 'abcdef';
    R.SelStart := 0;
    R.OnSelectionChange := Log.Selection;
    Log.Text := '';
    R.SelStart := 3;
    CheckEqS('sel:3+0;', Log.Text, 'the caret moved by code');
    Log.Text := '';
    R.SelLength := 2;
    CheckEqS('sel:3+2;', Log.Text, 'the selection changed by code');
    Log.Text := '';
    NativeSelect(R.Handle, 1, 4);
    CheckEqS('sel:1+3;', Log.Text, 'the selection changed with the engine''s message');
    Log.Text := '';
    SendMessageW(R.Handle, WM_KEYDOWN, VK_RIGHT, 1);
    CheckEqS('sel:4+0;', Log.Text, 'the caret moved with a key');
    Log.Text := '';
    R.WordWrap := False;
    R.WordWrap := True;
    CheckEqS('', Log.Text, 'not during the CreateWnd push');
    CheckEqI(4, R.SelStart, 'although the selection was restored');
    R.OnSelectionChange := nil;

    Section('OnProtectChange (UIF-1333)');
    R.Text := 'open locked open';
    NativeSelect(R.Handle, 5, 11);
    R.SelAttributes.Protected := True;
    NativeSelect(R.Handle, 7, 9);
    R.SelText := 'X';
    CheckEqS('open locked open', R.Text, 'no handler: a change of protected text is refused');
    NativeSelect(R.Handle, 0, 4);
    R.SelText := 'OPEN';
    CheckEqS('OPEN locked open', R.Text, 'unprotected text can be changed');
    R.OnProtectChange := Log.Protect;
    R.OnChange := Log.Changed;
    Log.Allow := False;
    Log.Text := '';
    NativeSelect(R.Handle, 7, 9);
    R.SelText := 'X';
    CheckEqS('protect:7..9:False;', Log.Text, 'the handler gets the range of the attempt and AllowChange = False');
    CheckEqS('OPEN locked open', R.Text, 'refused by the handler');
    Log.Text := '';
    SendMessageW(R.Handle, WM_CHAR, Ord('q'), 1);
    CheckEqS('protect:7..9:False;', Log.Text, 'typing asks too');
    CheckEqS('OPEN locked open', R.Text, 'and is refused');
    Log.Allow := True;
    Log.Text := '';
    R.SelText := 'X';
    CheckEqS('protect:7..9:False;change;', Log.Text, 'asked again, then the change is carried out');
    CheckEqS('OPEN loXed open', R.Text, 'allowed by the handler');
    Log.Allow := False;
    Log.Text := '';
    NativeSelect(R.Handle, 5, 10);
    R.SelAttributes.Style := [fsBold];
    CheckEqS('change;', Log.Text, 'a format change is not a change of the text: no question');
    CheckEqB(True, fsBold in R.SelAttributes.Style, 'and it is carried out');
    R.OnChange := nil;
    Log.Text := '';
    H := R.Handle;
    R.WordWrap := False;
    Check(R.Handle <> H, 'the window is re-created');
    CheckEqS('', Log.Text, 'not during the CreateWnd push');
    CheckEqS('OPEN loXed open', R.Text, 'protected content survives the re-creation');
    NativeSelect(R.Handle, 5, 10);
    CheckEqB(True, R.SelAttributes.Protected, 'with its protection');
    R.WordWrap := True;
    M := TMemoryStream.Create;
    try
      R.Lines.SaveToStream(M);
      M.Position := 0;
      Log.Text := '';
      R.Lines.LoadFromStream(M);
      CheckEqS('', Log.Text, 'loading replaces protected content without a question');
      CheckEqS('OPEN loXed open', R.Text, 'the loaded content');
      R.Zoom := 300;
      M.Position := 0;
      R.Lines.LoadFromStream(M);
      CheckEqI(300, R.Zoom, 'a load keeps the zoom (UIF-1334)');
      R.Zoom := 100;
    finally
      M.Free;
    end;
    Log.Text := '';
    R.Text := 'replaced';
    CheckEqS('protect:0..16:False;', Log.Text, 'Text := on protected content asks (the range includes the final paragraph mark)');
    CheckEqS('OPEN loXed open', R.Text, 'and is refused');
    Log.Allow := True;
    R.Text := 'replaced';
    CheckEqS('replaced', R.Text, 'allowed');
    R.OnProtectChange := nil;

    Section('Undo, Redo, ClearUndo (UIF-1336)');
    R.Text := 'abc';
    CheckEqS('False False', Format('%s %s', [BoolToStr(R.CanUndo, True), BoolToStr(R.CanRedo, True)]),
      'nothing to undo or redo after a Text assignment');
    R.SelStart := 3;
    R.SelText := 'd';
    R.SelText := 'e';
    CheckEqS('abcde', R.Text, 'two changes');
    CheckEqS('True False', Format('%s %s', [BoolToStr(R.CanUndo, True), BoolToStr(R.CanRedo, True)]),
      'CanUndo, not CanRedo');
    R.Undo;
    CheckEqS('abcd', R.Text, 'Undo takes back one change (multi-level)');
    R.Undo;
    CheckEqS('abc', R.Text, 'Undo again');
    CheckEqS('False True', Format('%s %s', [BoolToStr(R.CanUndo, True), BoolToStr(R.CanRedo, True)]),
      'CanRedo after Undo');
    R.Redo;
    CheckEqS('abcd', R.Text, 'Redo');
    R.Redo;
    CheckEqS('abcde', R.Text, 'Redo again');
    CheckEqB(False, R.CanRedo, 'nothing left to redo');
    R.Undo;
    R.ClearUndo;
    CheckEqS('False False', Format('%s %s', [BoolToStr(R.CanUndo, True), BoolToStr(R.CanRedo, True)]),
      'ClearUndo empties both');
    CheckEqS('abcd', R.Text, 'and leaves the text');
    N := TRichEdit.Create(nil);
    try
      N.Text := 'no window';
      N.Redo;
      N.ClearUndo;
      N.Undo;
      CheckEqS('False False', Format('%s %s', [BoolToStr(N.CanUndo, True), BoolToStr(N.CanRedo, True)]),
        'without a handle: False, and the methods do nothing');
      CheckEqS('no window', N.Text, 'text unchanged');
    finally
      N.Free;
    end;

    Section('Zoom (UIF-1334)');
    CheckEqI(100, R.Zoom, 'default 100');
    CheckEqS('0/0', NativeZoom(R.Handle), '100 is zoom off');
    R.Zoom := 250;
    CheckEqI(250, R.Zoom, 'Zoom 250');
    CheckEqS('250/100', NativeZoom(R.Handle), 'EM_SETZOOM(250, 100)');
    R.Zoom := 5;
    CheckEqI(10, R.Zoom, 'below the range: 10');
    R.Zoom := 900;
    CheckEqI(500, R.Zoom, 'above the range: 500');
    CheckEqS('500/100', NativeZoom(R.Handle), 'the clamped value reaches the engine');
    R.Zoom := 100;
    CheckEqS('0/0', NativeZoom(R.Handle), '100 switches the zoom off');
    SendMessageW(R.Handle, EM_SETZOOM, 3, 2);
    CheckEqI(150, R.Zoom, 'a zoom set in the engine (as Ctrl + wheel does) is read back');
    R.Text := 'new text';
    CheckEqI(150, R.Zoom, 'kept when Text is assigned');
    R.Lines.Text := 'other' + CRLF + 'lines';
    CheckEqI(150, R.Zoom, 'kept when Lines.Text is assigned');
    H := R.Handle;
    R.ScrollBars := ssVertical;
    Check(R.Handle <> H, 'the window is re-created');
    CheckEqI(150, R.Zoom, 'kept across the re-creation');
    R.DestroyHandle;
    CheckEqI(150, R.Zoom, 'kept without a window');
    R.Zoom := 1000;
    CheckEqI(500, R.Zoom, 'clamped without a window');
    R.HandleNeeded;
    CheckEqS('500/100', NativeZoom(R.Handle), 'pushed to the new window');
    R.SelectAll;
    CheckEqI(R.Font.Size, R.SelAttributes.Size, 'Zoom never changes a stored size');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
