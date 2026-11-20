{ Messages - window message numbers, notification codes and message-parameter values, TMessage and the cracked
  message records (rtl/winapi.md section 6, RTL-850/851; task R-111). Uses only Windows (RTL dependency graph).
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800): the "Window Notifications",
  "Keyboard Input Notifications", "Mouse Input Notifications", "Menu Notifications", "Dialog Box Notifications",
  "Clipboard Messages", "Painting and Drawing Messages" (including the WM_PRINT PRF_* options), High DPI and
  "Window Messages" pages of each WM_* message,
  and the Edit, Button, List Box, Combo Box, Static and Scroll Bar control reference (messages and notifications),
  including EM_SETCUEBANNER, EM_GETCUEBANNER, CB_SETMINVISIBLE, CB_GETMINVISIBLE, CB_SETCUEBANNER, CB_GETCUEBANNER.
  The common-control messages (TCM_, LVM_, TVM_, TB_, SB_SETTEXT..., PBM_, TBM_) are in CommCtrl.
  Cracked records overlay TMessage: Msg at 0, the WParam slot at 8, the LParam slot at 16, Result at 24, size 32.
  They are plain records with explicit filler fields (no variant parts), so the unit needs no variant records. }
unit Messages;

interface

uses Windows;

const
  { window messages }
  WM_NULL = $0000;
  WM_CREATE = $0001;
  WM_DESTROY = $0002;
  WM_MOVE = $0003;
  WM_SIZE = $0005;
  WM_ACTIVATE = $0006;
  WM_SETFOCUS = $0007;
  WM_KILLFOCUS = $0008;
  WM_ENABLE = $000A;
  WM_SETREDRAW = $000B;
  WM_SETTEXT = $000C;
  WM_GETTEXT = $000D;
  WM_GETTEXTLENGTH = $000E;
  WM_PAINT = $000F;
  WM_CLOSE = $0010;
  WM_QUERYENDSESSION = $0011;
  WM_QUIT = $0012;
  WM_QUERYOPEN = $0013;
  WM_ERASEBKGND = $0014;
  WM_SYSCOLORCHANGE = $0015;
  WM_ENDSESSION = $0016;
  WM_SHOWWINDOW = $0018;
  WM_SETTINGCHANGE = $001A;
  WM_WININICHANGE = $001A;
  WM_DEVMODECHANGE = $001B;
  WM_ACTIVATEAPP = $001C;
  WM_FONTCHANGE = $001D;
  WM_TIMECHANGE = $001E;
  WM_CANCELMODE = $001F;
  WM_SETCURSOR = $0020;
  WM_MOUSEACTIVATE = $0021;
  WM_CHILDACTIVATE = $0022;
  WM_QUEUESYNC = $0023;
  WM_GETMINMAXINFO = $0024;
  WM_PAINTICON = $0026;
  WM_ICONERASEBKGND = $0027;
  WM_NEXTDLGCTL = $0028;
  WM_SPOOLERSTATUS = $002A;
  WM_DRAWITEM = $002B;
  WM_MEASUREITEM = $002C;
  WM_DELETEITEM = $002D;
  WM_VKEYTOITEM = $002E;
  WM_CHARTOITEM = $002F;
  WM_SETFONT = $0030;
  WM_GETFONT = $0031;
  WM_SETHOTKEY = $0032;
  WM_GETHOTKEY = $0033;
  WM_QUERYDRAGICON = $0037;
  WM_COMPAREITEM = $0039;
  WM_GETOBJECT = $003D;
  WM_COMPACTING = $0041;
  WM_WINDOWPOSCHANGING = $0046;
  WM_WINDOWPOSCHANGED = $0047;
  WM_COPYDATA = $004A;
  WM_CANCELJOURNAL = $004B;
  WM_NOTIFY = $004E;
  WM_INPUTLANGCHANGEREQUEST = $0050;
  WM_INPUTLANGCHANGE = $0051;
  WM_TCARD = $0052;
  WM_HELP = $0053;
  WM_USERCHANGED = $0054;
  WM_NOTIFYFORMAT = $0055;
  WM_CONTEXTMENU = $007B;
  WM_STYLECHANGING = $007C;
  WM_STYLECHANGED = $007D;
  WM_DISPLAYCHANGE = $007E;
  WM_GETICON = $007F;
  WM_SETICON = $0080;
  WM_NCCREATE = $0081;
  WM_NCDESTROY = $0082;
  WM_NCCALCSIZE = $0083;
  WM_NCHITTEST = $0084;
  WM_NCPAINT = $0085;
  WM_NCACTIVATE = $0086;
  WM_GETDLGCODE = $0087;
  WM_NCMOUSEMOVE = $00A0;
  WM_NCLBUTTONDOWN = $00A1;
  WM_NCLBUTTONUP = $00A2;
  WM_NCLBUTTONDBLCLK = $00A3;
  WM_NCRBUTTONDOWN = $00A4;
  WM_NCRBUTTONUP = $00A5;
  WM_NCRBUTTONDBLCLK = $00A6;
  WM_NCMBUTTONDOWN = $00A7;
  WM_NCMBUTTONUP = $00A8;
  WM_NCMBUTTONDBLCLK = $00A9;
  WM_KEYFIRST = $0100;
  WM_KEYDOWN = $0100;
  WM_KEYUP = $0101;
  WM_CHAR = $0102;
  WM_DEADCHAR = $0103;
  WM_SYSKEYDOWN = $0104;
  WM_SYSKEYUP = $0105;
  WM_SYSCHAR = $0106;
  WM_SYSDEADCHAR = $0107;
  WM_UNICHAR = $0109;
  WM_KEYLAST = $0109;
  WM_IME_STARTCOMPOSITION = $010D;
  WM_IME_ENDCOMPOSITION = $010E;
  WM_IME_COMPOSITION = $010F;
  WM_INITDIALOG = $0110;
  WM_COMMAND = $0111;
  WM_SYSCOMMAND = $0112;
  WM_TIMER = $0113;
  WM_HSCROLL = $0114;
  WM_VSCROLL = $0115;
  WM_INITMENU = $0116;
  WM_INITMENUPOPUP = $0117;
  WM_MENUSELECT = $011F;
  WM_MENUCHAR = $0120;
  WM_ENTERIDLE = $0121;
  WM_MENURBUTTONUP = $0122;
  WM_MENUDRAG = $0123;
  WM_MENUGETOBJECT = $0124;
  WM_UNINITMENUPOPUP = $0125;
  WM_MENUCOMMAND = $0126;
  WM_CHANGEUISTATE = $0127;
  WM_UPDATEUISTATE = $0128;
  WM_QUERYUISTATE = $0129;
  WM_CTLCOLORMSGBOX = $0132;
  WM_CTLCOLOREDIT = $0133;
  WM_CTLCOLORLISTBOX = $0134;
  WM_CTLCOLORBTN = $0135;
  WM_CTLCOLORDLG = $0136;
  WM_CTLCOLORSCROLLBAR = $0137;
  WM_CTLCOLORSTATIC = $0138;
  WM_MOUSEFIRST = $0200;
  WM_MOUSEMOVE = $0200;
  WM_LBUTTONDOWN = $0201;
  WM_LBUTTONUP = $0202;
  WM_LBUTTONDBLCLK = $0203;
  WM_RBUTTONDOWN = $0204;
  WM_RBUTTONUP = $0205;
  WM_RBUTTONDBLCLK = $0206;
  WM_MBUTTONDOWN = $0207;
  WM_MBUTTONUP = $0208;
  WM_MBUTTONDBLCLK = $0209;
  WM_MOUSEWHEEL = $020A;
  WM_XBUTTONDOWN = $020B;
  WM_XBUTTONUP = $020C;
  WM_XBUTTONDBLCLK = $020D;
  WM_MOUSEHWHEEL = $020E;
  WM_MOUSELAST = $020E;
  WM_PARENTNOTIFY = $0210;
  WM_ENTERMENULOOP = $0211;
  WM_EXITMENULOOP = $0212;
  WM_NEXTMENU = $0213;
  WM_SIZING = $0214;
  WM_CAPTURECHANGED = $0215;
  WM_MOVING = $0216;
  WM_POWERBROADCAST = $0218;
  WM_DEVICECHANGE = $0219;
  WM_ENTERSIZEMOVE = $0231;
  WM_EXITSIZEMOVE = $0232;
  WM_DROPFILES = $0233;
  WM_IME_SETCONTEXT = $0281;
  WM_IME_NOTIFY = $0282;
  WM_NCMOUSEHOVER = $02A0;
  WM_MOUSEHOVER = $02A1;
  WM_NCMOUSELEAVE = $02A2;
  WM_MOUSELEAVE = $02A3;
  WM_DPICHANGED = $02E0;
  WM_DPICHANGED_BEFOREPARENT = $02E2;
  WM_DPICHANGED_AFTERPARENT = $02E3;
  WM_GETDPISCALEDSIZE = $02E4;
  WM_CUT = $0300;
  WM_COPY = $0301;
  WM_PASTE = $0302;
  WM_CLEAR = $0303;
  WM_UNDO = $0304;
  WM_RENDERFORMAT = $0305;
  WM_RENDERALLFORMATS = $0306;
  WM_DESTROYCLIPBOARD = $0307;
  WM_DRAWCLIPBOARD = $0308;
  WM_PAINTCLIPBOARD = $0309;
  WM_SIZECLIPBOARD = $030B;
  WM_CHANGECBCHAIN = $030D;
  WM_HSCROLLCLIPBOARD = $030E;
  WM_QUERYNEWPALETTE = $030F;
  WM_PALETTEISCHANGING = $0310;
  WM_PALETTECHANGED = $0311;
  WM_HOTKEY = $0312;
  WM_PRINT = $0317;
  WM_PRINTCLIENT = $0318;
  WM_APPCOMMAND = $0319;
  WM_THEMECHANGED = $031A;
  WM_CLIPBOARDUPDATE = $031D;
  WM_USER = $0400;
  WM_APP = $8000;

  { message parameter values }
  SIZE_RESTORED = 0;
  SIZE_MINIMIZED = 1;
  SIZE_MAXIMIZED = 2;
  SIZE_MAXSHOW = 3;
  SIZE_MAXHIDE = 4;
  WA_INACTIVE = 0;
  WA_ACTIVE = 1;
  WA_CLICKACTIVE = 2;
  MK_LBUTTON = $0001;
  MK_RBUTTON = $0002;
  MK_SHIFT = $0004;
  MK_CONTROL = $0008;
  MK_MBUTTON = $0010;
  MK_XBUTTON1 = $0020;
  MK_XBUTTON2 = $0040;
  WHEEL_DELTA = 120;
  ICON_SMALL = 0;
  ICON_BIG = 1;
  ICON_SMALL2 = 2;
  UIS_SET = 1;
  UIS_CLEAR = 2;
  UIS_INITIALIZE = 3;
  UISF_HIDEFOCUS = $1;
  UISF_HIDEACCEL = $2;
  UISF_ACTIVE = $4;

  { WM_NCHITTEST results }
  HTERROR = -2;
  HTTRANSPARENT = -1;
  HTNOWHERE = 0;
  HTCLIENT = 1;
  HTCAPTION = 2;
  HTSYSMENU = 3;
  HTGROWBOX = 4;
  HTSIZE = HTGROWBOX;
  HTMENU = 5;
  HTHSCROLL = 6;
  HTVSCROLL = 7;
  HTMINBUTTON = 8;
  HTREDUCE = HTMINBUTTON;
  HTMAXBUTTON = 9;
  HTZOOM = HTMAXBUTTON;
  HTLEFT = 10;
  HTRIGHT = 11;
  HTTOP = 12;
  HTTOPLEFT = 13;
  HTTOPRIGHT = 14;
  HTBOTTOM = 15;
  HTBOTTOMLEFT = 16;
  HTBOTTOMRIGHT = 17;
  HTBORDER = 18;
  HTCLOSE = 20;
  HTHELP = 21;

  { WM_SYSCOMMAND }
  SC_SIZE = $F000;
  SC_MOVE = $F010;
  SC_MINIMIZE = $F020;
  SC_MAXIMIZE = $F030;
  SC_NEXTWINDOW = $F040;
  SC_PREVWINDOW = $F050;
  SC_CLOSE = $F060;
  SC_VSCROLL = $F070;
  SC_HSCROLL = $F080;
  SC_MOUSEMENU = $F090;
  SC_KEYMENU = $F100;
  SC_RESTORE = $F120;
  SC_TASKLIST = $F130;
  SC_SCREENSAVE = $F140;
  SC_HOTKEY = $F150;
  SC_DEFAULT = $F160;
  SC_MONITORPOWER = $F170;
  SC_CONTEXTHELP = $F180;

  { WM_GETDLGCODE results }
  DLGC_WANTARROWS = $0001;
  DLGC_WANTTAB = $0002;
  DLGC_WANTALLKEYS = $0004;
  DLGC_WANTMESSAGE = $0004;
  DLGC_HASSETSEL = $0008;
  DLGC_DEFPUSHBUTTON = $0010;
  DLGC_UNDEFPUSHBUTTON = $0020;
  DLGC_RADIOBUTTON = $0040;
  DLGC_WANTCHARS = $0080;
  DLGC_STATIC = $0100;
  DLGC_BUTTON = $2000;

  { WM_PRINT / WM_PRINTCLIENT drawing options (lParam) }
  PRF_CHECKVISIBLE = $00000001;
  PRF_NONCLIENT = $00000002;
  PRF_CLIENT = $00000004;
  PRF_ERASEBKGND = $00000008;
  PRF_CHILDREN = $00000010;
  PRF_OWNED = $00000020;

  { edit control }
  EM_GETSEL = $00B0;
  EM_SETSEL = $00B1;
  EM_GETRECT = $00B2;
  EM_SETRECT = $00B3;
  EM_SETRECTNP = $00B4;
  EM_SCROLL = $00B5;
  EM_LINESCROLL = $00B6;
  EM_SCROLLCARET = $00B7;
  EM_GETMODIFY = $00B8;
  EM_SETMODIFY = $00B9;
  EM_GETLINECOUNT = $00BA;
  EM_LINEINDEX = $00BB;
  EM_SETHANDLE = $00BC;
  EM_GETHANDLE = $00BD;
  EM_GETTHUMB = $00BE;
  EM_LINELENGTH = $00C1;
  EM_REPLACESEL = $00C2;
  EM_GETLINE = $00C4;
  EM_LIMITTEXT = $00C5;
  EM_SETLIMITTEXT = EM_LIMITTEXT;
  EM_CANUNDO = $00C6;
  EM_UNDO = $00C7;
  EM_FMTLINES = $00C8;
  EM_LINEFROMCHAR = $00C9;
  EM_SETTABSTOPS = $00CB;
  EM_SETPASSWORDCHAR = $00CC;
  EM_EMPTYUNDOBUFFER = $00CD;
  EM_GETFIRSTVISIBLELINE = $00CE;
  EM_SETREADONLY = $00CF;
  EM_SETWORDBREAKPROC = $00D0;
  EM_GETWORDBREAKPROC = $00D1;
  EM_GETPASSWORDCHAR = $00D2;
  EM_SETMARGINS = $00D3;
  EM_GETMARGINS = $00D4;
  EM_GETLIMITTEXT = $00D5;
  EM_POSFROMCHAR = $00D6;
  EM_CHARFROMPOS = $00D7;
  EM_SETIMESTATUS = $00D8;
  EM_GETIMESTATUS = $00D9;
  ECM_FIRST = $1500;                   { edit messages of comctl32 version 6 (commctrl.h) }
  EM_SETCUEBANNER = ECM_FIRST + 1;
  EM_GETCUEBANNER = ECM_FIRST + 2;
  EC_LEFTMARGIN = $0001;
  EC_RIGHTMARGIN = $0002;
  EC_USEFONTINFO = $FFFF;
  EN_SETFOCUS = $0100;
  EN_KILLFOCUS = $0200;
  EN_CHANGE = $0300;
  EN_UPDATE = $0400;
  EN_ERRSPACE = $0500;
  EN_MAXTEXT = $0501;
  EN_HSCROLL = $0601;
  EN_VSCROLL = $0602;
  EN_ALIGN_LTR_EC = $0700;
  EN_ALIGN_RTL_EC = $0701;

  { button }
  BM_GETCHECK = $00F0;
  BM_SETCHECK = $00F1;
  BM_GETSTATE = $00F2;
  BM_SETSTATE = $00F3;
  BM_SETSTYLE = $00F4;
  BM_CLICK = $00F5;
  BM_GETIMAGE = $00F6;
  BM_SETIMAGE = $00F7;
  BM_SETDONTCLICK = $00F8;
  BN_CLICKED = 0;
  BN_PAINT = 1;
  BN_HILITE = 2;
  BN_UNHILITE = 3;
  BN_DISABLE = 4;
  BN_DOUBLECLICKED = 5;
  BN_PUSHED = BN_HILITE;
  BN_UNPUSHED = BN_UNHILITE;
  BN_DBLCLK = BN_DOUBLECLICKED;
  BN_SETFOCUS = 6;
  BN_KILLFOCUS = 7;
  BST_UNCHECKED = $0000;
  BST_CHECKED = $0001;
  BST_INDETERMINATE = $0002;
  BST_PUSHED = $0004;
  BST_FOCUS = $0008;

  { list box }
  LB_OKAY = 0;
  LB_ERR = -1;
  LB_ERRSPACE = -2;
  LB_ADDSTRING = $0180;
  LB_INSERTSTRING = $0181;
  LB_DELETESTRING = $0182;
  LB_SELITEMRANGEEX = $0183;
  LB_RESETCONTENT = $0184;
  LB_SETSEL = $0185;
  LB_SETCURSEL = $0186;
  LB_GETSEL = $0187;
  LB_GETCURSEL = $0188;
  LB_GETTEXT = $0189;
  LB_GETTEXTLEN = $018A;
  LB_GETCOUNT = $018B;
  LB_SELECTSTRING = $018C;
  LB_DIR = $018D;
  LB_GETTOPINDEX = $018E;
  LB_FINDSTRING = $018F;
  LB_GETSELCOUNT = $0190;
  LB_GETSELITEMS = $0191;
  LB_SETTABSTOPS = $0192;
  LB_GETHORIZONTALEXTENT = $0193;
  LB_SETHORIZONTALEXTENT = $0194;
  LB_SETCOLUMNWIDTH = $0195;
  LB_ADDFILE = $0196;
  LB_SETTOPINDEX = $0197;
  LB_GETITEMRECT = $0198;
  LB_GETITEMDATA = $0199;
  LB_SETITEMDATA = $019A;
  LB_SELITEMRANGE = $019B;
  LB_SETANCHORINDEX = $019C;
  LB_GETANCHORINDEX = $019D;
  LB_SETCARETINDEX = $019E;
  LB_GETCARETINDEX = $019F;
  LB_SETITEMHEIGHT = $01A0;
  LB_GETITEMHEIGHT = $01A1;
  LB_FINDSTRINGEXACT = $01A2;
  LB_SETLOCALE = $01A5;
  LB_GETLOCALE = $01A6;
  LB_SETCOUNT = $01A7;
  LB_INITSTORAGE = $01A8;
  LB_ITEMFROMPOINT = $01A9;
  LB_GETLISTBOXINFO = $01B2;
  LBN_ERRSPACE = -2;
  LBN_SELCHANGE = 1;
  LBN_DBLCLK = 2;
  LBN_SELCANCEL = 3;
  LBN_SETFOCUS = 4;
  LBN_KILLFOCUS = 5;

  { combo box }
  CB_OKAY = 0;
  CB_ERR = -1;
  CB_ERRSPACE = -2;
  CB_GETEDITSEL = $0140;
  CB_LIMITTEXT = $0141;
  CB_SETEDITSEL = $0142;
  CB_ADDSTRING = $0143;
  CB_DELETESTRING = $0144;
  CB_DIR = $0145;
  CB_GETCOUNT = $0146;
  CB_GETCURSEL = $0147;
  CB_GETLBTEXT = $0148;
  CB_GETLBTEXTLEN = $0149;
  CB_INSERTSTRING = $014A;
  CB_RESETCONTENT = $014B;
  CB_FINDSTRING = $014C;
  CB_SELECTSTRING = $014D;
  CB_SETCURSEL = $014E;
  CB_SHOWDROPDOWN = $014F;
  CB_GETITEMDATA = $0150;
  CB_SETITEMDATA = $0151;
  CB_GETDROPPEDCONTROLRECT = $0152;
  CB_SETITEMHEIGHT = $0153;
  CB_GETITEMHEIGHT = $0154;
  CB_SETEXTENDEDUI = $0155;
  CB_GETEXTENDEDUI = $0156;
  CB_GETDROPPEDSTATE = $0157;
  CB_FINDSTRINGEXACT = $0158;
  CB_SETLOCALE = $0159;
  CB_GETLOCALE = $015A;
  CB_GETTOPINDEX = $015B;
  CB_SETTOPINDEX = $015C;
  CB_GETHORIZONTALEXTENT = $015D;
  CB_SETHORIZONTALEXTENT = $015E;
  CB_GETDROPPEDWIDTH = $015F;
  CB_SETDROPPEDWIDTH = $0160;
  CB_INITSTORAGE = $0161;
  CB_GETCOMBOBOXINFO = $0164;
  CBM_FIRST = $1700;                   { combo box messages of comctl32 version 6 (commctrl.h) }
  CB_SETMINVISIBLE = CBM_FIRST + 1;
  CB_GETMINVISIBLE = CBM_FIRST + 2;
  CB_SETCUEBANNER = CBM_FIRST + 3;
  CB_GETCUEBANNER = CBM_FIRST + 4;
  CBN_ERRSPACE = -1;
  CBN_SELCHANGE = 1;
  CBN_DBLCLK = 2;
  CBN_SETFOCUS = 3;
  CBN_KILLFOCUS = 4;
  CBN_EDITCHANGE = 5;
  CBN_EDITUPDATE = 6;
  CBN_DROPDOWN = 7;
  CBN_CLOSEUP = 8;
  CBN_SELENDOK = 9;
  CBN_SELENDCANCEL = 10;

  { static control }
  STM_SETICON = $0170;
  STM_GETICON = $0171;
  STM_SETIMAGE = $0172;
  STM_GETIMAGE = $0173;
  STN_CLICKED = 0;
  STN_DBLCLK = 1;
  STN_ENABLE = 2;
  STN_DISABLE = 3;

  { scroll bar control }
  SBM_SETPOS = $00E0;
  SBM_GETPOS = $00E1;
  SBM_SETRANGE = $00E2;
  SBM_GETRANGE = $00E3;
  SBM_ENABLE_ARROWS = $00E4;
  SBM_SETRANGEREDRAW = $00E6;
  SBM_SETSCROLLINFO = $00E9;
  SBM_GETSCROLLINFO = $00EA;
  SBM_GETSCROLLBARINFO = $00EB;

