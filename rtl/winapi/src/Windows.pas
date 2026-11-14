{ Windows - Win32 bindings: base types, macros, kernel32, ntdll, user32 and gdi32 functions, constants and records
  (rtl/winapi.md; kernel32 part of Phase 2, task R-034; user32 part R-109; gdi32 part R-110).
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800):
  - Windows Data Types; LOWORD/HIWORD/MAKELONG/MAKEWPARAM/MAKELPARAM/MAKELRESULT/RGB/GetRValue/GetGValue/GetBValue/
    MAKEINTRESOURCEW/SUCCEEDED/FAILED/GET_X_LPARAM/GET_Y_LPARAM/GET_WHEEL_DELTA_WPARAM macro pages;
  - the reference page of every function declared below (processthreadsapi.h, libloaderapi.h, processenv.h,
    sysinfoapi.h, heapapi.h, memoryapi.h, winbase.h (including GlobalAlloc, GlobalLock, GlobalUnlock, GlobalFree),
    errhandlingapi.h, debugapi.h, consoleapi.h, fileapi.h,
    handleapi.h, timezoneapi.h, profileapi.h, synchapi.h, stringapiset.h, winnls.h, wdm.h RtlGetVersion);
  - the structure pages SYSTEMTIME, FILETIME, WIN32_FIND_DATAW, WIN32_FILE_ATTRIBUTE_DATA, RTL_CRITICAL_SECTION,
    EXCEPTION_RECORD, TIME_ZONE_INFORMATION, SECURITY_ATTRIBUTES, OVERLAPPED, SYSTEM_INFO, OSVERSIONINFOW,
    OSVERSIONINFOEXW;
  - System Error Codes (0-499), Generic Access Rights, File Attribute Constants, Locale Information Constants,
    Memory Protection Constants, Code Page Identifiers.
  - user32 (winuser.h): the reference page of every user32 function declared below; the structure pages MSG,
    WNDCLASSEXW, CREATESTRUCTW, PAINTSTRUCT, WINDOWPOS, MINMAXINFO, NMHDR, SCROLLINFO, MENUITEMINFOW,
    NONCLIENTMETRICSW, LOGFONTW, DRAWITEMSTRUCT, MEASUREITEMSTRUCT, WINDOWPLACEMENT, MONITORINFO, TRACKMOUSEEVENT, ICONINFO, ACCEL;
    Window Styles, Extended Window Styles, Window Class Styles, Virtual-Key Codes, GetSystemMetrics,
    SystemParametersInfoW, GetSysColor, LoadCursorW/LoadIconW, MessageBox, DPI_AWARENESS_CONTEXT handle pages;
  - gdi32 (wingdi.h): the reference page of every gdi32 function declared below; the structure pages TEXTMETRICW,
    LOGBRUSH, BITMAPINFOHEADER, RGBQUAD, BITMAPINFO, BITMAP, BITMAPFILEHEADER; Ternary Raster Operations, Binary
    Raster Operations, GetDeviceCaps, GetStockObject, LOGFONTW member pages.
  Unions are declared with the member the RTL uses until variant records exist (RTL-833, M2.5).
  POINT/RECT/SIZE are not declared: case-insensitively they would hide the Types functions Point, Rect and Size
  in every program that lists Types before Windows. TPoint, TRect, TSize are the Types records (RTL-600).
  The resource identifiers IDC_* / IDI_* are inline functions: an ordinal-to-pointer cast is not a constant
  expression (LANG-340). }
unit Windows;

interface

uses Types;

const
  kernel32 = 'kernel32.dll';
  ntdll = 'ntdll.dll';
  user32 = 'user32.dll';
  gdi32 = 'gdi32.dll';
  comctl32 = 'comctl32.dll';
  comdlg32 = 'comdlg32.dll';
  shell32 = 'shell32.dll';
  advapi32 = 'advapi32.dll';

