{ CommDlg - comdlg32 bindings: open/save, colour and font common dialogs (rtl/winapi.md sections 1 and 5.6; task
  R-113). Uses Windows.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the reference pages of
  GetOpenFileNameW, GetSaveFileNameW, ChooseColorW, ChooseFontW and CommDlgExtendedError (commdlg.h); the structure
  pages OPENFILENAMEW, CHOOSECOLORW, CHOOSEFONTW; the OFNHookProc, CCHookProc and CFHookProc callback pages.
  Only the W variants are declared (RTL-821), each with its unsuffixed alias. The structures are the 64-bit layout.
  CHOOSECOLORW/CHOOSECOLOR and CHOOSEFONTW/CHOOSEFONT have no upper-case type alias: case-insensitively they would
  equal the functions ChooseColorW/ChooseColor and ChooseFontW/ChooseFont (RTL-832 exception). }
unit CommDlg;

interface

uses Windows;

type
  TFNOFNHookProc = function(hdlg: HWND; uiMsg: UINT; wParam: WPARAM; lParam: LPARAM): UINT_PTR; stdcall;
  TFNCCHookProc = function(hdlg: HWND; uiMsg: UINT; wParam: WPARAM; lParam: LPARAM): UINT_PTR; stdcall;
  TFNCFHookProc = function(hdlg: HWND; uiMsg: UINT; wParam: WPARAM; lParam: LPARAM): UINT_PTR; stdcall;

  POpenFilenameW = ^TOpenFilenameW;
  TOpenFilenameW = record
    lStructSize: DWORD;
    hwndOwner: HWND;
    hInstance: HINST;
    lpstrFilter: LPCWSTR;
    lpstrCustomFilter: LPWSTR;
    nMaxCustFilter: DWORD;
    nFilterIndex: DWORD;
    lpstrFile: LPWSTR;
    nMaxFile: DWORD;
    lpstrFileTitle: LPWSTR;
    nMaxFileTitle: DWORD;
    lpstrInitialDir: LPCWSTR;
    lpstrTitle: LPCWSTR;
    Flags: DWORD;
    nFileOffset: Word;
    nFileExtension: Word;
    lpstrDefExt: LPCWSTR;
    lCustData: LPARAM;
    lpfnHook: TFNOFNHookProc;
    lpTemplateName: LPCWSTR;
    pvReserved: Pointer;
    dwReserved: DWORD;
    FlagsEx: DWORD;
  end;
  OPENFILENAMEW = TOpenFilenameW;
  POpenFilename = POpenFilenameW;
  TOpenFilename = TOpenFilenameW;
  OPENFILENAME = TOpenFilenameW;

  PChooseColorW = ^TChooseColorW;
  TChooseColorW = record
    lStructSize: DWORD;
    hwndOwner: HWND;
    hInstance: HWND;
    rgbResult: COLORREF;
    lpCustColors: PCardinal;
    Flags: DWORD;
    lCustData: LPARAM;
    lpfnHook: TFNCCHookProc;
    lpTemplateName: LPCWSTR;
  end;
  PChooseColor = PChooseColorW;
  TChooseColor = TChooseColorW;

  PChooseFontW = ^TChooseFontW;
  TChooseFontW = record
    lStructSize: DWORD;
    hwndOwner: HWND;
    hDC: HDC;
    lpLogFont: PLogFontW;
    iPointSize: Integer;
    Flags: DWORD;
    rgbColors: COLORREF;
    lCustData: LPARAM;
    lpfnHook: TFNCFHookProc;
    lpTemplateName: LPCWSTR;
    hInstance: HINST;
    lpszStyle: LPWSTR;
    nFontType: Word;
    ___MISSING_ALIGNMENT__: Word;
    nSizeMin: Integer;
    nSizeMax: Integer;
  end;
  PChooseFont = PChooseFontW;
  TChooseFont = TChooseFontW;

