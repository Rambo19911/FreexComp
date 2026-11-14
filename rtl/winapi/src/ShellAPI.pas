{ ShellAPI - shell32 bindings: ShellExecuteW and drag-and-drop of files (rtl/winapi.md sections 1 and 5.7; task
  R-113). Uses Windows.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the reference pages of ShellExecuteW,
  DragAcceptFiles, DragQueryFileW and DragFinish (shellapi.h), the DROPFILES structure page (shlobj_core.h) and
  the WM_DROPFILES page. Only the W variants are declared (RTL-821), each with its unsuffixed alias. }
unit ShellAPI;

interface

uses Windows;

type
  HDROP = type NativeUInt;

  { the memory block behind an HDROP: this header, then the file names (wide when fWide) ending in an empty name }
  PDropFiles = ^TDropFiles;
  TDropFiles = record
    pFiles: DWORD;
    pt: TPoint;
    fNC: BOOL;
    fWide: BOOL;
  end;
  DROPFILES = TDropFiles;

const
  { ShellExecuteW results of 32 or less }
  SE_ERR_FNF = 2;
  SE_ERR_PNF = 3;
  SE_ERR_ACCESSDENIED = 5;
  SE_ERR_OOM = 8;
  SE_ERR_SHARE = 26;
  SE_ERR_ASSOCINCOMPLETE = 27;
  SE_ERR_DDETIMEOUT = 28;
  SE_ERR_DDEFAIL = 29;
  SE_ERR_DDEBUSY = 30;
  SE_ERR_NOASSOC = 31;
  SE_ERR_DLLNOTFOUND = 32;

{ ---------------------------------------------------------------- shell32 functions (winapi.md section 5.7) }
function ShellExecuteW(hwnd: HWND; lpOperation, lpFile, lpParameters, lpDirectory: LPCWSTR; nShowCmd: Integer): HINST;
  stdcall; external shell32 name 'ShellExecuteW';
function ShellExecute(hwnd: HWND; lpOperation, lpFile, lpParameters, lpDirectory: LPCWSTR; nShowCmd: Integer): HINST;
  stdcall; external shell32 name 'ShellExecuteW';
procedure DragAcceptFiles(hWnd: HWND; fAccept: BOOL); stdcall; external shell32 name 'DragAcceptFiles';
function DragQueryFileW(hDrop: HDROP; iFile: UINT; lpszFile: LPWSTR; cch: UINT): UINT; stdcall;
  external shell32 name 'DragQueryFileW';
function DragQueryFile(hDrop: HDROP; iFile: UINT; lpszFile: LPWSTR; cch: UINT): UINT; stdcall;
  external shell32 name 'DragQueryFileW';
procedure DragFinish(hDrop: HDROP); stdcall; external shell32 name 'DragFinish';

implementation

end.