{ ---------------------------------------------------------------- base types (winapi.md section 3.1) }
type
  BOOL = LongBool;
  PBOOL = ^BOOL;
  BYTE = System.Byte;
  WORD = System.Word;
  ATOM = Word;
  LANGID = Word;
  SHORT = SmallInt;
  DWORD = Cardinal;
  UINT = Cardinal;
  ULONG = Cardinal;
  COLORREF = Cardinal;
  LCID = Cardinal;
  LCTYPE = Cardinal;
  INT = Integer;
  LONG = Integer;
  NTSTATUS = Integer;
  LONGLONG = Int64;
  ULONGLONG = UInt64;
  LARGE_INTEGER = Int64;
  ULARGE_INTEGER = UInt64;
  UINT_PTR = NativeUInt;
  ULONG_PTR = NativeUInt;
  DWORD_PTR = NativeUInt;
  SIZE_T = NativeUInt;
  WPARAM = NativeUInt;
  INT_PTR = NativeInt;
  LONG_PTR = NativeInt;
  LPARAM = NativeInt;
  LRESULT = NativeInt;
  HANDLE = THandle;
  HWND = type NativeUInt;
  HDC = type NativeUInt;
  HMENU = type NativeUInt;
  HICON = type NativeUInt;
  HCURSOR = type NativeUInt;
  HBRUSH = type NativeUInt;
  HPEN = type NativeUInt;
  HFONT = type NativeUInt;
  HBITMAP = type NativeUInt;
  HRGN = type NativeUInt;
  HGDIOBJ = type NativeUInt;
  HINST = type NativeUInt;
  HMODULE = type NativeUInt;
  HIMAGELIST = type NativeUInt;
  HMONITOR = type NativeUInt;
  HACCEL = type NativeUInt;
  HKEY = type NativeUInt;
  HTREEITEM = type NativeUInt;
  HGLOBAL = type NativeUInt;
  HLOCAL = type NativeUInt;
  HDWP = type NativeUInt;
  DPI_AWARENESS_CONTEXT = type NativeInt;
  LPCWSTR = PWideChar;
  LPWSTR = PWideChar;
  PLPWSTR = ^LPWSTR;
  LPCSTR = PAnsiChar;
  LPSTR = PAnsiChar;
  LPVOID = Pointer;
  PVOID = Pointer;
  WCHAR = WideChar;
  PHANDLE = ^THandle;
  PDWORD = ^DWORD;
  PLONG = ^LONG;
  PULONG_PTR = ^ULONG_PTR;
  PLargeInteger = ^Int64;
  PULargeInteger = ^UInt64;
  PDWORD_PTR = ^DWORD_PTR;

  { the geometry records of Types (RTL-600) }
  TPoint = Types.TPoint;
  PPoint = Types.PPoint;
  TRect = Types.TRect;
  PRect = Types.PRect;
  TSize = Types.TSize;
  PSize = Types.PSize;

  { LPTHREAD_START_ROUTINE }
  TFNThreadStartRoutine = function(lpThreadParameter: Pointer): DWORD; stdcall;
  { WNDPROC, TIMERPROC, WNDENUMPROC, MONITORENUMPROC }
  TFNWndProc = function(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  TFNTimerProc = procedure(hWnd: HWND; uMsg: UINT; idEvent: UINT_PTR; dwTime: DWORD); stdcall;
  TFNWndEnumProc = function(hWnd: HWND; lParam: LPARAM): BOOL; stdcall;
  TFNMonitorEnumProc = function(hMonitor: HMONITOR; hdcMonitor: HDC; lprcMonitor: PRect; dwData: LPARAM): BOOL; stdcall;

  TKeyboardState = array[0..255] of Byte;

{ ---------------------------------------------------------------- records (winapi.md section 4) }
  PSystemTime = ^TSystemTime;
  TSystemTime = record
    wYear: Word;
    wMonth: Word;
    wDayOfWeek: Word;
    wDay: Word;
    wHour: Word;
    wMinute: Word;
    wSecond: Word;
    wMilliseconds: Word;
  end;
  SYSTEMTIME = TSystemTime;

  PFileTime = ^TFileTime;
  TFileTime = record
    dwLowDateTime: DWORD;
    dwHighDateTime: DWORD;
  end;
  FILETIME = TFileTime;

  PWin32FindDataW = ^TWin32FindDataW;
  TWin32FindDataW = record
    dwFileAttributes: DWORD;
    ftCreationTime: TFileTime;
    ftLastAccessTime: TFileTime;
    ftLastWriteTime: TFileTime;
    nFileSizeHigh: DWORD;
    nFileSizeLow: DWORD;
    dwReserved0: DWORD;
    dwReserved1: DWORD;
    cFileName: array[0..259] of WideChar;
    cAlternateFileName: array[0..13] of WideChar;
  end;
  WIN32_FIND_DATAW = TWin32FindDataW;
  TWin32FindData = TWin32FindDataW;
  PWin32FindData = PWin32FindDataW;

  PWin32FileAttributeData = ^TWin32FileAttributeData;
  TWin32FileAttributeData = record
    dwFileAttributes: DWORD;
    ftCreationTime: TFileTime;
    ftLastAccessTime: TFileTime;
    ftLastWriteTime: TFileTime;
    nFileSizeHigh: DWORD;
    nFileSizeLow: DWORD;
  end;
  WIN32_FILE_ATTRIBUTE_DATA = TWin32FileAttributeData;

  { GET_FILEEX_INFO_LEVELS is a C enum (4 bytes) }
  TGetFileExInfoLevels = DWORD;

  PRTLCriticalSection = ^TRTLCriticalSection;
  TRTLCriticalSection = record
    DebugInfo: Pointer;
    LockCount: LONG;
    RecursionCount: LONG;
    OwningThread: THandle;
    LockSemaphore: THandle;
    SpinCount: ULONG_PTR;
  end;
  RTL_CRITICAL_SECTION = TRTLCriticalSection;
  TCriticalSection = TRTLCriticalSection;
  PCriticalSection = PRTLCriticalSection;

  PExceptionRecord = ^TExceptionRecord;
  TExceptionRecord = record
    ExceptionCode: DWORD;
    ExceptionFlags: DWORD;
    ExceptionRecord: PExceptionRecord;
    ExceptionAddress: Pointer;
    NumberParameters: DWORD;
    ExceptionInformation: array[0..14] of ULONG_PTR;
  end;
  EXCEPTION_RECORD = TExceptionRecord;

  PTimeZoneInformation = ^TTimeZoneInformation;
  TTimeZoneInformation = record
    Bias: LONG;
    StandardName: array[0..31] of WideChar;
    StandardDate: TSystemTime;
    StandardBias: LONG;
    DaylightName: array[0..31] of WideChar;
    DaylightDate: TSystemTime;
    DaylightBias: LONG;
  end;
  TIME_ZONE_INFORMATION = TTimeZoneInformation;

  PSecurityAttributes = ^TSecurityAttributes;
  TSecurityAttributes = record
    nLength: DWORD;
    lpSecurityDescriptor: Pointer;
    bInheritHandle: BOOL;
  end;
  SECURITY_ATTRIBUTES = TSecurityAttributes;

  { the union (Offset, OffsetHigh) or Pointer is declared with its Offset/OffsetHigh member (RTL-833) }
  POverlapped = ^TOverlapped;
  TOverlapped = record
    Internal: ULONG_PTR;
    InternalHigh: ULONG_PTR;
    Offset: DWORD;
    OffsetHigh: DWORD;
    hEvent: THandle;
  end;
  OVERLAPPED = TOverlapped;

  { the union dwOemId or (wProcessorArchitecture, wReserved) is declared with its structure member (RTL-833) }
  PSystemInfo = ^TSystemInfo;
  TSystemInfo = record
    wProcessorArchitecture: Word;
    wReserved: Word;
    dwPageSize: DWORD;
    lpMinimumApplicationAddress: Pointer;
    lpMaximumApplicationAddress: Pointer;
    dwActiveProcessorMask: DWORD_PTR;
    dwNumberOfProcessors: DWORD;
    dwProcessorType: DWORD;
    dwAllocationGranularity: DWORD;
    wProcessorLevel: Word;
    wProcessorRevision: Word;
  end;
  SYSTEM_INFO = TSystemInfo;

  POSVersionInfoW = ^TOSVersionInfoW;
  TOSVersionInfoW = record
    dwOSVersionInfoSize: DWORD;
    dwMajorVersion: DWORD;
    dwMinorVersion: DWORD;
    dwBuildNumber: DWORD;
    dwPlatformId: DWORD;
    szCSDVersion: array[0..127] of WideChar;
  end;
  OSVERSIONINFOW = TOSVersionInfoW;
  TOSVersionInfo = TOSVersionInfoW;
  POSVersionInfo = POSVersionInfoW;

  POSVersionInfoExW = ^TOSVersionInfoExW;
  TOSVersionInfoExW = record
    dwOSVersionInfoSize: DWORD;
    dwMajorVersion: DWORD;
    dwMinorVersion: DWORD;
    dwBuildNumber: DWORD;
    dwPlatformId: DWORD;
    szCSDVersion: array[0..127] of WideChar;
    wServicePackMajor: Word;
    wServicePackMinor: Word;
    wSuiteMask: Word;
    wProductType: Byte;
    wReserved: Byte;
  end;
  OSVERSIONINFOEXW = TOSVersionInfoExW;
  TOSVersionInfoEx = TOSVersionInfoExW;
  POSVersionInfoEx = POSVersionInfoExW;

  { user32 records (winuser.h) }
  PMsg = ^TMsg;
  TMsg = record
    hwnd: HWND;
    message: UINT;
    wParam: WPARAM;
    lParam: LPARAM;
    time: DWORD;
    pt: TPoint;
    lPrivate: DWORD;
  end;
  MSG = TMsg;

  PWndClassExW = ^TWndClassExW;
  TWndClassExW = record
    cbSize: UINT;
    style: UINT;
    lpfnWndProc: TFNWndProc;
    cbClsExtra: Integer;
    cbWndExtra: Integer;
    hInstance: HINST;
    hIcon: HICON;
    hCursor: HCURSOR;
    hbrBackground: HBRUSH;
    lpszMenuName: LPCWSTR;
    lpszClassName: LPCWSTR;
    hIconSm: HICON;
  end;
  WNDCLASSEXW = TWndClassExW;
  TWndClassEx = TWndClassExW;
  PWndClassEx = PWndClassExW;

  PCreateStructW = ^TCreateStructW;
  TCreateStructW = record
    lpCreateParams: Pointer;
    hInstance: HINST;
    hMenu: HMENU;
    hwndParent: HWND;
    cy: Integer;
    cx: Integer;
    y: Integer;
    x: Integer;
    style: LONG;
    lpszName: LPCWSTR;
    lpszClass: LPCWSTR;
    dwExStyle: DWORD;
  end;
  CREATESTRUCTW = TCreateStructW;
  TCreateStruct = TCreateStructW;
  PCreateStruct = PCreateStructW;

  PPaintStruct = ^TPaintStruct;
  TPaintStruct = record
    hdc: HDC;
    fErase: BOOL;
    rcPaint: TRect;
    fRestore: BOOL;
    fIncUpdate: BOOL;
    rgbReserved: array[0..31] of Byte;
  end;
  PAINTSTRUCT = TPaintStruct;

  PWindowPos = ^TWindowPos;
  TWindowPos = record
    hwnd: HWND;
    hwndInsertAfter: HWND;
    x: Integer;
    y: Integer;
    cx: Integer;
    cy: Integer;
    flags: UINT;
  end;
  WINDOWPOS = TWindowPos;

  PMinMaxInfo = ^TMinMaxInfo;
  TMinMaxInfo = record
    ptReserved: TPoint;
    ptMaxSize: TPoint;
    ptMaxPosition: TPoint;
    ptMinTrackSize: TPoint;
    ptMaxTrackSize: TPoint;
  end;
  MINMAXINFO = TMinMaxInfo;

  PNMHdr = ^TNMHdr;
  TNMHdr = record
    hwndFrom: HWND;
    idFrom: UINT_PTR;
    code: UINT;
  end;
  NMHDR = TNMHdr;

  PScrollInfo = ^TScrollInfo;
  TScrollInfo = record
    cbSize: UINT;
    fMask: UINT;
    nMin: Integer;
    nMax: Integer;
    nPage: UINT;
    nPos: Integer;
    nTrackPos: Integer;
  end;
  SCROLLINFO = TScrollInfo;

  PMenuItemInfoW = ^TMenuItemInfoW;
  TMenuItemInfoW = record
    cbSize: UINT;
    fMask: UINT;
    fType: UINT;
    fState: UINT;
    wID: UINT;
    hSubMenu: HMENU;
    hbmpChecked: HBITMAP;
    hbmpUnchecked: HBITMAP;
    dwItemData: ULONG_PTR;
    dwTypeData: LPWSTR;
    cch: UINT;
    hbmpItem: HBITMAP;
  end;
  MENUITEMINFOW = TMenuItemInfoW;
  TMenuItemInfo = TMenuItemInfoW;
  PMenuItemInfo = PMenuItemInfoW;

  { LOGFONTW is a gdi32 structure; it is declared here because NONCLIENTMETRICSW embeds it }
  PLogFontW = ^TLogFontW;
  TLogFontW = record
    lfHeight: LONG;
    lfWidth: LONG;
    lfEscapement: LONG;
    lfOrientation: LONG;
    lfWeight: LONG;
    lfItalic: Byte;
    lfUnderline: Byte;
    lfStrikeOut: Byte;
    lfCharSet: Byte;
    lfOutPrecision: Byte;
    lfClipPrecision: Byte;
    lfQuality: Byte;
    lfPitchAndFamily: Byte;
    lfFaceName: array[0..31] of WideChar;
  end;
  LOGFONTW = TLogFontW;
  TLogFont = TLogFontW;
  PLogFont = PLogFontW;

  PNonClientMetricsW = ^TNonClientMetricsW;
  TNonClientMetricsW = record
    cbSize: UINT;
    iBorderWidth: Integer;
    iScrollWidth: Integer;
    iScrollHeight: Integer;
    iCaptionWidth: Integer;
    iCaptionHeight: Integer;
    lfCaptionFont: TLogFontW;
    iSmCaptionWidth: Integer;
    iSmCaptionHeight: Integer;
    lfSmCaptionFont: TLogFontW;
    iMenuWidth: Integer;
    iMenuHeight: Integer;
    lfMenuFont: TLogFontW;
    lfStatusFont: TLogFontW;
    lfMessageFont: TLogFontW;
    iPaddedBorderWidth: Integer;
  end;
  NONCLIENTMETRICSW = TNonClientMetricsW;
  TNonClientMetrics = TNonClientMetricsW;
  PNonClientMetrics = PNonClientMetricsW;

  PDrawItemStruct = ^TDrawItemStruct;
  TDrawItemStruct = record
    CtlType: UINT;
    CtlID: UINT;
    itemID: UINT;
    itemAction: UINT;
    itemState: UINT;
    hwndItem: HWND;
    hDC: HDC;
    rcItem: TRect;
    itemData: ULONG_PTR;
  end;
  DRAWITEMSTRUCT = TDrawItemStruct;

  PMeasureItemStruct = ^TMeasureItemStruct;
  TMeasureItemStruct = record
    CtlType: UINT;
    CtlID: UINT;
    itemID: UINT;
    itemWidth: UINT;
    itemHeight: UINT;
    itemData: ULONG_PTR;
  end;
  MEASUREITEMSTRUCT = TMeasureItemStruct;

  PWindowPlacement = ^TWindowPlacement;
  TWindowPlacement = record
    length: UINT;
    flags: UINT;
    showCmd: UINT;
    ptMinPosition: TPoint;
    ptMaxPosition: TPoint;
    rcNormalPosition: TRect;
  end;
  WINDOWPLACEMENT = TWindowPlacement;

  PMonitorInfo = ^TMonitorInfo;
  TMonitorInfo = record
    cbSize: DWORD;
    rcMonitor: TRect;
    rcWork: TRect;
    dwFlags: DWORD;
  end;
  MONITORINFO = TMonitorInfo;

  PTrackMouseEvent = ^TTrackMouseEvent;
  TTrackMouseEvent = record
    cbSize: DWORD;
    dwFlags: DWORD;
    hwndTrack: HWND;
    dwHoverTime: DWORD;
  end;
  { no TRACKMOUSEEVENT alias: it would be the same identifier as the function TrackMouseEvent }

  PIconInfo = ^TIconInfo;
  TIconInfo = record
    fIcon: BOOL;
    xHotspot: DWORD;
    yHotspot: DWORD;
    hbmMask: HBITMAP;
    hbmColor: HBITMAP;
  end;
  ICONINFO = TIconInfo;

  { ACCEL: natural 2-byte alignment, the padding after fVirt is an explicit member (RTL-831) }
  PAccel = ^TAccel;
  TAccel = packed record
    fVirt: Byte;
    bPadding: Byte;
    key: Word;
    cmd: Word;
  end;
  ACCEL = TAccel;

  { gdi32 records (wingdi.h) }
  PTextMetricW = ^TTextMetricW;
  TTextMetricW = record
    tmHeight: LONG;
    tmAscent: LONG;
    tmDescent: LONG;
    tmInternalLeading: LONG;
    tmExternalLeading: LONG;
    tmAveCharWidth: LONG;
    tmMaxCharWidth: LONG;
    tmWeight: LONG;
    tmOverhang: LONG;
    tmDigitizedAspectX: LONG;
    tmDigitizedAspectY: LONG;
    tmFirstChar: WideChar;
    tmLastChar: WideChar;
    tmDefaultChar: WideChar;
    tmBreakChar: WideChar;
    tmItalic: Byte;
    tmUnderlined: Byte;
    tmStruckOut: Byte;
    tmPitchAndFamily: Byte;
    tmCharSet: Byte;
  end;
  TEXTMETRICW = TTextMetricW;
  TTextMetric = TTextMetricW;
  PTextMetric = PTextMetricW;

  PLogBrush = ^TLogBrush;
  TLogBrush = record
    lbStyle: UINT;
    lbColor: COLORREF;
    lbHatch: ULONG_PTR;
  end;
  LOGBRUSH = TLogBrush;

  PBitmapInfoHeader = ^TBitmapInfoHeader;
  TBitmapInfoHeader = record
    biSize: DWORD;
    biWidth: LONG;
    biHeight: LONG;
    biPlanes: Word;
    biBitCount: Word;
    biCompression: DWORD;
    biSizeImage: DWORD;
    biXPelsPerMeter: LONG;
    biYPelsPerMeter: LONG;
    biClrUsed: DWORD;
    biClrImportant: DWORD;
  end;
  BITMAPINFOHEADER = TBitmapInfoHeader;

  PRGBQuad = ^TRGBQuad;
  TRGBQuad = record
    rgbBlue: Byte;
    rgbGreen: Byte;
    rgbRed: Byte;
    rgbReserved: Byte;
  end;
  RGBQUAD = TRGBQuad;

  { bmiColors is a variable-length array; index it under POINTERMATH or through PRGBQuad }
  PBitmapInfo = ^TBitmapInfo;
  TBitmapInfo = record
    bmiHeader: TBitmapInfoHeader;
    bmiColors: array[0..0] of TRGBQuad;
  end;
  BITMAPINFO = TBitmapInfo;

  { GDI BITMAP, named TBitmapRec so that it does not clash with the graphics class (winapi.md section 4) }
  PBitmapRec = ^TBitmapRec;
  TBitmapRec = record
    bmType: LONG;
    bmWidth: LONG;
    bmHeight: LONG;
    bmWidthBytes: LONG;
    bmPlanes: Word;
    bmBitsPixel: Word;
    bmBits: Pointer;
  end;

  { BITMAPFILEHEADER: 2-byte packing (RTL-831) }
  PBitmapFileHeader = ^TBitmapFileHeader;
  TBitmapFileHeader = packed record
    bfType: Word;
    bfSize: DWORD;
    bfReserved1: Word;
    bfReserved2: Word;
    bfOffBits: DWORD;
  end;
  BITMAPFILEHEADER = TBitmapFileHeader;

  { FONTENUMPROCW: the ENUMLOGFONTEXW / NEWTEXTMETRICEXW arguments start with a LOGFONTW / TEXTMETRICW }
  TFNFontEnumProcW = function(lpelfe: PLogFontW; lpntme: PTextMetricW; FontType: DWORD; lParam: LPARAM): Integer; stdcall;
  TFNFontEnumProc = TFNFontEnumProcW;

{ ---------------------------------------------------------------- constants }
const
  MAX_PATH = 260;

  { COM result codes beyond those of System }
  E_POINTER = HResult($80004003);
  E_ABORT = HResult($80004004);
  E_FAIL = HResult($80004005);
  E_ACCESSDENIED = HResult($80070005);
  E_OUTOFMEMORY = HResult($8007000E);
  E_INVALIDARG = HResult($80070057);

  { handles }
  INVALID_HANDLE_VALUE = THandle(-1);
  STD_INPUT_HANDLE = DWORD(-10);
  STD_OUTPUT_HANDLE = DWORD(-11);
  STD_ERROR_HANDLE = DWORD(-12);

  { System Error Codes }
  ERROR_SUCCESS = 0;
  NO_ERROR = 0;
  ERROR_INVALID_FUNCTION = 1;
  ERROR_FILE_NOT_FOUND = 2;
  ERROR_PATH_NOT_FOUND = 3;
  ERROR_TOO_MANY_OPEN_FILES = 4;
  ERROR_ACCESS_DENIED = 5;
  ERROR_INVALID_HANDLE = 6;
  ERROR_NOT_ENOUGH_MEMORY = 8;
  ERROR_INVALID_DATA = 13;
  ERROR_OUTOFMEMORY = 14;
  ERROR_INVALID_DRIVE = 15;
  ERROR_NO_MORE_FILES = 18;
  ERROR_WRITE_PROTECT = 19;
  ERROR_NOT_READY = 21;
  ERROR_SHARING_VIOLATION = 32;
  ERROR_LOCK_VIOLATION = 33;
  ERROR_HANDLE_EOF = 38;
  ERROR_HANDLE_DISK_FULL = 39;
  ERROR_NOT_SUPPORTED = 50;
  ERROR_FILE_EXISTS = 80;
  ERROR_INVALID_PARAMETER = 87;
  ERROR_BROKEN_PIPE = 109;
  ERROR_DISK_FULL = 112;
  ERROR_INSUFFICIENT_BUFFER = 122;
  ERROR_INVALID_NAME = 123;
  ERROR_DIR_NOT_EMPTY = 145;
  ERROR_ALREADY_EXISTS = 183;
  ERROR_ENVVAR_NOT_FOUND = 203;
  ERROR_MORE_DATA = 234;
  ERROR_NO_MORE_ITEMS = 259;
  ERROR_NOT_OWNER = 288;
  ERROR_OPERATION_ABORTED = 995;
  ERROR_IO_PENDING = 997;
  ERROR_NOACCESS = 998;
  ERROR_INVALID_FLAGS = 1004;
  ERROR_NO_UNICODE_TRANSLATION = 1113;
  ERROR_TIMEOUT = 1460;

  { access rights, sharing, creation }
  GENERIC_READ = DWORD($80000000);
  GENERIC_WRITE = $40000000;
  GENERIC_EXECUTE = $20000000;
  GENERIC_ALL = $10000000;
  FILE_SHARE_READ = $00000001;
  FILE_SHARE_WRITE = $00000002;
  FILE_SHARE_DELETE = $00000004;
  CREATE_NEW = 1;
  CREATE_ALWAYS = 2;
  OPEN_EXISTING = 3;
  OPEN_ALWAYS = 4;
  TRUNCATE_EXISTING = 5;

  { file attributes and flags }
  FILE_ATTRIBUTE_READONLY = $00000001;
  FILE_ATTRIBUTE_HIDDEN = $00000002;
  FILE_ATTRIBUTE_SYSTEM = $00000004;
  FILE_ATTRIBUTE_DIRECTORY = $00000010;
  FILE_ATTRIBUTE_ARCHIVE = $00000020;
  FILE_ATTRIBUTE_DEVICE = $00000040;
  FILE_ATTRIBUTE_NORMAL = $00000080;
  FILE_ATTRIBUTE_TEMPORARY = $00000100;
  FILE_ATTRIBUTE_SPARSE_FILE = $00000200;
  FILE_ATTRIBUTE_REPARSE_POINT = $00000400;
  FILE_ATTRIBUTE_COMPRESSED = $00000800;
  FILE_ATTRIBUTE_OFFLINE = $00001000;
  FILE_ATTRIBUTE_NOT_CONTENT_INDEXED = $00002000;
  FILE_ATTRIBUTE_ENCRYPTED = $00004000;
  INVALID_FILE_ATTRIBUTES = DWORD($FFFFFFFF);
  FILE_FLAG_WRITE_THROUGH = DWORD($80000000);
  FILE_FLAG_OVERLAPPED = $40000000;
  FILE_FLAG_NO_BUFFERING = $20000000;
  FILE_FLAG_RANDOM_ACCESS = $10000000;
  FILE_FLAG_SEQUENTIAL_SCAN = $08000000;
  FILE_FLAG_DELETE_ON_CLOSE = $04000000;
  FILE_FLAG_BACKUP_SEMANTICS = $02000000;

  { SetFilePointerEx move methods }
  FILE_BEGIN = 0;
  FILE_CURRENT = 1;
  FILE_END = 2;

  { GetFileType }
  FILE_TYPE_UNKNOWN = $0000;
  FILE_TYPE_DISK = $0001;
  FILE_TYPE_CHAR = $0002;
  FILE_TYPE_PIPE = $0003;
  FILE_TYPE_REMOTE = $8000;

  { MoveFileExW }
  MOVEFILE_REPLACE_EXISTING = $00000001;
  MOVEFILE_COPY_ALLOWED = $00000002;
  MOVEFILE_DELAY_UNTIL_REBOOT = $00000004;
  MOVEFILE_WRITE_THROUGH = $00000008;

  { GET_FILEEX_INFO_LEVELS }
  GetFileExInfoStandard = 0;
  GetFileExMaxInfoLevel = 1;

  { waits, threads, TLS }
  INFINITE = DWORD($FFFFFFFF);
  WAIT_OBJECT_0 = $00000000;
  WAIT_ABANDONED = $00000080;
  WAIT_ABANDONED_0 = $00000080;
  WAIT_TIMEOUT = $00000102;
  WAIT_FAILED = DWORD($FFFFFFFF);
  MAXIMUM_WAIT_OBJECTS = 64;
  CREATE_SUSPENDED = $00000004;
  STACK_SIZE_PARAM_IS_A_RESERVATION = $00010000;
  STILL_ACTIVE = $00000103;
  TLS_OUT_OF_INDEXES = DWORD($FFFFFFFF);

  { memory }
  MEM_COMMIT = $00001000;
  MEM_RESERVE = $00002000;
  MEM_DECOMMIT = $00004000;
  MEM_RELEASE = $00008000;
  MEM_RESET = $00080000;
  MEM_TOP_DOWN = $00100000;
  PAGE_NOACCESS = $01;
  PAGE_READONLY = $02;
  PAGE_READWRITE = $04;
  PAGE_WRITECOPY = $08;
  PAGE_EXECUTE = $10;
  PAGE_EXECUTE_READ = $20;
  PAGE_EXECUTE_READWRITE = $40;
  PAGE_GUARD = $100;
  HEAP_NO_SERIALIZE = $00000001;
  HEAP_GENERATE_EXCEPTIONS = $00000004;
  HEAP_ZERO_MEMORY = $00000008;
  GMEM_FIXED = $0000;
  GMEM_MOVEABLE = $0002;
  GMEM_ZEROINIT = $0040;
  GHND = GMEM_MOVEABLE or GMEM_ZEROINIT;
  GPTR = GMEM_FIXED or GMEM_ZEROINIT;
  HEAP_REALLOC_IN_PLACE_ONLY = $00000010;

  { FormatMessageW }
  FORMAT_MESSAGE_ALLOCATE_BUFFER = $00000100;
  FORMAT_MESSAGE_IGNORE_INSERTS = $00000200;
  FORMAT_MESSAGE_FROM_STRING = $00000400;
  FORMAT_MESSAGE_FROM_HMODULE = $00000800;
  FORMAT_MESSAGE_FROM_SYSTEM = $00001000;
  FORMAT_MESSAGE_ARGUMENT_ARRAY = $00002000;
  FORMAT_MESSAGE_MAX_WIDTH_MASK = $000000FF;

  { exception codes and flags }
  EXCEPTION_GUARD_PAGE = DWORD($80000001);
  EXCEPTION_DATATYPE_MISALIGNMENT = DWORD($80000002);
  EXCEPTION_BREAKPOINT = DWORD($80000003);
  EXCEPTION_SINGLE_STEP = DWORD($80000004);
  EXCEPTION_ACCESS_VIOLATION = DWORD($C0000005);
  EXCEPTION_IN_PAGE_ERROR = DWORD($C0000006);
  EXCEPTION_INVALID_HANDLE = DWORD($C0000008);
  EXCEPTION_ILLEGAL_INSTRUCTION = DWORD($C000001D);
  EXCEPTION_NONCONTINUABLE_EXCEPTION = DWORD($C0000025);
  EXCEPTION_INVALID_DISPOSITION = DWORD($C0000026);
  EXCEPTION_ARRAY_BOUNDS_EXCEEDED = DWORD($C000008C);
  EXCEPTION_FLT_DENORMAL_OPERAND = DWORD($C000008D);
  EXCEPTION_FLT_DIVIDE_BY_ZERO = DWORD($C000008E);
  EXCEPTION_FLT_INEXACT_RESULT = DWORD($C000008F);
  EXCEPTION_FLT_INVALID_OPERATION = DWORD($C0000090);
  EXCEPTION_FLT_OVERFLOW = DWORD($C0000091);
  EXCEPTION_FLT_STACK_CHECK = DWORD($C0000092);
  EXCEPTION_FLT_UNDERFLOW = DWORD($C0000093);
  EXCEPTION_INT_DIVIDE_BY_ZERO = DWORD($C0000094);
  EXCEPTION_INT_OVERFLOW = DWORD($C0000095);
  EXCEPTION_PRIV_INSTRUCTION = DWORD($C0000096);
  EXCEPTION_STACK_OVERFLOW = DWORD($C00000FD);
  EXCEPTION_NONCONTINUABLE = $00000001;
  EXCEPTION_MAXIMUM_PARAMETERS = 15;
  STATUS_SUCCESS = 0;

  { code pages and conversion flags }
  CP_ACP = 0;
  CP_OEMCP = 1;
  CP_UTF7 = 65000;
  CP_UTF8 = 65001;
  MB_PRECOMPOSED = $00000001;
  MB_ERR_INVALID_CHARS = $00000008;
  WC_ERR_INVALID_CHARS = $00000080;
  WC_NO_BEST_FIT_CHARS = $00000400;

  { locale names, information types and comparison flags }
  LOCALE_NAME_MAX_LENGTH = 85;
  LOCALE_NAME_INVARIANT = '';
  LOCALE_NAME_USER_DEFAULT = nil;
  LOCALE_NAME_SYSTEM_DEFAULT = '!x-sys-default-locale';
  LOCALE_NOUSEROVERRIDE = DWORD($80000000);
  LOCALE_RETURN_NUMBER = $20000000;
  LOCALE_SLIST = $0000000C;
  LOCALE_IMEASURE = $0000000D;
  LOCALE_SDECIMAL = $0000000E;
  LOCALE_STHOUSAND = $0000000F;
  LOCALE_SGROUPING = $00000010;
  LOCALE_IDIGITS = $00000011;
  LOCALE_ILZERO = $00000012;
  LOCALE_SCURRENCY = $00000014;
  LOCALE_SINTLSYMBOL = $00000015;
  LOCALE_SMONDECIMALSEP = $00000016;
  LOCALE_SMONTHOUSANDSEP = $00000017;
  LOCALE_SMONGROUPING = $00000018;
  LOCALE_ICURRDIGITS = $00000019;
  LOCALE_IINTLCURRDIGITS = $0000001A;
  LOCALE_ICURRENCY = $0000001B;
  LOCALE_INEGCURR = $0000001C;
  LOCALE_SDATE = $0000001D;
  LOCALE_STIME = $0000001E;
  LOCALE_SSHORTDATE = $0000001F;
  LOCALE_SLONGDATE = $00000020;
  LOCALE_IDATE = $00000021;
  LOCALE_ILDATE = $00000022;
  LOCALE_ITIME = $00000023;
  LOCALE_ICENTURY = $00000024;
  LOCALE_ITLZERO = $00000025;
  LOCALE_IDAYLZERO = $00000026;
  LOCALE_IMONLZERO = $00000027;
  LOCALE_S1159 = $00000028;
  LOCALE_S2359 = $00000029;
  LOCALE_SDAYNAME1 = $0000002A;
  LOCALE_SDAYNAME7 = $00000030;
  LOCALE_SABBREVDAYNAME1 = $00000031;
  LOCALE_SABBREVDAYNAME7 = $00000037;
  LOCALE_SMONTHNAME1 = $00000038;
  LOCALE_SMONTHNAME12 = $00000043;
  LOCALE_SABBREVMONTHNAME1 = $00000044;
  LOCALE_SABBREVMONTHNAME12 = $0000004F;
  LOCALE_SPOSITIVESIGN = $00000050;
  LOCALE_SNEGATIVESIGN = $00000051;
  LOCALE_SNAME = $0000005C;
  LOCALE_SSHORTTIME = $00000079;
  LOCALE_STIMEFORMAT = $00001003;
  LOCALE_SYEARMONTH = $00001006;
  LOCALE_IFIRSTDAYOFWEEK = $0000100C;
  LOCALE_INEGNUMBER = $00001010;
  NORM_IGNORECASE = $00000001;
  NORM_IGNORENONSPACE = $00000002;
  NORM_IGNORESYMBOLS = $00000004;
  SORT_DIGITSASNUMBERS = $00000008;
  LINGUISTIC_IGNORECASE = $00000010;
  LINGUISTIC_IGNOREDIACRITIC = $00000020;
  SORT_STRINGSORT = $00001000;
  NORM_IGNOREKANATYPE = $00010000;
  NORM_IGNOREWIDTH = $00020000;
  NORM_LINGUISTIC_CASING = $08000000;
  CSTR_LESS_THAN = 1;
  CSTR_EQUAL = 2;
  CSTR_GREATER_THAN = 3;

  { GetTimeZoneInformation }
  TIME_ZONE_ID_UNKNOWN = 0;
  TIME_ZONE_ID_STANDARD = 1;
  TIME_ZONE_ID_DAYLIGHT = 2;
  TIME_ZONE_ID_INVALID = DWORD($FFFFFFFF);

  { SYSTEM_INFO, OSVERSIONINFOW }
  PROCESSOR_ARCHITECTURE_INTEL = 0;
  PROCESSOR_ARCHITECTURE_ARM = 5;
  PROCESSOR_ARCHITECTURE_IA64 = 6;
  PROCESSOR_ARCHITECTURE_AMD64 = 9;
  PROCESSOR_ARCHITECTURE_ARM64 = 12;
  PROCESSOR_ARCHITECTURE_UNKNOWN = $FFFF;
  VER_PLATFORM_WIN32_NT = 2;
  VER_NT_WORKSTATION = 1;
  VER_NT_DOMAIN_CONTROLLER = 2;
  VER_NT_SERVER = 3;

{ ---------------------------------------------------------------- user32 constants (winuser.h) }
  { window styles }
  WS_OVERLAPPED = $00000000;
  WS_POPUP = DWORD($80000000);
  WS_CHILD = $40000000;
  WS_MINIMIZE = $20000000;
  WS_VISIBLE = $10000000;
  WS_DISABLED = $08000000;
  WS_CLIPSIBLINGS = $04000000;
  WS_CLIPCHILDREN = $02000000;
  WS_MAXIMIZE = $01000000;
  WS_CAPTION = $00C00000;
  WS_BORDER = $00800000;
  WS_DLGFRAME = $00400000;
  WS_VSCROLL = $00200000;
  WS_HSCROLL = $00100000;
  WS_SYSMENU = $00080000;
  WS_THICKFRAME = $00040000;
  WS_GROUP = $00020000;
  WS_TABSTOP = $00010000;
  WS_MINIMIZEBOX = $00020000;
  WS_MAXIMIZEBOX = $00010000;
  WS_TILED = WS_OVERLAPPED;
  WS_ICONIC = WS_MINIMIZE;
  WS_SIZEBOX = WS_THICKFRAME;
  WS_CHILDWINDOW = WS_CHILD;
  WS_OVERLAPPEDWINDOW = WS_OVERLAPPED or WS_CAPTION or WS_SYSMENU or WS_THICKFRAME or WS_MINIMIZEBOX or WS_MAXIMIZEBOX;
  WS_TILEDWINDOW = WS_OVERLAPPEDWINDOW;
  WS_POPUPWINDOW = WS_POPUP or WS_BORDER or WS_SYSMENU;

  { extended window styles }
  WS_EX_DLGMODALFRAME = $00000001;
  WS_EX_NOPARENTNOTIFY = $00000004;
  WS_EX_TOPMOST = $00000008;
  WS_EX_ACCEPTFILES = $00000010;
  WS_EX_TRANSPARENT = $00000020;
  WS_EX_MDICHILD = $00000040;
  WS_EX_TOOLWINDOW = $00000080;
  WS_EX_WINDOWEDGE = $00000100;
  WS_EX_CLIENTEDGE = $00000200;
  WS_EX_CONTEXTHELP = $00000400;
  WS_EX_RIGHT = $00001000;
  WS_EX_LEFT = $00000000;
  WS_EX_RTLREADING = $00002000;
  WS_EX_LTRREADING = $00000000;
  WS_EX_LEFTSCROLLBAR = $00004000;
  WS_EX_RIGHTSCROLLBAR = $00000000;
  WS_EX_CONTROLPARENT = $00010000;
  WS_EX_STATICEDGE = $00020000;
  WS_EX_APPWINDOW = $00040000;
  WS_EX_LAYERED = $00080000;
  WS_EX_NOINHERITLAYOUT = $00100000;
  WS_EX_LAYOUTRTL = $00400000;
  WS_EX_COMPOSITED = $02000000;
  WS_EX_NOACTIVATE = $08000000;
  WS_EX_OVERLAPPEDWINDOW = WS_EX_WINDOWEDGE or WS_EX_CLIENTEDGE;
  WS_EX_PALETTEWINDOW = WS_EX_WINDOWEDGE or WS_EX_TOOLWINDOW or WS_EX_TOPMOST;

  { class styles }
  CS_VREDRAW = $0001;
  CS_HREDRAW = $0002;
  CS_DBLCLKS = $0008;
  CS_OWNDC = $0020;
  CS_CLASSDC = $0040;
  CS_PARENTDC = $0080;
  CS_NOCLOSE = $0200;
  CS_SAVEBITS = $0800;
  CS_BYTEALIGNCLIENT = $1000;
  CS_BYTEALIGNWINDOW = $2000;
  CS_GLOBALCLASS = $4000;
  CS_DROPSHADOW = $00020000;

  CW_USEDEFAULT = Integer($80000000);

  { ShowWindow }
  SW_HIDE = 0;
  SW_SHOWNORMAL = 1;
  SW_NORMAL = 1;
  SW_SHOWMINIMIZED = 2;
  SW_SHOWMAXIMIZED = 3;
  SW_MAXIMIZE = 3;
  SW_SHOWNOACTIVATE = 4;
  SW_SHOW = 5;
  SW_MINIMIZE = 6;
  SW_SHOWMINNOACTIVE = 7;
  SW_SHOWNA = 8;
  SW_RESTORE = 9;
  SW_SHOWDEFAULT = 10;
  SW_FORCEMINIMIZE = 11;
  SW_MAX = 11;

  { ScrollWindowEx }
  SW_SCROLLCHILDREN = $0001;
  SW_INVALIDATE = $0002;
  SW_ERASE = $0004;
  SW_SMOOTHSCROLL = $0010;

  { SetWindowPos, DeferWindowPos }
  SWP_NOSIZE = $0001;
  SWP_NOMOVE = $0002;
  SWP_NOZORDER = $0004;
  SWP_NOREDRAW = $0008;
  SWP_NOACTIVATE = $0010;
  SWP_FRAMECHANGED = $0020;
  SWP_DRAWFRAME = $0020;
  SWP_SHOWWINDOW = $0040;
  SWP_HIDEWINDOW = $0080;
  SWP_NOCOPYBITS = $0100;
  SWP_NOOWNERZORDER = $0200;
  SWP_NOREPOSITION = $0200;
  SWP_NOSENDCHANGING = $0400;
  SWP_DEFERERASE = $2000;
  SWP_ASYNCWINDOWPOS = $4000;
  HWND_TOP = HWND(0);
  HWND_BOTTOM = HWND(1);
  HWND_TOPMOST = HWND(-1);
  HWND_NOTOPMOST = HWND(-2);
  HWND_MESSAGE = HWND(-3);
  HWND_DESKTOP = HWND(0);
  HWND_BROADCAST = HWND($FFFF);

  { GetWindowLongPtrW, GetClassLongPtrW }
  GWL_STYLE = -16;
  GWL_EXSTYLE = -20;
  GWL_ID = -12;
  GWLP_WNDPROC = -4;
  GWLP_HINSTANCE = -6;
  GWLP_HWNDPARENT = -8;
  GWLP_ID = -12;
  GWLP_USERDATA = -21;
  GCLP_MENUNAME = -8;
  GCLP_HBRBACKGROUND = -10;
  GCLP_HCURSOR = -12;
  GCLP_HICON = -14;
  GCLP_HMODULE = -16;
  GCLP_WNDPROC = -24;
  GCLP_HICONSM = -34;
  GCL_CBWNDEXTRA = -18;
  GCL_CBCLSEXTRA = -20;
  GCL_STYLE = -26;
  GCW_ATOM = -32;

  { GetWindow, GetAncestor, ChildWindowFromPointEx, WINDOWPLACEMENT }
  GW_HWNDFIRST = 0;
  GW_HWNDLAST = 1;
  GW_HWNDNEXT = 2;
  GW_HWNDPREV = 3;
  GW_OWNER = 4;
  GW_CHILD = 5;
  GW_ENABLEDPOPUP = 6;
  GA_PARENT = 1;
  GA_ROOT = 2;
  GA_ROOTOWNER = 3;
  CWP_ALL = $0000;
  CWP_SKIPINVISIBLE = $0001;
  CWP_SKIPDISABLED = $0002;
  CWP_SKIPTRANSPARENT = $0004;
  WPF_SETMINPOSITION = $0001;
  WPF_RESTORETOMAXIMIZED = $0002;
  WPF_ASYNCWINDOWPLACEMENT = $0004;

  { message queue }
  PM_NOREMOVE = $0000;
  PM_REMOVE = $0001;
  PM_NOYIELD = $0002;
  QS_KEY = $0001;
  QS_MOUSEMOVE = $0002;
  QS_MOUSEBUTTON = $0004;
  QS_POSTMESSAGE = $0008;
  QS_TIMER = $0010;
  QS_PAINT = $0020;
  QS_SENDMESSAGE = $0040;
  QS_HOTKEY = $0080;
  QS_ALLPOSTMESSAGE = $0100;
  QS_RAWINPUT = $0400;
  QS_MOUSE = QS_MOUSEMOVE or QS_MOUSEBUTTON;
  QS_INPUT = QS_MOUSE or QS_KEY or QS_RAWINPUT;
  QS_ALLEVENTS = QS_INPUT or QS_POSTMESSAGE or QS_TIMER or QS_PAINT or QS_HOTKEY;
  QS_ALLINPUT = QS_INPUT or QS_POSTMESSAGE or QS_TIMER or QS_PAINT or QS_HOTKEY or QS_SENDMESSAGE;
  SMTO_NORMAL = $0000;
  SMTO_BLOCK = $0001;
  SMTO_ABORTIFHUNG = $0002;
  SMTO_NOTIMEOUTIFNOTHUNG = $0008;
  SMTO_ERRORONEXIT = $0020;

  { virtual-key codes }
  VK_LBUTTON = $01;
  VK_RBUTTON = $02;
  VK_CANCEL = $03;
  VK_MBUTTON = $04;
  VK_XBUTTON1 = $05;
  VK_XBUTTON2 = $06;
  VK_BACK = $08;
  VK_TAB = $09;
  VK_CLEAR = $0C;
  VK_RETURN = $0D;
  VK_SHIFT = $10;
  VK_CONTROL = $11;
  VK_MENU = $12;
  VK_PAUSE = $13;
  VK_CAPITAL = $14;
  VK_ESCAPE = $1B;
  VK_SPACE = $20;
  VK_PRIOR = $21;
  VK_NEXT = $22;
  VK_END = $23;
  VK_HOME = $24;
  VK_LEFT = $25;
  VK_UP = $26;
  VK_RIGHT = $27;
  VK_DOWN = $28;
  VK_SELECT = $29;
  VK_PRINT = $2A;
  VK_EXECUTE = $2B;
  VK_SNAPSHOT = $2C;
  VK_INSERT = $2D;
  VK_DELETE = $2E;
  VK_HELP = $2F;
  VK_LWIN = $5B;
  VK_RWIN = $5C;
  VK_APPS = $5D;
  VK_SLEEP = $5F;
  VK_NUMPAD0 = $60;
  VK_NUMPAD1 = $61;
  VK_NUMPAD2 = $62;
  VK_NUMPAD3 = $63;
  VK_NUMPAD4 = $64;
  VK_NUMPAD5 = $65;
  VK_NUMPAD6 = $66;
  VK_NUMPAD7 = $67;
  VK_NUMPAD8 = $68;
  VK_NUMPAD9 = $69;
  VK_MULTIPLY = $6A;
  VK_ADD = $6B;
  VK_SEPARATOR = $6C;
  VK_SUBTRACT = $6D;
  VK_DECIMAL = $6E;
  VK_DIVIDE = $6F;
  VK_F1 = $70;
  VK_F2 = $71;
  VK_F3 = $72;
  VK_F4 = $73;
  VK_F5 = $74;
  VK_F6 = $75;
  VK_F7 = $76;
  VK_F8 = $77;
  VK_F9 = $78;
  VK_F10 = $79;
  VK_F11 = $7A;
  VK_F12 = $7B;
  VK_F13 = $7C;
  VK_F14 = $7D;
  VK_F15 = $7E;
  VK_F16 = $7F;
  VK_F17 = $80;
  VK_F18 = $81;
  VK_F19 = $82;
  VK_F20 = $83;
  VK_F21 = $84;
  VK_F22 = $85;
  VK_F23 = $86;
  VK_F24 = $87;
  VK_NUMLOCK = $90;
  VK_SCROLL = $91;
  VK_LSHIFT = $A0;
  VK_RSHIFT = $A1;
  VK_LCONTROL = $A2;
  VK_RCONTROL = $A3;
  VK_LMENU = $A4;
  VK_RMENU = $A5;
  VK_OEM_1 = $BA;
  VK_OEM_PLUS = $BB;
  VK_OEM_COMMA = $BC;
  VK_OEM_MINUS = $BD;
  VK_OEM_PERIOD = $BE;
  VK_OEM_2 = $BF;
  VK_OEM_3 = $C0;
  VK_OEM_4 = $DB;
  VK_OEM_5 = $DC;
  VK_OEM_6 = $DD;
  VK_OEM_7 = $DE;
  MAPVK_VK_TO_VSC = 0;
  MAPVK_VSC_TO_VK = 1;
  MAPVK_VK_TO_CHAR = 2;
  MAPVK_VSC_TO_VK_EX = 3;
  MAPVK_VK_TO_VSC_EX = 4;

  { TrackMouseEvent }
  TME_HOVER = $00000001;
  TME_LEAVE = $00000002;
  TME_NONCLIENT = $00000010;
  TME_QUERY = $40000000;
  TME_CANCEL = DWORD($80000000);
  HOVER_DEFAULT = DWORD($FFFFFFFF);

  { LoadImageW, DrawIconEx }
  IMAGE_BITMAP = 0;
  IMAGE_ICON = 1;
  IMAGE_CURSOR = 2;
  LR_DEFAULTCOLOR = $00000000;
  LR_MONOCHROME = $00000001;
  LR_LOADFROMFILE = $00000010;
  LR_LOADTRANSPARENT = $00000020;
  LR_DEFAULTSIZE = $00000040;
  LR_VGACOLOR = $00000080;
  LR_LOADMAP3DCOLORS = $00001000;
  LR_CREATEDIBSECTION = $00002000;
  LR_SHARED = $00008000;
  DI_MASK = $0001;
  DI_IMAGE = $0002;
  DI_NORMAL = $0003;
  DI_COMPAT = $0004;
  DI_DEFAULTSIZE = $0008;

  { MessageBoxW, MessageBeep }
  MB_OK = $00000000;
  MB_OKCANCEL = $00000001;
  MB_ABORTRETRYIGNORE = $00000002;
  MB_YESNOCANCEL = $00000003;
  MB_YESNO = $00000004;
  MB_RETRYCANCEL = $00000005;
  MB_CANCELTRYCONTINUE = $00000006;
  MB_ICONHAND = $00000010;
  MB_ICONQUESTION = $00000020;
  MB_ICONEXCLAMATION = $00000030;
  MB_ICONASTERISK = $00000040;
  MB_ICONWARNING = MB_ICONEXCLAMATION;
  MB_ICONERROR = MB_ICONHAND;
  MB_ICONINFORMATION = MB_ICONASTERISK;
  MB_ICONSTOP = MB_ICONHAND;
  MB_DEFBUTTON1 = $00000000;
  MB_DEFBUTTON2 = $00000100;
  MB_DEFBUTTON3 = $00000200;
  MB_DEFBUTTON4 = $00000300;
  MB_APPLMODAL = $00000000;
  MB_SYSTEMMODAL = $00001000;
  MB_TASKMODAL = $00002000;
  MB_HELP = $00004000;
  MB_SETFOREGROUND = $00010000;
  MB_DEFAULT_DESKTOP_ONLY = $00020000;
  MB_TOPMOST = $00040000;
  MB_RIGHT = $00080000;
  MB_RTLREADING = $00100000;
  IDOK = 1;
  IDCANCEL = 2;
  IDABORT = 3;
  IDRETRY = 4;
  IDIGNORE = 5;
  IDYES = 6;
  IDNO = 7;
  IDCLOSE = 8;
  IDHELP = 9;
  IDTRYAGAIN = 10;
  IDCONTINUE = 11;

  { menus }
  MF_INSERT = $00000000;
  MF_CHANGE = $00000080;
  MF_APPEND = $00000100;
  MF_DELETE = $00000200;
  MF_REMOVE = $00001000;
  MF_BYCOMMAND = $00000000;
  MF_BYPOSITION = $00000400;
  MF_SEPARATOR = $00000800;
  MF_ENABLED = $00000000;
  MF_GRAYED = $00000001;
  MF_DISABLED = $00000002;
  MF_UNCHECKED = $00000000;
  MF_CHECKED = $00000008;
  MF_USECHECKBITMAPS = $00000200;
  MF_STRING = $00000000;
  MF_BITMAP = $00000004;
  MF_OWNERDRAW = $00000100;
  MF_POPUP = $00000010;
  MF_MENUBARBREAK = $00000020;
  MF_MENUBREAK = $00000040;
  MF_UNHILITE = $00000000;
  MF_HILITE = $00000080;
  MF_DEFAULT = $00001000;
  MF_RIGHTJUSTIFY = $00004000;
  MFT_STRING = MF_STRING;
  MFT_BITMAP = MF_BITMAP;
  MFT_MENUBARBREAK = MF_MENUBARBREAK;
  MFT_MENUBREAK = MF_MENUBREAK;
  MFT_OWNERDRAW = MF_OWNERDRAW;
  MFT_RADIOCHECK = $00000200;
  MFT_SEPARATOR = MF_SEPARATOR;
  MFT_RIGHTORDER = $00002000;
  MFT_RIGHTJUSTIFY = MF_RIGHTJUSTIFY;
  MFS_GRAYED = $00000003;
  MFS_DISABLED = MFS_GRAYED;
  MFS_CHECKED = MF_CHECKED;
  MFS_HILITE = MF_HILITE;
  MFS_ENABLED = MF_ENABLED;
  MFS_UNCHECKED = MF_UNCHECKED;
  MFS_UNHILITE = MF_UNHILITE;
  MFS_DEFAULT = MF_DEFAULT;
  MIIM_STATE = $00000001;
  MIIM_ID = $00000002;
  MIIM_SUBMENU = $00000004;
  MIIM_CHECKMARKS = $00000008;
  MIIM_TYPE = $00000010;
  MIIM_DATA = $00000020;
  MIIM_STRING = $00000040;
  MIIM_BITMAP = $00000080;
  MIIM_FTYPE = $00000100;
  TPM_LEFTBUTTON = $0000;
  TPM_RIGHTBUTTON = $0002;
  TPM_LEFTALIGN = $0000;
  TPM_CENTERALIGN = $0004;
  TPM_RIGHTALIGN = $0008;
  TPM_TOPALIGN = $0000;
  TPM_VCENTERALIGN = $0010;
  TPM_BOTTOMALIGN = $0020;
  TPM_HORIZONTAL = $0000;
  TPM_VERTICAL = $0040;
  TPM_NONOTIFY = $0080;
  TPM_RETURNCMD = $0100;
  TPM_RECURSE = $0001;

  { ACCEL.fVirt }
  FVIRTKEY = $01;
  FNOINVERT = $02;
  FSHIFT = $04;
  FCONTROL = $08;
  FALT = $10;

  { GetSysColor }
  COLOR_SCROLLBAR = 0;
  COLOR_BACKGROUND = 1;
  COLOR_DESKTOP = 1;
  COLOR_ACTIVECAPTION = 2;
  COLOR_INACTIVECAPTION = 3;
  COLOR_MENU = 4;
  COLOR_WINDOW = 5;
  COLOR_WINDOWFRAME = 6;
  COLOR_MENUTEXT = 7;
  COLOR_WINDOWTEXT = 8;
  COLOR_CAPTIONTEXT = 9;
  COLOR_ACTIVEBORDER = 10;
  COLOR_INACTIVEBORDER = 11;
  COLOR_APPWORKSPACE = 12;
  COLOR_HIGHLIGHT = 13;
  COLOR_HIGHLIGHTTEXT = 14;
  COLOR_BTNFACE = 15;
  COLOR_3DFACE = 15;
  COLOR_BTNSHADOW = 16;
  COLOR_3DSHADOW = 16;
  COLOR_GRAYTEXT = 17;
  COLOR_BTNTEXT = 18;
  COLOR_INACTIVECAPTIONTEXT = 19;
  COLOR_BTNHIGHLIGHT = 20;
  COLOR_BTNHILIGHT = 20;
  COLOR_3DHIGHLIGHT = 20;
  COLOR_3DHILIGHT = 20;
  COLOR_3DDKSHADOW = 21;
  COLOR_3DLIGHT = 22;
  COLOR_INFOTEXT = 23;
  COLOR_INFOBK = 24;
  COLOR_HOTLIGHT = 26;
  COLOR_GRADIENTACTIVECAPTION = 27;
  COLOR_GRADIENTINACTIVECAPTION = 28;
  COLOR_MENUHILIGHT = 29;
  COLOR_MENUBAR = 30;

  { GetSystemMetrics }
  SM_CXSCREEN = 0;
  SM_CYSCREEN = 1;
  SM_CXVSCROLL = 2;
  SM_CYHSCROLL = 3;
  SM_CYCAPTION = 4;
  SM_CXBORDER = 5;
  SM_CYBORDER = 6;
  SM_CXDLGFRAME = 7;
  SM_CXFIXEDFRAME = 7;
  SM_CYDLGFRAME = 8;
  SM_CYFIXEDFRAME = 8;
  SM_CYVTHUMB = 9;
  SM_CXHTHUMB = 10;
  SM_CXICON = 11;
  SM_CYICON = 12;
  SM_CXCURSOR = 13;
  SM_CYCURSOR = 14;
  SM_CYMENU = 15;
  SM_CXFULLSCREEN = 16;
  SM_CYFULLSCREEN = 17;
  SM_MOUSEPRESENT = 19;
  SM_CYVSCROLL = 20;
  SM_CXHSCROLL = 21;
  SM_DEBUG = 22;
  SM_SWAPBUTTON = 23;
  SM_CXMIN = 28;
  SM_CYMIN = 29;
  SM_CXSIZE = 30;
  SM_CYSIZE = 31;
  SM_CXFRAME = 32;
  SM_CXSIZEFRAME = 32;
  SM_CYFRAME = 33;
  SM_CYSIZEFRAME = 33;
  SM_CXMINTRACK = 34;
  SM_CYMINTRACK = 35;
  SM_CXDOUBLECLK = 36;
  SM_CYDOUBLECLK = 37;
  SM_CXICONSPACING = 38;
  SM_CYICONSPACING = 39;
  SM_MENUDROPALIGNMENT = 40;
  SM_CMOUSEBUTTONS = 43;
  SM_CXEDGE = 45;
  SM_CYEDGE = 46;
  SM_CXSMICON = 49;
  SM_CYSMICON = 50;
  SM_CYSMCAPTION = 51;
  SM_CXSMSIZE = 52;
  SM_CYSMSIZE = 53;
  SM_CXMENUSIZE = 54;
  SM_CYMENUSIZE = 55;
  SM_CXMAXTRACK = 59;
  SM_CYMAXTRACK = 60;
  SM_CXMAXIMIZED = 61;
  SM_CYMAXIMIZED = 62;
  SM_CXMENUCHECK = 71;
  SM_CYMENUCHECK = 72;
  SM_MOUSEWHEELPRESENT = 75;
  SM_XVIRTUALSCREEN = 76;
  SM_YVIRTUALSCREEN = 77;
  SM_CXVIRTUALSCREEN = 78;
  SM_CYVIRTUALSCREEN = 79;
  SM_CMONITORS = 80;
  SM_CXFOCUSBORDER = 83;
  SM_CYFOCUSBORDER = 84;
  SM_CXPADDEDBORDER = 92;
  SM_REMOTESESSION = $1000;

  { SystemParametersInfoW }
  SPI_GETICONTITLELOGFONT = $001F;
  SPI_GETDRAGFULLWINDOWS = $0026;
  SPI_GETNONCLIENTMETRICS = $0029;
  SPI_SETNONCLIENTMETRICS = $002A;
  SPI_GETWORKAREA = $0030;
  SPI_GETHIGHCONTRAST = $0042;
  SPI_GETFONTSMOOTHING = $004A;
  SPI_GETMOUSEHOVERWIDTH = $0062;
  SPI_GETMOUSEHOVERHEIGHT = $0064;
  SPI_GETMOUSEHOVERTIME = $0066;
  SPI_GETWHEELSCROLLLINES = $0068;
  SPI_GETMENUSHOWDELAY = $006A;
  SPI_GETWHEELSCROLLCHARS = $006C;
  SPI_GETKEYBOARDCUES = $100A;
  SPI_GETFLATMENU = $1022;
  SPI_GETCARETWIDTH = $2006;
  SPIF_UPDATEINIFILE = $0001;
  SPIF_SENDCHANGE = $0002;
  SPIF_SENDWININICHANGE = $0002;

  { monitors and DPI }
  MONITOR_DEFAULTTONULL = $00000000;
  MONITOR_DEFAULTTOPRIMARY = $00000001;
  MONITOR_DEFAULTTONEAREST = $00000002;
  MONITORINFOF_PRIMARY = $00000001;
  USER_DEFAULT_SCREEN_DPI = 96;
  DPI_AWARENESS_CONTEXT_UNAWARE = DPI_AWARENESS_CONTEXT(-1);
  DPI_AWARENESS_CONTEXT_SYSTEM_AWARE = DPI_AWARENESS_CONTEXT(-2);
  DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE = DPI_AWARENESS_CONTEXT(-3);
  DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = DPI_AWARENESS_CONTEXT(-4);
  DPI_AWARENESS_CONTEXT_UNAWARE_GDISCALED = DPI_AWARENESS_CONTEXT(-5);

  { scroll bars }
  SB_HORZ = 0;
  SB_VERT = 1;
  SB_CTL = 2;
  SB_BOTH = 3;
  SB_LINEUP = 0;
  SB_LINELEFT = 0;
  SB_LINEDOWN = 1;
  SB_LINERIGHT = 1;
  SB_PAGEUP = 2;
  SB_PAGELEFT = 2;
  SB_PAGEDOWN = 3;
  SB_PAGERIGHT = 3;
  SB_THUMBPOSITION = 4;
  SB_THUMBTRACK = 5;
  SB_TOP = 6;
  SB_LEFT = 6;
  SB_BOTTOM = 7;
  SB_RIGHT = 7;
  SB_ENDSCROLL = 8;
  SIF_RANGE = $0001;
  SIF_PAGE = $0002;
  SIF_POS = $0004;
  SIF_DISABLENOSCROLL = $0008;
  SIF_TRACKPOS = $0010;
  SIF_ALL = SIF_RANGE or SIF_PAGE or SIF_POS or SIF_TRACKPOS;
  ESB_ENABLE_BOTH = $0000;
  ESB_DISABLE_BOTH = $0003;
  ESB_DISABLE_LEFT = $0001;
  ESB_DISABLE_RIGHT = $0002;
  ESB_DISABLE_UP = $0001;
  ESB_DISABLE_DOWN = $0002;
  ESB_DISABLE_LTUP = ESB_DISABLE_LEFT;
  ESB_DISABLE_RTDN = ESB_DISABLE_RIGHT;

  { DrawTextW }
  DT_TOP = $00000000;
  DT_LEFT = $00000000;
  DT_CENTER = $00000001;
  DT_RIGHT = $00000002;
  DT_VCENTER = $00000004;
  DT_BOTTOM = $00000008;
  DT_WORDBREAK = $00000010;
  DT_SINGLELINE = $00000020;
  DT_EXPANDTABS = $00000040;
  DT_TABSTOP = $00000080;
  DT_NOCLIP = $00000100;
  DT_EXTERNALLEADING = $00000200;
  DT_CALCRECT = $00000400;
  DT_NOPREFIX = $00000800;
  DT_INTERNAL = $00001000;
  DT_EDITCONTROL = $00002000;
  DT_PATH_ELLIPSIS = $00004000;
  DT_END_ELLIPSIS = $00008000;
  DT_MODIFYSTRING = $00010000;
  DT_RTLREADING = $00020000;
  DT_WORD_ELLIPSIS = $00040000;
  DT_NOFULLWIDTHCHARBREAK = $00080000;
  DT_HIDEPREFIX = $00100000;
  DT_PREFIXONLY = $00200000;

  { DrawEdge }
  BDR_RAISEDOUTER = $0001;
  BDR_SUNKENOUTER = $0002;
  BDR_RAISEDINNER = $0004;
  BDR_SUNKENINNER = $0008;
  BDR_OUTER = BDR_RAISEDOUTER or BDR_SUNKENOUTER;
  BDR_INNER = BDR_RAISEDINNER or BDR_SUNKENINNER;
  BDR_RAISED = BDR_RAISEDOUTER or BDR_RAISEDINNER;
  BDR_SUNKEN = BDR_SUNKENOUTER or BDR_SUNKENINNER;
  EDGE_RAISED = BDR_RAISEDOUTER or BDR_RAISEDINNER;
  EDGE_SUNKEN = BDR_SUNKENOUTER or BDR_SUNKENINNER;
  EDGE_ETCHED = BDR_SUNKENOUTER or BDR_RAISEDINNER;
  EDGE_BUMP = BDR_RAISEDOUTER or BDR_SUNKENINNER;
  BF_LEFT = $0001;
  BF_TOP = $0002;
  BF_RIGHT = $0004;
  BF_BOTTOM = $0008;
  BF_TOPLEFT = BF_TOP or BF_LEFT;
  BF_TOPRIGHT = BF_TOP or BF_RIGHT;
  BF_BOTTOMLEFT = BF_BOTTOM or BF_LEFT;
  BF_BOTTOMRIGHT = BF_BOTTOM or BF_RIGHT;
  BF_RECT = BF_LEFT or BF_TOP or BF_RIGHT or BF_BOTTOM;
  BF_DIAGONAL = $0010;
  BF_MIDDLE = $0800;
  BF_SOFT = $1000;
  BF_ADJUST = $2000;
  BF_FLAT = $4000;
  BF_MONO = $8000;

  { DrawFrameControl }
  DFC_CAPTION = 1;
  DFC_MENU = 2;
  DFC_SCROLL = 3;
  DFC_BUTTON = 4;
  DFC_POPUPMENU = 5;
  DFCS_CAPTIONCLOSE = $0000;
  DFCS_CAPTIONMIN = $0001;
  DFCS_CAPTIONMAX = $0002;
  DFCS_CAPTIONRESTORE = $0003;
  DFCS_CAPTIONHELP = $0004;
  DFCS_MENUARROW = $0000;
  DFCS_MENUCHECK = $0001;
  DFCS_MENUBULLET = $0002;
  DFCS_SCROLLUP = $0000;
  DFCS_SCROLLDOWN = $0001;
  DFCS_SCROLLLEFT = $0002;
  DFCS_SCROLLRIGHT = $0003;
  DFCS_SCROLLCOMBOBOX = $0005;
  DFCS_SCROLLSIZEGRIP = $0008;
  DFCS_BUTTONCHECK = $0000;
  DFCS_BUTTONRADIOIMAGE = $0001;
  DFCS_BUTTONRADIOMASK = $0002;
  DFCS_BUTTONRADIO = $0004;
  DFCS_BUTTON3STATE = $0008;
  DFCS_BUTTONPUSH = $0010;
  DFCS_INACTIVE = $0100;
  DFCS_PUSHED = $0200;
  DFCS_CHECKED = $0400;
  DFCS_TRANSPARENT = $0800;
  DFCS_HOT = $1000;
  DFCS_ADJUSTRECT = $2000;
  DFCS_FLAT = $4000;
  DFCS_MONO = $8000;

  { RedrawWindow }
  RDW_INVALIDATE = $0001;
  RDW_INTERNALPAINT = $0002;
  RDW_ERASE = $0004;
  RDW_VALIDATE = $0008;
  RDW_NOINTERNALPAINT = $0010;
  RDW_NOERASE = $0020;
  RDW_NOCHILDREN = $0040;
  RDW_ALLCHILDREN = $0080;
  RDW_UPDATENOW = $0100;
  RDW_ERASENOW = $0200;
  RDW_FRAME = $0400;
  RDW_NOFRAME = $0800;

  { DRAWITEMSTRUCT }
  ODT_MENU = 1;
  ODT_LISTBOX = 2;
  ODT_COMBOBOX = 3;
  ODT_BUTTON = 4;
  ODT_STATIC = 5;
  ODA_DRAWENTIRE = $0001;
  ODA_SELECT = $0002;
  ODA_FOCUS = $0004;
  ODS_SELECTED = $0001;
  ODS_GRAYED = $0002;
  ODS_DISABLED = $0004;
  ODS_CHECKED = $0008;
  ODS_FOCUS = $0010;
  ODS_DEFAULT = $0020;
  ODS_HOTLIGHT = $0040;
  ODS_INACTIVE = $0080;
  ODS_NOACCEL = $0100;
  ODS_NOFOCUSRECT = $0200;
  ODS_COMBOBOXEDIT = $1000;

{ ---------------------------------------------------------------- gdi32 constants (wingdi.h) }
  CLR_INVALID = DWORD($FFFFFFFF);
  GDI_ERROR = DWORD($FFFFFFFF);

  { GetStockObject }
  WHITE_BRUSH = 0;
  LTGRAY_BRUSH = 1;
  GRAY_BRUSH = 2;
  DKGRAY_BRUSH = 3;
  BLACK_BRUSH = 4;
  NULL_BRUSH = 5;
  HOLLOW_BRUSH = NULL_BRUSH;
  WHITE_PEN = 6;
  BLACK_PEN = 7;
  NULL_PEN = 8;
  OEM_FIXED_FONT = 10;
  ANSI_FIXED_FONT = 11;
  ANSI_VAR_FONT = 12;
  SYSTEM_FONT = 13;
  DEVICE_DEFAULT_FONT = 14;
  DEFAULT_PALETTE = 15;
  SYSTEM_FIXED_FONT = 16;
  DEFAULT_GUI_FONT = 17;
  DC_BRUSH = 18;
  DC_PEN = 19;

  { GetCurrentObject }
  OBJ_PEN = 1;
  OBJ_BRUSH = 2;
  OBJ_DC = 3;
  OBJ_METADC = 4;
  OBJ_PAL = 5;
  OBJ_FONT = 6;
  OBJ_BITMAP = 7;
  OBJ_REGION = 8;
  OBJ_METAFILE = 9;
  OBJ_MEMDC = 10;
  OBJ_EXTPEN = 11;
  OBJ_ENHMETADC = 12;
  OBJ_ENHMETAFILE = 13;
  OBJ_COLORSPACE = 14;

  { pens, brushes }
  PS_SOLID = 0;
  PS_DASH = 1;
  PS_DOT = 2;
  PS_DASHDOT = 3;
  PS_DASHDOTDOT = 4;
  PS_NULL = 5;
  PS_INSIDEFRAME = 6;
  BS_SOLID = 0;
  BS_NULL = 1;
  BS_HOLLOW = BS_NULL;
  BS_HATCHED = 2;
  BS_PATTERN = 3;
  HS_HORIZONTAL = 0;
  HS_VERTICAL = 1;
  HS_FDIAGONAL = 2;
  HS_BDIAGONAL = 3;
  HS_CROSS = 4;
  HS_DIAGCROSS = 5;

  { raster operations }
  SRCCOPY = DWORD($00CC0020);
  SRCPAINT = DWORD($00EE0086);
  SRCAND = DWORD($008800C6);
  SRCINVERT = DWORD($00660046);
  SRCERASE = DWORD($00440328);
  NOTSRCCOPY = DWORD($00330008);
  NOTSRCERASE = DWORD($001100A6);
  MERGECOPY = DWORD($00C000CA);
  MERGEPAINT = DWORD($00BB0226);
  PATCOPY = DWORD($00F00021);
  PATPAINT = DWORD($00FB0A09);
  PATINVERT = DWORD($005A0049);
  DSTINVERT = DWORD($00550009);
  BLACKNESS = DWORD($00000042);
  WHITENESS = DWORD($00FF0062);
  R2_BLACK = 1;
  R2_NOTMERGEPEN = 2;
  R2_MASKNOTPEN = 3;
  R2_NOTCOPYPEN = 4;
  R2_MASKPENNOT = 5;
  R2_NOT = 6;
  R2_XORPEN = 7;
  R2_NOTMASKPEN = 8;
  R2_MASKPEN = 9;
  R2_NOTXORPEN = 10;
  R2_NOP = 11;
  R2_MERGENOTPEN = 12;
  R2_COPYPEN = 13;
  R2_MERGEPENNOT = 14;
  R2_MERGEPEN = 15;
  R2_WHITE = 16;

  { background, stretch and text modes }
  TRANSPARENT = 1;
  OPAQUE = 2;
  BLACKONWHITE = 1;
  WHITEONBLACK = 2;
  COLORONCOLOR = 3;
  HALFTONE = 4;
  STRETCH_ANDSCANS = BLACKONWHITE;
  STRETCH_ORSCANS = WHITEONBLACK;
  STRETCH_DELETESCANS = COLORONCOLOR;
  STRETCH_HALFTONE = HALFTONE;
  TA_NOUPDATECP = 0;
  TA_UPDATECP = 1;
  TA_LEFT = 0;
  TA_RIGHT = 2;
  TA_CENTER = 6;
  TA_TOP = 0;
  TA_BOTTOM = 8;
  TA_BASELINE = 24;
  TA_RTLREADING = 256;
  ETO_OPAQUE = $0002;
  ETO_CLIPPED = $0004;
  ETO_GLYPH_INDEX = $0010;
  ETO_RTLREADING = $0080;
  ETO_IGNORELANGUAGE = $1000;

  { region results }
  ERROR = 0;
  RGN_ERROR = ERROR;
  NULLREGION = 1;
  SIMPLEREGION = 2;
  COMPLEXREGION = 3;

  { DIBs }
  DIB_RGB_COLORS = 0;
  DIB_PAL_COLORS = 1;
  BI_RGB = 0;
  BI_RLE8 = 1;
  BI_RLE4 = 2;
  BI_BITFIELDS = 3;

  { GetDeviceCaps }
  DRIVERVERSION = 0;
  TECHNOLOGY = 2;
  HORZSIZE = 4;
  VERTSIZE = 6;
  HORZRES = 8;
  VERTRES = 10;
  BITSPIXEL = 12;
  PLANES = 14;
  NUMBRUSHES = 16;
  NUMPENS = 18;
  NUMFONTS = 22;
  NUMCOLORS = 24;
  RASTERCAPS = 38;
  LOGPIXELSX = 88;
  LOGPIXELSY = 90;
  SIZEPALETTE = 104;

  { fonts }
  LF_FACESIZE = 32;
  FW_DONTCARE = 0;
  FW_THIN = 100;
  FW_EXTRALIGHT = 200;
  FW_LIGHT = 300;
  FW_NORMAL = 400;
  FW_REGULAR = 400;
  FW_MEDIUM = 500;
  FW_SEMIBOLD = 600;
  FW_DEMIBOLD = 600;
  FW_BOLD = 700;
  FW_EXTRABOLD = 800;
  FW_HEAVY = 900;
  FW_BLACK = 900;
  ANSI_CHARSET = 0;
  DEFAULT_CHARSET = 1;
  SYMBOL_CHARSET = 2;
  SHIFTJIS_CHARSET = 128;
  HANGEUL_CHARSET = 129;
  GB2312_CHARSET = 134;
  CHINESEBIG5_CHARSET = 136;
  GREEK_CHARSET = 161;
  TURKISH_CHARSET = 162;
  HEBREW_CHARSET = 177;
  ARABIC_CHARSET = 178;
  BALTIC_CHARSET = 186;
  RUSSIAN_CHARSET = 204;
  THAI_CHARSET = 222;
  EASTEUROPE_CHARSET = 238;
  OEM_CHARSET = 255;
  OUT_DEFAULT_PRECIS = 0;
  OUT_STRING_PRECIS = 1;
  OUT_CHARACTER_PRECIS = 2;
  OUT_STROKE_PRECIS = 3;
  OUT_TT_PRECIS = 4;
  OUT_DEVICE_PRECIS = 5;
  OUT_RASTER_PRECIS = 6;
  OUT_TT_ONLY_PRECIS = 7;
  OUT_OUTLINE_PRECIS = 8;
  CLIP_DEFAULT_PRECIS = 0;
  CLIP_CHARACTER_PRECIS = 1;
  CLIP_STROKE_PRECIS = 2;
  DEFAULT_QUALITY = 0;
  DRAFT_QUALITY = 1;
  PROOF_QUALITY = 2;
  NONANTIALIASED_QUALITY = 3;
  ANTIALIASED_QUALITY = 4;
  CLEARTYPE_QUALITY = 5;
  DEFAULT_PITCH = 0;
  FIXED_PITCH = 1;
  VARIABLE_PITCH = 2;
  FF_DONTCARE = $00;
  FF_ROMAN = $10;
  FF_SWISS = $20;
  FF_MODERN = $30;
  FF_SCRIPT = $40;
  FF_DECORATIVE = $50;
  RASTER_FONTTYPE = $0001;
  DEVICE_FONTTYPE = $0002;
  TRUETYPE_FONTTYPE = $0004;

{ ---------------------------------------------------------------- macros (RTL-825) }
function MakeWord(A, B: Byte): Word; inline;
function MakeLong(Lo, Hi: Word): Cardinal; inline;
function LoWord(L: NativeUInt): Word; inline;
function HiWord(L: NativeUInt): Word; inline;
function LoByte(W: Word): Byte; inline;
function HiByte(W: Word): Byte; inline;
function MakeWParam(Lo, Hi: Word): WPARAM; inline;
function MakeLParam(Lo, Hi: Word): LPARAM; inline;
function MakeLResult(Lo, Hi: Word): LRESULT; inline;
function RGB(R, G, B: Byte): COLORREF; inline;
function GetRValue(RGB: COLORREF): Byte; inline;
function GetGValue(RGB: COLORREF): Byte; inline;
function GetBValue(RGB: COLORREF): Byte; inline;
function MakeIntResource(ID: Word): PWideChar; inline;
function Succeeded(hr: HRESULT): Boolean; inline;
function Failed(hr: HRESULT): Boolean; inline;
function GET_X_LPARAM(lp: LPARAM): Integer; inline;
function GET_Y_LPARAM(lp: LPARAM): Integer; inline;
function GET_WHEEL_DELTA_WPARAM(wp: WPARAM): SmallInt; inline;

{ ---------------------------------------------------------------- kernel32: process / module }
procedure ExitProcess(uExitCode: UINT); stdcall; external kernel32 name 'ExitProcess';
function GetCommandLineW: LPWSTR; stdcall; external kernel32 name 'GetCommandLineW';
function GetCommandLine: LPWSTR; stdcall; external kernel32 name 'GetCommandLineW';
function GetModuleHandleW(lpModuleName: LPCWSTR): HMODULE; stdcall; external kernel32 name 'GetModuleHandleW';
function GetModuleHandle(lpModuleName: LPCWSTR): HMODULE; stdcall; external kernel32 name 'GetModuleHandleW';
function GetModuleFileNameW(hModule: HMODULE; lpFilename: LPWSTR; nSize: DWORD): DWORD; stdcall;
  external kernel32 name 'GetModuleFileNameW';
function GetModuleFileName(hModule: HMODULE; lpFilename: LPWSTR; nSize: DWORD): DWORD; stdcall;
  external kernel32 name 'GetModuleFileNameW';
function GetCurrentProcess: THandle; stdcall; external kernel32 name 'GetCurrentProcess';
function GetCurrentProcessId: DWORD; stdcall; external kernel32 name 'GetCurrentProcessId';
function GetEnvironmentVariableW(lpName: LPCWSTR; lpBuffer: LPWSTR; nSize: DWORD): DWORD; stdcall;
  external kernel32 name 'GetEnvironmentVariableW';
function GetEnvironmentVariable(lpName: LPCWSTR; lpBuffer: LPWSTR; nSize: DWORD): DWORD; stdcall;
  external kernel32 name 'GetEnvironmentVariableW';
function SetEnvironmentVariableW(lpName, lpValue: LPCWSTR): BOOL; stdcall; external kernel32 name 'SetEnvironmentVariableW';
function SetEnvironmentVariable(lpName, lpValue: LPCWSTR): BOOL; stdcall; external kernel32 name 'SetEnvironmentVariableW';
function IsDebuggerPresent: BOOL; stdcall; external kernel32 name 'IsDebuggerPresent';
procedure OutputDebugStringW(lpOutputString: LPCWSTR); stdcall; external kernel32 name 'OutputDebugStringW';
procedure OutputDebugString(lpOutputString: LPCWSTR); stdcall; external kernel32 name 'OutputDebugStringW';
procedure GetSystemInfo(var lpSystemInfo: TSystemInfo); stdcall; external kernel32 name 'GetSystemInfo';
function MulDiv(nNumber, nNumerator, nDenominator: Integer): Integer; stdcall; external kernel32 name 'MulDiv';

{ ---------------------------------------------------------------- kernel32: heap / memory }
function GetProcessHeap: THandle; stdcall; external kernel32 name 'GetProcessHeap';
function HeapAlloc(hHeap: THandle; dwFlags: DWORD; dwBytes: SIZE_T): Pointer; stdcall; external kernel32 name 'HeapAlloc';
function HeapFree(hHeap: THandle; dwFlags: DWORD; lpMem: Pointer): BOOL; stdcall; external kernel32 name 'HeapFree';
function HeapReAlloc(hHeap: THandle; dwFlags: DWORD; lpMem: Pointer; dwBytes: SIZE_T): Pointer; stdcall;
  external kernel32 name 'HeapReAlloc';
function HeapSize(hHeap: THandle; dwFlags: DWORD; lpMem: Pointer): SIZE_T; stdcall; external kernel32 name 'HeapSize';
function VirtualAlloc(lpAddress: Pointer; dwSize: SIZE_T; flAllocationType, flProtect: DWORD): Pointer; stdcall;
  external kernel32 name 'VirtualAlloc';
function VirtualFree(lpAddress: Pointer; dwSize: SIZE_T; dwFreeType: DWORD): BOOL; stdcall;
  external kernel32 name 'VirtualFree';
function VirtualProtect(lpAddress: Pointer; dwSize: SIZE_T; flNewProtect: DWORD; var lpflOldProtect: DWORD): BOOL; stdcall;
  external kernel32 name 'VirtualProtect';
function FlushInstructionCache(hProcess: THandle; lpBaseAddress: Pointer; dwSize: SIZE_T): BOOL; stdcall;
  external kernel32 name 'FlushInstructionCache';
function LocalFree(hMem: HLOCAL): HLOCAL; stdcall; external kernel32 name 'LocalFree';
function GlobalAlloc(uFlags: UINT; dwBytes: SIZE_T): HGLOBAL; stdcall; external kernel32 name 'GlobalAlloc';
function GlobalLock(hMem: HGLOBAL): Pointer; stdcall; external kernel32 name 'GlobalLock';
function GlobalUnlock(hMem: HGLOBAL): BOOL; stdcall; external kernel32 name 'GlobalUnlock';
function GlobalFree(hMem: HGLOBAL): HGLOBAL; stdcall; external kernel32 name 'GlobalFree';

{ ---------------------------------------------------------------- kernel32: errors / exceptions }
function GetLastError: DWORD; stdcall; external kernel32 name 'GetLastError';
procedure SetLastError(dwErrCode: DWORD); stdcall; external kernel32 name 'SetLastError';
function FormatMessageW(dwFlags: DWORD; lpSource: Pointer; dwMessageId, dwLanguageId: DWORD; lpBuffer: LPWSTR;
  nSize: DWORD; Arguments: Pointer): DWORD; stdcall; external kernel32 name 'FormatMessageW';
function FormatMessage(dwFlags: DWORD; lpSource: Pointer; dwMessageId, dwLanguageId: DWORD; lpBuffer: LPWSTR;
  nSize: DWORD; Arguments: Pointer): DWORD; stdcall; external kernel32 name 'FormatMessageW';
procedure RaiseException(dwExceptionCode, dwExceptionFlags, nNumberOfArguments: DWORD; lpArguments: PULONG_PTR); stdcall;
  external kernel32 name 'RaiseException';
{ Handler: function(ExceptionInfo: Pointer): LONG; stdcall - ExceptionInfo is the EXCEPTION_POINTERS pair }
function AddVectoredExceptionHandler(First: ULONG; Handler: Pointer): Pointer; stdcall;
  external kernel32 name 'AddVectoredExceptionHandler';
function RemoveVectoredExceptionHandler(Handle: Pointer): ULONG; stdcall;
  external kernel32 name 'RemoveVectoredExceptionHandler';

{ ---------------------------------------------------------------- kernel32: console / std handles }
function GetStdHandle(nStdHandle: DWORD): THandle; stdcall; external kernel32 name 'GetStdHandle';
function GetConsoleMode(hConsoleHandle: THandle; var lpMode: DWORD): BOOL; stdcall; external kernel32 name 'GetConsoleMode';
function WriteConsoleW(hConsoleOutput: THandle; lpBuffer: Pointer; nNumberOfCharsToWrite: DWORD;
  var lpNumberOfCharsWritten: DWORD; lpReserved: Pointer): BOOL; stdcall; external kernel32 name 'WriteConsoleW';
function WriteConsole(hConsoleOutput: THandle; lpBuffer: Pointer; nNumberOfCharsToWrite: DWORD;
  var lpNumberOfCharsWritten: DWORD; lpReserved: Pointer): BOOL; stdcall; external kernel32 name 'WriteConsoleW';
function ReadConsoleW(hConsoleInput: THandle; lpBuffer: Pointer; nNumberOfCharsToRead: DWORD;
  var lpNumberOfCharsRead: DWORD; pInputControl: Pointer): BOOL; stdcall; external kernel32 name 'ReadConsoleW';
function ReadConsole(hConsoleInput: THandle; lpBuffer: Pointer; nNumberOfCharsToRead: DWORD;
  var lpNumberOfCharsRead: DWORD; pInputControl: Pointer): BOOL; stdcall; external kernel32 name 'ReadConsoleW';
function GetFileType(hFile: THandle): DWORD; stdcall; external kernel32 name 'GetFileType';

{ ---------------------------------------------------------------- kernel32: files }
function CreateFileW(lpFileName: LPCWSTR; dwDesiredAccess, dwShareMode: DWORD; lpSecurityAttributes: PSecurityAttributes;
  dwCreationDisposition, dwFlagsAndAttributes: DWORD; hTemplateFile: THandle): THandle; stdcall;
  external kernel32 name 'CreateFileW';
function CreateFile(lpFileName: LPCWSTR; dwDesiredAccess, dwShareMode: DWORD; lpSecurityAttributes: PSecurityAttributes;
  dwCreationDisposition, dwFlagsAndAttributes: DWORD; hTemplateFile: THandle): THandle; stdcall;
  external kernel32 name 'CreateFileW';
function ReadFile(hFile: THandle; lpBuffer: Pointer; nNumberOfBytesToRead: DWORD; var lpNumberOfBytesRead: DWORD;
  lpOverlapped: POverlapped): BOOL; stdcall; external kernel32 name 'ReadFile';
function WriteFile(hFile: THandle; lpBuffer: Pointer; nNumberOfBytesToWrite: DWORD; var lpNumberOfBytesWritten: DWORD;
  lpOverlapped: POverlapped): BOOL; stdcall; external kernel32 name 'WriteFile';
function CloseHandle(hObject: THandle): BOOL; stdcall; external kernel32 name 'CloseHandle';
function SetFilePointerEx(hFile: THandle; liDistanceToMove: LARGE_INTEGER; lpNewFilePointer: PLargeInteger;
  dwMoveMethod: DWORD): BOOL; stdcall; external kernel32 name 'SetFilePointerEx';
function SetEndOfFile(hFile: THandle): BOOL; stdcall; external kernel32 name 'SetEndOfFile';
function GetFileSizeEx(hFile: THandle; var lpFileSize: LARGE_INTEGER): BOOL; stdcall; external kernel32 name 'GetFileSizeEx';
function FlushFileBuffers(hFile: THandle): BOOL; stdcall; external kernel32 name 'FlushFileBuffers';
function DeleteFileW(lpFileName: LPCWSTR): BOOL; stdcall; external kernel32 name 'DeleteFileW';
function DeleteFile(lpFileName: LPCWSTR): BOOL; stdcall; external kernel32 name 'DeleteFileW';
function MoveFileW(lpExistingFileName, lpNewFileName: LPCWSTR): BOOL; stdcall; external kernel32 name 'MoveFileW';
function MoveFile(lpExistingFileName, lpNewFileName: LPCWSTR): BOOL; stdcall; external kernel32 name 'MoveFileW';
function MoveFileExW(lpExistingFileName, lpNewFileName: LPCWSTR; dwFlags: DWORD): BOOL; stdcall;
  external kernel32 name 'MoveFileExW';
function MoveFileEx(lpExistingFileName, lpNewFileName: LPCWSTR; dwFlags: DWORD): BOOL; stdcall;
  external kernel32 name 'MoveFileExW';
function CopyFileW(lpExistingFileName, lpNewFileName: LPCWSTR; bFailIfExists: BOOL): BOOL; stdcall;
  external kernel32 name 'CopyFileW';
function CopyFile(lpExistingFileName, lpNewFileName: LPCWSTR; bFailIfExists: BOOL): BOOL; stdcall;
  external kernel32 name 'CopyFileW';
function GetFileAttributesW(lpFileName: LPCWSTR): DWORD; stdcall; external kernel32 name 'GetFileAttributesW';
function GetFileAttributes(lpFileName: LPCWSTR): DWORD; stdcall; external kernel32 name 'GetFileAttributesW';
function SetFileAttributesW(lpFileName: LPCWSTR; dwFileAttributes: DWORD): BOOL; stdcall;
  external kernel32 name 'SetFileAttributesW';
function SetFileAttributes(lpFileName: LPCWSTR; dwFileAttributes: DWORD): BOOL; stdcall;
  external kernel32 name 'SetFileAttributesW';
function GetFileAttributesExW(lpFileName: LPCWSTR; fInfoLevelId: TGetFileExInfoLevels; lpFileInformation: Pointer): BOOL;
  stdcall; external kernel32 name 'GetFileAttributesExW';
function GetFileAttributesEx(lpFileName: LPCWSTR; fInfoLevelId: TGetFileExInfoLevels; lpFileInformation: Pointer): BOOL;
  stdcall; external kernel32 name 'GetFileAttributesExW';
function CreateDirectoryW(lpPathName: LPCWSTR; lpSecurityAttributes: PSecurityAttributes): BOOL; stdcall;
  external kernel32 name 'CreateDirectoryW';
function CreateDirectory(lpPathName: LPCWSTR; lpSecurityAttributes: PSecurityAttributes): BOOL; stdcall;
  external kernel32 name 'CreateDirectoryW';
function RemoveDirectoryW(lpPathName: LPCWSTR): BOOL; stdcall; external kernel32 name 'RemoveDirectoryW';
function RemoveDirectory(lpPathName: LPCWSTR): BOOL; stdcall; external kernel32 name 'RemoveDirectoryW';
function GetCurrentDirectoryW(nBufferLength: DWORD; lpBuffer: LPWSTR): DWORD; stdcall;
  external kernel32 name 'GetCurrentDirectoryW';
function GetCurrentDirectory(nBufferLength: DWORD; lpBuffer: LPWSTR): DWORD; stdcall;
  external kernel32 name 'GetCurrentDirectoryW';
function SetCurrentDirectoryW(lpPathName: LPCWSTR): BOOL; stdcall; external kernel32 name 'SetCurrentDirectoryW';
function SetCurrentDirectory(lpPathName: LPCWSTR): BOOL; stdcall; external kernel32 name 'SetCurrentDirectoryW';
function GetFullPathNameW(lpFileName: LPCWSTR; nBufferLength: DWORD; lpBuffer: LPWSTR; lpFilePart: PLPWSTR): DWORD;
  stdcall; external kernel32 name 'GetFullPathNameW';
function GetFullPathName(lpFileName: LPCWSTR; nBufferLength: DWORD; lpBuffer: LPWSTR; lpFilePart: PLPWSTR): DWORD;
  stdcall; external kernel32 name 'GetFullPathNameW';
function GetTempPathW(nBufferLength: DWORD; lpBuffer: LPWSTR): DWORD; stdcall; external kernel32 name 'GetTempPathW';
function GetTempPath(nBufferLength: DWORD; lpBuffer: LPWSTR): DWORD; stdcall; external kernel32 name 'GetTempPathW';
function FindFirstFileW(lpFileName: LPCWSTR; var lpFindFileData: TWin32FindDataW): THandle; stdcall;
  external kernel32 name 'FindFirstFileW';
function FindFirstFile(lpFileName: LPCWSTR; var lpFindFileData: TWin32FindDataW): THandle; stdcall;
  external kernel32 name 'FindFirstFileW';
function FindNextFileW(hFindFile: THandle; var lpFindFileData: TWin32FindDataW): BOOL; stdcall;
  external kernel32 name 'FindNextFileW';
function FindNextFile(hFindFile: THandle; var lpFindFileData: TWin32FindDataW): BOOL; stdcall;
  external kernel32 name 'FindNextFileW';
function FindClose(hFindFile: THandle): BOOL; stdcall; external kernel32 name 'FindClose';

{ ---------------------------------------------------------------- kernel32: time
  An input FILETIME is declared var: an 8-byte const record would be passed by value (ABI-080), not by address. }
procedure GetLocalTime(var lpSystemTime: TSystemTime); stdcall; external kernel32 name 'GetLocalTime';
procedure GetSystemTime(var lpSystemTime: TSystemTime); stdcall; external kernel32 name 'GetSystemTime';
procedure GetSystemTimeAsFileTime(var lpSystemTimeAsFileTime: TFileTime); stdcall;
  external kernel32 name 'GetSystemTimeAsFileTime';
function FileTimeToLocalFileTime(var lpFileTime: TFileTime; var lpLocalFileTime: TFileTime): BOOL; stdcall;
  external kernel32 name 'FileTimeToLocalFileTime';
function LocalFileTimeToFileTime(var lpLocalFileTime: TFileTime; var lpFileTime: TFileTime): BOOL; stdcall;
  external kernel32 name 'LocalFileTimeToFileTime';
function FileTimeToSystemTime(var lpFileTime: TFileTime; var lpSystemTime: TSystemTime): BOOL; stdcall;
  external kernel32 name 'FileTimeToSystemTime';
function SystemTimeToFileTime(const lpSystemTime: TSystemTime; var lpFileTime: TFileTime): BOOL; stdcall;
  external kernel32 name 'SystemTimeToFileTime';
function GetTimeZoneInformation(var lpTimeZoneInformation: TTimeZoneInformation): DWORD; stdcall;
  external kernel32 name 'GetTimeZoneInformation';
function GetTickCount: DWORD; stdcall; external kernel32 name 'GetTickCount';
function GetTickCount64: ULONGLONG; stdcall; external kernel32 name 'GetTickCount64';
function QueryPerformanceCounter(var lpPerformanceCount: LARGE_INTEGER): BOOL; stdcall;
  external kernel32 name 'QueryPerformanceCounter';
function QueryPerformanceFrequency(var lpFrequency: LARGE_INTEGER): BOOL; stdcall;
  external kernel32 name 'QueryPerformanceFrequency';
procedure Sleep(dwMilliseconds: DWORD); stdcall; external kernel32 name 'Sleep';

{ ---------------------------------------------------------------- kernel32: text / locale }
function MultiByteToWideChar(CodePage: UINT; dwFlags: DWORD; lpMultiByteStr: LPCSTR; cbMultiByte: Integer;
  lpWideCharStr: LPWSTR; cchWideChar: Integer): Integer; stdcall; external kernel32 name 'MultiByteToWideChar';
function WideCharToMultiByte(CodePage: UINT; dwFlags: DWORD; lpWideCharStr: LPCWSTR; cchWideChar: Integer;
  lpMultiByteStr: LPSTR; cbMultiByte: Integer; lpDefaultChar: LPCSTR; lpUsedDefaultChar: PBOOL): Integer; stdcall;
  external kernel32 name 'WideCharToMultiByte';
function GetLocaleInfoEx(lpLocaleName: LPCWSTR; LCType: LCTYPE; lpLCData: LPWSTR; cchData: Integer): Integer; stdcall;
  external kernel32 name 'GetLocaleInfoEx';
function CompareStringEx(lpLocaleName: LPCWSTR; dwCmpFlags: DWORD; lpString1: LPCWSTR; cchCount1: Integer;
  lpString2: LPCWSTR; cchCount2: Integer; lpVersionInformation: Pointer; lpReserved: Pointer; lParam: LPARAM): Integer;
  stdcall; external kernel32 name 'CompareStringEx';

{ ---------------------------------------------------------------- kernel32: threads / synchronisation }
function CreateThread(lpThreadAttributes: PSecurityAttributes; dwStackSize: SIZE_T; lpStartAddress: TFNThreadStartRoutine;
  lpParameter: Pointer; dwCreationFlags: DWORD; lpThreadId: PDWORD): THandle; stdcall; external kernel32 name 'CreateThread';
procedure ExitThread(dwExitCode: DWORD); stdcall; external kernel32 name 'ExitThread';
function GetCurrentThreadId: DWORD; stdcall; external kernel32 name 'GetCurrentThreadId';
function GetCurrentThread: THandle; stdcall; external kernel32 name 'GetCurrentThread';
function ResumeThread(hThread: THandle): DWORD; stdcall; external kernel32 name 'ResumeThread';
function GetExitCodeThread(hThread: THandle; var lpExitCode: DWORD): BOOL; stdcall; external kernel32 name 'GetExitCodeThread';
function SwitchToThread: BOOL; stdcall; external kernel32 name 'SwitchToThread';
function WaitForSingleObject(hHandle: THandle; dwMilliseconds: DWORD): DWORD; stdcall;
  external kernel32 name 'WaitForSingleObject';
function WaitForMultipleObjects(nCount: DWORD; lpHandles: PHANDLE; bWaitAll: BOOL; dwMilliseconds: DWORD): DWORD; stdcall;
  external kernel32 name 'WaitForMultipleObjects';
function TlsAlloc: DWORD; stdcall; external kernel32 name 'TlsAlloc';
function TlsFree(dwTlsIndex: DWORD): BOOL; stdcall; external kernel32 name 'TlsFree';
function TlsGetValue(dwTlsIndex: DWORD): Pointer; stdcall; external kernel32 name 'TlsGetValue';
function TlsSetValue(dwTlsIndex: DWORD; lpTlsValue: Pointer): BOOL; stdcall; external kernel32 name 'TlsSetValue';
function InitializeCriticalSectionAndSpinCount(var lpCriticalSection: TRTLCriticalSection; dwSpinCount: DWORD): BOOL;
  stdcall; external kernel32 name 'InitializeCriticalSectionAndSpinCount';
procedure EnterCriticalSection(var lpCriticalSection: TRTLCriticalSection); stdcall;
  external kernel32 name 'EnterCriticalSection';
procedure LeaveCriticalSection(var lpCriticalSection: TRTLCriticalSection); stdcall;
  external kernel32 name 'LeaveCriticalSection';
function TryEnterCriticalSection(var lpCriticalSection: TRTLCriticalSection): BOOL; stdcall;
  external kernel32 name 'TryEnterCriticalSection';
procedure DeleteCriticalSection(var lpCriticalSection: TRTLCriticalSection); stdcall;
  external kernel32 name 'DeleteCriticalSection';
function CreateEventW(lpEventAttributes: PSecurityAttributes; bManualReset, bInitialState: BOOL; lpName: LPCWSTR): THandle;
  stdcall; external kernel32 name 'CreateEventW';
function CreateEvent(lpEventAttributes: PSecurityAttributes; bManualReset, bInitialState: BOOL; lpName: LPCWSTR): THandle;
  stdcall; external kernel32 name 'CreateEventW';
function SetEvent(hEvent: THandle): BOOL; stdcall; external kernel32 name 'SetEvent';
function ResetEvent(hEvent: THandle): BOOL; stdcall; external kernel32 name 'ResetEvent';
function CreateMutexW(lpMutexAttributes: PSecurityAttributes; bInitialOwner: BOOL; lpName: LPCWSTR): THandle; stdcall;
  external kernel32 name 'CreateMutexW';
function CreateMutex(lpMutexAttributes: PSecurityAttributes; bInitialOwner: BOOL; lpName: LPCWSTR): THandle; stdcall;
  external kernel32 name 'CreateMutexW';
function ReleaseMutex(hMutex: THandle): BOOL; stdcall; external kernel32 name 'ReleaseMutex';
function CreateSemaphoreW(lpSemaphoreAttributes: PSecurityAttributes; lInitialCount, lMaximumCount: LONG;
  lpName: LPCWSTR): THandle; stdcall; external kernel32 name 'CreateSemaphoreW';
function CreateSemaphore(lpSemaphoreAttributes: PSecurityAttributes; lInitialCount, lMaximumCount: LONG;
  lpName: LPCWSTR): THandle; stdcall; external kernel32 name 'CreateSemaphoreW';
function ReleaseSemaphore(hSemaphore: THandle; lReleaseCount: LONG; lpPreviousCount: PLONG): BOOL; stdcall;
  external kernel32 name 'ReleaseSemaphore';

{ ---------------------------------------------------------------- ntdll }
function RtlGetVersion(lpVersionInformation: POSVersionInfoW): NTSTATUS; stdcall; external ntdll name 'RtlGetVersion';

{ ---------------------------------------------------------------- user32: system resource identifiers
  MAKEINTRESOURCEW values; functions, not constants, because an ordinal-to-pointer cast is not a constant expression
  (LANG-340). A call site reads like the constant: LoadCursorW(0, IDC_ARROW). }
function IDC_ARROW: PWideChar; inline;
function IDC_IBEAM: PWideChar; inline;
function IDC_WAIT: PWideChar; inline;
function IDC_CROSS: PWideChar; inline;
function IDC_UPARROW: PWideChar; inline;
function IDC_SIZENWSE: PWideChar; inline;
function IDC_SIZENESW: PWideChar; inline;
function IDC_SIZEWE: PWideChar; inline;
function IDC_SIZENS: PWideChar; inline;
function IDC_SIZEALL: PWideChar; inline;
function IDC_NO: PWideChar; inline;
function IDC_HAND: PWideChar; inline;
function IDC_APPSTARTING: PWideChar; inline;
function IDC_HELP: PWideChar; inline;
function IDI_APPLICATION: PWideChar; inline;
function IDI_HAND: PWideChar; inline;
function IDI_QUESTION: PWideChar; inline;
function IDI_EXCLAMATION: PWideChar; inline;
function IDI_ASTERISK: PWideChar; inline;
function IDI_WINLOGO: PWideChar; inline;
function IDI_SHIELD: PWideChar; inline;
function IDI_WARNING: PWideChar; inline;
function IDI_ERROR: PWideChar; inline;
function IDI_INFORMATION: PWideChar; inline;

{ ---------------------------------------------------------------- user32: classes / windows }
function RegisterClassExW(const lpwcx: TWndClassExW): ATOM; stdcall; external user32 name 'RegisterClassExW';
function RegisterClassEx(const lpwcx: TWndClassExW): ATOM; stdcall; external user32 name 'RegisterClassExW';
function UnregisterClassW(lpClassName: LPCWSTR; hInstance: HINST): BOOL; stdcall; external user32 name 'UnregisterClassW';
function UnregisterClass(lpClassName: LPCWSTR; hInstance: HINST): BOOL; stdcall; external user32 name 'UnregisterClassW';
function GetClassInfoExW(hInstance: HINST; lpszClass: LPCWSTR; var lpwcx: TWndClassExW): BOOL; stdcall;
  external user32 name 'GetClassInfoExW';
function GetClassInfoEx(hInstance: HINST; lpszClass: LPCWSTR; var lpwcx: TWndClassExW): BOOL; stdcall;
  external user32 name 'GetClassInfoExW';
function CreateWindowExW(dwExStyle: DWORD; lpClassName, lpWindowName: LPCWSTR; dwStyle: DWORD; X, Y, nWidth, nHeight: Integer;
  hWndParent: HWND; hMenu: HMENU; hInstance: HINST; lpParam: Pointer): HWND; stdcall; external user32 name 'CreateWindowExW';
function CreateWindowEx(dwExStyle: DWORD; lpClassName, lpWindowName: LPCWSTR; dwStyle: DWORD; X, Y, nWidth, nHeight: Integer;
  hWndParent: HWND; hMenu: HMENU; hInstance: HINST; lpParam: Pointer): HWND; stdcall; external user32 name 'CreateWindowExW';
function DestroyWindow(hWnd: HWND): BOOL; stdcall; external user32 name 'DestroyWindow';
function DefWindowProcW(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'DefWindowProcW';
function DefWindowProc(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'DefWindowProcW';
function CallWindowProcW(lpPrevWndFunc: TFNWndProc; hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'CallWindowProcW';
function CallWindowProc(lpPrevWndFunc: TFNWndProc; hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'CallWindowProcW';
function IsWindow(hWnd: HWND): BOOL; stdcall; external user32 name 'IsWindow';
function IsWindowVisible(hWnd: HWND): BOOL; stdcall; external user32 name 'IsWindowVisible';
function IsWindowEnabled(hWnd: HWND): BOOL; stdcall; external user32 name 'IsWindowEnabled';
function IsIconic(hWnd: HWND): BOOL; stdcall; external user32 name 'IsIconic';
function IsZoomed(hWnd: HWND): BOOL; stdcall; external user32 name 'IsZoomed';
function IsChild(hWndParent, hWnd: HWND): BOOL; stdcall; external user32 name 'IsChild';
function ShowWindow(hWnd: HWND; nCmdShow: Integer): BOOL; stdcall; external user32 name 'ShowWindow';
function EnableWindow(hWnd: HWND; bEnable: BOOL): BOOL; stdcall; external user32 name 'EnableWindow';
function UpdateWindow(hWnd: HWND): BOOL; stdcall; external user32 name 'UpdateWindow';
function SetWindowPos(hWnd, hWndInsertAfter: HWND; X, Y, cx, cy: Integer; uFlags: UINT): BOOL; stdcall;
  external user32 name 'SetWindowPos';
function MoveWindow(hWnd: HWND; X, Y, nWidth, nHeight: Integer; bRepaint: BOOL): BOOL; stdcall;
  external user32 name 'MoveWindow';
function BeginDeferWindowPos(nNumWindows: Integer): HDWP; stdcall; external user32 name 'BeginDeferWindowPos';
function DeferWindowPos(hWinPosInfo: HDWP; hWnd, hWndInsertAfter: HWND; x, y, cx, cy: Integer; uFlags: UINT): HDWP; stdcall;
  external user32 name 'DeferWindowPos';
function EndDeferWindowPos(hWinPosInfo: HDWP): BOOL; stdcall; external user32 name 'EndDeferWindowPos';
function GetWindowRect(hWnd: HWND; var lpRect: TRect): BOOL; stdcall; external user32 name 'GetWindowRect';
function GetClientRect(hWnd: HWND; var lpRect: TRect): BOOL; stdcall; external user32 name 'GetClientRect';
{ an 8-byte const POINT would be passed by value (ABI-080), so the in/out points are var }
function ClientToScreen(hWnd: HWND; var lpPoint: TPoint): BOOL; stdcall; external user32 name 'ClientToScreen';
function ScreenToClient(hWnd: HWND; var lpPoint: TPoint): BOOL; stdcall; external user32 name 'ScreenToClient';
function MapWindowPoints(hWndFrom, hWndTo: HWND; lpPoints: PPoint; cPoints: UINT): Integer; stdcall;
  external user32 name 'MapWindowPoints';
function AdjustWindowRectEx(var lpRect: TRect; dwStyle: DWORD; bMenu: BOOL; dwExStyle: DWORD): BOOL; stdcall;
  external user32 name 'AdjustWindowRectEx';
function AdjustWindowRectExForDpi(var lpRect: TRect; dwStyle: DWORD; bMenu: BOOL; dwExStyle: DWORD; dpi: UINT): BOOL; stdcall;
  external user32 name 'AdjustWindowRectExForDpi';
function GetWindowLongPtrW(hWnd: HWND; nIndex: Integer): LONG_PTR; stdcall; external user32 name 'GetWindowLongPtrW';
function GetWindowLongPtr(hWnd: HWND; nIndex: Integer): LONG_PTR; stdcall; external user32 name 'GetWindowLongPtrW';
function SetWindowLongPtrW(hWnd: HWND; nIndex: Integer; dwNewLong: LONG_PTR): LONG_PTR; stdcall;
  external user32 name 'SetWindowLongPtrW';
function SetWindowLongPtr(hWnd: HWND; nIndex: Integer; dwNewLong: LONG_PTR): LONG_PTR; stdcall;
  external user32 name 'SetWindowLongPtrW';
function GetClassLongPtrW(hWnd: HWND; nIndex: Integer): ULONG_PTR; stdcall; external user32 name 'GetClassLongPtrW';
function GetClassLongPtr(hWnd: HWND; nIndex: Integer): ULONG_PTR; stdcall; external user32 name 'GetClassLongPtrW';
function SetClassLongPtrW(hWnd: HWND; nIndex: Integer; dwNewLong: LONG_PTR): ULONG_PTR; stdcall;
  external user32 name 'SetClassLongPtrW';
function SetClassLongPtr(hWnd: HWND; nIndex: Integer; dwNewLong: LONG_PTR): ULONG_PTR; stdcall;
  external user32 name 'SetClassLongPtrW';
function GetWindowTextW(hWnd: HWND; lpString: LPWSTR; nMaxCount: Integer): Integer; stdcall; external user32 name 'GetWindowTextW';
function GetWindowText(hWnd: HWND; lpString: LPWSTR; nMaxCount: Integer): Integer; stdcall; external user32 name 'GetWindowTextW';
function GetWindowTextLengthW(hWnd: HWND): Integer; stdcall; external user32 name 'GetWindowTextLengthW';
function GetWindowTextLength(hWnd: HWND): Integer; stdcall; external user32 name 'GetWindowTextLengthW';
function SetWindowTextW(hWnd: HWND; lpString: LPCWSTR): BOOL; stdcall; external user32 name 'SetWindowTextW';
function SetWindowText(hWnd: HWND; lpString: LPCWSTR): BOOL; stdcall; external user32 name 'SetWindowTextW';
function GetParent(hWnd: HWND): HWND; stdcall; external user32 name 'GetParent';
function SetParent(hWndChild, hWndNewParent: HWND): HWND; stdcall; external user32 name 'SetParent';
function GetWindow(hWnd: HWND; uCmd: UINT): HWND; stdcall; external user32 name 'GetWindow';
function GetAncestor(hwnd: HWND; gaFlags: UINT): HWND; stdcall; external user32 name 'GetAncestor';
function GetDesktopWindow: HWND; stdcall; external user32 name 'GetDesktopWindow';
function GetForegroundWindow: HWND; stdcall; external user32 name 'GetForegroundWindow';
function SetForegroundWindow(hWnd: HWND): BOOL; stdcall; external user32 name 'SetForegroundWindow';
function BringWindowToTop(hWnd: HWND): BOOL; stdcall; external user32 name 'BringWindowToTop';
function GetWindowPlacement(hWnd: HWND; var lpwndpl: TWindowPlacement): BOOL; stdcall; external user32 name 'GetWindowPlacement';
function SetWindowPlacement(hWnd: HWND; const lpwndpl: TWindowPlacement): BOOL; stdcall; external user32 name 'SetWindowPlacement';
function EnumThreadWindows(dwThreadId: DWORD; lpfn: TFNWndEnumProc; lParam: LPARAM): BOOL; stdcall;
  external user32 name 'EnumThreadWindows';
function EnumChildWindows(hWndParent: HWND; lpEnumFunc: TFNWndEnumProc; lParam: LPARAM): BOOL; stdcall;
  external user32 name 'EnumChildWindows';
function GetWindowThreadProcessId(hWnd: HWND; lpdwProcessId: PDWORD): DWORD; stdcall;
  external user32 name 'GetWindowThreadProcessId';
function FindWindowW(lpClassName, lpWindowName: LPCWSTR): HWND; stdcall; external user32 name 'FindWindowW';
function FindWindow(lpClassName, lpWindowName: LPCWSTR): HWND; stdcall; external user32 name 'FindWindowW';
function FindWindowExW(hWndParent, hWndChildAfter: HWND; lpszClass, lpszWindow: LPCWSTR): HWND; stdcall;
  external user32 name 'FindWindowExW';
function FindWindowEx(hWndParent, hWndChildAfter: HWND; lpszClass, lpszWindow: LPCWSTR): HWND; stdcall;
  external user32 name 'FindWindowExW';
function WindowFromPoint(Point: TPoint): HWND; stdcall; external user32 name 'WindowFromPoint';
function ChildWindowFromPointEx(hwnd: HWND; pt: TPoint; flags: UINT): HWND; stdcall; external user32 name 'ChildWindowFromPointEx';
function GetClassNameW(hWnd: HWND; lpClassName: LPWSTR; nMaxCount: Integer): Integer; stdcall; external user32 name 'GetClassNameW';
function GetClassName(hWnd: HWND; lpClassName: LPWSTR; nMaxCount: Integer): Integer; stdcall; external user32 name 'GetClassNameW';
function SetPropW(hWnd: HWND; lpString: LPCWSTR; hData: THandle): BOOL; stdcall; external user32 name 'SetPropW';
function SetProp(hWnd: HWND; lpString: LPCWSTR; hData: THandle): BOOL; stdcall; external user32 name 'SetPropW';
function GetPropW(hWnd: HWND; lpString: LPCWSTR): THandle; stdcall; external user32 name 'GetPropW';
function GetProp(hWnd: HWND; lpString: LPCWSTR): THandle; stdcall; external user32 name 'GetPropW';
function RemovePropW(hWnd: HWND; lpString: LPCWSTR): THandle; stdcall; external user32 name 'RemovePropW';
function RemoveProp(hWnd: HWND; lpString: LPCWSTR): THandle; stdcall; external user32 name 'RemovePropW';
function FlashWindow(hWnd: HWND; bInvert: BOOL): BOOL; stdcall; external user32 name 'FlashWindow';

{ ---------------------------------------------------------------- user32: messages }
function GetMessageW(var lpMsg: TMsg; hWnd: HWND; wMsgFilterMin, wMsgFilterMax: UINT): BOOL; stdcall;
  external user32 name 'GetMessageW';
function GetMessage(var lpMsg: TMsg; hWnd: HWND; wMsgFilterMin, wMsgFilterMax: UINT): BOOL; stdcall;
  external user32 name 'GetMessageW';
function PeekMessageW(var lpMsg: TMsg; hWnd: HWND; wMsgFilterMin, wMsgFilterMax, wRemoveMsg: UINT): BOOL; stdcall;
  external user32 name 'PeekMessageW';
function PeekMessage(var lpMsg: TMsg; hWnd: HWND; wMsgFilterMin, wMsgFilterMax, wRemoveMsg: UINT): BOOL; stdcall;
  external user32 name 'PeekMessageW';
function TranslateMessage(const lpMsg: TMsg): BOOL; stdcall; external user32 name 'TranslateMessage';
function DispatchMessageW(const lpMsg: TMsg): LRESULT; stdcall; external user32 name 'DispatchMessageW';
function DispatchMessage(const lpMsg: TMsg): LRESULT; stdcall; external user32 name 'DispatchMessageW';
function PostMessageW(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): BOOL; stdcall; external user32 name 'PostMessageW';
function PostMessage(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): BOOL; stdcall; external user32 name 'PostMessageW';
function SendMessageW(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'SendMessageW';
function SendMessage(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM): LRESULT; stdcall;
  external user32 name 'SendMessageW';
function SendMessageTimeoutW(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM; fuFlags, uTimeout: UINT;
  lpdwResult: PDWORD_PTR): LRESULT; stdcall; external user32 name 'SendMessageTimeoutW';
function SendMessageTimeout(hWnd: HWND; Msg: UINT; wParam: WPARAM; lParam: LPARAM; fuFlags, uTimeout: UINT;
  lpdwResult: PDWORD_PTR): LRESULT; stdcall; external user32 name 'SendMessageTimeoutW';
procedure PostQuitMessage(nExitCode: Integer); stdcall; external user32 name 'PostQuitMessage';
function PostThreadMessageW(idThread: DWORD; Msg: UINT; wParam: WPARAM; lParam: LPARAM): BOOL; stdcall;
  external user32 name 'PostThreadMessageW';
function PostThreadMessage(idThread: DWORD; Msg: UINT; wParam: WPARAM; lParam: LPARAM): BOOL; stdcall;
  external user32 name 'PostThreadMessageW';
function WaitMessage: BOOL; stdcall; external user32 name 'WaitMessage';
function MsgWaitForMultipleObjects(nCount: DWORD; pHandles: PHANDLE; fWaitAll: BOOL; dwMilliseconds, dwWakeMask: DWORD): DWORD;
  stdcall; external user32 name 'MsgWaitForMultipleObjects';
function RegisterWindowMessageW(lpString: LPCWSTR): UINT; stdcall; external user32 name 'RegisterWindowMessageW';
function RegisterWindowMessage(lpString: LPCWSTR): UINT; stdcall; external user32 name 'RegisterWindowMessageW';
function IsDialogMessageW(hDlg: HWND; var lpMsg: TMsg): BOOL; stdcall; external user32 name 'IsDialogMessageW';
function IsDialogMessage(hDlg: HWND; var lpMsg: TMsg): BOOL; stdcall; external user32 name 'IsDialogMessageW';
function TranslateAcceleratorW(hWnd: HWND; hAccTable: HACCEL; var lpMsg: TMsg): Integer; stdcall;
  external user32 name 'TranslateAcceleratorW';
function TranslateAccelerator(hWnd: HWND; hAccTable: HACCEL; var lpMsg: TMsg): Integer; stdcall;
  external user32 name 'TranslateAcceleratorW';
function GetMessageTime: LONG; stdcall; external user32 name 'GetMessageTime';
function GetMessagePos: DWORD; stdcall; external user32 name 'GetMessagePos';
function GetQueueStatus(flags: UINT): DWORD; stdcall; external user32 name 'GetQueueStatus';

{ ---------------------------------------------------------------- user32: focus / input }
function SetFocus(hWnd: HWND): HWND; stdcall; external user32 name 'SetFocus';
function GetFocus: HWND; stdcall; external user32 name 'GetFocus';
function SetActiveWindow(hWnd: HWND): HWND; stdcall; external user32 name 'SetActiveWindow';
function GetActiveWindow: HWND; stdcall; external user32 name 'GetActiveWindow';
function SetCapture(hWnd: HWND): HWND; stdcall; external user32 name 'SetCapture';
function ReleaseCapture: BOOL; stdcall; external user32 name 'ReleaseCapture';
function GetCapture: HWND; stdcall; external user32 name 'GetCapture';
function GetKeyState(nVirtKey: Integer): SHORT; stdcall; external user32 name 'GetKeyState';
function GetAsyncKeyState(vKey: Integer): SHORT; stdcall; external user32 name 'GetAsyncKeyState';
function GetKeyboardState(var lpKeyState: TKeyboardState): BOOL; stdcall; external user32 name 'GetKeyboardState';
function MapVirtualKeyW(uCode, uMapType: UINT): UINT; stdcall; external user32 name 'MapVirtualKeyW';
function MapVirtualKey(uCode, uMapType: UINT): UINT; stdcall; external user32 name 'MapVirtualKeyW';
function GetKeyNameTextW(lParam: LONG; lpString: LPWSTR; cchSize: Integer): Integer; stdcall; external user32 name 'GetKeyNameTextW';
function GetKeyNameText(lParam: LONG; lpString: LPWSTR; cchSize: Integer): Integer; stdcall; external user32 name 'GetKeyNameTextW';
function GetCursorPos(var lpPoint: TPoint): BOOL; stdcall; external user32 name 'GetCursorPos';
function SetCursorPos(X, Y: Integer): BOOL; stdcall; external user32 name 'SetCursorPos';
function SetCursor(hCursor: HCURSOR): HCURSOR; stdcall; external user32 name 'SetCursor';
function LoadCursorW(hInstance: HINST; lpCursorName: LPCWSTR): HCURSOR; stdcall; external user32 name 'LoadCursorW';
function LoadCursor(hInstance: HINST; lpCursorName: LPCWSTR): HCURSOR; stdcall; external user32 name 'LoadCursorW';
function ShowCursor(bShow: BOOL): Integer; stdcall; external user32 name 'ShowCursor';
function TrackMouseEvent(var lpEventTrack: TTrackMouseEvent): BOOL; stdcall; external user32 name 'TrackMouseEvent';
function GetDoubleClickTime: UINT; stdcall; external user32 name 'GetDoubleClickTime';
function MessageBeep(uType: UINT): BOOL; stdcall; external user32 name 'MessageBeep';

{ ---------------------------------------------------------------- user32: painting }
function BeginPaint(hWnd: HWND; var lpPaint: TPaintStruct): HDC; stdcall; external user32 name 'BeginPaint';
function EndPaint(hWnd: HWND; const lpPaint: TPaintStruct): BOOL; stdcall; external user32 name 'EndPaint';
function GetDC(hWnd: HWND): HDC; stdcall; external user32 name 'GetDC';
function GetWindowDC(hWnd: HWND): HDC; stdcall; external user32 name 'GetWindowDC';
function ReleaseDC(hWnd: HWND; hDC: HDC): Integer; stdcall; external user32 name 'ReleaseDC';
function InvalidateRect(hWnd: HWND; lpRect: PRect; bErase: BOOL): BOOL; stdcall; external user32 name 'InvalidateRect';
function ValidateRect(hWnd: HWND; lpRect: PRect): BOOL; stdcall; external user32 name 'ValidateRect';
function RedrawWindow(hWnd: HWND; lprcUpdate: PRect; hrgnUpdate: HRGN; flags: UINT): BOOL; stdcall;
  external user32 name 'RedrawWindow';
function GetUpdateRect(hWnd: HWND; lpRect: PRect; bErase: BOOL): BOOL; stdcall; external user32 name 'GetUpdateRect';
function FillRect(hDC: HDC; const lprc: TRect; hbr: HBRUSH): Integer; stdcall; external user32 name 'FillRect';
function FrameRect(hDC: HDC; const lprc: TRect; hbr: HBRUSH): Integer; stdcall; external user32 name 'FrameRect';
function InvertRect(hDC: HDC; const lprc: TRect): BOOL; stdcall; external user32 name 'InvertRect';
function DrawTextW(hdc: HDC; lpchText: LPCWSTR; cchText: Integer; var lprc: TRect; format: UINT): Integer; stdcall;
  external user32 name 'DrawTextW';
function DrawText(hdc: HDC; lpchText: LPCWSTR; cchText: Integer; var lprc: TRect; format: UINT): Integer; stdcall;
  external user32 name 'DrawTextW';
function DrawEdge(hdc: HDC; var qrc: TRect; edge, grfFlags: UINT): BOOL; stdcall; external user32 name 'DrawEdge';
function DrawFocusRect(hDC: HDC; const lprc: TRect): BOOL; stdcall; external user32 name 'DrawFocusRect';
function DrawFrameControl(hdc: HDC; var lprc: TRect; uType, uState: UINT): BOOL; stdcall; external user32 name 'DrawFrameControl';
function DrawIconEx(hdc: HDC; xLeft, yTop: Integer; hIcon: HICON; cxWidth, cyWidth: Integer; istepIfAniCur: UINT;
  hbrFlickerFreeDraw: HBRUSH; diFlags: UINT): BOOL; stdcall; external user32 name 'DrawIconEx';
function GetSysColor(nIndex: Integer): DWORD; stdcall; external user32 name 'GetSysColor';
function GetSysColorBrush(nIndex: Integer): HBRUSH; stdcall; external user32 name 'GetSysColorBrush';
function ScrollWindowEx(hWnd: HWND; dx, dy: Integer; prcScroll, prcClip: PRect; hrgnUpdate: HRGN; prcUpdate: PRect;
  flags: UINT): Integer; stdcall; external user32 name 'ScrollWindowEx';

{ ---------------------------------------------------------------- user32: scroll bars }
function SetScrollInfo(hwnd: HWND; nBar: Integer; const lpsi: TScrollInfo; redraw: BOOL): Integer; stdcall;
  external user32 name 'SetScrollInfo';
function GetScrollInfo(hwnd: HWND; nBar: Integer; var lpsi: TScrollInfo): BOOL; stdcall; external user32 name 'GetScrollInfo';
function ShowScrollBar(hWnd: HWND; wBar: Integer; bShow: BOOL): BOOL; stdcall; external user32 name 'ShowScrollBar';
function EnableScrollBar(hWnd: HWND; wSBflags, wArrows: UINT): BOOL; stdcall; external user32 name 'EnableScrollBar';

{ ---------------------------------------------------------------- user32: menus }
function CreateMenu: HMENU; stdcall; external user32 name 'CreateMenu';
function CreatePopupMenu: HMENU; stdcall; external user32 name 'CreatePopupMenu';
function DestroyMenu(hMenu: HMENU): BOOL; stdcall; external user32 name 'DestroyMenu';
function InsertMenuItemW(hmenu: HMENU; item: UINT; fByPosition: BOOL; const lpmi: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'InsertMenuItemW';
function InsertMenuItem(hmenu: HMENU; item: UINT; fByPosition: BOOL; const lpmi: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'InsertMenuItemW';
function SetMenuItemInfoW(hmenu: HMENU; item: UINT; fByPositon: BOOL; const lpmii: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'SetMenuItemInfoW';
function SetMenuItemInfo(hmenu: HMENU; item: UINT; fByPositon: BOOL; const lpmii: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'SetMenuItemInfoW';
function GetMenuItemInfoW(hmenu: HMENU; item: UINT; fByPosition: BOOL; var lpmii: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'GetMenuItemInfoW';
function GetMenuItemInfo(hmenu: HMENU; item: UINT; fByPosition: BOOL; var lpmii: TMenuItemInfoW): BOOL; stdcall;
  external user32 name 'GetMenuItemInfoW';
function RemoveMenu(hMenu: HMENU; uPosition, uFlags: UINT): BOOL; stdcall; external user32 name 'RemoveMenu';
function DeleteMenu(hMenu: HMENU; uPosition, uFlags: UINT): BOOL; stdcall; external user32 name 'DeleteMenu';
function GetMenuItemCount(hMenu: HMENU): Integer; stdcall; external user32 name 'GetMenuItemCount';
function SetMenu(hWnd: HWND; hMenu: HMENU): BOOL; stdcall; external user32 name 'SetMenu';
function GetMenu(hWnd: HWND): HMENU; stdcall; external user32 name 'GetMenu';
function DrawMenuBar(hWnd: HWND): BOOL; stdcall; external user32 name 'DrawMenuBar';
function TrackPopupMenuEx(hMenu: HMENU; uFlags: UINT; x, y: Integer; hwnd: HWND; lptpm: Pointer): BOOL; stdcall;
  external user32 name 'TrackPopupMenuEx';
function EnableMenuItem(hMenu: HMENU; uIDEnableItem, uEnable: UINT): BOOL; stdcall; external user32 name 'EnableMenuItem';
function CheckMenuItem(hMenu: HMENU; uIDCheckItem, uCheck: UINT): DWORD; stdcall; external user32 name 'CheckMenuItem';
function CheckMenuRadioItem(hmenu: HMENU; first, last, check, flags: UINT): BOOL; stdcall; external user32 name 'CheckMenuRadioItem';
function GetSubMenu(hMenu: HMENU; nPos: Integer): HMENU; stdcall; external user32 name 'GetSubMenu';
function GetSystemMenu(hWnd: HWND; bRevert: BOOL): HMENU; stdcall; external user32 name 'GetSystemMenu';
function CreateAcceleratorTableW(paccel: PAccel; cAccel: Integer): HACCEL; stdcall; external user32 name 'CreateAcceleratorTableW';
function CreateAcceleratorTable(paccel: PAccel; cAccel: Integer): HACCEL; stdcall; external user32 name 'CreateAcceleratorTableW';
function DestroyAcceleratorTable(hAccel: HACCEL): BOOL; stdcall; external user32 name 'DestroyAcceleratorTable';

{ ---------------------------------------------------------------- user32: timers }
function SetTimer(hWnd: HWND; nIDEvent: UINT_PTR; uElapse: UINT; lpTimerFunc: TFNTimerProc): UINT_PTR; stdcall;
  external user32 name 'SetTimer';
function KillTimer(hWnd: HWND; uIDEvent: UINT_PTR): BOOL; stdcall; external user32 name 'KillTimer';

{ ---------------------------------------------------------------- user32: dialog helpers }
function MessageBoxW(hWnd: HWND; lpText, lpCaption: LPCWSTR; uType: UINT): Integer; stdcall; external user32 name 'MessageBoxW';
function MessageBox(hWnd: HWND; lpText, lpCaption: LPCWSTR; uType: UINT): Integer; stdcall; external user32 name 'MessageBoxW';
function GetDlgCtrlID(hWnd: HWND): Integer; stdcall; external user32 name 'GetDlgCtrlID';
function GetNextDlgTabItem(hDlg, hCtl: HWND; bPrevious: BOOL): HWND; stdcall; external user32 name 'GetNextDlgTabItem';

{ ---------------------------------------------------------------- user32: resources / icons
  The parameter "type" of LoadImageW is named uType (type is a reserved word). }
function LoadIconW(hInstance: HINST; lpIconName: LPCWSTR): HICON; stdcall; external user32 name 'LoadIconW';
function LoadIcon(hInstance: HINST; lpIconName: LPCWSTR): HICON; stdcall; external user32 name 'LoadIconW';
function LoadImageW(hInst: HINST; name: LPCWSTR; uType: UINT; cx, cy: Integer; fuLoad: UINT): THandle; stdcall;
  external user32 name 'LoadImageW';
function LoadImage(hInst: HINST; name: LPCWSTR; uType: UINT; cx, cy: Integer; fuLoad: UINT): THandle; stdcall;
  external user32 name 'LoadImageW';
function DestroyIcon(hIcon: HICON): BOOL; stdcall; external user32 name 'DestroyIcon';
function CreateIconIndirect(const piconinfo: TIconInfo): HICON; stdcall; external user32 name 'CreateIconIndirect';
function GetIconInfo(hIcon: HICON; var piconinfo: TIconInfo): BOOL; stdcall; external user32 name 'GetIconInfo';
function DestroyCursor(hCursor: HCURSOR): BOOL; stdcall; external user32 name 'DestroyCursor';

{ ---------------------------------------------------------------- user32: system / DPI / monitors }
function GetSystemMetrics(nIndex: Integer): Integer; stdcall; external user32 name 'GetSystemMetrics';
function GetSystemMetricsForDpi(nIndex: Integer; dpi: UINT): Integer; stdcall; external user32 name 'GetSystemMetricsForDpi';
function SystemParametersInfoW(uiAction, uiParam: UINT; pvParam: Pointer; fWinIni: UINT): BOOL; stdcall;
  external user32 name 'SystemParametersInfoW';
function SystemParametersInfo(uiAction, uiParam: UINT; pvParam: Pointer; fWinIni: UINT): BOOL; stdcall;
  external user32 name 'SystemParametersInfoW';
function SystemParametersInfoForDpi(uiAction, uiParam: UINT; pvParam: Pointer; fWinIni, dpi: UINT): BOOL; stdcall;
  external user32 name 'SystemParametersInfoForDpi';
function GetDpiForWindow(hwnd: HWND): UINT; stdcall; external user32 name 'GetDpiForWindow';
function GetDpiForSystem: UINT; stdcall; external user32 name 'GetDpiForSystem';
function SetProcessDpiAwarenessContext(value: DPI_AWARENESS_CONTEXT): BOOL; stdcall;
  external user32 name 'SetProcessDpiAwarenessContext';
function GetThreadDpiAwarenessContext: DPI_AWARENESS_CONTEXT; stdcall; external user32 name 'GetThreadDpiAwarenessContext';
function AreDpiAwarenessContextsEqual(dpiContextA, dpiContextB: DPI_AWARENESS_CONTEXT): BOOL; stdcall;
  external user32 name 'AreDpiAwarenessContextsEqual';
function MonitorFromWindow(hwnd: HWND; dwFlags: DWORD): HMONITOR; stdcall; external user32 name 'MonitorFromWindow';
function MonitorFromPoint(pt: TPoint; dwFlags: DWORD): HMONITOR; stdcall; external user32 name 'MonitorFromPoint';
function MonitorFromRect(const lprc: TRect; dwFlags: DWORD): HMONITOR; stdcall; external user32 name 'MonitorFromRect';
function GetMonitorInfoW(hMonitor: HMONITOR; var lpmi: TMonitorInfo): BOOL; stdcall; external user32 name 'GetMonitorInfoW';
function GetMonitorInfo(hMonitor: HMONITOR; var lpmi: TMonitorInfo): BOOL; stdcall; external user32 name 'GetMonitorInfoW';
function EnumDisplayMonitors(hdc: HDC; lprcClip: PRect; lpfnEnum: TFNMonitorEnumProc; dwData: LPARAM): BOOL; stdcall;
  external user32 name 'EnumDisplayMonitors';

{ ---------------------------------------------------------------- user32: text helpers (RTL-841) }
function CharUpperBuffW(lpsz: LPWSTR; cchLength: DWORD): DWORD; stdcall; external user32 name 'CharUpperBuffW';
function CharUpperBuff(lpsz: LPWSTR; cchLength: DWORD): DWORD; stdcall; external user32 name 'CharUpperBuffW';
function CharLowerBuffW(lpsz: LPWSTR; cchLength: DWORD): DWORD; stdcall; external user32 name 'CharLowerBuffW';
function CharLowerBuff(lpsz: LPWSTR; cchLength: DWORD): DWORD; stdcall; external user32 name 'CharLowerBuffW';

{ ---------------------------------------------------------------- gdi32: objects
  The parameter "type" of GetCurrentObject is named uType (type is a reserved word). }
function CreateFontIndirectW(const lplf: TLogFontW): HFONT; stdcall; external gdi32 name 'CreateFontIndirectW';
function CreateFontIndirect(const lplf: TLogFontW): HFONT; stdcall; external gdi32 name 'CreateFontIndirectW';
function CreatePen(iStyle, cWidth: Integer; color: COLORREF): HPEN; stdcall; external gdi32 name 'CreatePen';
function CreateSolidBrush(color: COLORREF): HBRUSH; stdcall; external gdi32 name 'CreateSolidBrush';
function CreateBrushIndirect(const plbrush: TLogBrush): HBRUSH; stdcall; external gdi32 name 'CreateBrushIndirect';
function CreatePatternBrush(hbm: HBITMAP): HBRUSH; stdcall; external gdi32 name 'CreatePatternBrush';
function GetStockObject(i: Integer): HGDIOBJ; stdcall; external gdi32 name 'GetStockObject';
function SelectObject(hdc: HDC; h: HGDIOBJ): HGDIOBJ; stdcall; external gdi32 name 'SelectObject';
function DeleteObject(ho: HGDIOBJ): BOOL; stdcall; external gdi32 name 'DeleteObject';
function GetObjectW(h: THandle; c: Integer; pv: Pointer): Integer; stdcall; external gdi32 name 'GetObjectW';
function GetObject(h: THandle; c: Integer; pv: Pointer): Integer; stdcall; external gdi32 name 'GetObjectW';
function GetCurrentObject(hdc: HDC; uType: UINT): HGDIOBJ; stdcall; external gdi32 name 'GetCurrentObject';

{ ---------------------------------------------------------------- gdi32: device contexts and bitmaps }
function CreateCompatibleDC(hdc: HDC): HDC; stdcall; external gdi32 name 'CreateCompatibleDC';
function DeleteDC(hdc: HDC): BOOL; stdcall; external gdi32 name 'DeleteDC';
function SaveDC(hdc: HDC): Integer; stdcall; external gdi32 name 'SaveDC';
function RestoreDC(hdc: HDC; nSavedDC: Integer): BOOL; stdcall; external gdi32 name 'RestoreDC';
function CreateCompatibleBitmap(hdc: HDC; cx, cy: Integer): HBITMAP; stdcall; external gdi32 name 'CreateCompatibleBitmap';
function CreateBitmap(nWidth, nHeight: Integer; nPlanes, nBitCount: UINT; lpBits: Pointer): HBITMAP; stdcall;
  external gdi32 name 'CreateBitmap';
function CreateDIBSection(hdc: HDC; pbmi: PBitmapInfo; usage: UINT; var ppvBits: Pointer; hSection: THandle;
  offset: DWORD): HBITMAP; stdcall; external gdi32 name 'CreateDIBSection';
function GetDIBits(hdc: HDC; hbm: HBITMAP; start, cLines: UINT; lpvBits: Pointer; lpbmi: PBitmapInfo; usage: UINT): Integer;
  stdcall; external gdi32 name 'GetDIBits';
function SetDIBits(hdc: HDC; hbm: HBITMAP; start, cLines: UINT; lpBits: Pointer; lpbmi: PBitmapInfo; ColorUse: UINT): Integer;
  stdcall; external gdi32 name 'SetDIBits';
function BitBlt(hdc: HDC; x, y, cx, cy: Integer; hdcSrc: HDC; x1, y1: Integer; rop: DWORD): BOOL; stdcall;
  external gdi32 name 'BitBlt';
function StretchBlt(hdcDest: HDC; xDest, yDest, wDest, hDest: Integer; hdcSrc: HDC; xSrc, ySrc, wSrc, hSrc: Integer;
  rop: DWORD): BOOL; stdcall; external gdi32 name 'StretchBlt';
function PatBlt(hdc: HDC; x, y, w, h: Integer; rop: DWORD): BOOL; stdcall; external gdi32 name 'PatBlt';
function SetStretchBltMode(hdc: HDC; mode: Integer): Integer; stdcall; external gdi32 name 'SetStretchBltMode';
function SetBrushOrgEx(hdc: HDC; x, y: Integer; lppt: PPoint): BOOL; stdcall; external gdi32 name 'SetBrushOrgEx';
function GdiFlush: BOOL; stdcall; external gdi32 name 'GdiFlush';
function GetDeviceCaps(hdc: HDC; index: Integer): Integer; stdcall; external gdi32 name 'GetDeviceCaps';

{ ---------------------------------------------------------------- gdi32: colours, modes, text }
function SetTextColor(hdc: HDC; color: COLORREF): COLORREF; stdcall; external gdi32 name 'SetTextColor';
function GetTextColor(hdc: HDC): COLORREF; stdcall; external gdi32 name 'GetTextColor';
function SetBkColor(hdc: HDC; color: COLORREF): COLORREF; stdcall; external gdi32 name 'SetBkColor';
function GetBkColor(hdc: HDC): COLORREF; stdcall; external gdi32 name 'GetBkColor';
function SetBkMode(hdc: HDC; mode: Integer): Integer; stdcall; external gdi32 name 'SetBkMode';
function SetTextAlign(hdc: HDC; align: UINT): UINT; stdcall; external gdi32 name 'SetTextAlign';
function SetROP2(hdc: HDC; rop2: Integer): Integer; stdcall; external gdi32 name 'SetROP2';
function TextOutW(hdc: HDC; x, y: Integer; lpString: LPCWSTR; c: Integer): BOOL; stdcall; external gdi32 name 'TextOutW';
function TextOut(hdc: HDC; x, y: Integer; lpString: LPCWSTR; c: Integer): BOOL; stdcall; external gdi32 name 'TextOutW';
function ExtTextOutW(hdc: HDC; x, y: Integer; options: UINT; lprect: PRect; lpString: LPCWSTR; c: UINT; lpDx: PInteger): BOOL;
  stdcall; external gdi32 name 'ExtTextOutW';
function ExtTextOut(hdc: HDC; x, y: Integer; options: UINT; lprect: PRect; lpString: LPCWSTR; c: UINT; lpDx: PInteger): BOOL;
  stdcall; external gdi32 name 'ExtTextOutW';
function GetTextExtentPoint32W(hdc: HDC; lpString: LPCWSTR; c: Integer; var psizl: TSize): BOOL; stdcall;
  external gdi32 name 'GetTextExtentPoint32W';
function GetTextExtentPoint32(hdc: HDC; lpString: LPCWSTR; c: Integer; var psizl: TSize): BOOL; stdcall;
  external gdi32 name 'GetTextExtentPoint32W';
function GetTextMetricsW(hdc: HDC; var lptm: TTextMetricW): BOOL; stdcall; external gdi32 name 'GetTextMetricsW';
function GetTextMetrics(hdc: HDC; var lptm: TTextMetricW): BOOL; stdcall; external gdi32 name 'GetTextMetricsW';
function EnumFontFamiliesExW(hdc: HDC; var lpLogfont: TLogFontW; lpProc: TFNFontEnumProcW; lParam: LPARAM; dwFlags: DWORD): Integer;
  stdcall; external gdi32 name 'EnumFontFamiliesExW';
function EnumFontFamiliesEx(hdc: HDC; var lpLogfont: TLogFontW; lpProc: TFNFontEnumProcW; lParam: LPARAM; dwFlags: DWORD): Integer;
  stdcall; external gdi32 name 'EnumFontFamiliesExW';

{ ---------------------------------------------------------------- gdi32: lines and shapes }
function MoveToEx(hdc: HDC; x, y: Integer; lppt: PPoint): BOOL; stdcall; external gdi32 name 'MoveToEx';
function LineTo(hdc: HDC; x, y: Integer): BOOL; stdcall; external gdi32 name 'LineTo';
function Rectangle(hdc: HDC; left, top, right, bottom: Integer): BOOL; stdcall; external gdi32 name 'Rectangle';
function RoundRect(hdc: HDC; left, top, right, bottom, width, height: Integer): BOOL; stdcall; external gdi32 name 'RoundRect';
function Ellipse(hdc: HDC; left, top, right, bottom: Integer): BOOL; stdcall; external gdi32 name 'Ellipse';
function Arc(hdc: HDC; x1, y1, x2, y2, x3, y3, x4, y4: Integer): BOOL; stdcall; external gdi32 name 'Arc';
function Pie(hdc: HDC; left, top, right, bottom, xr1, yr1, xr2, yr2: Integer): BOOL; stdcall; external gdi32 name 'Pie';
function Chord(hdc: HDC; x1, y1, x2, y2, x3, y3, x4, y4: Integer): BOOL; stdcall; external gdi32 name 'Chord';
function Polygon(hdc: HDC; apt: PPoint; cpt: Integer): BOOL; stdcall; external gdi32 name 'Polygon';
function Polyline(hdc: HDC; apt: PPoint; cpt: Integer): BOOL; stdcall; external gdi32 name 'Polyline';
function GetPixel(hdc: HDC; x, y: Integer): COLORREF; stdcall; external gdi32 name 'GetPixel';
function SetPixel(hdc: HDC; x, y: Integer; color: COLORREF): COLORREF; stdcall; external gdi32 name 'SetPixel';

{ ---------------------------------------------------------------- gdi32: clipping and coordinates }
function CreateRectRgn(x1, y1, x2, y2: Integer): HRGN; stdcall; external gdi32 name 'CreateRectRgn';
function SelectClipRgn(hdc: HDC; hrgn: HRGN): Integer; stdcall; external gdi32 name 'SelectClipRgn';
function IntersectClipRect(hdc: HDC; left, top, right, bottom: Integer): Integer; stdcall; external gdi32 name 'IntersectClipRect';
function ExcludeClipRect(hdc: HDC; left, top, right, bottom: Integer): Integer; stdcall; external gdi32 name 'ExcludeClipRect';
function GetClipBox(hdc: HDC; var lprect: TRect): Integer; stdcall; external gdi32 name 'GetClipBox';
function SetViewportOrgEx(hdc: HDC; x, y: Integer; lppt: PPoint): BOOL; stdcall; external gdi32 name 'SetViewportOrgEx';
function GetViewportOrgEx(hdc: HDC; var lppoint: TPoint): BOOL; stdcall; external gdi32 name 'GetViewportOrgEx';
function OffsetViewportOrgEx(hdc: HDC; x, y: Integer; lppt: PPoint): BOOL; stdcall; external gdi32 name 'OffsetViewportOrgEx';

implementation

function IDC_ARROW: PWideChar;
begin
  Result := PWideChar(32512);
end;

function IDC_IBEAM: PWideChar;
begin
  Result := PWideChar(32513);
end;

function IDC_WAIT: PWideChar;
begin
  Result := PWideChar(32514);
end;

function IDC_CROSS: PWideChar;
begin
  Result := PWideChar(32515);
end;

function IDC_UPARROW: PWideChar;
begin
  Result := PWideChar(32516);
end;

function IDC_SIZENWSE: PWideChar;
begin
  Result := PWideChar(32642);
end;

function IDC_SIZENESW: PWideChar;
begin
  Result := PWideChar(32643);
end;

function IDC_SIZEWE: PWideChar;
begin
  Result := PWideChar(32644);
end;

function IDC_SIZENS: PWideChar;
begin
  Result := PWideChar(32645);
end;

function IDC_SIZEALL: PWideChar;
begin
  Result := PWideChar(32646);
end;

function IDC_NO: PWideChar;
begin
  Result := PWideChar(32648);
end;

function IDC_HAND: PWideChar;
begin
  Result := PWideChar(32649);
end;

function IDC_APPSTARTING: PWideChar;
begin
  Result := PWideChar(32650);
end;

function IDC_HELP: PWideChar;
begin
  Result := PWideChar(32651);
end;

function IDI_APPLICATION: PWideChar;
begin
  Result := PWideChar(32512);
end;

function IDI_HAND: PWideChar;
begin
  Result := PWideChar(32513);
end;

function IDI_QUESTION: PWideChar;
begin
  Result := PWideChar(32514);
end;

function IDI_EXCLAMATION: PWideChar;
begin
  Result := PWideChar(32515);
end;

function IDI_ASTERISK: PWideChar;
begin
  Result := PWideChar(32516);
end;

function IDI_WINLOGO: PWideChar;
begin
  Result := PWideChar(32517);
end;

function IDI_SHIELD: PWideChar;
begin
  Result := PWideChar(32518);
end;

function IDI_WARNING: PWideChar;
begin
  Result := PWideChar(32515);
end;

function IDI_ERROR: PWideChar;
begin
  Result := PWideChar(32513);
end;

function IDI_INFORMATION: PWideChar;
begin
  Result := PWideChar(32516);
end;

function MakeWord(A, B: Byte): Word;
begin
  Result := Word(A) or (Word(B) shl 8);
end;

function MakeLong(Lo, Hi: Word): Cardinal;
begin
  Result := Cardinal(Lo) or (Cardinal(Hi) shl 16);
end;

function LoWord(L: NativeUInt): Word;
begin
  Result := Word(L and $FFFF);
end;

function HiWord(L: NativeUInt): Word;
begin
  Result := Word((L shr 16) and $FFFF);
end;

function LoByte(W: Word): Byte;
begin
  Result := Byte(W and $FF);
end;

function HiByte(W: Word): Byte;
begin
  Result := Byte(W shr 8);
end;

function MakeWParam(Lo, Hi: Word): WPARAM;
begin
  Result := WPARAM(MakeLong(Lo, Hi));
end;

function MakeLParam(Lo, Hi: Word): LPARAM;
begin
  Result := LPARAM(MakeLong(Lo, Hi));
end;

function MakeLResult(Lo, Hi: Word): LRESULT;
begin
  Result := LRESULT(MakeLong(Lo, Hi));
end;

function RGB(R, G, B: Byte): COLORREF;
begin
  Result := COLORREF(R) or (COLORREF(G) shl 8) or (COLORREF(B) shl 16);
end;

function GetRValue(RGB: COLORREF): Byte;
begin
  Result := Byte(RGB and $FF);
end;

function GetGValue(RGB: COLORREF): Byte;
begin
  Result := Byte((RGB shr 8) and $FF);
end;

function GetBValue(RGB: COLORREF): Byte;
begin
  Result := Byte((RGB shr 16) and $FF);
end;

function MakeIntResource(ID: Word): PWideChar;
begin
  Result := PWideChar(NativeUInt(ID));
end;

function Succeeded(hr: HRESULT): Boolean;
begin
  Result := hr >= 0;
end;

function Failed(hr: HRESULT): Boolean;
begin
  Result := hr < 0;
end;

function GET_X_LPARAM(lp: LPARAM): Integer;
begin
  Result := SmallInt(LoWord(NativeUInt(lp)));
end;

function GET_Y_LPARAM(lp: LPARAM): Integer;
begin
  Result := SmallInt(HiWord(NativeUInt(lp)));
end;

function GET_WHEEL_DELTA_WPARAM(wp: WPARAM): SmallInt;
begin
  Result := SmallInt(HiWord(wp));
end;

end.
