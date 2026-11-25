// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ RichEdit - constants and records of the rich edit engine (Msftedit.dll, window class RICHEDIT50W)
  (rtl/winapi.md sections 1 and 4.1; task R-235). Uses Windows, Messages.
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the "Rich Edit Controls" reference
  (controls/rich-edit-controls) - its message pages (EM_EXGETSEL ... EM_SETZOOM), notification pages (EN_SELCHANGE,
  EN_LINK, EN_PROTECTED), the structure pages CHARRANGE, TEXTRANGEW, FINDTEXTEXW, CHARFORMAT2W (with CHARFORMATW),
  PARAFORMAT2 (with PARAFORMAT), EDITSTREAM, GETTEXTLENGTHEX, GETTEXTEX, SETTEXTEX, ENLINK, SELCHANGE, ENPROTECTED,
  the EditStreamCallback callback page, and "About Rich Edit Controls" (library and class name).
  The unit declares no function: the engine is driven by window messages only, so a program that uses the unit
  imports nothing more (RTL-1025). The library is loaded by its user with LoadLibraryExW.
  Layout (RTL-1024): the platform header declares these structures with 4-byte packing, so a pointer-sized member
  is not aligned to 8. Records whose default layout would differ are declared packed (with explicit padding where
  the 4-byte rule still pads); the others are plain records. Sizes in the comments are the 64-bit ones. }
unit RichEdit;

interface

uses Windows, Messages;

const
  MSFTEDIT_DLL = 'Msftedit.dll';
  MSFTEDIT_CLASS = 'RICHEDIT50W';

{ ---------------------------------------------------------------- messages }
const
  EM_EXGETSEL = WM_USER + 52;
  EM_EXLIMITTEXT = WM_USER + 53;
  EM_EXSETSEL = WM_USER + 55;
  EM_GETCHARFORMAT = WM_USER + 58;
  EM_GETEVENTMASK = WM_USER + 59;
  EM_GETPARAFORMAT = WM_USER + 61;
  EM_GETSELTEXT = WM_USER + 62;
  EM_HIDESELECTION = WM_USER + 63;
  EM_SETBKGNDCOLOR = WM_USER + 67;
  EM_SETCHARFORMAT = WM_USER + 68;
  EM_SETEVENTMASK = WM_USER + 69;
  EM_SETPARAFORMAT = WM_USER + 71;
  EM_SETTARGETDEVICE = WM_USER + 72;
  EM_STREAMIN = WM_USER + 73;
  EM_STREAMOUT = WM_USER + 74;
  EM_GETTEXTRANGE = WM_USER + 75;
  EM_SETUNDOLIMIT = WM_USER + 82;
  EM_REDO = WM_USER + 84;
  EM_CANREDO = WM_USER + 85;
  EM_AUTOURLDETECT = WM_USER + 91;
  EM_GETAUTOURLDETECT = WM_USER + 92;
  EM_GETTEXTEX = WM_USER + 94;
  EM_GETTEXTLENGTHEX = WM_USER + 95;
  EM_SETTEXTEX = WM_USER + 97;
  EM_FINDTEXTEXW = WM_USER + 124;
  EM_GETZOOM = WM_USER + 224;
  EM_SETZOOM = WM_USER + 225;

{ ---------------------------------------------------------------- notification codes (WM_NOTIFY) and event mask }
const
  EN_SELCHANGE = $0702;
  EN_PROTECTED = $0704;
  EN_LINK = $070B;

  ENM_NONE = $00000000;
  ENM_CHANGE = $00000001;
  ENM_SELCHANGE = $00080000;
  ENM_PROTECTED = $00200000;
  ENM_LINK = $04000000;

