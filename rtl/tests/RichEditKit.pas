unit RichEditKit;
{ Shared part of winapi/Win_RichEditSmoke and winapi/Win_RichEditFormat (winapi.md section 4.1, RTL-1026): one
  control of the rich edit engine under a hidden top-level window, the window procedure of the parent that records
  the notifications, the stream callbacks and one helper per message group. Nothing is shown and no input is sent.
  Text positions of Sample: Hello 0..5, rich 6..10, world 11..16. }

interface

uses Windows, Messages, RichEdit;

const
  Sample = 'Hello rich world';

var
  Edit: HWND;
  SelCount, ProtCount, ChangeCount: Integer;
  RefuseProtected: Boolean;
  LastSel: TSelChange;
  LastProt: TENProtected;
  Stream: array[0..65535] of Byte;
  StreamLen, StreamPos: Integer;
  LastCookie: NativeUInt;
  Chars: array[0..255] of WideChar;

{ Loads the engine library from the system directory and creates the hidden parent and the control. }
procedure OpenEditor;
procedure CloseEditor;
function Send(Msg: UINT; W: WPARAM; L: LPARAM): LRESULT;
function BufStr(P: PWideChar): string;
{ The stream bytes as text, one character per byte (the streams used here are ASCII). }
function StreamStr(Count: Integer): string;
function SetText(const S: string; Flags: DWORD): LRESULT;
function GetText(Flags: DWORD): string;
function TextLength(Flags: DWORD): LRESULT;
function ReplaceSelection(const S: string): LRESULT;
function SelectRange(First, Last: Longint): LRESULT;
{ The character format of the selection (Scope = SCF_SELECTION) or the default one (SCF_DEFAULT). }
procedure GetFormat(var CF: TCharFormat2W; Scope: WPARAM);
function SetEffects(Mask, Effects: DWORD; Scope: WPARAM): LRESULT;
procedure GetParagraph(var PF: TParaFormat2);
function Find(const What: string; Flags: WPARAM; First, Last: Longint; var Found: TCharRange): LRESULT;
function StreamOut(Format: WPARAM; var ES: TEditStream): LRESULT;
function StreamIn(Format: WPARAM; var ES: TEditStream): LRESULT;

implementation

uses TestKit;

{$POINTERMATH ON}

const
  ParentClass = 'FreexCompRichEditSmoke';
  CodePageUtf16 = 1200;

var
  Lib: HMODULE;
  Parent: HWND;

