// phase: 4
// covers: RTL-820, RTL-821, RTL-822, RTL-830, RTL-832, RTL-860, RTL-862
program Win_CommDlgDecl;
{ CommDlg and ShellAPI units (winapi.md sections 1, 5.6, 5.7). The common dialogs and ShellExecuteW cannot run
  unattended (they show a modal dialog or start another program; README.md), so their declarations are checked by
  assigning each one to a procedural variable of the documented signature, which only compiles when parameter and
  result types match. CommDlgExtendedError is called. The drag-and-drop functions are called on a hidden window and
  on an HDROP block built here exactly as the shell builds it for WM_DROPFILES. }
uses Windows, CommDlg, ShellAPI, Types, TestKit;

{$POINTERMATH ON}

type
  TFileDialogFn = function(var lpofn: TOpenFilenameW): BOOL; stdcall;
  TColorDialogFn = function(var lpcc: TChooseColorW): BOOL; stdcall;
  TFontDialogFn = function(var lpcf: TChooseFontW): BOOL; stdcall;
  TShellExecuteFn = function(hwnd: HWND; lpOperation, lpFile, lpParameters, lpDirectory: LPCWSTR;
    nShowCmd: Integer): HINST; stdcall;

const
  WndClass = 'FreexCompCommDlgDecl';

function BufStr(P: PWideChar): UnicodeString;
begin
  Result := '';
  while P^ <> #0 do
  begin
    Result := Result + P^;
    Inc(P);
  end;
end;

{ An HDROP block: TDropFiles header, then 'C:\a.txt', 'C:\dir\b.dat', empty name (wide). }
function NewDrop: HDROP;
const
  Names = 'C:\a.txt'#0'C:\dir\b.dat'#0#0;
var
  G: HGLOBAL;
  DF: PDropFiles;
  Dest: PWideChar;
  I: Integer;
  S: UnicodeString;
begin
  S := Names;
  G := GlobalAlloc(GHND, SizeOf(TDropFiles) + Length(S) * SizeOf(WideChar));
  DF := PDropFiles(GlobalLock(G));
  DF^.pFiles := SizeOf(TDropFiles);
  DF^.pt := Point(3, 4);
  DF^.fNC := False;
  DF^.fWide := True;
  Dest := PWideChar(PByte(DF) + SizeOf(TDropFiles));
  for I := 1 to Length(S) do
    Dest[I - 1] := S[I];
  GlobalUnlock(G);
  Result := HDROP(G);
end;

var
  OpenFn, SaveFn: TFileDialogFn;
  ColorFn: TColorDialogFn;
  FontFn: TFontDialogFn;
  ExecFn: TShellExecuteFn;
  WC: TWndClassExW;
  Wnd: HWND;
  Drop: HDROP;
  Buffer: array[0..63] of WideChar;

begin
  Section('comdlg32 declarations');
  OpenFn := GetOpenFileNameW;
  Check(Assigned(OpenFn), 'GetOpenFileNameW');
  OpenFn := GetOpenFileName;
  Check(Assigned(OpenFn), 'GetOpenFileName alias');
  SaveFn := GetSaveFileNameW;
  Check(Assigned(SaveFn), 'GetSaveFileNameW');
  SaveFn := GetSaveFileName;
  Check(Assigned(SaveFn), 'GetSaveFileName alias');
  ColorFn := ChooseColorW;
  Check(Assigned(ColorFn), 'ChooseColorW');
  ColorFn := ChooseColor;
  Check(Assigned(ColorFn), 'ChooseColor alias');
  FontFn := ChooseFontW;
  Check(Assigned(FontFn), 'ChooseFontW');
  FontFn := ChooseFont;
  Check(Assigned(FontFn), 'ChooseFont alias');
  CheckEqI(0, CommDlgExtendedError, 'CommDlgExtendedError before any dialog');
  CheckEqI(152, SizeOf(TOpenFilenameW), 'SizeOf(TOpenFilenameW)');
  CheckEqI(72, SizeOf(TChooseColorW), 'SizeOf(TChooseColorW)');
  CheckEqI(104, SizeOf(TChooseFontW), 'SizeOf(TChooseFontW)');

  Section('shell32 declarations');
  ExecFn := ShellExecuteW;
  Check(Assigned(ExecFn), 'ShellExecuteW');
  ExecFn := ShellExecute;
  Check(Assigned(ExecFn), 'ShellExecute alias');
  CheckEqI(20, SizeOf(TDropFiles), 'SizeOf(TDropFiles)');

  Section('drag and drop');
  FillChar(WC, SizeOf(WC), 0);
  WC.cbSize := SizeOf(WC);
  WC.lpfnWndProc := @DefWindowProcW;
  WC.hInstance := HINST(HInstance);
  WC.lpszClassName := WndClass;
  Check(RegisterClassExW(WC) <> 0, 'RegisterClassExW');
  Wnd := CreateWindowExW(0, WndClass, 'Drop', WS_OVERLAPPEDWINDOW, 0, 0, 100, 100, 0, 0, HINST(HInstance), nil);
  Check(Wnd <> 0, 'hidden window');
  DragAcceptFiles(Wnd, True);
  CheckEqI(WS_EX_ACCEPTFILES, GetWindowLongPtrW(Wnd, GWL_EXSTYLE) and WS_EX_ACCEPTFILES, 'DragAcceptFiles True');
  DragAcceptFiles(Wnd, False);
  CheckEqI(0, GetWindowLongPtrW(Wnd, GWL_EXSTYLE) and WS_EX_ACCEPTFILES, 'DragAcceptFiles False');
  Drop := NewDrop;
  Check(Drop <> 0, 'HDROP block');
  CheckEqI(2, DragQueryFileW(Drop, $FFFFFFFF, nil, 0), 'DragQueryFileW count');
  CheckEqI(8, DragQueryFileW(Drop, 0, nil, 0), 'DragQueryFileW length');
  CheckEqI(8, DragQueryFileW(Drop, 0, @Buffer[0], 64), 'DragQueryFileW copied');
  CheckEqS('C:\a.txt', BufStr(@Buffer[0]), 'DragQueryFileW first');
  CheckEqI(12, DragQueryFile(Drop, 1, @Buffer[0], 64), 'DragQueryFile alias');
  CheckEqS('C:\dir\b.dat', BufStr(@Buffer[0]), 'DragQueryFile second');
  DragFinish(Drop);
  Check(DestroyWindow(Wnd), 'DestroyWindow');
  Check(UnregisterClassW(WndClass, HINST(HInstance)), 'UnregisterClassW');
  Done;
end.