type
  { the first 4 bytes are the id that TObject.Dispatch reads (ABI-073) }
  PMessage = ^TMessage;
  TMessage = record
    Msg: Cardinal;
    WParam: WPARAM;
    LParam: LPARAM;
    Result: LRESULT;
  end;

  { (Msg, MsgFiller) occupy bytes 0..7 in every cracked record }
  TWMNoParams = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: array[0..1] of NativeUInt;
    Result: LRESULT;
  end;
  TWMClose = TWMNoParams;
  TWMCancelMode = TWMNoParams;
  TWMDestroy = TWMNoParams;

  TWMSize = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    SizeType: WPARAM;
    Width: Word;
    Height: Word;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMMove = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMCommand = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    ItemID: Word;
    NotifyCode: Word;
    WParamFiller: Cardinal;
    Ctl: HWND;
    Result: LRESULT;
  end;

  TWMNotify = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    IDCtrl: WPARAM;
    NMHdr: PNMHdr;
    Result: LRESULT;
  end;

  TWMPaint = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    DC: HDC;
    Unused: LPARAM;
    Result: LRESULT;
  end;

  TWMEraseBkgnd = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    DC: HDC;
    Unused: LPARAM;
    Result: LRESULT;
  end;

  TWMKey = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    CharCode: Word;
    Unused: Word;
    WParamFiller: Cardinal;
    KeyData: LPARAM;
    Result: LRESULT;
  end;
  TWMChar = TWMKey;
  TWMKeyDown = TWMKey;
  TWMKeyUp = TWMKey;
  TWMSysChar = TWMKey;
  TWMSysKeyDown = TWMKey;
  TWMSysKeyUp = TWMKey;

  TWMMouse = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Keys: WPARAM;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;
  TWMMouseMove = TWMMouse;
  TWMLButtonDown = TWMMouse;
  TWMLButtonUp = TWMMouse;
  TWMLButtonDblClk = TWMMouse;
  TWMRButtonDown = TWMMouse;
  TWMRButtonUp = TWMMouse;
  TWMRButtonDblClk = TWMMouse;
  TWMMButtonDown = TWMMouse;
  TWMMButtonUp = TWMMouse;
  TWMMButtonDblClk = TWMMouse;

  TWMMouseWheel = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Keys: SmallInt;
    WheelDelta: SmallInt;
    WParamFiller: Cardinal;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMSetFocus = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    FocusedWnd: HWND;
    Unused: LPARAM;
    Result: LRESULT;
  end;

  TWMKillFocus = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    FocusedWnd: HWND;
    Unused: LPARAM;
    Result: LRESULT;
  end;

  TWMActivate = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Active: Word;
    Minimized: WordBool;
    WParamFiller: Cardinal;
    ActiveWindow: HWND;
    Result: LRESULT;
  end;

  TWMShowWindow = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Show: BOOL;
    WParamFiller: Cardinal;
    Status: LPARAM;
    Result: LRESULT;
  end;

  TWMWindowPosMsg = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    WindowPos: PWindowPos;
    Result: LRESULT;
  end;
  TWMWindowPosChanged = TWMWindowPosMsg;
  TWMWindowPosChanging = TWMWindowPosMsg;

  TWMGetMinMaxInfo = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    MinMaxInfo: PMinMaxInfo;
    Result: LRESULT;
  end;

  TWMSetCursor = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    CursorWnd: HWND;
    HitTest: SmallInt;
    MouseMsg: Word;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMTimer = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    TimerID: WPARAM;
    TimerProc: TFNTimerProc;
    Result: LRESULT;
  end;

  TWMScroll = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    ScrollCode: SmallInt;
    Pos: SmallInt;
    WParamFiller: Cardinal;
    ScrollBar: HWND;
    Result: LRESULT;
  end;
  TWMHScroll = TWMScroll;
  TWMVScroll = TWMScroll;

  TWMSysCommand = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    CmdType: WPARAM;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMNCHitTest = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMDrawItem = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Ctl: WPARAM;
    DrawItemStruct: PDrawItemStruct;
    Result: LRESULT;
  end;

  TWMMeasureItem = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    IDCtl: WPARAM;
    MeasureItemStruct: PMeasureItemStruct;
    Result: LRESULT;
  end;

  TWMDpiChanged = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    XDpi: Word;
    YDpi: Word;
    WParamFiller: Cardinal;
    SuggestedRect: PRect;
    Result: LRESULT;
  end;

  TWMContextMenu = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    hWnd: HWND;
    XPos: SmallInt;
    YPos: SmallInt;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

  TWMGetDlgCode = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    MsgPtr: PMsg;
    Result: LRESULT;
  end;

  TWMSetText = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Unused: WPARAM;
    Text: PWideChar;
    Result: LRESULT;
  end;

  TWMSetFont = record
    Msg: Cardinal;
    MsgFiller: Cardinal;
    Font: HFONT;
    Redraw: WordBool;
    Unused: Word;
    LParamFiller: Cardinal;
    Result: LRESULT;
  end;

implementation

end.
