{ Windows - Win32 bindings: base types, macros, kernel32 and ntdll functions, constants and records
  (rtl/winapi.md; kernel32 part of Phase 2, task R-034; user32/gdi32 follow with R-109/R-110).
  Written by hand from the Win32 reference on learn.microsoft.com (RTL-800):
  - Windows Data Types; LOWORD/HIWORD/MAKELONG/MAKEWPARAM/MAKELPARAM/MAKELRESULT/RGB/GetRValue/GetGValue/GetBValue/
    MAKEINTRESOURCEW/SUCCEEDED/FAILED/GET_X_LPARAM/GET_Y_LPARAM/GET_WHEEL_DELTA_WPARAM macro pages;
  - the reference page of every function declared below (processthreadsapi.h, libloaderapi.h, processenv.h,
    sysinfoapi.h, heapapi.h, memoryapi.h, winbase.h, errhandlingapi.h, debugapi.h, consoleapi.h, fileapi.h,
    handleapi.h, timezoneapi.h, profileapi.h, synchapi.h, stringapiset.h, winnls.h, wdm.h RtlGetVersion);
  - the structure pages SYSTEMTIME, FILETIME, WIN32_FIND_DATAW, WIN32_FILE_ATTRIBUTE_DATA, RTL_CRITICAL_SECTION,
    EXCEPTION_RECORD, TIME_ZONE_INFORMATION, SECURITY_ATTRIBUTES, OVERLAPPED, SYSTEM_INFO, OSVERSIONINFOW,
    OSVERSIONINFOEXW;
  - System Error Codes (0-499), Generic Access Rights, File Attribute Constants, Locale Information Constants,
    Memory Protection Constants, Code Page Identifiers.
  Unions are declared with the member the RTL uses until variant records exist (RTL-833, M2.5). }
unit Windows;

interface

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

  { LPTHREAD_START_ROUTINE }
  TFNThreadStartRoutine = function(lpThreadParameter: Pointer): DWORD; stdcall;

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
function LocalFree(hMem: HLOCAL): HLOCAL; stdcall; external kernel32 name 'LocalFree';

{ ---------------------------------------------------------------- kernel32: errors / exceptions }
function GetLastError: DWORD; stdcall; external kernel32 name 'GetLastError';
procedure SetLastError(dwErrCode: DWORD); stdcall; external kernel32 name 'SetLastError';
function FormatMessageW(dwFlags: DWORD; lpSource: Pointer; dwMessageId, dwLanguageId: DWORD; lpBuffer: LPWSTR;
  nSize: DWORD; Arguments: Pointer): DWORD; stdcall; external kernel32 name 'FormatMessageW';
function FormatMessage(dwFlags: DWORD; lpSource: Pointer; dwMessageId, dwLanguageId: DWORD; lpBuffer: LPWSTR;
  nSize: DWORD; Arguments: Pointer): DWORD; stdcall; external kernel32 name 'FormatMessageW';
procedure RaiseException(dwExceptionCode, dwExceptionFlags, nNumberOfArguments: DWORD; lpArguments: PULONG_PTR); stdcall;
  external kernel32 name 'RaiseException';

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

implementation

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
