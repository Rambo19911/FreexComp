// covers: UIF-1300, UIF-1302, UIF-1303, UIF-1304, UIF-1305, UIF-1306, UIF-1307, UIF-1308, UIF-1309, UIF-1310, UIF-1311
// needs: desktop
// phase: 8
program Ctl_RichEdit;
{ U-144, rich-edit.md sections 2 and 7: TRichEdit defaults; the engine library loaded from the system directory and
  the sub-classed window class RICHEDIT50W with the listed style bits (UIF-1300, UIF-1303); each "-> recreate"
  property re-creating the window and keeping the text and a bold run (UIF-1304); HideSelection and ReadOnly without
  re-creation; the state pushed after creation (limit, event mask, colour, content, selection, Modified); engine
  positions for the selection (UIF-1305); Lines without and with a handle, edits in place that keep the formatting of
  the other paragraphs (UIF-1306); Text and its default format (UIF-1307); the inherited keyboard rules and no
  intrinsic height (UIF-1308); streaming, Lines always plain text (UIF-1309); MaxLength (UIF-1310); Color
  (UIF-1311); OnChange; leak check. No Geometry section: the control has no intrinsic size. }
uses
  Windows, Messages, Types, SysUtils, Classes, Graphics, Controls, Forms, StdCtrls, ComCtrls, RichEdit, TestKit;

type
  TLog = class
  public
    Text: string;
    procedure Changed(Sender: TObject);
  end;

  TRichForm = class(TForm)
  published
    Notes: TRichEdit;
    procedure NotesChange(Sender: TObject);
  end;

{$FORM Ctl_RichEdit.form}

const
  CRLF = #13#10;
  Tall = '1'#13#10'2'#13#10'3'#13#10'4'#13#10'5'#13#10'6'#13#10'7'#13#10'8'#13#10'9'#13#10'10'#13#10'11'#13#10'12';
  Wide = 'a line that is much wider than the control when the words are not wrapped at its right edge';

var
  Log: TLog;

procedure TLog.Changed(Sender: TObject);
begin
  Text := Text + 'change;';
end;

procedure TRichForm.NotesChange(Sender: TObject);
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

procedure NativeSelect(H: HWND; First, Last: Integer);
var
  R: TCharRange;
begin
  R.cpMin := First;
  R.cpMax := Last;
  SendMessageW(H, EM_EXSETSEL, 0, LPARAM(@R));
end;

function NativeSelection(H: HWND): string;
var
  R: TCharRange;
begin
  R.cpMin := -1;
  R.cpMax := -1;
  SendMessageW(H, EM_EXGETSEL, 0, LPARAM(@R));
  Result := Format('%d..%d', [R.cpMin, R.cpMax]);
end;