function WndProc(Wnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
var
  N: PNMHdr;
begin
  Result := 0;
  if Msg = WM_NOTIFY then
  begin
    N := PNMHdr(lParam);
    if N^.code = EN_SELCHANGE then
    begin
      SelCount := SelCount + 1;
      LastSel := PSelChange(lParam)^;
    end
    else if N^.code = EN_PROTECTED then
    begin
      ProtCount := ProtCount + 1;
      LastProt := PENProtected(lParam)^;
      if RefuseProtected then
        Result := 1; { non-zero: the change is refused }
    end;
  end
  else if Msg = WM_COMMAND then
  begin
    if HiWord(wParam) = EN_CHANGE then
      ChangeCount := ChangeCount + 1;
  end
  else
    Result := DefWindowProcW(Wnd, Msg, wParam, lParam);
end;

function WriteCallback(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;
begin
  LastCookie := dwCookie;
  if StreamLen + cb > SizeOf(Stream) then
    cb := SizeOf(Stream) - StreamLen;
  Move(pbBuff^, Stream[StreamLen], cb);
  StreamLen := StreamLen + cb;
  pcb := cb;
  Result := 0;
end;

function ReadCallback(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;
begin
  LastCookie := dwCookie;
  if cb > StreamLen - StreamPos then
    cb := StreamLen - StreamPos;
  Move(Stream[StreamPos], pbBuff^, cb);
  StreamPos := StreamPos + cb;
  pcb := cb;
  Result := 0;
end;

function Send(Msg: UINT; W: WPARAM; L: LPARAM): LRESULT;
begin
  Result := SendMessageW(Edit, Msg, W, L);
end;

function BufStr(P: PWideChar): string;
begin
  Result := '';
  while P^ <> #0 do
  begin
    Result := Result + P^;
    Inc(P);
  end;
end;

function StreamStr(Count: Integer): string;
var
  I: Integer;
begin
  Result := '';
  if Count > StreamLen then
    Count := StreamLen;
  for I := 0 to Count - 1 do
    Result := Result + WideChar(Stream[I]);
end;

function SetText(const S: string; Flags: DWORD): LRESULT;
var
  ST: TSetTextEx;
begin
  ST.flags := Flags;
  ST.codepage := CodePageUtf16;
  Result := Send(EM_SETTEXTEX, WPARAM(@ST), LPARAM(PWideChar(S)));
end;

function GetText(Flags: DWORD): string;
var
  GT: TGetTextEx;
begin
  FillChar(GT, SizeOf(GT), 0);
  FillChar(Chars, SizeOf(Chars), 0);
  GT.cb := SizeOf(Chars);
  GT.flags := Flags;
  GT.codepage := CodePageUtf16;
  Send(EM_GETTEXTEX, WPARAM(@GT), LPARAM(@Chars[0]));
  Result := BufStr(@Chars[0]);
end;

function TextLength(Flags: DWORD): LRESULT;
var
  GL: TGetTextLengthEx;
begin
  GL.flags := Flags;
  GL.codepage := CodePageUtf16;
  Result := Send(EM_GETTEXTLENGTHEX, WPARAM(@GL), 0);
end;

function ReplaceSelection(const S: string): LRESULT;
begin
  Result := Send(EM_REPLACESEL, 1, LPARAM(PWideChar(S)));
end;

function SelectRange(First, Last: Longint): LRESULT;
var
  CR: TCharRange;
begin
  CR.cpMin := First;
  CR.cpMax := Last;
  Result := Send(EM_EXSETSEL, 0, LPARAM(@CR));
end;

procedure GetFormat(var CF: TCharFormat2W; Scope: WPARAM);
begin
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  Send(EM_GETCHARFORMAT, Scope, LPARAM(@CF));
end;

function SetEffects(Mask, Effects: DWORD; Scope: WPARAM): LRESULT;
var
  CF: TCharFormat2W;
begin
  FillChar(CF, SizeOf(CF), 0);
  CF.cbSize := SizeOf(CF);
  CF.dwMask := Mask;
  CF.dwEffects := Effects;
  Result := Send(EM_SETCHARFORMAT, Scope, LPARAM(@CF));
end;

function Find(const What: string; Flags: WPARAM; First, Last: Longint; var Found: TCharRange): LRESULT;
var
  FT: TFindTextExW;
begin
  FillChar(FT, SizeOf(FT), 0);
  FT.chrg.cpMin := First;
  FT.chrg.cpMax := Last;
  FT.lpstrText := PWideChar(What);
  Result := Send(EM_FINDTEXTEXW, Flags, LPARAM(@FT));
  Found := FT.chrgText;
end;

function StreamOut(Format: WPARAM; var ES: TEditStream): LRESULT;
begin
  StreamLen := 0;
  FillChar(ES, SizeOf(ES), 0);
  ES.dwCookie := 1234;
  ES.pfnCallback := @WriteCallback;
  Result := Send(EM_STREAMOUT, Format, LPARAM(@ES));
end;

function StreamIn(Format: WPARAM; var ES: TEditStream): LRESULT;
begin
  StreamPos := 0;
  FillChar(ES, SizeOf(ES), 0);
  ES.dwCookie := 4321;
  ES.pfnCallback := @ReadCallback;
  Result := Send(EM_STREAMIN, Format, LPARAM(@ES));
end;

procedure GetParagraph(var PF: TParaFormat2);
begin
  FillChar(PF, SizeOf(PF), 0);
  PF.cbSize := SizeOf(PF);
  Send(EM_GETPARAFORMAT, 0, LPARAM(@PF));
end;

procedure OpenEditor;
var
  WC: TWndClassExW;
begin
  Lib := LoadLibraryExW(MSFTEDIT_DLL, 0, LOAD_LIBRARY_SEARCH_SYSTEM32);
  Check(Lib <> 0, 'LoadLibraryExW(MSFTEDIT_DLL)');
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @WndProc;
  WC.hInstance := HINST(HInstance);
  WC.lpszClassName := ParentClass;
  Check(RegisterClassExW(WC) <> 0, 'RegisterClassExW');
  Parent := CreateWindowExW(0, ParentClass, 'RichEdit', WS_OVERLAPPEDWINDOW, 0, 0, 400, 300, 0, 0, HINST(HInstance), nil);
  Check(Parent <> 0, 'parent');
  Edit := CreateWindowExW(0, MSFTEDIT_CLASS, nil, WS_CHILD or ES_MULTILINE, 0, 0, 300, 200, Parent, HMENU(1),
    HINST(HInstance), nil);
  Check(Edit <> 0, 'MSFTEDIT_CLASS');
  Check(not IsWindowVisible(Parent), 'parent stays hidden');
end;

procedure CloseEditor;
begin
  Check(DestroyWindow(Parent), 'DestroyWindow parent');
  Check(UnregisterClassW(ParentClass, HINST(HInstance)), 'UnregisterClassW');
  Check(FreeLibrary(Lib), 'FreeLibrary');
end;

end.