{ ---------------------------------------------------------------- character format }
const
  CFM_BOLD = $00000001;
  CFM_ITALIC = $00000002;
  CFM_UNDERLINE = $00000004;
  CFM_STRIKEOUT = $00000008;
  CFM_PROTECTED = $00000010;
  CFM_LINK = $00000020;
  CFM_SIZE = $80000000;
  CFM_COLOR = $40000000;
  CFM_FACE = $20000000;
  CFM_CHARSET = $08000000;

  CFE_BOLD = $00000001;
  CFE_ITALIC = $00000002;
  CFE_UNDERLINE = $00000004;
  CFE_STRIKEOUT = $00000008;
  CFE_PROTECTED = $00000010;
  CFE_LINK = $00000020;
  CFE_AUTOCOLOR = $40000000;

  SCF_DEFAULT = $0000;
  SCF_SELECTION = $0001;
  SCF_ALL = $0004;

{ ---------------------------------------------------------------- paragraph format }
const
  PFM_STARTINDENT = $00000001;
  PFM_RIGHTINDENT = $00000002;
  PFM_OFFSET = $00000004;
  PFM_ALIGNMENT = $00000008;
  PFM_TABSTOPS = $00000010;
  PFM_NUMBERING = $00000020;

  PFA_LEFT = 1;
  PFA_RIGHT = 2;
  PFA_CENTER = 3;

  PFN_BULLET = 1;

  MAX_TAB_STOPS = 32;

{ ---------------------------------------------------------------- streams, text, selection type, search }
const
  SF_TEXT = $0001;
  SF_RTF = $0002;
  SF_UNICODE = $0010;
  SFF_SELECTION = $8000;

  ST_DEFAULT = 0;
  ST_KEEPUNDO = 1;
  ST_SELECTION = 2;

  GT_DEFAULT = 0;
  GT_USECRLF = 1;

  GTL_DEFAULT = 0;
  GTL_USECRLF = 1;
  GTL_NUMCHARS = 8;

  SEL_EMPTY = $0000;
  SEL_TEXT = $0001;

  FR_DOWN = $00000001;
  FR_WHOLEWORD = $00000002;
  FR_MATCHCASE = $00000004;