{ makes First .. Last bold with the engine's own message (the formatting classes are tested in Ctl_RichEditFormat) }
procedure MakeBold(R: TRichEdit; First, Last: Integer);
var
  CF: TCharFormat2W;
begin
  NativeSelect(R.Handle, First, Last);
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  CF.dwMask := CFM_BOLD;
  CF.dwEffects := CFE_BOLD;
  SendMessageW(R.Handle, EM_SETCHARFORMAT, SCF_SELECTION, LPARAM(@CF));
end;

{ 'B' when all of First .. Last is bold, 'n' when none of it is, '?' when mixed }
function Bold(R: TRichEdit; First, Last: Integer): string;
var
  CF: TCharFormat2W;
begin
  NativeSelect(R.Handle, First, Last);
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  SendMessageW(R.Handle, EM_GETCHARFORMAT, SCF_SELECTION, LPARAM(@CF));
  if (CF.dwMask and CFM_BOLD) = 0 then
    Result := '?'
  else if (CF.dwEffects and CFE_BOLD) <> 0 then
    Result := 'B'
  else
    Result := 'n';
end;

{ the background colour of the engine: EM_SETBKGNDCOLOR returns the previous one }
function NativeColor(H: HWND): Integer;
begin
  Result := Integer(SendMessageW(H, EM_SETBKGNDCOLOR, 0, 0));
  SendMessageW(H, EM_SETBKGNDCOLOR, 0, LPARAM(Result));
end;

{ True when the engine library of the process was loaded from the system directory }
function EngineInSystemDirectory: Boolean;
var
  Sys, Path: array[0..519] of WideChar;
  M: HMODULE;
  S, P: string;
begin
  Result := False;
  M := GetModuleHandleW(MSFTEDIT_DLL);
  if M = 0 then
    Exit;
  SetString(S, PChar(@Sys[0]), Integer(GetSystemDirectoryW(@Sys[0], 520)));
  SetString(P, PChar(@Path[0]), Integer(GetModuleFileNameW(M, @Path[0], 520)));
  Result := SameText(P, S + '\' + MSFTEDIT_DLL);
end;

{ the same Lines checks without and with a handle }
procedure CheckLines(M: TRichEdit; const Kind: string);
begin
  M.Lines.Clear;
  CheckEqI(0, M.Lines.Count, Kind + ': empty control has no lines');
  CheckEqS('', M.Lines.Text, Kind + ': empty Lines.Text');
  M.Lines.Add('a');
  M.Lines.Add('b');
  M.Lines.Add('c');
  CheckEqI(3, M.Lines.Count, Kind + ': Add');
  CheckEqS('a|b|c', Shown(M.Text), Kind + ': Text is the plain text (no trailing break)');
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
  M: TRichEdit;
  SF: TRichForm;
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
    M := TRichEdit.Create(F);

    Section('defaults');
    CheckEqI(185, M.Width, 'Width 185');
    CheckEqI(89, M.Height, 'Height 89');
    CheckEqB(True, M.Alignment = taLeftJustify, 'Alignment');
    CheckEqB(False, M.AutoURLDetect, 'AutoURLDetect');
    CheckEqB(True, M.BorderStyle = bsSingle, 'BorderStyle');
    CheckEqB(True, M.Color = clWindow, 'Color clWindow');
    CheckEqB(True, M.HideSelection, 'HideSelection');
    CheckEqI(0, M.Lines.Count, 'Lines empty');
    CheckEqI(0, M.MaxLength, 'MaxLength');
    CheckEqB(False, M.ParentColor, 'ParentColor False');
    CheckEqB(True, M.ParentFont, 'ParentFont');
    CheckEqB(False, M.PlainText, 'PlainText');
    CheckEqB(False, M.ReadOnly, 'ReadOnly');
    CheckEqB(True, M.ScrollBars = ssNone, 'ScrollBars ssNone');
    CheckEqI(-1, M.TabOrder, 'TabOrder');
    CheckEqB(True, M.TabStop, 'TabStop True');
    CheckEqB(True, M.WantReturns, 'WantReturns');
    CheckEqB(False, M.WantTabs, 'WantTabs');
    CheckEqB(True, M.WordWrap, 'WordWrap');
    CheckEqI(100, M.Zoom, 'Zoom 100');
    Check(GetClass('TRichEdit') <> nil, 'registered');
    Check(M is TCustomMemo, 'a TCustomMemo (UIF-1302)');
    Check(not (M.Lines is TMemoStrings), 'Lines is the control''s own TStrings (UIF-1302)');
    M.Height := 40;
    CheckEqI(40, M.Height, 'no intrinsic height (UIF-1308)');
    M.Height := 89;

    Section('Lines without a handle (UIF-1306)');
    CheckLines(M, 'no handle');
    M.Lines.Clear;
    M.Lines.Add('');
    M.Lines.Add('x');
    CheckEqS('|x', Shown(M.Text), 'a leading empty line is kept');
    M.Lines.Clear;
    Check(not M.HandleAllocated, 'still no handle');

    Section('window (UIF-1300, UIF-1303)');
    M.Lines.Add('kept bold');
    M.Parent := F;
    H := M.Handle;
    CheckEqS('RICHEDIT50W', ClassOf(H), 'window class RICHEDIT50W');
    Check(EngineInSystemDirectory, 'the engine library comes from the system directory');
    CheckEqB(True, HasStyle(M, ES_MULTILINE or ES_AUTOVSCROLL), 'ES_MULTILINE, ES_AUTOVSCROLL');
    CheckEqB(False, HasStyle(M, ES_AUTOHSCROLL), 'WordWrap: no ES_AUTOHSCROLL');
    CheckEqB(True, HasStyle(M, ES_WANTRETURN), 'ES_WANTRETURN');
    CheckEqB(False, HasStyle(M, ES_NOHIDESEL), 'HideSelection: no ES_NOHIDESEL');
    CheckEqB(False, HasStyle(M, ES_READONLY), 'no ES_READONLY');
    CheckEqB(True, (GetWindowLongPtrW(H, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) <> 0, 'WS_EX_CLIENTEDGE');
    CheckEqS('kept bold', NativeText(H), 'lines pushed as the content');
    CheckEqI(ENM_CHANGE or ENM_SELCHANGE or ENM_LINK or ENM_PROTECTED, SendMessageW(H, EM_GETEVENTMASK, 0, 0),
      'event mask');
    CheckEqI($7FFFFFF0, SendMessageW(H, EM_GETLIMITTEXT, 0, 0), 'MaxLength 0: no 64 K limit (UIF-1310)');
    CheckEqI(ColorToRGB(clWindow), NativeColor(H), 'background colour pushed (UIF-1311)');

    Section('recreate keeps text and formatting (UIF-1304)');
    MakeBold(M, 5, 9);
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'a bold run');
    M.WordWrap := False;
    Check(M.Handle <> H, 'WordWrap recreates');
    CheckEqB(True, HasStyle(M, ES_AUTOHSCROLL), 'no WordWrap: ES_AUTOHSCROLL');
    CheckEqS('kept bold', NativeText(M.Handle), 'WordWrap: text kept');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'WordWrap: bold run kept');
    M.WordWrap := True;
    H := M.Handle;
    M.ScrollBars := ssBoth;
    Check(M.Handle <> H, 'ScrollBars recreates');
    CheckEqS('kept bold', NativeText(M.Handle), 'ScrollBars: text kept');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'ScrollBars: bold run kept');
    { the engine shows a scroll bar - and sets its style bit - only while the content needs it }
    CheckEqB(False, HasStyle(M, WS_VSCROLL), 'ssBoth, short text: no scroll bar yet');
    M.Text := Tall;
    CheckEqB(True, HasStyle(M, WS_VSCROLL), 'ssBoth, tall text: WS_VSCROLL');
    M.ScrollBars := ssVertical;
    CheckEqB(True, HasStyle(M, WS_VSCROLL) and not HasStyle(M, WS_HSCROLL), 'ssVertical, tall text: WS_VSCROLL only');
    M.ScrollBars := ssNone;
    CheckEqB(False, HasStyle(M, WS_VSCROLL), 'ssNone, tall text: no scroll bar');
    M.WordWrap := False;
    M.Text := Wide;
    CheckEqB(False, HasStyle(M, WS_HSCROLL), 'ssNone, wide text: no scroll bar');
    M.ScrollBars := ssHorizontal;
    CheckEqB(True, HasStyle(M, WS_HSCROLL) and not HasStyle(M, WS_VSCROLL), 'ssHorizontal, wide text: WS_HSCROLL only');
    M.ScrollBars := ssNone;
    M.WordWrap := True;
    M.Text := 'kept bold';
    MakeBold(M, 5, 9);
    H := M.Handle;
    M.WantReturns := False;
    Check(M.Handle <> H, 'WantReturns recreates');
    CheckEqB(False, HasStyle(M, ES_WANTRETURN), 'WantReturns False: no ES_WANTRETURN');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'WantReturns: bold run kept');
    M.WantReturns := True;
    H := M.Handle;
    M.Alignment := taCenter;
    Check(M.Handle <> H, 'Alignment recreates');
    CheckEqB(True, HasStyle(M, ES_CENTER), 'ES_CENTER');
    CheckEqS('kept bold', NativeText(M.Handle), 'Alignment: text kept');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'Alignment: bold run kept');
    M.Alignment := taLeftJustify;
    H := M.Handle;
    M.BorderStyle := bsNone;
    Check(M.Handle <> H, 'BorderStyle recreates');
    CheckEqB(True, (GetWindowLongPtrW(M.Handle, GWL_EXSTYLE) and WS_EX_CLIENTEDGE) = 0, 'bsNone');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'BorderStyle: bold run kept');
    M.BorderStyle := bsSingle;
    M.SelStart := 2;
    M.SelLength := 3;
    M.Modified := True;
    M.DestroyHandle;
    CheckEqS('kept bold', M.Text, 'without the window: the plain text');
    CheckEqS('2+3', Format('%d+%d', [M.SelStart, M.SelLength]), 'without the window: the selection');
    CheckEqB(True, M.Modified, 'without the window: Modified');
    M.HandleNeeded;
    CheckEqS('2..5', NativeSelection(M.Handle), 'selection restored');
    CheckEqB(True, SendMessageW(M.Handle, EM_GETMODIFY, 0, 0) <> 0, 'Modified restored');
    CheckEqS('nB', Bold(M, 0, 5) + Bold(M, 5, 9), 'formatting restored with the window');
    M.Modified := False;
    M.DestroyHandle;
    M.Text := 'assigned';
    M.HandleNeeded;
    CheckEqS('assigned', NativeText(M.Handle), 'text assigned without a window replaces the saved content');
    CheckEqS('n', Bold(M, 0, 8), 'and is plain');

    Section('no recreate: HideSelection, ReadOnly, MaxLength, Color');
    H := M.Handle;
    M.HideSelection := False;
    M.ReadOnly := True;
    CheckEqB(True, HasStyle(M, ES_READONLY), 'ReadOnly: ES_READONLY');
    M.ReadOnly := False;
    CheckEqB(False, HasStyle(M, ES_READONLY), 'ReadOnly off');
    M.MaxLength := 10;
    CheckEqI(10, SendMessageW(M.Handle, EM_GETLIMITTEXT, 0, 0), 'MaxLength 10');
    M.Text := 'longer than ten characters';
    CheckEqS('longer than ten characters', NativeText(M.Handle), 'text set by code is not truncated');
    M.MaxLength := 0;
    CheckEqI($7FFFFFF0, SendMessageW(M.Handle, EM_GETLIMITTEXT, 0, 0), 'MaxLength 0 again');
    M.Color := clYellow;
    CheckEqI(ColorToRGB(clYellow), NativeColor(M.Handle), 'Color change reaches the engine');
    M.Color := clWindow;
    Check(M.Handle = H, 'none of them recreates');
    M.BorderStyle := bsNone;
    CheckEqB(True, HasStyle(M, ES_NOHIDESEL), 'HideSelection False: ES_NOHIDESEL after a re-creation');
    M.BorderStyle := bsSingle;
    M.HideSelection := True;

    Section('positions (UIF-1305)');
    M.Text := 'ab' + CRLF + 'cde';
    M.SelStart := 3;
    M.SelLength := 2;
    CheckEqS('3..5', NativeSelection(M.Handle), 'a paragraph break is one position');
    CheckEqS('cd', M.SelText, 'SelText');
    CheckEqS('3+2', Format('%d+%d', [M.SelStart, M.SelLength]), 'SelStart, SelLength');
    P := M.CaretPos;
    CheckEqS('0,1', Format('%d,%d', [P.X, P.Y]), 'CaretPos (column, line)');
    M.CaretPos := Point(2, 1);
    CheckEqI(5, M.SelStart, 'CaretPos := (2, 1)');
    M.SelStart := 1;
    M.SelLength := 3;
    CheckEqS('b|c', Shown(M.SelText), 'SelText across a paragraph break');
    CheckEqB(False, M.CanUndo, 'nothing to undo after a Text assignment');
    M.SelText := 'X';
    CheckEqS('aXde', M.Text, 'SelText := replaces the selection');
    CheckEqB(True, M.CanUndo, 'the replacement can be undone');
    M.Undo;
    CheckEqS('ab|cde', Shown(M.Text), 'Undo');
    M.SelectAll;
    CheckEqS('ab|cde', Shown(M.SelText), 'SelectAll');

    Section('Lines with a handle (UIF-1306)');
    CheckLines(M, 'handle');
    CheckEqS('u|v', Shown(NativeText(M.Handle)), 'the window holds the text');
    M.Lines.Clear;
    M.Width := 60;
    M.Lines.Add('aaaa bbbb cccc dddd eeee ffff gggg');
    Check(SendMessageW(M.Handle, EM_GETLINECOUNT, 0, 0) > 1, 'word wrap: several display lines');
    CheckEqI(1, M.Lines.Count, 'word wrap: one logical line');
    M.Width := 185;
    M.Lines.Text := 'one' + CRLF + 'two' + CRLF + 'three';
    MakeBold(M, 4, 7);
    M.Lines.Insert(0, 'zero');
    M.Lines.Add('four');
    M.Lines[1] := 'ONE';
    M.Lines.Delete(3);
    CheckEqS('zero|ONE|two|four', Shown(M.Text), 'edits in place');
    CheckEqS('nBn', Bold(M, 5, 8) + Bold(M, 9, 12) + Bold(M, 13, 17), 'the untouched paragraph keeps its formatting');
    M.DestroyHandle;
    CheckEqI(4, M.Lines.Count, 'lines survive the window');
    CheckEqS('two', M.Lines[2], 'third line after the window went');
    M.HandleNeeded;
    CheckEqS('zero|ONE|two|four', Shown(NativeText(M.Handle)), 'lines pushed again');
    CheckEqS('B', Bold(M, 9, 12), 'with their formatting');

    Section('Text (UIF-1307)');
    M.Text := 'plain again';
    CheckEqS('n', Bold(M, 0, 11), 'Text := gives the default format');
    MakeBold(M, 0, 5);
    M.Lines.Text := 'lines' + CRLF + 'text';
    CheckEqS('n', Bold(M, 0, 5), 'Lines.Text := gives the default format');
    CheckEqS('lines|text', Shown(M.Text), 'Text has no trailing break');
    M.Text := '{\rtf1 not parsed}';
    CheckEqS('{\rtf1 not parsed}', NativeText(M.Handle), 'Text is never read as RTF');
    M.Lines.Text := '{\rtf1 not parsed either}';
    CheckEqS('{\rtf1 not parsed either}', NativeText(M.Handle), 'Lines.Text is never read as RTF');

    Section('native -> object');
    M.Text := 'ab' + CRLF + 'cd';
    NativeSelect(M.Handle, 1, 4);
    CheckEqS('1+3', Format('%d+%d', [M.SelStart, M.SelLength]), 'EM_EXSETSEL -> SelStart, SelLength');
    SendMessageW(M.Handle, EM_REPLACESEL, 1, LPARAM(PChar('--')));
    CheckEqS('a--d', M.Text, 'EM_REPLACESEL -> Text');
    CheckEqI(1, M.Lines.Count, 'EM_REPLACESEL -> Lines');
    CheckEqB(True, M.Modified, 'the engine sets Modified');
    SendMessageW(M.Handle, EM_SETMODIFY, 0, 0);
    CheckEqB(False, M.Modified, 'EM_SETMODIFY -> Modified');
    SendMessageW(M.Handle, EM_SETREADONLY, 1, 0);
    CheckEqB(True, HasStyle(M, ES_READONLY), 'EM_SETREADONLY sets the style bit');
    SendMessageW(M.Handle, EM_SETREADONLY, 0, 0);

    Section('events');
    M.OnChange := Log.Changed;
    Log.Text := '';
    M.Lines.Add('more');
    CheckEqS('change;', Log.Text, 'Lines.Add fires OnChange once');
    Log.Text := '';
    M.Text := M.Text;
    CheckEqS('', Log.Text, 'same text: no OnChange');
    Log.Text := '';
    M.Text := 'set' + CRLF + 'by code';
    CheckEqS('change;', Log.Text, 'Text := with a window fires OnChange once');
    Log.Text := '';
    M.Lines.Text := 'by' + CRLF + 'Lines.Text';
    CheckEqS('change;', Log.Text, 'Lines.Text := with a window fires OnChange once');
    Log.Text := '';
    M.SelStart := 0;
    M.SelText := 'typed ';
    CheckEqS('change;', Log.Text, 'SelText := fires OnChange once');
    Log.Text := '';
    M.WordWrap := False;
    M.WordWrap := True;
    CheckEqS('', Log.Text, 'no OnChange during the CreateWnd push (UIF-1303)');
    M.DestroyHandle;
    Log.Text := '';
    M.Text := 'without a window';
    CheckEqS('change;', Log.Text, 'Text := without a window fires OnChange once');
    M.HandleNeeded;
    M.OnChange := nil;

    Section('special keys (UIF-1308)');
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

    Section('handle-less state');
    M.Free;
    M := TRichEdit.Create(F);
    M.Lines.Add('first');
    M.Lines.Add('second');
    M.SelStart := 6;
    M.SelLength := 3;
    M.Modified := True;
    M.MaxLength := 50;
    M.Color := clAqua;
    M.ReadOnly := True;
    M.HideSelection := False;
    M.AutoURLDetect := True;
    M.Zoom := 200;
    M.Font.Style := [fsBold];
    M.Parent := F;
    H := M.Handle;
    CheckEqS('first|second', Shown(NativeText(H)), 'content');
    CheckEqS('6..9', NativeSelection(H), 'selection');
    CheckEqB(True, SendMessageW(H, EM_GETMODIFY, 0, 0) <> 0, 'Modified');
    CheckEqI(50, SendMessageW(H, EM_GETLIMITTEXT, 0, 0), 'limit');
    CheckEqI(ColorToRGB(clAqua), NativeColor(H), 'colour');
    CheckEqB(True, HasStyle(M, ES_READONLY), 'ES_READONLY');
    CheckEqB(True, HasStyle(M, ES_NOHIDESEL), 'ES_NOHIDESEL');
    CheckEqB(True, SendMessageW(H, EM_GETAUTOURLDETECT, 0, 0) <> 0, 'URL detection');
    CheckEqI(200, M.Zoom, 'zoom');
    CheckEqS('B', Bold(M, 0, 12), 'the content has the font of the control');

    Section('streaming (UIF-1309)');
    Log.Text := '';
    SF := TRichForm.Create(nil);
    try
      CheckEqS('', Log.Text, 'no OnChange while loading');
      CheckEqI(3, SF.Notes.Lines.Count, 'Lines');
      CheckEqS('{\rtf1 plain}||third', Shown(SF.Notes.Text), 'Lines are plain text, also when they look like RTF');
      CheckEqB(True, SF.Notes.Alignment = taRightJustify, 'Alignment');
      CheckEqB(True, SF.Notes.AutoURLDetect, 'AutoURLDetect');
      CheckEqB(True, SF.Notes.BorderStyle = bsNone, 'BorderStyle');
      CheckEqB(True, SF.Notes.Color = clYellow, 'Color');
      CheckEqI(-15, SF.Notes.Font.Height, 'Font.Height');
      CheckEqB(False, SF.Notes.HideSelection, 'HideSelection');
      CheckEqI(100, SF.Notes.MaxLength, 'MaxLength');
      CheckEqB(True, SF.Notes.PlainText, 'PlainText');
      CheckEqB(True, SF.Notes.ReadOnly, 'ReadOnly');
      CheckEqB(True, SF.Notes.ScrollBars = ssVertical, 'ScrollBars');
      CheckEqB(False, SF.Notes.TabStop, 'TabStop False');
      CheckEqB(False, SF.Notes.WantReturns, 'WantReturns');
      CheckEqB(True, SF.Notes.WantTabs, 'WantTabs');
      CheckEqB(False, SF.Notes.WordWrap, 'WordWrap');
      CheckEqI(150, SF.Notes.Zoom, 'Zoom');
      CheckEqS('10,10,200,120', Format('%d,%d,%d,%d', [SF.Notes.Left, SF.Notes.Top, SF.Notes.Width,
        SF.Notes.Height]), 'bounds (no intrinsic height)');
      Check(Assigned(SF.Notes.OnChange), 'OnChange bound');
      SF.HandleNeeded;
      CheckEqS('', Log.Text, 'no OnChange when the window is created');
      CheckEqS('{\rtf1 plain}||third', Shown(NativeText(SF.Notes.Handle)), 'the window holds the plain lines');
      SF.Notes.ReadOnly := False;
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
      M := TRichEdit.Create(nil);
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