const
  { TOpenFilenameW.Flags }
  OFN_READONLY = $00000001;
  OFN_OVERWRITEPROMPT = $00000002;
  OFN_HIDEREADONLY = $00000004;
  OFN_NOCHANGEDIR = $00000008;
  OFN_SHOWHELP = $00000010;
  OFN_ENABLEHOOK = $00000020;
  OFN_ENABLETEMPLATE = $00000040;
  OFN_ENABLETEMPLATEHANDLE = $00000080;
  OFN_NOVALIDATE = $00000100;
  OFN_ALLOWMULTISELECT = $00000200;
  OFN_EXTENSIONDIFFERENT = $00000400;
  OFN_PATHMUSTEXIST = $00000800;
  OFN_FILEMUSTEXIST = $00001000;
  OFN_CREATEPROMPT = $00002000;
  OFN_SHAREAWARE = $00004000;
  OFN_NOREADONLYRETURN = $00008000;
  OFN_NOTESTFILECREATE = $00010000;
  OFN_NONETWORKBUTTON = $00020000;
  OFN_NOLONGNAMES = $00040000;
  OFN_EXPLORER = $00080000;
  OFN_NODEREFERENCELINKS = $00100000;
  OFN_LONGNAMES = $00200000;
  OFN_ENABLEINCLUDENOTIFY = $00400000;
  OFN_ENABLESIZING = $00800000;
  OFN_DONTADDTORECENT = $02000000;
  OFN_FORCESHOWHIDDEN = $10000000;

  { TOpenFilenameW.FlagsEx }
  OFN_EX_NOPLACESBAR = $00000001;

  { TChooseColorW.Flags }
  CC_RGBINIT = $00000001;
  CC_FULLOPEN = $00000002;
  CC_PREVENTFULLOPEN = $00000004;
  CC_SHOWHELP = $00000008;
  CC_ENABLEHOOK = $00000010;
  CC_ENABLETEMPLATE = $00000020;
  CC_ENABLETEMPLATEHANDLE = $00000040;
  CC_SOLIDCOLOR = $00000080;
  CC_ANYCOLOR = $00000100;

  { TChooseFontW.Flags }
  CF_SCREENFONTS = $00000001;
  CF_PRINTERFONTS = $00000002;
  CF_BOTH = CF_SCREENFONTS or CF_PRINTERFONTS;
  CF_SHOWHELP = $00000004;
  CF_ENABLEHOOK = $00000008;
  CF_ENABLETEMPLATE = $00000010;
  CF_ENABLETEMPLATEHANDLE = $00000020;
  CF_INITTOLOGFONTSTRUCT = $00000040;
  CF_USESTYLE = $00000080;
  CF_EFFECTS = $00000100;
  CF_APPLY = $00000200;
  CF_ANSIONLY = $00000400;
  CF_SCRIPTSONLY = CF_ANSIONLY;
  CF_NOVECTORFONTS = $00000800;
  CF_NOOEMFONTS = CF_NOVECTORFONTS;
  CF_NOSIMULATIONS = $00001000;
  CF_LIMITSIZE = $00002000;
  CF_FIXEDPITCHONLY = $00004000;
  CF_WYSIWYG = $00008000;
  CF_FORCEFONTEXIST = $00010000;
  CF_SCALABLEONLY = $00020000;
  CF_TTONLY = $00040000;
  CF_NOFACESEL = $00080000;
  CF_NOSTYLESEL = $00100000;
  CF_NOSIZESEL = $00200000;
  CF_SELECTSCRIPT = $00400000;
  CF_NOSCRIPTSEL = $00800000;
  CF_NOVERTFONTS = $01000000;
  CF_INACTIVEFONTS = $02000000;

  { TChooseFontW.nFontType }
  BOLD_FONTTYPE = $0100;
  ITALIC_FONTTYPE = $0200;
  REGULAR_FONTTYPE = $0400;
  SCREEN_FONTTYPE = $2000;
  PRINTER_FONTTYPE = $4000;
  SIMULATED_FONTTYPE = $8000;

  { CommDlgExtendedError }
  CDERR_DIALOGFAILURE = $FFFF;
  CDERR_GENERALCODES = $0000;
  CDERR_STRUCTSIZE = $0001;
  CDERR_INITIALIZATION = $0002;
  CDERR_NOTEMPLATE = $0003;
  CDERR_NOHINSTANCE = $0004;
  CDERR_LOADSTRFAILURE = $0005;
  CDERR_FINDRESFAILURE = $0006;
  CDERR_LOADRESFAILURE = $0007;
  CDERR_LOCKRESFAILURE = $0008;
  CDERR_MEMALLOCFAILURE = $0009;
  CDERR_MEMLOCKFAILURE = $000A;
  CDERR_NOHOOK = $000B;
  CDERR_REGISTERMSGFAIL = $000C;
  CFERR_CHOOSEFONTCODES = $2000;
  CFERR_NOFONTS = $2001;
  CFERR_MAXLESSTHANMIN = $2002;
  FNERR_FILENAMECODES = $3000;
  FNERR_SUBCLASSFAILURE = $3001;
  FNERR_INVALIDFILENAME = $3002;
  FNERR_BUFFERTOOSMALL = $3003;
  CCERR_CHOOSECOLORCODES = $5000;

{ ---------------------------------------------------------------- comdlg32 functions (winapi.md section 5.6) }
function GetOpenFileNameW(var lpofn: TOpenFilenameW): BOOL; stdcall; external comdlg32 name 'GetOpenFileNameW';
function GetOpenFileName(var lpofn: TOpenFilenameW): BOOL; stdcall; external comdlg32 name 'GetOpenFileNameW';
function GetSaveFileNameW(var lpofn: TOpenFilenameW): BOOL; stdcall; external comdlg32 name 'GetSaveFileNameW';
function GetSaveFileName(var lpofn: TOpenFilenameW): BOOL; stdcall; external comdlg32 name 'GetSaveFileNameW';
function ChooseColorW(var lpcc: TChooseColorW): BOOL; stdcall; external comdlg32 name 'ChooseColorW';
function ChooseColor(var lpcc: TChooseColorW): BOOL; stdcall; external comdlg32 name 'ChooseColorW';
function ChooseFontW(var lpcf: TChooseFontW): BOOL; stdcall; external comdlg32 name 'ChooseFontW';
function ChooseFont(var lpcf: TChooseFontW): BOOL; stdcall; external comdlg32 name 'ChooseFontW';
function CommDlgExtendedError: DWORD; stdcall; external comdlg32 name 'CommDlgExtendedError';

implementation

end.