{ ---------------------------------------------------------------- records }
type
  { CHARRANGE: 8 bytes }
  PCharRange = ^TCharRange;
  TCharRange = record
    cpMin: Longint;
    cpMax: Longint;
  end;
  CHARRANGE = TCharRange;

  { TEXTRANGEW: 16 bytes }
  PTextRangeW = ^TTextRangeW;
  TTextRangeW = record
    chrg: TCharRange;
    lpstrText: LPWSTR;
  end;
  TEXTRANGEW = TTextRangeW;
  PTextRange = PTextRangeW;
  TTextRange = TTextRangeW;

  { FINDTEXTEXW: 24 bytes }
  PFindTextExW = ^TFindTextExW;
  TFindTextExW = record
    chrg: TCharRange;
    lpstrText: LPCWSTR;
    chrgText: TCharRange;
  end;
  FINDTEXTEXW = TFindTextExW;
  PFindTextEx = PFindTextExW;
  TFindTextEx = TFindTextExW;

  { CHARFORMAT2W: 116 bytes (the CHARFORMATW members up to szFaceName, then the extension) }
  PCharFormat2W = ^TCharFormat2W;
  TCharFormat2W = record
    cbSize: UINT;
    dwMask: DWORD;
    dwEffects: DWORD;
    yHeight: Longint;
    yOffset: Longint;
    crTextColor: COLORREF;
    bCharSet: Byte;
    bPitchAndFamily: Byte;
    szFaceName: array[0..LF_FACESIZE - 1] of WideChar;
    wWeight: Word;
    sSpacing: SmallInt;
    crBackColor: COLORREF;
    lcid: LCID;
    dwCookie: DWORD;
    sStyle: SmallInt;
    wKerning: Word;
    bUnderlineType: Byte;
    bAnimation: Byte;
    bRevAuthor: Byte;
    bUnderlineColor: Byte;
  end;
  CHARFORMAT2W = TCharFormat2W;
  PCharFormat2 = PCharFormat2W;
  TCharFormat2 = TCharFormat2W;

  { PARAFORMAT2: 188 bytes (the PARAFORMAT members up to rgxTabs, then the extension) }
  PParaFormat2 = ^TParaFormat2;
  TParaFormat2 = record
    cbSize: UINT;
    dwMask: DWORD;
    wNumbering: Word;
    wEffects: Word;
    dxStartIndent: Longint;
    dxRightIndent: Longint;
    dxOffset: Longint;
    wAlignment: Word;
    cTabCount: SmallInt;
    rgxTabs: array[0..MAX_TAB_STOPS - 1] of Longint;
    dySpaceBefore: Longint;
    dySpaceAfter: Longint;
    dyLineSpacing: Longint;
    sStyle: SmallInt;
    bLineSpacingRule: Byte;
    bOutlineLevel: Byte;
    wShadingWeight: Word;
    wShadingStyle: Word;
    wNumberingStart: Word;
    wNumberingStyle: Word;
    wNumberingTab: Word;
    wBorderSpace: Word;
    wBorderWidth: Word;
    wBorders: Word;
  end;
  PARAFORMAT2 = TParaFormat2;

  { EditStreamCallback: returns 0 to continue; pcb receives the number of bytes transferred }
  TEditStreamCallback = function(dwCookie: NativeUInt; pbBuff: PByte; cb: Longint; var pcb: Longint): DWORD; stdcall;

  { EDITSTREAM: 20 bytes; pfnCallback at 12 (4-byte packing) }
  PEditStream = ^TEditStream;
  TEditStream = packed record
    dwCookie: DWORD_PTR;
    dwError: DWORD;
    pfnCallback: TEditStreamCallback;
  end;
  EDITSTREAM = TEditStream;

  { GETTEXTLENGTHEX: 8 bytes }
  PGetTextLengthEx = ^TGetTextLengthEx;
  TGetTextLengthEx = record
    flags: DWORD;
    codepage: UINT;
  end;
  GETTEXTLENGTHEX = TGetTextLengthEx;

  { GETTEXTEX: 28 bytes; lpDefaultChar at 12, lpUsedDefChar at 20 (4-byte packing) }
  PGetTextEx = ^TGetTextEx;
  TGetTextEx = packed record
    cb: DWORD;
    flags: DWORD;
    codepage: UINT;
    lpDefaultChar: LPCSTR;
    lpUsedDefChar: PBOOL;
  end;
  GETTEXTEX = TGetTextEx;

  { SETTEXTEX: 8 bytes }
  PSetTextEx = ^TSetTextEx;
  TSetTextEx = record
    flags: DWORD;
    codepage: UINT;
  end;
  SETTEXTEX = TSetTextEx;

  { ENLINK: 52 bytes; wParam at 28, lParam at 36, chrg at 44 (4-byte packing) }
  PENLink = ^TENLink;
  TENLink = packed record
    nmhdr: TNMHdr;
    msg: UINT;
    wParam: WPARAM;
    lParam: LPARAM;
    chrg: TCharRange;
  end;
  ENLINK = TENLink;

  { SELCHANGE: 36 bytes; chrg at 24, seltyp at 32, padded to a multiple of 4 }
  PSelChange = ^TSelChange;
  TSelChange = packed record
    nmhdr: TNMHdr;
    chrg: TCharRange;
    seltyp: Word;
    _pad1: Word;
  end;
  SELCHANGE = TSelChange;

  { ENPROTECTED: 52 bytes; the layout of ENLINK }
  PENProtected = ^TENProtected;
  TENProtected = packed record
    nmhdr: TNMHdr;
    msg: UINT;
    wParam: WPARAM;
    lParam: LPARAM;
    chrg: TCharRange;
  end;
  ENPROTECTED = TENProtected;

implementation

end.
