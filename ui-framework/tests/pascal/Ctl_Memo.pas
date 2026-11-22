// covers: UIF-630, UIF-631, UIF-632
// needs: desktop
program Ctl_Memo;
{ U-055, controls-standard.md section 3: TMemo defaults (185 x 89, WordWrap, WantReturns, no intrinsic height);
  a sub-classed multi-line EDIT with the listed style bits, each "-> recreate" property re-creating the window
  and keeping the lines; Lines as a view of the text (UIF-630) without and with a handle: CR LF separators, the
  trailing line break of Lines.Text, an empty memo has no lines, logical lines under word wrap, edits through
  EM_SETSEL + EM_REPLACESEL, the lines surviving the window; Text is the raw text (UIF-631); CaretPos;
  CM_WANTSPECIALKEY and WM_GETDLGCODE for Return/Tab, Escape passed to the form and never closing it (UIF-632);
  OnChange; streaming of every published property kind; leak check. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, TestKit;

type
  TKeyBox = class(TCustomControl)
  protected
    procedure WndProc(var Message: TMessage); override;
  public
    constructor Create(AOwner: TComponent); override;
  end;

  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
  end;

  TMemoForm = class(TForm)
  published
    Notes: TMemo;
    procedure NotesChange(Sender: TObject);
  end;


{$FORM Ctl_Memo.form}

const
  CRLF = #13#10;

var
  Log: TLog;

constructor TKeyBox.Create(AOwner: TComponent);
begin
  inherited Create(AOwner);
  SetBounds(250, 0, 20, 20);
end;

procedure TKeyBox.WndProc(var Message: TMessage);
begin
  if Message.Msg = CM_DIALOGKEY then
  begin
    Log.Text := Log.Text + 'dialogkey:' + IntToStr(Message.WParam) + ';';
    Message.Result := 1;
  end
  else
    inherited WndProc(Message);
end;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

procedure TMemoForm.NotesChange(Sender: TObject);
begin
  Log.Text := Log.Text + 'streamed change;';
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
  Buf: array[0..1023] of WideChar;
  N: Integer;
begin
  N := Integer(SendMessageW(H, WM_GETTEXT, 1024, LPARAM(@Buf[0])));
  SetLength(Result, N);
  if N > 0 then
    Move(Buf[0], PChar(Result)^, N * SizeOf(Char));
end;

{ the text with CR LF shown as '|' }
function Shown(const S: string): string;
begin
  Result := StringReplace(S, CRLF, '|', [rfReplaceAll]);
end;

function HasStyle(E: TWinControl; Bits: NativeInt): Boolean;
begin
  Result := (GetWindowLongPtrW(E.Handle, GWL_STYLE) and Bits) = Bits;
end;

{ GDI + USER objects of the process }
function GuiObjects: Integer;
begin
  Result := Integer(GetGuiResources(GetCurrentProcess, GR_GDIOBJECTS))
    + Integer(GetGuiResources(GetCurrentProcess, GR_USEROBJECTS));
end;

procedure Press(C: TWinControl; Key: Word);
begin
  Log.Text := '';
  PostMessageW(C.Handle, WM_KEYDOWN, Key, 1);
  Application.ProcessMessages;
end;

procedure SetCtrl(Down: Boolean);
var
  State: TKeyboardState;
begin
  GetKeyboardState(State);
  if Down then
    State[VK_CONTROL] := $80
  else
    State[VK_CONTROL] := 0;
  SetKeyboardState(State);
end;

{ the same Lines checks without and with a handle }
procedure CheckLines(M: TMemo; const Kind: string);
begin
  M.Lines.Clear;
  CheckEqI(0, M.Lines.Count, Kind + ': empty memo has no lines');
  CheckEqS('', M.Lines.Text, Kind + ': empty Lines.Text');
  M.Lines.Add('a');
  M.Lines.Add('b');
  M.Lines.Add('c');
  CheckEqI(3, M.Lines.Count, Kind + ': Add');
  CheckEqS('a|b|c', Shown(M.Text), Kind + ': Text is the raw text (no trailing break)');
  CheckEqS('a|b|c|', Shown(M.Lines.Text), Kind + ': Lines.Text adds the trailing break');
  M.Lines.Insert(0, 'z');
  CheckEqS('z|a|b|c', Shown(M.Text), Kind + ': Insert at 0');
  M.Lines.Insert(4, 'end');
  CheckEqS('z|a|b|c|end', Shown(M.Text), Kind + ': Insert at Count');
  M.Lines.Delete(1);
  CheckEqS('z|b|c|end', Shown(M.Text), Kind + ': Delete in the middle');
  M.Lines.Delete(3);
  CheckEqS('z|b|c', Shown(M.Text), Kind + ': Delete the last');
  M.Lines[1] := 'BB';
  CheckEqS('z|BB|c', Shown(M.Text), Kind + ': Put');
  CheckEqS('c', M.Lines[2], Kind + ': Get');
  M.Lines.Delete(0);
  M.Lines.Delete(0);
  M.Lines.Delete(0);
  CheckEqI(0, M.Lines.Count, Kind + ': all deleted');
  CheckEqS('', M.Text, Kind + ': text empty');
  M.Text := 'p' + CRLF + 'q';
  CheckEqI(2, M.Lines.Count, Kind + ': Text assignment splits');
  CheckEqS('q', M.Lines[1], Kind + ': second line');
  M.Text := 'p' + CRLF;
  CheckEqI(2, M.Lines.Count, Kind + ': trailing break in Text: an empty last line');
  CheckEqS('', M.Lines[1], Kind + ': the empty last line');
  M.Lines.Text := 'u' + CRLF + 'v' + CRLF;
  CheckEqS('u|v', Shown(M.Text), Kind + ': Lines.Text removes the trailing break');
  CheckEqI(2, M.Lines.Count, Kind + ': two lines');
end;

procedure Run;
var
  F: TForm;
  M: TMemo;
  K: TKeyBox;
  SF: TMemoForm;
  H: HWND;
  P: TPoint;
  I, Before: Integer;
  Code: LRESULT;
begin
  Application.Initialize;
  Log := TLog.Create;
  F := TForm.CreateNew(nil);
  try
    F.ClientWidth := 400;
    F.ClientHeight := 300;
    M := TMemo.Create(F);

    Section('defaults');
    CheckEqI(185, M.Width, 'Width 185');
    CheckEqI(89, M.Height, 'Height 89');
    CheckEqB(True, M.Alignment = taLeftJustify, 'Alignment');
    CheckEqB(True, M.BorderStyle = bsSingle, 'BorderStyle');
    CheckEqB(True, M.Color = clWindow, 'Color clWindow');
    CheckEqB(True, M.HideSelection, 'HideSelection');
    CheckEqI(0, M.Lines.Count, 'Lines empty');
    CheckEqI(0, M.MaxLength, 'MaxLength');
    CheckEqB(False, M.ParentColor, 'ParentColor False');
    CheckEqB(True, M.ParentFont, 'ParentFont');
    CheckEqB(False, M.ReadOnly, 'ReadOnly');
    CheckEqB(True, M.ScrollBars = ssNone, 'ScrollBars ssNone');
    CheckEqI(-1, M.TabOrder, 'TabOrder');
    CheckEqB(True, M.TabStop, 'TabStop True');
    CheckEqB(True, M.WantReturns, 'WantReturns');
    CheckEqB(False, M.WantTabs, 'WantTabs');
    CheckEqB(True, M.WordWrap, 'WordWrap');
    Check(GetClass('TMemo') <> nil, 'registered');
    M.Height := 40;
    CheckEqI(40, M.Height, 'no intrinsic height');
    M.Height := 89;

    Section('Lines without a handle (UIF-630)');
    CheckLines(M, 'no handle');
    M.Lines.Clear;
    M.Lines.Add('');
    M.Lines.Add('x');
    CheckEqS('|x', Shown(M.Text), 'a leading empty line is kept');
    M.Lines.Clear;

    Section('window');
    M.Lines.Add('kept');
    M.Parent := F;
    H := M.Handle;
    CheckEqS('Edit', ClassOf(H), 'system class EDIT');
    CheckEqB(True, HasStyle(M, ES_MULTILINE or ES_AUTOVSCROLL), 'ES_MULTILINE, ES_AUTOVSCROLL');
    CheckEqB(False, HasStyle(M, ES_AUTOHSCROLL), 'WordWrap: no ES_AUTOHSCROLL');
    CheckEqB(True, HasStyle(M, ES_WANTRETURN), 'ES_WANTRETURN');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) <> 0, 'WS_EX_CLIENTEDGE');
    CheckEqS('kept', NativeText(H), 'lines pushed as the window text');
    M.WordWrap := False;
    Check(M.Handle <> H, 'WordWrap recreates');
    CheckEqB(True, HasStyle(M, ES_AUTOHSCROLL), 'no WordWrap: ES_AUTOHSCROLL');
    CheckEqS('kept', NativeText(M.Handle), 'text kept');
    M.WordWrap := True;
    H := M.Handle;
    M.ScrollBars := ssBoth;
    Check(M.Handle <> H, 'ScrollBars recreates');
    CheckEqB(True, HasStyle(M, WS_VSCROLL or WS_HSCROLL), 'ssBoth: WS_VSCROLL, WS_HSCROLL');
    M.ScrollBars := ssVertical;
    CheckEqB(True, HasStyle(M, WS_VSCROLL) and not HasStyle(M, WS_HSCROLL), 'ssVertical');
    M.ScrollBars := ssNone;
    H := M.Handle;
    M.WantReturns := False;
    Check(M.Handle <> H, 'WantReturns recreates');
    CheckEqB(False, HasStyle(M, ES_WANTRETURN), 'WantReturns False: no ES_WANTRETURN');
    M.WantReturns := True;
    H := M.Handle;
    M.Alignment := taCenter;
    Check(M.Handle <> H, 'Alignment recreates');
    CheckEqB(True, HasStyle(M, ES_CENTER), 'ES_CENTER');
    M.Alignment := taLeftJustify;
    H := M.Handle;
    M.BorderStyle := bsNone;
    Check(M.Handle <> H, 'BorderStyle recreates');
    CheckEqB(True, (GetWindowLongPtrW(M.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) = 0, 'bsNone');
    M.BorderStyle := bsSingle;
    M.ReadOnly := True;
    CheckEqB(True, HasStyle(M, ES_READONLY), 'ReadOnly');
    M.ReadOnly := False;

    Section('Lines with a handle (UIF-630)');
    CheckLines(M, 'handle');
    CheckEqS('u|v', Shown(NativeText(M.Handle)), 'the window holds the text');
    M.Lines.Clear;
    M.Width := 60;
    M.Lines.Add('aaaa bbbb cccc dddd eeee ffff gggg');
    Check(SendMessageW(M.Handle, EM_GETLINECOUNT, 0, 0) > 1, 'word wrap: several display lines');
    CheckEqI(1, M.Lines.Count, 'word wrap: one logical line');
    M.Width := 185;
    M.Lines.Text := 'one' + CRLF + 'two';
    M.DestroyHandle;
    CheckEqI(2, M.Lines.Count, 'lines survive the window');
    CheckEqS('two', M.Lines[1], 'second line after the window went');
    M.HandleNeeded;
    CheckEqS('one|two', Shown(NativeText(M.Handle)), 'lines pushed again');

    Section('CaretPos');
    M.Text := 'ab' + CRLF + 'cde';
    M.SelStart := 5;
    P := M.CaretPos;
    CheckEqS('1,1', Format('%d,%d', [P.X, P.Y]), 'CaretPos (column, line)');
    M.SelStart := 2;
    P := M.CaretPos;
    CheckEqS('2,0', Format('%d,%d', [P.X, P.Y]), 'CaretPos at the end of the first line');
    M.CaretPos := Point(2, 1);
    CheckEqI(6, M.SelStart, 'CaretPos := (2, 1)');

    Section('OnChange');
    M.OnChange := Log.Changed;
    Log.Text := '';
    M.Lines.Add('more');
    CheckEqS('change;', Log.Text, 'Lines.Add fires OnChange');
    Log.Text := '';
    M.Text := M.Text;
    CheckEqS('', Log.Text, 'same text: no OnChange');
    Check(M.HandleAllocated, 'the memo has a window');
    Log.Text := '';
    M.Text := 'set' + CRLF + 'by code';
    CheckEqS('change;', Log.Text, 'Text := with a window fires OnChange once (UIF-620; WM_SETTEXT sends no EN_CHANGE to a multi-line edit)');
    Log.Text := '';
    M.Lines.Text := 'by' + CRLF + 'Lines.Text';
    CheckEqS('change;', Log.Text, 'Lines.Text := with a window fires OnChange once');
    M.OnChange := nil;

    Section('special keys (UIF-632)');
    CheckEqI(1, M.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'WantReturns: wants Return');
    CheckEqI(0, M.Perform(CM_WANTSPECIALKEY, VK_TAB, 0), 'WantTabs False: Tab not wanted');
    CheckEqI(0, M.Perform(CM_WANTSPECIALKEY, VK_ESCAPE, 0), 'Escape not wanted');
    Code := SendMessageW(M.Handle, WM_GETDLGCODE, 0, 0);
    CheckEqB(False, (Code and DLGC_WANTALLKEYS) <> 0, 'WM_GETDLGCODE without DLGC_WANTALLKEYS');
    CheckEqB(False, (Code and DLGC_WANTTAB) <> 0, 'WM_GETDLGCODE without DLGC_WANTTAB');
    M.WantTabs := True;
    CheckEqI(1, M.Perform(CM_WANTSPECIALKEY, VK_TAB, 0), 'WantTabs: wants Tab');
    CheckEqB(True, (SendMessageW(M.Handle, WM_GETDLGCODE, 0, 0) and DLGC_WANTTAB) <> 0, 'WantTabs: DLGC_WANTTAB');
    M.WantTabs := False;
    M.WantReturns := False;
    CheckEqI(0, M.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'WantReturns False: Return not wanted');
    SetCtrl(True);
    CheckEqI(1, M.Perform(CM_WANTSPECIALKEY, VK_RETURN, 0), 'WantReturns False: Ctrl+Return wanted');
    SetCtrl(False);
    K := TKeyBox.Create(F);
    K.Parent := F;
    F.Show;
    M.SetFocus;
    Press(M, VK_RETURN);
    CheckEqS('dialogkey:13;', Log.Text, 'WantReturns False: Return goes to the form');
    K.TabStop := True;
    Press(M, VK_TAB);
    CheckEqB(True, F.ActiveControl = K, 'WantTabs False: Tab moves to the next control');
    CheckEqI(0, Pos(#9, M.Text), 'no tab inserted');
    K.TabStop := False;
    M.WantReturns := True;
    M.SetFocus;
    Press(M, VK_RETURN);
    CheckEqS('', Log.Text, 'WantReturns: Return stays in the memo');
    Press(M, VK_ESCAPE);
    CheckEqS('dialogkey:27;', Log.Text, 'Escape goes to the form');
    K.Free;
    M.SetFocus;
    Press(M, VK_ESCAPE);
    CheckEqB(True, F.Visible and IsWindowVisible(F.Handle), 'Escape without a cancel button closes nothing');

    Section('streaming');
    Log.Text := '';
    SF := TMemoForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnChange while loading');
      CheckEqI(3, SF.Notes.Lines.Count, 'Lines');
      CheckEqS('first||third', Shown(SF.Notes.Text), 'Lines with an empty line');
      CheckEqB(True, SF.Notes.Alignment = taRightJustify, 'Alignment');
      CheckEqB(True, SF.Notes.BorderStyle = bsNone, 'BorderStyle');
      CheckEqB(True, SF.Notes.Color = clYellow, 'Color');
      CheckEqI(-15, SF.Notes.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Notes.HideSelection, 'HideSelection');
      CheckEqI(100, SF.Notes.MaxLength, 'MaxLength');
      CheckEqB(True, SF.Notes.ReadOnly, 'ReadOnly');
      CheckEqB(True, SF.Notes.ScrollBars = ssVertical, 'ScrollBars');
      CheckEqB(False, SF.Notes.TabStop, 'TabStop False');
      CheckEqB(False, SF.Notes.WantReturns, 'WantReturns');
      CheckEqB(True, SF.Notes.WantTabs, 'WantTabs');
      CheckEqB(False, SF.Notes.WordWrap, 'WordWrap');
      CheckEqS('10,10,200,120', Format('%d,%d,%d,%d', [SF.Notes.Left, SF.Notes.Top, SF.Notes.Width,
        SF.Notes.Height]), 'bounds (no intrinsic height)');
      Check(Assigned(SF.Notes.OnChange), 'OnChange bound');
      SF.Notes.Lines.Add('x');
      CheckEqS('streamed change;', Log.Text, 'OnChange handler of the form');
    finally
      SF.Free;
    end;

    Section('leak');
    M.Free;
    Before := GuiObjects;
    for I := 1 to 100 do
    begin
      M := TMemo.Create(nil);
      M.Lines.Add('Leak');
      M.Parent := F;
      M.HandleNeeded;
      M.Free;
    end;
    Check(GuiObjects - Before <= 2, 'GUI objects after 100 instances with handles');
  finally
    F.Free;
    Log.Free;
  end;
end;

begin
  Run;
  Done;
end.
