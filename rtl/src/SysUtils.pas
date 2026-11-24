{ SysUtils - exceptions, conversions, Format, floats, date/time, files and string routines (docs: rtl/sysutils.md).
  Locale-independent unless a routine says otherwise (RTL-200); FormatSettings starts invariant (RTL-205). Message
  texts live in RTLConsts (RTL-201). Win32 reference: learn.microsoft.com pages of the functions used. }
unit SysUtils;

interface

uses
  Windows, Types, RTLConsts;

{ ------------------------------------------------------------------------------------------- format settings (1) }
type
  TFormatSettings = record
    CurrencyString: string;
    CurrencyFormat: Byte;
    CurrencyDecimals: Byte;
    NegCurrFormat: Byte;
    ThousandSeparator: Char;
    DecimalSeparator: Char;
    DateSeparator: Char;
    TimeSeparator: Char;
    ListSeparator: Char;
    ShortDateFormat: string;
    LongDateFormat: string;
    ShortTimeFormat: string;
    LongTimeFormat: string;
    TimeAMString: string;
    TimePMString: string;
    ShortMonthNames: array[1..12] of string;
    LongMonthNames: array[1..12] of string;
    ShortDayNames: array[1..7] of string;          { 1 = Sunday }
    LongDayNames: array[1..7] of string;
    TwoDigitYearCenturyWindow: Word;
    { RTL-203 (R-124): member forms forwarding to the unit-level functions below }
    class function Invariant: TFormatSettings; static;
    class function Create: TFormatSettings; overload; static;
    class function Create(const LocaleName: string): TFormatSettings; overload; static;
  end;

var
  FormatSettings: TFormatSettings;

function InvariantFormatSettings: TFormatSettings;
{ RTL-207: the user locale / a named locale through GetLocaleInfoEx; a failing query keeps the invariant value;
  '' gives the invariant record }
function CreateFormatSettings: TFormatSettings; overload;
function CreateFormatSettings(const LocaleName: string): TFormatSettings; overload;

{ ------------------------------------------------------------------------- integer and Boolean conversions (3) }
function IntToStr(Value: Integer): string; overload;
function IntToStr(Value: Int64): string; overload;
function IntToStr(Value: UInt64): string; overload;
function UIntToStr(Value: Cardinal): string; overload;
function UIntToStr(Value: UInt64): string; overload;
{ upper-case hex of the bit pattern in the value's own width, left-padded with 0 to at least Digits }
function IntToHex(Value: Integer; Digits: Integer): string; overload;
function IntToHex(Value: Int64; Digits: Integer): string; overload;
function IntToHex(Value: UInt64; Digits: Integer): string; overload;
function IntToHex(Value: Integer): string; overload;
function IntToHex(Value: Int64): string; overload;
function IntToHex(Value: UInt64): string; overload;
{ the Val grammar after trimming characters <= U+0020 at both ends; Value is undefined on False }
function TryStrToInt(const S: string; out Value: Integer): Boolean;
function TryStrToInt64(const S: string; out Value: Int64): Boolean;
function TryStrToUInt(const S: string; out Value: Cardinal): Boolean;
function TryStrToUInt64(const S: string; out Value: UInt64): Boolean;
{ R-068: the raising forms; EConvertError '<S>' is not a valid integer value (S untrimmed) }
function StrToInt(const S: string): Integer;
function StrToInt64(const S: string): Int64;
function StrToUInt(const S: string): Cardinal;
function StrToUInt64(const S: string): UInt64;
function StrToIntDef(const S: string; Default: Integer): Integer;
function StrToInt64Def(const S: string; Default: Int64): Int64;
function StrToUIntDef(const S: string; Default: Cardinal): Cardinal;
function StrToUInt64Def(const S: string; Default: UInt64): UInt64;
function BoolToStr(B: Boolean; UseBoolStrs: Boolean = False): string;
{ 'true' / 'false' (any case), else a number in FormatSettings: non-zero is True }
function TryStrToBool(const S: string; out Value: Boolean): Boolean;
function StrToBoolDef(const S: string; Default: Boolean): Boolean;
function StrToBool(const S: string): Boolean;

{ ------------------------------------------------------------------------------ case and comparison (7.1, 7.2) }
function UpperCase(const S: string): string;
function LowerCase(const S: string): string;
function CompareStr(const S1, S2: string): Integer;
function CompareText(const S1, S2: string): Integer;
function SameStr(const S1, S2: string): Boolean;
function SameText(const S1, S2: string): Boolean;
{ section 7.3: OS collation (CompareStringEx, user locale) and full simple case mapping; the only locale-dependent
  string routines }
function AnsiUpperCase(const S: string): string;
function AnsiLowerCase(const S: string): string;
function AnsiCompareStr(const S1, S2: string): Integer;
function AnsiCompareText(const S1, S2: string): Integer;
function AnsiSameStr(const S1, S2: string): Boolean;
function AnsiSameText(const S1, S2: string): Boolean;
function AnsiPos(const Substr, S: string): Integer;
function CompareMem(P1, P2: Pointer; Length: Integer): Boolean;
function Trim(const S: string): string;
function TrimLeft(const S: string): string;
function TrimRight(const S: string): string;

type
  TSysCharSet = set of AnsiChar;
  TFileName = type string;            { a distinct string type for file names (LANG-205) }
  TReplaceFlag = (rfReplaceAll, rfIgnoreCase);
  TReplaceFlags = set of TReplaceFlag;

function QuotedStr(const S: string): string;
function AnsiQuotedStr(const S: string; Quote: Char): string;
function AnsiDequotedStr(const S: string; Quote: Char): string;
function AnsiExtractQuotedStr(var Src: PChar; Quote: Char): string;
function StringReplace(const S, OldPattern, NewPattern: string; Flags: TReplaceFlags): string;
function IsDelimiter(const Delimiters, S: string; Index: Integer): Boolean;
function LastDelimiter(const Delimiters, S: string): Integer;
{ breaks after the last break character at or before MaxCol (outside quotes); spaces and tabs at a break are dropped }
function WrapText(const Line, BreakStr: string; const BreakChars: TSysCharSet; MaxCol: Integer): string; overload;
function WrapText(const Line: string; MaxCol: Integer = 45): string; overload;
function AdjustLineBreaks(const S: string; Style: TTextLineBreakStyle = tlbsCRLF): string;
function IsValidIdent(const Ident: string; AllowDots: Boolean = False): Boolean;
function CharInSet(C: Char; const CharSet: TSysCharSet): Boolean;
// GUID text {XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX}, upper-case hex (section 7.2); CreateGUID waits for ole32 (Phase 8+)
function StringToGUID(const S: string): TGUID;
function GUIDToString(const Guid: TGUID): string;
function IsEqualGUID(const Guid1, Guid2: TGUID): Boolean;
{ section 2.4: a nil instance supports nothing; no reference is left behind by the forms without Intf. The out forms
  store a counted reference in Intf (an interface variable or a Pointer), nil when the result is False. }
function Supports(const Instance: IInterface; const IID: TGUID; out Intf): Boolean; overload;
function Supports(const Instance: TObject; const IID: TGUID; out Intf): Boolean; overload;
function Supports(const Instance: IInterface; const IID: TGUID): Boolean; overload;
function Supports(const Instance: TObject; const IID: TGUID): Boolean; overload;
function Supports(const AClass: TClass; const IID: TGUID): Boolean; overload;

{ ---------------------------------------------------------------------------------------- PChar routines (7.4) }
function StrLen(P: PChar): Cardinal;
function StrEnd(P: PChar): PChar;
function StrCopy(Dest, Source: PChar): PChar;
function StrLCopy(Dest, Source: PChar; MaxLen: Cardinal): PChar;
function StrPCopy(Dest: PChar; const Source: string): PChar;
function StrPLCopy(Dest: PChar; const Source: string; MaxLen: Cardinal): PChar;
function StrCat(Dest, Source: PChar): PChar;
{ MaxLen = the total number of characters Dest may hold, excluding the terminator }
function StrLCat(Dest, Source: PChar; MaxLen: Cardinal): PChar;
function StrComp(S1, S2: PChar): Integer;
function StrIComp(S1, S2: PChar): Integer;
function StrLComp(S1, S2: PChar; MaxLen: Cardinal): Integer;
function StrLIComp(S1, S2: PChar; MaxLen: Cardinal): Integer;
function StrScan(P: PChar; Ch: Char): PChar;
function StrRScan(P: PChar; Ch: Char): PChar;
function StrPos(Str, SubStr: PChar): PChar;
function StrUpper(P: PChar): PChar;
function StrLower(P: PChar): PChar;
function StrPas(P: PChar): string;
{ Size characters; the size is stored in front of the block (StrBufSize) }
function StrAlloc(Size: Cardinal): PChar;
function StrBufSize(P: PChar): Cardinal;
function StrNew(P: PChar): PChar;
procedure StrDispose(P: PChar);

{ -------------------------------------------------------------------------- path string manipulation (8.2) }
const
  PathDelim = '\';
  DriveDelim = ':';
  PathSep = ';';

{ pure string functions: '\' and '/' are both delimiters on input; ExpandFileName asks the OS }
function ExtractFilePath(const FileName: string): string;
function ExtractFileDir(const FileName: string): string;
function ExtractFileName(const FileName: string): string;
function ExtractFileExt(const FileName: string): string;
function ExtractFileDrive(const FileName: string): string;
function ChangeFileExt(const FileName, Extension: string): string;
function ChangeFilePath(const FileName, Path: string): string;
function IncludeTrailingPathDelimiter(const S: string): string;
function ExcludeTrailingPathDelimiter(const S: string): string;
function IsPathDelimiter(const S: string; Index: Integer): Boolean;
function ExpandFileName(const FileName: string): string;
function ExtractRelativePath(const BaseName, DestName: string): string;
function IsRelativePath(const Path: string): Boolean;
function SameFileName(const S1, S2: string): Boolean;

{ --------------------------------------------------------------------------------- files and directories (8.1) }
const
  fmOpenRead = $0000;
  fmOpenWrite = $0001;
  fmOpenReadWrite = $0002;
  fmShareExclusive = $0010;
  fmShareDenyWrite = $0020;
  fmShareDenyRead = $0030;
  fmShareDenyNone = $0040;
  INVALID_HANDLE_VALUE = THandle(-1);
  faReadOnly = $01;
  faHidden = $02;
  faSysFile = $04;
  faDirectory = $10;
  faArchive = $20;
  faNormal = $80;
  faTemporary = $100;
  faSymLink = $400;
  faAnyFile = $1FF;
  fsFromBeginning = 0;
  fsFromCurrent = 1;
  fsFromEnd = 2;

type
  { ExcludeAttr, FindHandle and FindData belong to FindFirst / FindNext / FindClose }
  TSearchRec = record
    Name: string;
    Size: Int64;
    Attr: Integer;
    TimeStamp: TDateTime;
    ExcludeAttr: Integer;
    FindHandle: THandle;
    FindData: TWin32FindDataW;
  end;

{ RTL-260: access from Mode and 3, share from Mode and $F0 (0 = deny none); INVALID_HANDLE_VALUE on failure.
  FileRead / FileWrite: the number of bytes moved, -1 on error. }
function FileOpen(const FileName: string; Mode: Cardinal): THandle;
function FileCreate(const FileName: string): THandle; overload;
function FileCreate(const FileName: string; Mode: Cardinal): THandle; overload;
function FileRead(Handle: THandle; var Buffer; Count: Cardinal): Integer;
function FileWrite(Handle: THandle; const Buffer; Count: Cardinal): Integer;
function FileSeek(Handle: THandle; const Offset: Int64; Origin: Integer): Int64;
procedure FileClose(Handle: THandle);
function FileExists(const FileName: string): Boolean;
function DirectoryExists(const Directory: string): Boolean;
function DeleteFile(const FileName: string): Boolean;
function RenameFile(const OldName, NewName: string): Boolean;
function CreateDir(const Dir: string): Boolean;
function RemoveDir(const Dir: string): Boolean;
function SetCurrentDir(const Dir: string): Boolean;
function GetCurrentDir: string;
{ every missing level; '' raises EInOutError }
function ForceDirectories(Dir: string): Boolean;
{ the last-write time in local time }
function FileAge(const FileName: string; out FileDateTime: TDateTime): Boolean;
function FileGetAttr(const FileName: string): Integer;
function FileSetAttr(const FileName: string; Attr: Integer): Integer;
function GetTempDir: string;
function GetModuleName(Module: THandle): string;
{ LoadLibraryW under the process error mode ErrorMode; the error mode and the calling thread's floating-point control
  state are as before the call afterwards. 0 on failure, with the last-error value of LoadLibraryW. Never raises
  (dll-runtime.md RTL-953) }
function SafeLoadLibrary(const FileName: string; ErrorMode: Cardinal = SEM_NOOPENFILEERRORBOX): HMODULE;
{ 0 = found, else a Win32 error (18 at the end, 2 when nothing matches) }
function FindFirst(const Path: string; Attr: Integer; var F: TSearchRec): Integer;
function FindNext(var F: TSearchRec): Integer;
procedure FindClose(var F: TSearchRec);

{ ------------------------------------------------------------------------------------- floats and text (5) }
type
  TFloatFormat = (ffGeneral, ffExponent, ffFixed, ffNumber, ffCurrency);

function FloatToStr(Value: Double): string; overload;
function FloatToStr(Value: Double; const FS: TFormatSettings): string; overload;
function FloatToStrF(Value: Double; Format: TFloatFormat; Precision, Digits: Integer): string; overload;
function FloatToStrF(Value: Double; Format: TFloatFormat; Precision, Digits: Integer;
  const FS: TFormatSettings): string; overload;
function TryStrToFloat(const S: string; out Value: Double): Boolean; overload;
function TryStrToFloat(const S: string; out Value: Double; const FS: TFormatSettings): Boolean; overload;
function StrToFloatDef(const S: string; const Default: Double): Double; overload;
function StrToFloat(const S: string): Double; overload;
function StrToFloat(const S: string; const FS: TFormatSettings): Double; overload;
function StrToFloatDef(const S: string; const Default: Double; const FS: TFormatSettings): Double; overload;
{ picture strings (RTL-239): '0', '#', '.', ',', E+ / E-, up to three sections separated by ';', quoted literals }
function FormatFloat(const Fmt: string; Value: Double): string; overload;
function FormatFloat(const Fmt: string; Value: Double; const FS: TFormatSettings): string; overload;

{ ------------------------------------------------------------------------------------------ date and time (6) }
const
  DateDelta = 693594;                  { days from 0001-01-01 (day 1) to 1899-12-30 }
  UnixDateDelta = 25569;
  HoursPerDay = 24;
  MinsPerHour = 60;
  SecsPerMin = 60;
  MSecsPerSec = 1000;
  MinsPerDay = 1440;
  SecsPerDay = 86400;
  MSecsPerDay = 86400000;
  MinDateTime = -693593.0;
  MaxDateTime = 2958465.99999999;

{ RTL-240/241: a TDateTime counts days from 1899-12-30; for negative values the integer part (toward zero) is the day
  and the absolute fraction the time of day }
function IsLeapYear(Year: Word): Boolean;
function TryEncodeDate(Year, Month, Day: Word; out Date: TDateTime): Boolean;
function TryEncodeTime(Hour, Min, Sec, MSec: Word; out Time: TDateTime): Boolean;
function EncodeDate(Year, Month, Day: Word): TDateTime;
function EncodeTime(Hour, Min, Sec, MSec: Word): TDateTime;
procedure DecodeDate(const DateTime: TDateTime; var Year, Month, Day: Word);
{ Result = Year is a leap year; DOW 1 = Sunday }
function DecodeDateFully(const DateTime: TDateTime; var Year, Month, Day, DOW: Word): Boolean;
procedure DecodeTime(const DateTime: TDateTime; var Hour, Min, Sec, MSec: Word);
function DayOfWeek(const DateTime: TDateTime): Word;
function IncMonth(const DateTime: TDateTime; NumberOfMonths: Integer = 1): TDateTime;
procedure DateTimeToSystemTime(const DateTime: TDateTime; var SystemTime: TSystemTime);
function SystemTimeToDateTime(const SystemTime: TSystemTime): TDateTime;
{ the local clock (GetLocalTime, millisecond resolution) }
function Now: TDateTime;
function Date: TDateTime;
function Time: TDateTime;
function CurrentYear: Word;
procedure Sleep(Milliseconds: Cardinal);
function GetTickCount64: UInt64;
{ section 8.1: '' when Name is not defined }
function GetEnvironmentVariable(const Name: string): string;
{ RTL-243/244: the date/time picture language; an empty format is 'c' }
function FormatDateTime(const Fmt: string; DateTime: TDateTime): string; overload;
function FormatDateTime(const Fmt: string; DateTime: TDateTime; const FS: TFormatSettings): string; overload;
procedure DateTimeToString(var Result: string; const Fmt: string; DateTime: TDateTime); overload;
procedure DateTimeToString(var Result: string; const Fmt: string; DateTime: TDateTime;
  const FS: TFormatSettings); overload;
function DateToStr(const DateTime: TDateTime): string; overload;
function DateToStr(const DateTime: TDateTime; const FS: TFormatSettings): string; overload;
function TimeToStr(const DateTime: TDateTime): string; overload;
function TimeToStr(const DateTime: TDateTime; const FS: TFormatSettings): string; overload;
function DateTimeToStr(const DateTime: TDateTime): string; overload;
function DateTimeToStr(const DateTime: TDateTime; const FS: TFormatSettings): string; overload;
{ RTL-245: parsing; part order from ShortDateFormat, separators DateSeparator / TimeSeparator plus the fixed ones }
function TryStrToDate(const S: string; out Value: TDateTime): Boolean; overload;
function TryStrToDate(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean; overload;
function TryStrToTime(const S: string; out Value: TDateTime): Boolean; overload;
function TryStrToTime(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean; overload;
function TryStrToDateTime(const S: string; out Value: TDateTime): Boolean; overload;
function TryStrToDateTime(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean; overload;
function StrToDate(const S: string): TDateTime; overload;
function StrToDate(const S: string; const FS: TFormatSettings): TDateTime; overload;
function StrToTime(const S: string): TDateTime; overload;
function StrToTime(const S: string; const FS: TFormatSettings): TDateTime; overload;
function StrToDateTime(const S: string): TDateTime; overload;
function StrToDateTime(const S: string; const FS: TFormatSettings): TDateTime; overload;
function StrToDateDef(const S: string; const Default: TDateTime): TDateTime; overload;
function StrToDateDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime; overload;
function StrToTimeDef(const S: string; const Default: TDateTime): TDateTime; overload;
function StrToTimeDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime; overload;
function StrToDateTimeDef(const S: string; const Default: TDateTime): TDateTime; overload;
function StrToDateTimeDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime; overload;

{ ------------------------------------------------------------------------------------------------ Format (4) }
function Format(const Fmt: string; const Args: array of const): string; overload;
function Format(const Fmt: string; const Args: array of const; const FS: TFormatSettings): string; overload;
procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const); overload;
procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const;
  const FS: TFormatSettings); overload;
{ PChar forms: Buffer must hold the whole result and its terminator (StrLFmt: at most MaxBufLen characters) }
function StrFmt(Buffer, Fmt: PChar; const Args: array of const): PChar;
function StrLFmt(Buffer: PChar; MaxBufLen: Cardinal; Fmt: PChar; const Args: array of const): PChar;
{ Fmt is FmtLen characters (no terminator needed); at most BufLen characters are written to Buffer, no terminator;
  the result is the number of characters written }
function FormatBuf(var Buffer; BufLen: Cardinal; const Fmt; FmtLen: Cardinal;
  const Args: array of const): Cardinal; overload;
function FormatBuf(var Buffer; BufLen: Cardinal; const Fmt; FmtLen: Cardinal; const Args: array of const;
  const FS: TFormatSettings): Cardinal; overload;

{ -------------------------------------------------------------------------------------------- exceptions (2) }
type
  Exception = class(TObject)
  private
    FMessage: string;                  { MUST stay the first field and keep this name (ABI-125: debugger contract) }
    FHelpContext: Integer;
    FInnerException: Exception;
  public
    constructor Create(const Msg: string);
    constructor CreateFmt(const Msg: string; const Args: array of const);
    constructor CreateHelp(const Msg: string; AHelpContext: Integer);
    constructor CreateFmtHelp(const Msg: string; const Args: array of const; AHelpContext: Integer);
    destructor Destroy; override;
    function ToString: string; override;
    function GetBaseException: Exception;
    { RTL-210: acquires the exception being handled as E's InnerException, then raises E }
    class procedure RaiseOuterException(E: Exception);
    property Message: string read FMessage write FMessage;
    property HelpContext: Integer read FHelpContext write FHelpContext;
    property InnerException: Exception read FInnerException;
  end;
  ExceptClass = class of Exception;

  EAbort = class(Exception);
  { RTL-211: the out-of-memory and invalid-pointer instances are pre-allocated and never freed while in use }
  EHeapException = class(Exception)
  private
    AllowFree: Boolean;
  public
    procedure FreeInstance; override;
  end;
  EOutOfMemory = class(EHeapException);
  EInvalidPointer = class(EHeapException);
  EInOutError = class(Exception)
  public
    ErrorCode: Integer;
  end;
  EExternal = class(Exception)
  public
    ExceptionCode: Cardinal;
    ExceptionAddress: Pointer;
  end;
  EExternalException = class(EExternal);
  EIntError = class(EExternal);
  EDivByZero = class(EIntError);
  ERangeError = class(EIntError);
  EIntOverflow = class(EIntError);
  EMathError = class(EExternal);
  EInvalidOp = class(EMathError);
  EZeroDivide = class(EMathError);
  EOverflow = class(EMathError);
  EUnderflow = class(EMathError);
  EAccessViolation = class(EExternal);
  EPrivilege = class(EExternal);
  { pre-allocated like the heap exceptions (RTL-213): never freed while in use }
  EStackOverflow = class(EExternal)
  private
    AllowFree: Boolean;
  public
    procedure FreeInstance; override;
  end;
  EControlC = class(EExternal);
  EInvalidCast = class(Exception);
  EConvertError = class(Exception);
  EAssertionFailed = class(Exception);
  EAbstractError = class(Exception);
  EIntfCastError = class(Exception);
  { com-support.md RTL-1004: run-time errors 230 and 231 }
  ESafecallException = class(Exception);
  EDispatchError = class(Exception);
  EOSError = class(Exception)
  public
    ErrorCode: Cardinal;
  end;
  ENotImplemented = class(Exception);
  ENotSupportedException = class(Exception);
  EInvalidOpException = class(Exception);
  EArgumentException = class(Exception);
  EArgumentOutOfRangeException = class(EArgumentException);
  EArgumentNilException = class(EArgumentException);
  EEncodingError = class(Exception);
  EFileNotFoundException = class(Exception);
  EDirectoryNotFoundException = class(Exception);

{ ---------------------------------------------------------------------------------- bytes and encodings (7.5) }
type
  { RTL-258: the standard encodings are singletons freed at finalization (callers never free them); decoding never
    raises for bad input (U+FFFD), only for a range outside the buffer (EEncodingError) }
  TEncoding = class
  public
    class function Default: TEncoding;
    class function ANSI: TEncoding;
    class function ASCII: TEncoding;
    class function UTF8: TEncoding;
    class function Unicode: TEncoding;
    class function BigEndianUnicode: TEncoding;
    { AEncoding nil: detects a UTF-8 / UTF-16LE / UTF-16BE BOM, else ADefault (or Default) and 0; AEncoding given:
      its preamble length when the buffer starts with it, else 0 }
    class function GetBufferEncoding(const Buffer: TBytes; var AEncoding: TEncoding;
      ADefault: TEncoding = nil): Integer;
    function GetPreamble: TBytes; virtual; abstract;
    function GetBytes(const S: string): TBytes; virtual; abstract;
    function GetString(const Bytes: TBytes): string; overload;
    function GetString(const Bytes: TBytes; Index, Count: Integer): string; overload; virtual; abstract;
    function GetByteCount(const S: string): Integer; virtual;
    function GetCharCount(const Bytes: TBytes): Integer; virtual;
    function EncodingName: string; virtual; abstract;
  end;

{ UTF-8 bytes / UTF-8 decoding }
function BytesOf(const S: string): TBytes;
function StringOf(const Bytes: TBytes): string;

{ ------------------------------------------------------------------------------------------- TStringBuilder (9) }
type
  { RTL-270: indexes are 0-based; a bad index or length raises EArgumentOutOfRangeException 'Index out of range (n)'
    naming the offending argument. RTL-271: growth to max(needed, Capacity * 2) }
  TStringBuilder = class
  private
    FData: string;                     { Length(FData) is the capacity }
    FLength: Integer;
    function GetCapacity: Integer;
    procedure SetCapacity(Value: Integer);
    procedure SetLen(Value: Integer);
    function GetChars(Index: Integer): Char;
    procedure SetChars(Index: Integer; Value: Char);
    procedure Grow(Needed: Integer);
    function GetMaxCapacity: Integer;
  public
    constructor Create; overload;
    constructor Create(ACapacity: Integer); overload;
    constructor Create(const Value: string); overload;
    function Append(const Value: string): TStringBuilder; overload;
    function Append(Value: Char): TStringBuilder; overload;
    function Append(Value: Char; RepeatCount: Integer): TStringBuilder; overload;
    function Append(Value: Integer): TStringBuilder; overload;
    function Append(Value: Int64): TStringBuilder; overload;
    function Append(Value: Boolean): TStringBuilder; overload;
    function Append(Value: Double): TStringBuilder; overload;
    function Append(Value: TObject): TStringBuilder; overload;
    function AppendFormat(const Fmt: string; const Args: array of const): TStringBuilder;
    function AppendLine: TStringBuilder; overload;
    function AppendLine(const Value: string): TStringBuilder; overload;
    function Insert(Index: Integer; const Value: string): TStringBuilder;
    function Remove(StartIndex, RemLength: Integer): TStringBuilder;
    { all occurrences, ordinal; an empty OldValue changes nothing }
    function Replace(const OldValue, NewValue: string): TStringBuilder; overload;
    function Replace(OldChar, NewChar: Char): TStringBuilder; overload;
    procedure Clear;
    function EnsureCapacity(ACapacity: Integer): Integer;
    function ToString: string; overload; override;
    function ToString(StartIndex, StrLength: Integer): string; reintroduce; overload;
    property Length: Integer read FLength write SetLen;
    property Capacity: Integer read GetCapacity write SetCapacity;
    property MaxCapacity: Integer read GetMaxCapacity;
    property Chars[Index: Integer]: Char read GetChars write SetChars; default;
  end;

{ ------------------------------------------------------------------------------------- helper routines (2.4, 10) }
procedure Abort;
procedure RaiseLastOSError; overload;
procedure RaiseLastOSError(LastError: Integer); overload;
{ the OS text of ErrorCode without trailing white space; '' for an unknown code }
function SysErrorMessage(ErrorCode: Cardinal): string;
function Win32Check(RetVal: LongBool): LongBool;
procedure OutOfMemoryError;
{ 'Exception <Class> in module <exe> at <16 hex>.' + line break + message (Exception only); at most Size - 1 characters
  and a terminator are written to Buffer; the result is the number of characters written }
function ExceptionErrorMessage(ExceptObject: TObject; ExceptAddr: Pointer; Buffer: PChar; Size: Integer): Integer;
procedure ShowException(ExceptObject: TObject; ExceptAddr: Pointer);
{ Proc runs at program end, the last added first (one System.ExitProc hook runs the list) }
procedure AddExitProc(Proc: TProcedure);
{ Obj (an object reference variable) is set to nil, then the object it held is freed }
procedure FreeAndNil(var Obj);
function GetLastError: Cardinal;
function CheckWin32Version(Major: Integer; Minor: Integer = 0): Boolean;

var
  { RtlGetVersion at initialization }
  Win32MajorVersion: Integer;
  Win32MinorVersion: Integer;
  Win32BuildNumber: Integer;

type
  { the operating system the program runs on (os-version.md): read once at initialization from RtlGetVersion and
    GetNativeSystemInfo; the members only return those values, so they are safe from any thread }
  TOSVersion = record
  public type
    TArchitecture = (arIntelX86, arIntelX64);
    TPlatform = (pfWindows);
  public
    class function Architecture: TArchitecture; static;
    class function Platform: TPlatform; static;
    class function Major: Integer; static;
    class function Minor: Integer; static;
    class function Build: Integer; static;
    class function ServicePackMajor: Integer; static;
    class function ServicePackMinor: Integer; static;
    { the product name in English: 'Windows 11', 'Windows 10', 'Windows Server 2025' ...; 'Windows' when unknown }
    class function Name: string; static;
    { True when the system version is at least the given one }
    class function Check(AMajor: Integer): Boolean; overload; static;
    class function Check(AMajor, AMinor: Integer): Boolean; overload; static;
    class function Check(AMajor, AMinor, AServicePackMajor: Integer): Boolean; overload; static;
    { True when the system is version 10.0 build ABuild or later, e.g. CheckBuild(22000) for Windows 11 }
    class function CheckBuild(ABuild: Integer): Boolean; static;
    { '<Name> (<Major>.<Minor>.<Build>, x64)' }
    class function ToString: string; static;
  end;

const
  { the values of the two enumerations under their plain names, so `TOSVersion.Platform = pfWindows` compiles (the
    elements of a nested enumeration are otherwise reached only through the record: TOSVersion.pfWindows) }
  arIntelX86 = TOSVersion.arIntelX86;
  arIntelX64 = TOSVersion.arIntelX64;
  pfWindows = TOSVersion.pfWindows;

implementation

{ ------------------------------------------------------------------------------------------- format settings (1) }

{ fills A[0..] with the space-separated words of S (typed constant arrays of strings are not available) }
procedure SplitWords(const S: string; var A: array of string);
var
  I, Start, K: Integer;
begin
  K := 0;
  Start := 1;
  for I := 1 to Length(S) + 1 do
    if (I > Length(S)) or (S[I] = ' ') then
    begin
      if (I > Start) and (K <= High(A)) then
      begin
        A[K] := Copy(S, Start, I - Start);
        K := K + 1;
      end;
      Start := I + 1;
    end;
end;

class function TFormatSettings.Invariant: TFormatSettings;
begin
  Result := InvariantFormatSettings;
end;

class function TFormatSettings.Create: TFormatSettings;
begin
  Result := CreateFormatSettings;
end;

class function TFormatSettings.Create(const LocaleName: string): TFormatSettings;
begin
  Result := CreateFormatSettings(LocaleName);
end;

function InvariantFormatSettings: TFormatSettings;
begin
  Result.CurrencyString := '$';
  Result.CurrencyFormat := 0;
  Result.CurrencyDecimals := 2;
  Result.NegCurrFormat := 1;
  Result.ThousandSeparator := ',';
  Result.DecimalSeparator := '.';
  Result.DateSeparator := '-';
  Result.TimeSeparator := ':';
  Result.ListSeparator := ',';
  Result.ShortDateFormat := 'yyyy-mm-dd';
  Result.LongDateFormat := 'dddd, d mmmm yyyy';
  Result.ShortTimeFormat := 'hh:nn';
  Result.LongTimeFormat := 'hh:nn:ss';
  Result.TimeAMString := 'AM';
  Result.TimePMString := 'PM';
  SplitWords('Jan Feb Mar Apr May Jun Jul Aug Sep Oct Nov Dec', Result.ShortMonthNames);
  SplitWords('January February March April May June July August September October November December',
    Result.LongMonthNames);
  SplitWords('Sun Mon Tue Wed Thu Fri Sat', Result.ShortDayNames);
  SplitWords('Sunday Monday Tuesday Wednesday Thursday Friday Saturday', Result.LongDayNames);
  Result.TwoDigitYearCenturyWindow := 50;
end;

{ GetLocaleInfoEx text of LCType for Locale (nil = user default); False when the query fails or gives '' }
function LocaleText(Locale: PChar; LCType: LCTYPE; var S: string): Boolean;
var
  Buffer: array[0..255] of Char;
  N: Integer;
begin
  N := GetLocaleInfoEx(Locale, LCType, @Buffer[0], Length(Buffer));
  Result := N > 1;
  if Result then
    S := Copy(StrPas(@Buffer[0]), 1, N - 1);
end;

procedure LocaleString(Locale: PChar; LCType: LCTYPE; var S: string);
var
  T: string;
begin
  if LocaleText(Locale, LCType, T) then
    S := T;
end;

{ the first character of the separator string }
procedure LocaleChar(Locale: PChar; LCType: LCTYPE; var C: Char);
var
  T: string;
begin
  if LocaleText(Locale, LCType, T) then
    C := T[1];
end;

procedure LocaleByte(Locale: PChar; LCType: LCTYPE; var B: Byte);
var
  T: string;
  V: Integer;
begin
  if LocaleText(Locale, LCType, T) and TryStrToInt(T, V) and (V >= 0) and (V <= 255) then
    B := V;
end;

{ OS picture -> RTL-243 picture: M -> m (month), m -> n (minutes; OS pictures write months upper case), H -> h,
  a run of t -> ampm; quoted text is copied unchanged }
function TranslatePicture(const Pic: string): string;
var
  I: Integer;
  Quoted: Boolean;
  C: Char;
begin
  Result := '';
  Quoted := False;
  I := 1;
  while I <= Length(Pic) do
  begin
    C := Pic[I];
    if C = '''' then
      Quoted := not Quoted;
    if Quoted or (C = '''') then
      Result := Result + C
    else if C = 'M' then
      Result := Result + 'm'
    else if C = 'm' then
      Result := Result + 'n'
    else if C = 'H' then
      Result := Result + 'h'
    else if C = 't' then
    begin
      while (I < Length(Pic)) and (Pic[I + 1] = 't') do
        I := I + 1;
      Result := Result + 'ampm';
    end
    else
      Result := Result + C;
    I := I + 1;
  end;
end;

procedure LocalePicture(Locale: PChar; LCType: LCTYPE; var S: string);
var
  T: string;
begin
  if LocaleText(Locale, LCType, T) then
    S := TranslatePicture(T);
end;

function LocaleSettings(Locale: PChar): TFormatSettings;
var
  I: Integer;
begin
  Result := InvariantFormatSettings;
  LocaleChar(Locale, LOCALE_SDECIMAL, Result.DecimalSeparator);
  LocaleChar(Locale, LOCALE_STHOUSAND, Result.ThousandSeparator);
  LocaleChar(Locale, LOCALE_SDATE, Result.DateSeparator);
  LocaleChar(Locale, LOCALE_STIME, Result.TimeSeparator);
  LocaleChar(Locale, LOCALE_SLIST, Result.ListSeparator);
  LocaleString(Locale, LOCALE_SCURRENCY, Result.CurrencyString);
  LocaleByte(Locale, LOCALE_ICURRDIGITS, Result.CurrencyDecimals);
  LocaleByte(Locale, LOCALE_ICURRENCY, Result.CurrencyFormat);
  LocaleByte(Locale, LOCALE_INEGCURR, Result.NegCurrFormat);
  LocalePicture(Locale, LOCALE_SSHORTDATE, Result.ShortDateFormat);
  LocalePicture(Locale, LOCALE_SLONGDATE, Result.LongDateFormat);
  LocalePicture(Locale, LOCALE_SSHORTTIME, Result.ShortTimeFormat);
  LocalePicture(Locale, LOCALE_STIMEFORMAT, Result.LongTimeFormat);
  LocaleString(Locale, LOCALE_S1159, Result.TimeAMString);
  LocaleString(Locale, LOCALE_S2359, Result.TimePMString);
  for I := 1 to 12 do
  begin
    LocaleString(Locale, LOCALE_SABBREVMONTHNAME1 + I - 1, Result.ShortMonthNames[I]);
    LocaleString(Locale, LOCALE_SMONTHNAME1 + I - 1, Result.LongMonthNames[I]);
  end;
  { the OS numbers days from Monday (DAYNAME1) to Sunday (DAYNAME7); index 1 here is Sunday }
  LocaleString(Locale, LOCALE_SABBREVDAYNAME7, Result.ShortDayNames[1]);
  LocaleString(Locale, LOCALE_SDAYNAME7, Result.LongDayNames[1]);
  for I := 2 to 7 do
  begin
    LocaleString(Locale, LOCALE_SABBREVDAYNAME1 + I - 2, Result.ShortDayNames[I]);
    LocaleString(Locale, LOCALE_SDAYNAME1 + I - 2, Result.LongDayNames[I]);
  end;
end;

function CreateFormatSettings: TFormatSettings;
begin
  Result := LocaleSettings(nil);
end;

function CreateFormatSettings(const LocaleName: string): TFormatSettings;
begin
  if LocaleName = '' then
    Result := InvariantFormatSettings
  else
    Result := LocaleSettings(PChar(LocaleName));
end;

{ ------------------------------------------------------------------------- integer and Boolean conversions (3) }

function IntToStr(Value: Integer): string;
begin
  Str(Value, Result);
end;

function IntToStr(Value: Int64): string;
begin
  Str(Value, Result);
end;

function IntToStr(Value: UInt64): string;
begin
  Str(Value, Result);
end;

function UIntToStr(Value: Cardinal): string;
var
  U: UInt64;
begin
  U := Value;
  Str(U, Result);
end;

function UIntToStr(Value: UInt64): string;
begin
  Str(Value, Result);
end;

{ the shortest upper-case hex text of U, left-padded with '0' to MinDigits }
function HexText(U: UInt64; MinDigits: Integer): string;
const
  HexDigits = '0123456789ABCDEF';
var
  Buf: array[0..15] of Char;
  N, I: Integer;
begin
  N := 0;
  repeat
    Buf[N] := HexDigits[Integer(U and 15) + 1];
    U := U shr 4;
    N := N + 1;
  until U = 0;
  if MinDigits < N then
    MinDigits := N;
  SetLength(Result, MinDigits);
  for I := 1 to MinDigits - N do
    Result[I] := '0';
  for I := 0 to N - 1 do
    Result[MinDigits - I] := Buf[I];
end;

function IntToHex(Value: Integer; Digits: Integer): string;
begin
  Result := HexText(Cardinal(Value), Digits);
end;

function IntToHex(Value: Int64; Digits: Integer): string;
begin
  Result := HexText(UInt64(Value), Digits);
end;

function IntToHex(Value: UInt64; Digits: Integer): string;
begin
  Result := HexText(Value, Digits);
end;

function IntToHex(Value: Integer): string;
begin
  Result := HexText(Cardinal(Value), 8);
end;

function IntToHex(Value: Int64): string;
begin
  Result := HexText(UInt64(Value), 16);
end;

function IntToHex(Value: UInt64): string;
begin
  Result := HexText(Value, 16);
end;

{ EConvertError with Msg formatted with the (untrimmed) text S }
procedure ConvertError(const Msg, S: string);
begin
  raise EConvertError.CreateFmt(Msg, [S]);
end;

function TryStrToInt64(const S: string; out Value: Int64): Boolean;
var
  Code: Integer;
begin
  Val(Trim(S), Value, Code);
  Result := Code = 0;
end;

function TryStrToInt(const S: string; out Value: Integer): Boolean;
var
  V: Int64;
begin
  Result := TryStrToInt64(S, V) and (V >= -2147483648) and (V <= 2147483647);
  if Result then
    Value := Integer(V);
end;

function TryStrToUInt64(const S: string; out Value: UInt64): Boolean;
var
  T: string;
  Code: Integer;
begin
  T := Trim(S);
  Result := False;
  if Pos('-', T) > 0 then
    Exit;
  Val(T, Value, Code);
  Result := Code = 0;
end;

function TryStrToUInt(const S: string; out Value: Cardinal): Boolean;
var
  V: UInt64;
begin
  Result := TryStrToUInt64(S, V) and (V <= $FFFFFFFF);
  if Result then
    Value := Cardinal(V);
end;

function StrToInt(const S: string): Integer;
begin
  if not TryStrToInt(S, Result) then
    ConvertError(SInvalidInteger, S);
end;

function StrToInt64(const S: string): Int64;
begin
  if not TryStrToInt64(S, Result) then
    ConvertError(SInvalidInteger, S);
end;

function StrToUInt(const S: string): Cardinal;
begin
  if not TryStrToUInt(S, Result) then
    ConvertError(SInvalidInteger, S);
end;

function StrToUInt64(const S: string): UInt64;
begin
  if not TryStrToUInt64(S, Result) then
    ConvertError(SInvalidInteger, S);
end;

function StrToIntDef(const S: string; Default: Integer): Integer;
begin
  if not TryStrToInt(S, Result) then
    Result := Default;
end;

function StrToInt64Def(const S: string; Default: Int64): Int64;
begin
  if not TryStrToInt64(S, Result) then
    Result := Default;
end;

function StrToUIntDef(const S: string; Default: Cardinal): Cardinal;
begin
  if not TryStrToUInt(S, Result) then
    Result := Default;
end;

function StrToUInt64Def(const S: string; Default: UInt64): UInt64;
begin
  if not TryStrToUInt64(S, Result) then
    Result := Default;
end;

function BoolToStr(B: Boolean; UseBoolStrs: Boolean): string;
begin
  if UseBoolStrs then
  begin
    if B then
      Result := 'True'
    else
      Result := 'False';
  end
  else if B then
    Result := '-1'
  else
    Result := '0';
end;

function TryStrToBool(const S: string; out Value: Boolean): Boolean;
var
  D: Double;
begin
  Result := True;
  if SameText(S, 'true') then
    Value := True
  else if SameText(S, 'false') then
    Value := False
  else if TryStrToFloat(S, D) then
    Value := D <> 0
  else
    Result := False;
end;

function StrToBoolDef(const S: string; Default: Boolean): Boolean;
begin
  if not TryStrToBool(S, Result) then
    Result := Default;
end;

function StrToBool(const S: string): Boolean;
begin
  if not TryStrToBool(S, Result) then
    ConvertError(SInvalidBoolean, S);
end;

{ ------------------------------------------------------------------------------ case and comparison (7.1, 7.2) }

function UpperCase(const S: string): string;
var
  I: Integer;
begin
  Result := S;
  for I := 1 to Length(S) do
    if (S[I] >= 'a') and (S[I] <= 'z') then
      Result[I] := Char(Ord(S[I]) - 32);
end;

function LowerCase(const S: string): string;
var
  I: Integer;
begin
  Result := S;
  for I := 1 to Length(S) do
    if (S[I] >= 'A') and (S[I] <= 'Z') then
      Result[I] := Char(Ord(S[I]) + 32);
end;

function Sign3(D: Integer): Integer;
begin
  if D < 0 then
    Result := -1
  else if D > 0 then
    Result := 1
  else
    Result := 0;
end;

function CompareStr(const S1, S2: string): Integer;
var
  I, N: Integer;
begin
  N := Length(S1);
  if Length(S2) < N then
    N := Length(S2);
  for I := 1 to N do
    if S1[I] <> S2[I] then
    begin
      Result := Sign3(Ord(S1[I]) - Ord(S2[I]));
      Exit;
    end;
  Result := Sign3(Length(S1) - Length(S2));
end;

function UpCode(C: Char): Integer;
begin
  Result := Ord(C);
  if (Result >= Ord('a')) and (Result <= Ord('z')) then
    Result := Result - 32;
end;

function CompareText(const S1, S2: string): Integer;
var
  I, N, A, B: Integer;
begin
  N := Length(S1);
  if Length(S2) < N then
    N := Length(S2);
  for I := 1 to N do
  begin
    A := UpCode(S1[I]);
    B := UpCode(S2[I]);
    if A <> B then
    begin
      Result := Sign3(A - B);
      Exit;
    end;
  end;
  Result := Sign3(Length(S1) - Length(S2));
end;

function SameStr(const S1, S2: string): Boolean;
begin
  Result := S1 = S2;
end;

function SameText(const S1, S2: string): Boolean;
begin
  Result := (Length(S1) = Length(S2)) and (CompareText(S1, S2) = 0);
end;

{ ------------------------------------------------------------------------------------- Ansi* family (7.3) }
{ RTL-255 / RTL-841: case mapping by LCMapStringEx (kernel32), so that a console program does not import user32;
  without LCMAP_LINGUISTIC_CASING the mapping is the locale-neutral simple one. }

function MapCase(const S: string; Flags: DWORD): string;
var
  N: Integer;
begin
  Result := S;
  if S = '' then
    Exit;
  N := LCMapStringEx(LOCALE_NAME_USER_DEFAULT, Flags, PChar(S), Length(S), nil, 0, nil, nil, 0);
  if N <= 0 then
    Exit;
  SetLength(Result, N);
  UniqueString(Result);
  if LCMapStringEx(LOCALE_NAME_USER_DEFAULT, Flags, PChar(S), Length(S), PChar(Result), N, nil, nil, 0) <= 0 then
    Result := S;
end;

function AnsiUpperCase(const S: string): string;
begin
  Result := MapCase(S, LCMAP_UPPERCASE);
end;

function AnsiLowerCase(const S: string): string;
begin
  Result := MapCase(S, LCMAP_LOWERCASE);
end;

{ CompareStringEx (user locale) − 2, i.e. -1, 0 or 1; an empty operand sorts first without an OS call; should the
  call fail, the ordinal comparison decides }
function LocaleCompare(const S1, S2: string; Flags: DWORD): Integer;
begin
  if (S1 = '') or (S2 = '') then
    Result := Ord(S1 <> '') - Ord(S2 <> '')
  else
  begin
    Result := CompareStringEx(LOCALE_NAME_USER_DEFAULT, Flags, PChar(S1), Length(S1), PChar(S2), Length(S2), nil, nil,
      0);
    if Result = 0 then
    begin
      Result := CompareStr(S1, S2);
      if Result < 0 then
        Result := -1
      else if Result > 0 then
        Result := 1;
    end
    else
      Result := Result - CSTR_EQUAL;
  end;
end;

function AnsiCompareStr(const S1, S2: string): Integer;
begin
  Result := LocaleCompare(S1, S2, 0);
end;

function AnsiCompareText(const S1, S2: string): Integer;
begin
  Result := LocaleCompare(S1, S2, NORM_IGNORECASE);
end;

function AnsiSameStr(const S1, S2: string): Boolean;
begin
  Result := AnsiCompareStr(S1, S2) = 0;
end;

function AnsiSameText(const S1, S2: string): Boolean;
begin
  Result := AnsiCompareText(S1, S2) = 0;
end;

function AnsiPos(const Substr, S: string): Integer;
begin
  Result := Pos(Substr, S);
end;

function CompareMem(P1, P2: Pointer; Length: Integer): Boolean;
var
  A, B: PByte;
  I: Integer;
begin
  Result := True;
  A := P1;
  B := P2;
  for I := 1 to Length do
  begin
    if A^ <> B^ then
    begin
      Result := False;
      Exit;
    end;
    Inc(A);
    Inc(B);
  end;
end;

function Trim(const S: string): string;
var
  L, R: Integer;
begin
  L := 1;
  R := Length(S);
  while (L <= R) and (S[L] <= ' ') do
    L := L + 1;
  while (R >= L) and (S[R] <= ' ') do
    R := R - 1;
  if (L = 1) and (R = Length(S)) then
    Result := S
  else
    Result := Copy(S, L, R - L + 1);
end;

function TrimLeft(const S: string): string;
var
  L: Integer;
begin
  L := 1;
  while (L <= Length(S)) and (S[L] <= ' ') do
    L := L + 1;
  if L = 1 then
    Result := S
  else
    Result := Copy(S, L, Length(S) - L + 1);
end;

function TrimRight(const S: string): string;
var
  R: Integer;
begin
  R := Length(S);
  while (R >= 1) and (S[R] <= ' ') do
    R := R - 1;
  if R = Length(S) then
    Result := S
  else
    Result := Copy(S, 1, R);
end;

function AnsiQuotedStr(const S: string; Quote: Char): string;
var
  I: Integer;
begin
  Result := Quote;
  for I := 1 to Length(S) do
    if S[I] = Quote then
      Result := Result + Quote + Quote
    else
      Result := Result + S[I];
  Result := Result + Quote;
end;

function QuotedStr(const S: string): string;
begin
  Result := AnsiQuotedStr(S, '''');
end;

function AnsiExtractQuotedStr(var Src: PChar; Quote: Char): string;
var
  P: PChar;
begin
  Result := '';
  if (Src = nil) or (Src^ <> Quote) then
    Exit;
  P := Src + 1;
  while P^ <> #0 do
  begin
    if P^ = Quote then
    begin
      if (P + 1)^ <> Quote then
      begin
        P := P + 1;                              { past the closing quote }
        Break;
      end;
      P := P + 1;                                { a doubled quote stands for one }
    end;
    Result := Result + P^;
    P := P + 1;
  end;
  Src := P;
end;

function AnsiDequotedStr(const S: string; Quote: Char): string;
var
  P: PChar;
begin
  if (S = '') or (S[1] <> Quote) then
    Result := S
  else
  begin
    P := PChar(S);
    Result := AnsiExtractQuotedStr(P, Quote);
  end;
end;

function StringReplace(const S, OldPattern, NewPattern: string; Flags: TReplaceFlags): string;
var
  Src, Pat: string;
  Start, Found: Integer;
begin
  if OldPattern = '' then
  begin
    Result := S;
    Exit;
  end;
  if rfIgnoreCase in Flags then
  begin
    Src := UpperCase(S);                         { ASCII upper-casing keeps every index }
    Pat := UpperCase(OldPattern);
  end
  else
  begin
    Src := S;
    Pat := OldPattern;
  end;
  Result := '';
  Start := 1;
  repeat
    Found := Pos(Pat, Src, Start);
    if Found = 0 then
      Break;
    Result := Result + Copy(S, Start, Found - Start) + NewPattern;
    Start := Found + Length(Pat);
  until not (rfReplaceAll in Flags);
  Result := Result + Copy(S, Start, Length(S) - Start + 1);
end;

function IsDelimiter(const Delimiters, S: string; Index: Integer): Boolean;
begin
  Result := (Index >= 1) and (Index <= Length(S)) and (Pos(S[Index], Delimiters) > 0);
end;

function LastDelimiter(const Delimiters, S: string): Integer;
begin
  Result := Length(S);
  while (Result > 0) and (Pos(S[Result], Delimiters) = 0) do
    Result := Result - 1;
end;

function WrapText(const Line, BreakStr: string; const BreakChars: TSysCharSet; MaxCol: Integer): string;
var
  I, LineStart, BreakPos, E: Integer;
  C, Quote: Char;
begin
  Result := '';
  LineStart := 1;
  BreakPos := 0;
  Quote := #0;
  I := 1;
  while I <= Length(Line) do
  begin
    C := Line[I];
    if Quote <> #0 then
    begin
      if C = Quote then
        Quote := #0;
    end
    else if (C = '''') or (C = '"') then
      Quote := C
    else if (C = #13) or (C = #10) then
    begin                                        { an existing line break starts a new line }
      Result := Result + Copy(Line, LineStart, I - LineStart + 1);
      LineStart := I + 1;
      BreakPos := 0;
    end
    else if CharInSet(C, BreakChars) then
      BreakPos := I;
    if (I - LineStart + 1 > MaxCol) and (BreakPos >= LineStart) then
    begin
      E := BreakPos;
      while (E >= LineStart) and ((Line[E] = ' ') or (Line[E] = #9)) do
        E := E - 1;
      Result := Result + Copy(Line, LineStart, E - LineStart + 1) + BreakStr;
      LineStart := BreakPos + 1;
      BreakPos := 0;
    end;
    I := I + 1;
  end;
  Result := Result + Copy(Line, LineStart, Length(Line) - LineStart + 1);
end;

function WrapText(const Line: string; MaxCol: Integer): string;
begin
  Result := WrapText(Line, sLineBreak, [' ', #9, '-'], MaxCol);
end;

function AdjustLineBreaks(const S: string; Style: TTextLineBreakStyle): string;
var
  I: Integer;
  Brk: string;
begin
  if Style = tlbsLF then
    Brk := #10
  else
    Brk := #13#10;
  Result := '';
  I := 1;
  while I <= Length(S) do
  begin
    if S[I] = #13 then
    begin
      Result := Result + Brk;
      if (I < Length(S)) and (S[I + 1] = #10) then
        I := I + 1;
    end
    else if S[I] = #10 then
      Result := Result + Brk
    else
      Result := Result + S[I];
    I := I + 1;
  end;
end;

function IsValidIdent(const Ident: string; AllowDots: Boolean): Boolean;
var
  I: Integer;
  First: Boolean;
  C: Char;
begin
  Result := False;
  First := True;
  for I := 1 to Length(Ident) do
  begin
    C := Ident[I];
    if (C = '.') and AllowDots then
    begin
      if First then
        Exit;                                    { an empty part }
      First := True;
    end
    else if ((C >= 'A') and (C <= 'Z')) or ((C >= 'a') and (C <= 'z')) or (C = '_') then
      First := False
    else if (C >= '0') and (C <= '9') and not First then
      First := False
    else
      Exit;
  end;
  Result := not First;
end;

function CharInSet(C: Char; const CharSet: TSysCharSet): Boolean;
begin
  Result := (Ord(C) <= 255) and (AnsiChar(Ord(C)) in CharSet);
end;

{ ------------------------------------------------------------------------------------------------- GUIDs (7.2) }

const
  { the length of the text form, braces included }
  GuidTextLength = 38;

function HexDigitValue(C: Char): Integer;
begin
  case C of
    '0'..'9': Result := Ord(C) - Ord('0');
    'A'..'F': Result := Ord(C) - Ord('A') + 10;
    'a'..'f': Result := Ord(C) - Ord('a') + 10;
  else
    Result := -1;
  end;
end;

{ the value of Count hex digits of S from Index on; False when one is not a hex digit }
function ReadHex(const S: string; Index, Count: Integer; out Value: Cardinal): Boolean;
var
  I, D: Integer;
begin
  Value := 0;
  Result := False;
  for I := Index to Index + Count - 1 do
  begin
    D := HexDigitValue(S[I]);
    if D < 0 then
      Exit;
    Value := Value * 16 + Cardinal(D);
  end;
  Result := True;
end;

function StringToGUID(const S: string): TGUID;
var
  V: Cardinal;
  I: Integer;
  Ok: Boolean;
begin
  V := 0;
  Ok := (Length(S) = GuidTextLength) and (S[1] = '{') and (S[10] = '-') and (S[15] = '-') and (S[20] = '-') and
    (S[25] = '-') and (S[38] = '}');
  if Ok then
    Ok := ReadHex(S, 2, 8, V);
  if Ok then
  begin
    Result.D1 := V;
    Ok := ReadHex(S, 11, 4, V);
  end;
  if Ok then
  begin
    Result.D2 := Word(V);
    Ok := ReadHex(S, 16, 4, V);
  end;
  if Ok then
  begin
    Result.D3 := Word(V);
    for I := 0 to 7 do
      if Ok then
      begin
        if I < 2 then
          Ok := ReadHex(S, 21 + 2 * I, 2, V)
        else
          Ok := ReadHex(S, 26 + 2 * (I - 2), 2, V);
        Result.D4[I] := Byte(V);
      end;
  end;
  if not Ok then
    raise EConvertError.CreateFmt(SInvalidGUID, [S]);
end;

function GUIDToString(const Guid: TGUID): string;
var
  I: Integer;
begin
  Result := '{' + IntToHex(Guid.D1, 8) + '-' + IntToHex(Guid.D2, 4) + '-' + IntToHex(Guid.D3, 4) + '-' +
    IntToHex(Guid.D4[0], 2) + IntToHex(Guid.D4[1], 2) + '-';
  for I := 2 to 7 do
    Result := Result + IntToHex(Guid.D4[I], 2);
  Result := Result + '}';
end;

function IsEqualGUID(const Guid1, Guid2: TGUID): Boolean;
var
  I: Integer;
begin
  Result := (Guid1.D1 = Guid2.D1) and (Guid1.D2 = Guid2.D2) and (Guid1.D3 = Guid2.D3);
  for I := 0 to 7 do
    if Guid1.D4[I] <> Guid2.D4[I] then
      Result := False;
end;

function Supports(const Instance: IInterface; const IID: TGUID; out Intf): Boolean;
begin
  Pointer(Intf) := nil;
  Result := (Instance <> nil) and (Instance.QueryInterface(IID, Intf) = S_OK);
end;

function Supports(const Instance: TObject; const IID: TGUID; out Intf): Boolean;
begin
  Pointer(Intf) := nil;
  Result := (Instance <> nil) and Instance.GetInterface(IID, Intf);
end;

function Supports(const Instance: IInterface; const IID: TGUID): Boolean;
var
  P: Pointer;
begin
  Result := Supports(Instance, IID, P);
  _IntfClr(P);
end;

function Supports(const Instance: TObject; const IID: TGUID): Boolean;
begin
  Result := _ObjSupports(Pointer(Instance), @IID);
end;

function Supports(const AClass: TClass; const IID: TGUID): Boolean;
begin
  Result := (AClass <> nil) and (AClass.GetInterfaceEntry(IID) <> nil);
end;

{ ---------------------------------------------------------------------------------------- PChar routines (7.4) }

function StrLen(P: PChar): Cardinal;
begin
  Result := 0;
  if P <> nil then
    while P[Result] <> #0 do
      Result := Result + 1;
end;

function StrEnd(P: PChar): PChar;
begin
  Result := P + StrLen(P);
end;

function StrLCopy(Dest, Source: PChar; MaxLen: Cardinal): PChar;
var
  I: Cardinal;
begin
  I := 0;
  if Source <> nil then
    while (I < MaxLen) and (Source[I] <> #0) do
    begin
      Dest[I] := Source[I];
      I := I + 1;
    end;
  Dest[I] := #0;
  Result := Dest;
end;

function StrCopy(Dest, Source: PChar): PChar;
begin
  Result := StrLCopy(Dest, Source, $FFFFFFFF);
end;

function StrPLCopy(Dest: PChar; const Source: string; MaxLen: Cardinal): PChar;
begin
  Result := StrLCopy(Dest, PChar(Source), MaxLen);
end;

function StrPCopy(Dest: PChar; const Source: string): PChar;
begin
  Result := StrLCopy(Dest, PChar(Source), Length(Source));
end;

function StrCat(Dest, Source: PChar): PChar;
begin
  StrCopy(StrEnd(Dest), Source);
  Result := Dest;
end;

function StrLCat(Dest, Source: PChar; MaxLen: Cardinal): PChar;
var
  L: Cardinal;
begin
  L := StrLen(Dest);
  if L < MaxLen then
    StrLCopy(Dest + L, Source, MaxLen - L);
  Result := Dest;
end;

function LowCode(C: Char): Integer;
begin
  Result := Ord(C);
  if (Result >= Ord('a')) and (Result <= Ord('z')) then
    Result := Result - 32;
end;

{ the common loop of the four comparisons: at most MaxLen characters, ASCII case folded when IgnoreCase }
function CompareP(S1, S2: PChar; MaxLen: Cardinal; IgnoreCase: Boolean): Integer;
var
  I: Cardinal;
  A, B: Integer;
begin
  Result := 0;
  I := 0;
  while I < MaxLen do
  begin
    if IgnoreCase then
    begin
      A := LowCode(S1[I]);
      B := LowCode(S2[I]);
    end
    else
    begin
      A := Ord(S1[I]);
      B := Ord(S2[I]);
    end;
    if A <> B then
    begin
      Result := Sign3(A - B);
      Exit;
    end;
    if A = 0 then
      Exit;
    I := I + 1;
  end;
end;

function StrComp(S1, S2: PChar): Integer;
begin
  Result := CompareP(S1, S2, $FFFFFFFF, False);
end;

function StrIComp(S1, S2: PChar): Integer;
begin
  Result := CompareP(S1, S2, $FFFFFFFF, True);
end;

function StrLComp(S1, S2: PChar; MaxLen: Cardinal): Integer;
begin
  Result := CompareP(S1, S2, MaxLen, False);
end;

function StrLIComp(S1, S2: PChar; MaxLen: Cardinal): Integer;
begin
  Result := CompareP(S1, S2, MaxLen, True);
end;

function StrScan(P: PChar; Ch: Char): PChar;
begin
  Result := nil;
  if P = nil then
    Exit;
  while True do
  begin
    if P^ = Ch then
    begin
      Result := P;
      Exit;
    end;
    if P^ = #0 then
      Exit;
    P := P + 1;
  end;
end;

function StrRScan(P: PChar; Ch: Char): PChar;
begin
  Result := nil;
  if P = nil then
    Exit;
  while True do
  begin
    if P^ = Ch then
      Result := P;
    if P^ = #0 then
      Exit;
    P := P + 1;
  end;
end;

function StrPos(Str, SubStr: PChar): PChar;
var
  L: Cardinal;
begin
  Result := nil;
  if (Str = nil) or (SubStr = nil) then
    Exit;
  L := StrLen(SubStr);
  while Str^ <> #0 do
  begin
    if StrLComp(Str, SubStr, L) = 0 then
    begin
      Result := Str;
      Exit;
    end;
    Str := Str + 1;
  end;
end;

function StrUpper(P: PChar): PChar;
var
  I: Cardinal;
begin
  I := 0;
  while P[I] <> #0 do
  begin
    if (P[I] >= 'a') and (P[I] <= 'z') then
      P[I] := Char(Ord(P[I]) - 32);
    I := I + 1;
  end;
  Result := P;
end;

function StrLower(P: PChar): PChar;
var
  I: Cardinal;
begin
  I := 0;
  while P[I] <> #0 do
  begin
    if (P[I] >= 'A') and (P[I] <= 'Z') then
      P[I] := Char(Ord(P[I]) + 32);
    I := I + 1;
  end;
  Result := P;
end;

function StrPas(P: PChar): string;
begin
  Result := string(P);
end;

function StrAlloc(Size: Cardinal): PChar;
var
  B: PNativeUInt;
begin
  GetMem(B, NativeInt(Size) * SizeOf(Char) + SizeOf(NativeUInt));
  B^ := Size;
  Result := PChar(NativeUInt(B) + SizeOf(NativeUInt));
  Result^ := #0;
end;

function StrBufSize(P: PChar): Cardinal;
begin
  Result := 0;
  if P <> nil then
    Result := Cardinal(PNativeUInt(NativeUInt(P) - SizeOf(NativeUInt))^);
end;

function StrNew(P: PChar): PChar;
begin
  Result := nil;
  if P <> nil then
    Result := StrCopy(StrAlloc(StrLen(P) + 1), P);
end;

procedure StrDispose(P: PChar);
begin
  if P <> nil then
    FreeMem(Pointer(NativeUInt(P) - SizeOf(NativeUInt)));
end;

{ -------------------------------------------------------------------------- path string manipulation (8.2) }

function IsDelimChar(C: Char): Boolean;
begin
  Result := (C = '\') or (C = '/');
end;

{ index of the last delimiter or drive colon; 0 if none }
function LastPathSep(const S: string): Integer;
begin
  Result := Length(S);
  while (Result > 0) and not IsDelimChar(S[Result]) and (S[Result] <> ':') do
    Result := Result - 1;
end;

function ExtractFilePath(const FileName: string): string;
begin
  Result := Copy(FileName, 1, LastPathSep(FileName));
end;

function ExtractFileDir(const FileName: string): string;
var
  P: Integer;
begin
  P := LastPathSep(FileName);
  { a root ('\' at 1, or 'X:\') keeps its delimiter }
  if (P > 1) and IsDelimChar(FileName[P]) and not ((P = 3) and (FileName[2] = ':')) then
    P := P - 1;
  Result := Copy(FileName, 1, P);
end;

function ExtractFileName(const FileName: string): string;
var
  P: Integer;
begin
  P := LastPathSep(FileName);
  Result := Copy(FileName, P + 1, Length(FileName) - P);
end;

{ index of the extension's dot in the file-name part; 0 if none }
function ExtDot(const FileName: string): Integer;
var
  Sep: Integer;
begin
  Sep := LastPathSep(FileName);
  Result := Length(FileName);
  while (Result > Sep) and (FileName[Result] <> '.') do
    Result := Result - 1;
  if Result <= Sep then
    Result := 0;
end;

function ExtractFileExt(const FileName: string): string;
var
  D: Integer;
begin
  D := ExtDot(FileName);
  if D = 0 then
    Result := ''
  else
    Result := Copy(FileName, D, Length(FileName) - D + 1);
end;

function ExtractFileDrive(const FileName: string): string;
var
  I, Parts: Integer;
begin
  Result := '';
  if (Length(FileName) >= 2) and (FileName[2] = ':') then
    Result := Copy(FileName, 1, 2)
  else if (Length(FileName) >= 2) and IsDelimChar(FileName[1]) and IsDelimChar(FileName[2]) then
  begin                                          { \\server\share }
    Parts := 0;
    I := 3;
    while I <= Length(FileName) do
    begin
      if IsDelimChar(FileName[I]) then
      begin
        Parts := Parts + 1;
        if Parts = 2 then
          Break;
      end;
      I := I + 1;
    end;
    if Parts >= 1 then
      Result := Copy(FileName, 1, I - 1);
  end;
end;

function ChangeFileExt(const FileName, Extension: string): string;
var
  D: Integer;
begin
  D := ExtDot(FileName);
  if D = 0 then
    Result := FileName + Extension
  else
    Result := Copy(FileName, 1, D - 1) + Extension;
end;

function IncludeTrailingPathDelimiter(const S: string): string;
begin
  if (S <> '') and IsDelimChar(S[Length(S)]) then
    Result := S
  else
    Result := S + PathDelim;
end;

function ExcludeTrailingPathDelimiter(const S: string): string;
begin
  if (S <> '') and IsDelimChar(S[Length(S)]) then
    Result := Copy(S, 1, Length(S) - 1)
  else
    Result := S;
end;

function ChangeFilePath(const FileName, Path: string): string;
begin
  Result := IncludeTrailingPathDelimiter(Path) + ExtractFileName(FileName);
end;

function IsPathDelimiter(const S: string; Index: Integer): Boolean;
begin
  Result := (Index >= 1) and (Index <= Length(S)) and IsDelimChar(S[Index]);
end;

{ platform-layer.md "Long paths": no routine assumes a longest path. A call that returns a path first gets a stack
  buffer of PathStackSize units and, when that is too small, a heap buffer of the size the call reports (or a doubled
  one, up to PathSizeLimit, when it reports none). }
const
  PathStackSize = 300;
  PathSizeLimit = 65536;

function ExpandFileName(const FileName: string): string;
var
  Stack: array[0..PathStackSize - 1] of Char;
  N, Size: DWORD;
  Buf: string;
begin
  N := GetFullPathNameW(PChar(FileName), PathStackSize, @Stack[0], nil);
  if N < PathStackSize then
  begin
    if N = 0 then
      Result := FileName
    else
      SetString(Result, PChar(@Stack[0]), Integer(N));
    Exit;
  end;
  { too small: N is the size needed, terminator included; repeated because the current directory may change }
  repeat
    Size := N;
    SetLength(Buf, Size);
    N := GetFullPathNameW(PChar(FileName), Size, PChar(Buf), nil);
  until N < Size;
  if N = 0 then
    Result := FileName
  else
    Result := Copy(Buf, 1, Integer(N));
end;

{ the delimiter-separated parts of S (empty parts dropped) }
procedure SplitPath(const S: string; var Parts: TStringDynArray);
var
  I, Start, N: Integer;
begin
  SetLength(Parts, 0);
  N := 0;
  Start := 1;
  for I := 1 to Length(S) + 1 do
    if (I > Length(S)) or IsDelimChar(S[I]) then
    begin
      if I > Start then
      begin
        SetLength(Parts, N + 1);
        Parts[N] := Copy(S, Start, I - Start);
        N := N + 1;
      end;
      Start := I + 1;
    end;
end;

function ExtractRelativePath(const BaseName, DestName: string): string;
var
  BaseDrive, DestDrive: string;
  B, D: TStringDynArray;
  I, Common: Integer;
begin
  BaseDrive := ExtractFileDrive(BaseName);
  DestDrive := ExtractFileDrive(DestName);
  if not SameText(BaseDrive, DestDrive) then
  begin
    Result := DestName;
    Exit;
  end;
  SplitPath(Copy(ExtractFilePath(BaseName), Length(BaseDrive) + 1, MaxInt), B);
  SplitPath(Copy(ExtractFilePath(DestName), Length(DestDrive) + 1, MaxInt), D);
  Common := 0;
  while (Common < Length(B)) and (Common < Length(D)) and SameText(B[Common], D[Common]) do
    Common := Common + 1;
  Result := '';
  for I := Common to High(B) do
    Result := Result + '..' + PathDelim;
  for I := Common to High(D) do
    Result := Result + D[I] + PathDelim;
  Result := Result + ExtractFileName(DestName);
end;

function IsRelativePath(const Path: string): Boolean;
begin
  Result := not (((Path <> '') and IsDelimChar(Path[1])) or
    ((Length(Path) >= 3) and (Path[2] = ':') and IsDelimChar(Path[3])));
end;

function SameFileName(const S1, S2: string): Boolean;
begin
  Result := SameText(S1, S2);
end;

{ -------------------------------------------------------------------------------- files and directories (8.1) }

{ the share flags of Mode and $F0 (RTL-260: 0 = deny none) }
function ShareFlags(Mode: Cardinal): DWORD;
begin
  case Mode and $F0 of
    fmShareExclusive: Result := 0;
    fmShareDenyWrite: Result := FILE_SHARE_READ;
    fmShareDenyRead: Result := FILE_SHARE_WRITE;
  else
    Result := FILE_SHARE_READ or FILE_SHARE_WRITE;
  end;
end;

function FileOpen(const FileName: string; Mode: Cardinal): THandle;
var
  Access: DWORD;
begin
  case Mode and 3 of
    fmOpenRead: Access := GENERIC_READ;
    fmOpenWrite: Access := GENERIC_WRITE;
  else
    Access := GENERIC_READ or GENERIC_WRITE;
  end;
  Result := CreateFileW(PChar(FileName), Access, ShareFlags(Mode), nil, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
end;

{ share from Mode as in FileOpen, except that 0 is exclusive (the default of the one-argument form) }
function FileCreate(const FileName: string; Mode: Cardinal): THandle;
var
  Share: DWORD;
begin
  if Mode and $F0 = 0 then
    Share := 0
  else
    Share := ShareFlags(Mode);
  Result := CreateFileW(PChar(FileName), GENERIC_READ or GENERIC_WRITE, Share, nil, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL,
    0);
end;

function FileCreate(const FileName: string): THandle;
begin
  Result := FileCreate(FileName, fmShareExclusive);
end;

function FileRead(Handle: THandle; var Buffer; Count: Cardinal): Integer;
var
  Done: DWORD;
begin
  if Windows.ReadFile(Handle, @Buffer, Count, Done, nil) then
    Result := Integer(Done)
  else
    Result := -1;
end;

function FileWrite(Handle: THandle; const Buffer; Count: Cardinal): Integer;
var
  Done: DWORD;
begin
  if Windows.WriteFile(Handle, @Buffer, Count, Done, nil) then
    Result := Integer(Done)
  else
    Result := -1;
end;

function FileSeek(Handle: THandle; const Offset: Int64; Origin: Integer): Int64;
begin
  if not SetFilePointerEx(Handle, Offset, @Result, DWORD(Origin)) then
    Result := -1;
end;

procedure FileClose(Handle: THandle);
begin
  CloseHandle(Handle);
end;

function FileExists(const FileName: string): Boolean;
var
  A: DWORD;
begin
  A := GetFileAttributesW(PChar(FileName));
  Result := (A <> INVALID_FILE_ATTRIBUTES) and (A and FILE_ATTRIBUTE_DIRECTORY = 0);
end;

function DirectoryExists(const Directory: string): Boolean;
var
  A: DWORD;
begin
  A := GetFileAttributesW(PChar(Directory));
  Result := (A <> INVALID_FILE_ATTRIBUTES) and (A and FILE_ATTRIBUTE_DIRECTORY <> 0);
end;

function DeleteFile(const FileName: string): Boolean;
begin
  Result := DeleteFileW(PChar(FileName));
end;

function RenameFile(const OldName, NewName: string): Boolean;
begin
  Result := MoveFileW(PChar(OldName), PChar(NewName));
end;

function CreateDir(const Dir: string): Boolean;
begin
  Result := CreateDirectoryW(PChar(Dir), nil);
end;

function RemoveDir(const Dir: string): Boolean;
begin
  Result := RemoveDirectoryW(PChar(Dir));
end;

function SetCurrentDir(const Dir: string): Boolean;
begin
  Result := SetCurrentDirectoryW(PChar(Dir));
end;

function GetCurrentDir: string;
var
  Stack: array[0..PathStackSize - 1] of Char;
  Len, Size: DWORD;
begin
  Len := GetCurrentDirectoryW(PathStackSize, @Stack[0]);
  if Len < PathStackSize then
  begin
    SetString(Result, PChar(@Stack[0]), Integer(Len));
    Exit;
  end;
  { too small: Len is the size needed, terminator included; repeated because another thread may change the directory }
  repeat
    Size := Len;
    SetLength(Result, Size);
    Len := GetCurrentDirectoryW(Size, PChar(Result));
  until Len < Size;
  SetLength(Result, Len);
end;

{ creates the parent first; a level that cannot be split further (a drive, a root, a single name) ends the recursion }
function ForceDirectories(Dir: string): Boolean;
var
  Parent: string;
begin
  if Dir = '' then
    raise EInOutError.Create(SCannotCreateDir);
  Dir := ExcludeTrailingPathDelimiter(Dir);
  Result := True;
  if (Dir = '') or DirectoryExists(Dir) then
    Exit;
  Parent := ExcludeTrailingPathDelimiter(ExtractFilePath(Dir));
  if (Parent <> '') and (Length(Parent) < Length(Dir)) and (Parent <> ExtractFileDrive(Dir)) then
    Result := ForceDirectories(Parent);
  if Result then
    Result := CreateDir(Dir) or DirectoryExists(Dir);
end;

{ a FILETIME (UTC) as a local TDateTime }
function FileTimeToLocal(var FT: TFileTime; out DT: TDateTime): Boolean;
var
  Local: TFileTime;
  ST: TSystemTime;
begin
  Result := FileTimeToLocalFileTime(FT, Local) and FileTimeToSystemTime(Local, ST);
  if Result then
    DT := SystemTimeToDateTime(ST)
  else
    DT := 0;
end;

function FileAge(const FileName: string; out FileDateTime: TDateTime): Boolean;
var
  Data: TWin32FileAttributeData;
begin
  FileDateTime := 0;
  Result := GetFileAttributesExW(PChar(FileName), GetFileExInfoStandard, @Data) and
    FileTimeToLocal(Data.ftLastWriteTime, FileDateTime);
end;

function FileGetAttr(const FileName: string): Integer;
begin
  Result := Integer(GetFileAttributesW(PChar(FileName)));
end;

{ 0 or the Win32 error }
function FileSetAttr(const FileName: string; Attr: Integer): Integer;
begin
  if SetFileAttributesW(PChar(FileName), DWORD(Attr)) then
    Result := 0
  else
    Result := Integer(Windows.GetLastError);
end;

function GetTempDir: string;
var
  Stack: array[0..PathStackSize - 1] of Char;
  Len, Size: DWORD;
begin
  Len := GetTempPathW(PathStackSize, @Stack[0]);
  if Len < PathStackSize then
    SetString(Result, PChar(@Stack[0]), Integer(Len))
  else
  begin
    { too small: Len is the size needed, terminator included }
    repeat
      Size := Len;
      SetLength(Result, Size);
      Len := GetTempPathW(Size, PChar(Result));
    until Len < Size;
    SetLength(Result, Len);
  end;
  if Result <> '' then
    Result := IncludeTrailingPathDelimiter(Result);
end;

function GetModuleName(Module: THandle): string;
var
  Stack: array[0..PathStackSize - 1] of Char;
  Len, Size: DWORD;
begin
  Len := GetModuleFileNameW(HMODULE(Module), @Stack[0], PathStackSize);
  if Len < PathStackSize then
  begin
    SetString(Result, PChar(@Stack[0]), Integer(Len));
    Exit;
  end;
  { cut off: the call does not report the size needed, so the buffer doubles up to the longest path there is }
  Size := PathStackSize;
  repeat
    Size := Size * 2;
    SetLength(Result, Size);
    Len := GetModuleFileNameW(HMODULE(Module), PChar(Result), Size);
  until (Len < Size) or (Size > PathSizeLimit);
  if Len >= Size then
    Len := 0;
  SetLength(Result, Len);
end;

function SafeLoadLibrary(const FileName: string; ErrorMode: Cardinal = SEM_NOOPENFILEERRORBOX): HMODULE;
var
  FloatState: Cardinal;
  OldMode: UINT;
  Error: DWORD;
begin
  FloatState := _GetMXCSR;
  OldMode := SetErrorMode(ErrorMode);
  Result := LoadLibraryW(PChar(FileName));
  Error := Windows.GetLastError;
  SetErrorMode(OldMode);
  _SetMXCSR(FloatState);               { the initialisation of a foreign library may have changed it }
  Windows.SetLastError(Error);
end;

{ the entry in F.FindData copied to the public fields }
procedure FillSearchRec(var F: TSearchRec);
begin
  F.Name := StrPas(@F.FindData.cFileName[0]);
  F.Size := (Int64(F.FindData.nFileSizeHigh) shl 32) or Int64(F.FindData.nFileSizeLow);
  F.Attr := Integer(F.FindData.dwFileAttributes);
  FileTimeToLocal(F.FindData.ftLastWriteTime, F.TimeStamp);
end;

{ the current entry, or the next one without an excluded attribute; 0 or the Win32 error }
function FindMatching(var F: TSearchRec): Integer;
begin
  while Integer(F.FindData.dwFileAttributes) and F.ExcludeAttr <> 0 do
    if not FindNextFileW(F.FindHandle, F.FindData) then
    begin
      Result := Integer(Windows.GetLastError);
      Exit;
    end;
  FillSearchRec(F);
  Result := 0;
end;

function FindFirst(const Path: string; Attr: Integer; var F: TSearchRec): Integer;
begin
  F.ExcludeAttr := not Attr and (faHidden or faSysFile or faDirectory);
  F.FindHandle := FindFirstFileW(PChar(Path), F.FindData);
  if F.FindHandle = INVALID_HANDLE_VALUE then
  begin
    Result := Integer(Windows.GetLastError);
    Exit;
  end;
  Result := FindMatching(F);
  if Result = ERROR_NO_MORE_FILES then
    Result := ERROR_FILE_NOT_FOUND;
  if Result <> 0 then
    FindClose(F);
end;

function FindNext(var F: TSearchRec): Integer;
begin
  if (F.FindHandle = INVALID_HANDLE_VALUE) or (F.FindHandle = 0) then
  begin
    Result := ERROR_NO_MORE_FILES;
    Exit;
  end;
  if FindNextFileW(F.FindHandle, F.FindData) then
    Result := FindMatching(F)
  else
    Result := Integer(Windows.GetLastError);
end;

{ safe to call twice }
procedure FindClose(var F: TSearchRec);
begin
  if (F.FindHandle <> INVALID_HANDLE_VALUE) and (F.FindHandle <> 0) then
    Windows.FindClose(F.FindHandle);
  F.FindHandle := INVALID_HANDLE_VALUE;
end;

{ ------------------------------------------------------------------------------------- floats and text (5) }

function DigitChar(D: Byte): Char;
begin
  Result := Char(Ord('0') + D);
end;

{ Digits[0 .. Count-1] of R, with positions beyond Count read as 0 }
function DigitAt(const R: TFloatDigits; I: Integer): Char;
begin
  if (I >= 0) and (I < R.Count) then
    Result := DigitChar(R.Digits[I])
  else
    Result := '0';
end;

{ rounds R half-even so that it keeps Keep digits (Keep >= 0); a carry out of the first digit raises Exponent }
procedure RoundDigits(var R: TFloatDigits; Keep: Integer);
var
  Up: Boolean;
  I: Integer;
begin
  if Keep >= R.Count then
    Exit;
  if R.Digits[Keep] > 5 then
    Up := True
  else if R.Digits[Keep] < 5 then
    Up := False
  else
  begin
    Up := False;
    for I := Keep + 1 to R.Count - 1 do
      if R.Digits[I] <> 0 then
        Up := True;
    if not Up then                               { an exact tie: round to the even digit }
      Up := (Keep > 0) and Odd(R.Digits[Keep - 1]);
  end;
  R.Count := Keep;
  if Up then
  begin
    I := Keep - 1;
    while (I >= 0) and (R.Digits[I] = 9) do
    begin
      R.Digits[I] := 0;
      I := I - 1;
    end;
    if I >= 0 then
      R.Digits[I] := R.Digits[I] + 1
    else
    begin                                        { 99.9 -> 100: one digit 1, one place higher }
      R.Digits[0] := 1;
      R.Count := 1;
      R.Exponent := R.Exponent + 1;
      Exit;
    end;
  end;
  while (R.Count > 0) and (R.Digits[R.Count - 1] = 0) do
    R.Count := R.Count - 1;
  if R.Count = 0 then
    R.Exponent := 0;
end;

{ integer part (Exponent digits; '0' when there are none) and Decimals fraction digits of R, the integer part grouped
  with Thousand when it is not #0 }
function FixedText(const R: TFloatDigits; Decimals: Integer; Thousand, DecSep: Char): string;
var
  I, IntDigits: Integer;
begin
  Result := '';
  IntDigits := R.Exponent;
  if (R.Count = 0) or (IntDigits <= 0) then
    Result := '0'
  else
    for I := 0 to IntDigits - 1 do
    begin
      if (Thousand <> #0) and (I > 0) and ((IntDigits - I) mod 3 = 0) then
        Result := Result + Thousand;
      Result := Result + DigitAt(R, I);
    end;
  if Decimals > 0 then
  begin
    Result := Result + DecSep;
    for I := 0 to Decimals - 1 do
      if R.Count = 0 then
        Result := Result + '0'
      else
        Result := Result + DigitAt(R, IntDigits + I);
  end;
end;

function ExponentText(E, MinDigits: Integer; AlwaysSign: Boolean): string;
var
  T: string;
begin
  if E < 0 then
    Result := '-'
  else if AlwaysSign then
    Result := '+'
  else
    Result := '';
  T := IntToStr(Abs(E));
  while Length(T) < MinDigits do
    T := '0' + T;
  Result := Result + T;
end;

function CurrencyText(const N: string; Negative: Boolean; const FS: TFormatSettings): string;
var
  C: string;
begin
  C := FS.CurrencyString;
  if not Negative then
    case FS.CurrencyFormat of
      1: Result := N + C;
      2: Result := C + ' ' + N;
      3: Result := N + ' ' + C;
    else
      Result := C + N;
    end
  else
    case FS.NegCurrFormat of
      0: Result := '(' + C + N + ')';
      2: Result := C + '-' + N;
      3: Result := C + N + '-';
      4: Result := '(' + N + C + ')';
      5: Result := '-' + N + C;
      6: Result := N + '-' + C;
      7: Result := N + C + '-';
      8: Result := '-' + N + ' ' + C;
      9: Result := '-' + C + ' ' + N;
      10: Result := N + ' ' + C + '-';
      11: Result := C + ' ' + N + '-';
      12: Result := C + ' -' + N;
      13: Result := N + '- ' + C;
      14: Result := '(' + C + ' ' + N + ')';
      15: Result := '(' + N + ' ' + C + ')';
    else
      Result := '-' + C + N;
    end;
end;

function FloatToStrF(Value: Double; Format: TFloatFormat; Precision, Digits: Integer;
  const FS: TFormatSettings): string;
var
  R: TFloatDigits;
  E, I, MinExp: Integer;
  Neg: Boolean;
begin
  if Precision < 2 then
    Precision := 2
  else if Precision > 17 then
    Precision := 17;
  _FloatDigitsPrecision(Value, Precision, R);
  if R.IsNaN then
  begin
    Result := 'Nan';
    Exit;
  end;
  if R.IsInfinite then
  begin
    if R.Negative then
      Result := '-Inf'
    else
      Result := '+Inf';
    Exit;
  end;
  if R.Count = 0 then
    R.Exponent := 1;                             { zero: one integer digit 0 (E = 0) }
  Neg := R.Negative and (R.Count > 0);
  case Format of
    ffGeneral:
      begin
        while (R.Count > 0) and (R.Digits[R.Count - 1] = 0) do
          R.Count := R.Count - 1;
        E := R.Exponent - 1;
        if R.Count = 0 then
          Result := '0'
        else if (E >= -5) and (E < Precision) then
        begin
          if R.Count > R.Exponent then
            Result := FixedText(R, R.Count - R.Exponent, #0, FS.DecimalSeparator)
          else
            Result := FixedText(R, 0, #0, FS.DecimalSeparator);
        end
        else
        begin
          Result := DigitAt(R, 0);
          if R.Count > 1 then
          begin
            Result := Result + FS.DecimalSeparator;
            for I := 1 to R.Count - 1 do
              Result := Result + DigitAt(R, I);
          end;
          MinExp := Digits;
          if MinExp > 4 then
            MinExp := 4;
          if MinExp < 1 then
            MinExp := 1;
          Result := Result + 'E' + ExponentText(E, MinExp, False);
        end;
      end;
    ffExponent:
      begin
        E := R.Exponent - 1;
        if R.Count = 0 then
          E := 0;
        Result := DigitAt(R, 0) + FS.DecimalSeparator;
        for I := 1 to Precision - 1 do
          Result := Result + DigitAt(R, I);
        if Digits < 1 then
          Digits := 1
        else if Digits > 4 then
          Digits := 4;
        Result := Result + 'E' + ExponentText(E, Digits, True);
      end;
  else
    begin                                        { ffFixed, ffNumber, ffCurrency }
      if Digits < 0 then
        Digits := 0
      else if Digits > 18 then
        Digits := 18;
      if R.Count > 0 then
      begin
        if R.Exponent + Digits < 0 then
          R.Count := 0
        else
          RoundDigits(R, R.Exponent + Digits);
      end;
      Neg := Neg and (R.Count > 0);
      if Format = ffFixed then
        Result := FixedText(R, Digits, #0, FS.DecimalSeparator)
      else
        Result := FixedText(R, Digits, FS.ThousandSeparator, FS.DecimalSeparator);
      if Format = ffCurrency then
      begin
        Result := CurrencyText(Result, Neg, FS);
        Exit;
      end;
    end;
  end;
  if Neg then
    Result := '-' + Result;
end;

function FloatToStrF(Value: Double; Format: TFloatFormat; Precision, Digits: Integer): string;
begin
  Result := FloatToStrF(Value, Format, Precision, Digits, FormatSettings);
end;

function FloatToStr(Value: Double; const FS: TFormatSettings): string;
begin
  Result := FloatToStrF(Value, ffGeneral, 15, 0, FS);
end;

function FloatToStr(Value: Double): string;
begin
  Result := FloatToStrF(Value, ffGeneral, 15, 0, FormatSettings);
end;

{ ----- FormatFloat picture strings (RTL-239) ----- }

type
  TPictureInfo = record
    IntPlaces, DecPlaces: Integer;               { digit placeholders before / after the decimal point }
    FirstZero: Integer;                          { index of the first '0' among the integer placeholders; IntPlaces when none }
    LastZero: Integer;                           { decimal placeholders up to and including the last '0'; 0 when none }
    HasPoint, Thousands, Scientific: Boolean;
  end;

{ section Index (0-based; sections are separated by ';' outside quotes) of Fmt as Fmt[First..Last];
  Last < First when the section is empty or missing }
procedure PictureSection(const Fmt: string; Index: Integer; out First, Last: Integer);
var
  I, N: Integer;
  Q: Char;
begin
  First := 1;
  N := 0;
  I := 1;
  Q := #0;
  while I <= Length(Fmt) do
  begin
    if Q <> #0 then
    begin
      if Fmt[I] = Q then
        Q := #0;
    end
    else if (Fmt[I] = '''') or (Fmt[I] = '"') then
      Q := Fmt[I]
    else if Fmt[I] = ';' then
    begin
      if N = Index then
        Break;
      N := N + 1;
      First := I + 1;
    end;
    I := I + 1;
  end;
  if N = Index then
    Last := I - 1
  else
  begin
    First := 1;
    Last := 0;
  end;
end;

{ the section for a zero / negative / positive value: the third / second section when it is not empty, else the first }
function PickPictureSection(const Fmt: string; IsZero, IsNegative: Boolean; out First, Last: Integer): Integer;
begin
  Result := 0;
  if IsZero then
    Result := 2
  else if IsNegative then
    Result := 1;
  if Result > 0 then
  begin
    PictureSection(Fmt, Result, First, Last);
    if Last >= First then
      Exit;
  end;
  Result := 0;
  PictureSection(Fmt, 0, First, Last);
end;

{ the picture element at Fmt[I] (I <= Stop), I moved past it: '0', '#', '.', ',', 'E' (exponent with a sign always),
  'e' (exponent with a sign for negatives only) or 'L' (literal text Lit). For an exponent Lit is the letter as
  written and ExpDigits the number of '0' after the sign (at most 4 are counted). }
function PictureToken(const Fmt: string; var I: Integer; Stop: Integer; out Lit: string; out ExpDigits: Integer): Char;
var
  C: Char;
begin
  Lit := '';
  ExpDigits := 0;
  C := Fmt[I];
  I := I + 1;
  if (C = '0') or (C = '#') or (C = '.') or (C = ',') then
    Result := C
  else if (C = '''') or (C = '"') then
  begin
    Result := 'L';
    while (I <= Stop) and (Fmt[I] <> C) do
    begin
      Lit := Lit + Fmt[I];
      I := I + 1;
    end;
    I := I + 1;                                  { the closing quote (or the end of an unterminated literal) }
  end
  else if ((C = 'E') or (C = 'e')) and (I <= Stop) and ((Fmt[I] = '+') or (Fmt[I] = '-')) then
  begin
    if Fmt[I] = '+' then
      Result := 'E'
    else
      Result := 'e';
    Lit := C;
    I := I + 1;
    while (I <= Stop) and (Fmt[I] = '0') do
    begin
      if ExpDigits < 4 then
        ExpDigits := ExpDigits + 1;
      I := I + 1;
    end;
  end
  else
  begin
    Result := 'L';
    Lit := C;
  end;
end;

procedure ScanPicture(const Fmt: string; First, Last: Integer; out Info: TPictureInfo);
var
  I, ExpDigits: Integer;
  Lit: string;
  K: Char;
begin
  Info.IntPlaces := 0;
  Info.DecPlaces := 0;
  Info.FirstZero := -1;
  Info.LastZero := 0;
  Info.HasPoint := False;
  Info.Thousands := False;
  Info.Scientific := False;
  I := First;
  while I <= Last do
  begin
    K := PictureToken(Fmt, I, Last, Lit, ExpDigits);
    if (K = '0') or (K = '#') then
    begin
      if Info.HasPoint then
      begin
        Info.DecPlaces := Info.DecPlaces + 1;
        if K = '0' then
          Info.LastZero := Info.DecPlaces;
      end
      else
      begin
        if (K = '0') and (Info.FirstZero < 0) then
          Info.FirstZero := Info.IntPlaces;
        Info.IntPlaces := Info.IntPlaces + 1;
      end;
    end
    else if K = '.' then
      Info.HasPoint := True
    else if K = ',' then
      Info.Thousands := True
    else if (K = 'E') or (K = 'e') then
      Info.Scientific := True;
  end;
  if Info.FirstZero < 0 then
    Info.FirstZero := Info.IntPlaces;
end;

{ rounds R half away from zero to Keep digits (Keep >= 0) }
procedure RoundDigitsHalfUp(var R: TFloatDigits; Keep: Integer);
begin
  if (Keep < R.Count) and (R.Digits[Keep] = 5) then
    R.Digits[Keep] := 6;                         { a tie (or more) rounds up whatever follows }
  RoundDigits(R, Keep);
end;

{ the text of section Fmt[First..Last] for the digits R, already rounded for Info (no sign) }
function RenderPicture(const Fmt: string; First, Last: Integer; const Info: TPictureInfo; const R: TFloatDigits;
  const FS: TFormatSettings): string;
var
  I, ExpDigits, Base, IntDigits, DecEmit, IntSeen, DecSeen, K, ExpValue: Integer;
  Lit: string;
  T: Char;
  PointDone, Group: Boolean;
begin
  Result := '';
  { digit index Base is the first digit after the decimal point }
  ExpValue := 0;
  if Info.Scientific then
  begin
    Base := Info.IntPlaces;
    IntDigits := Info.IntPlaces;
    if R.Count > 0 then
      ExpValue := R.Exponent - Info.IntPlaces;
  end
  else
  begin
    Base := R.Exponent;
    IntDigits := 0;
    if (R.Count > 0) and (Base > 0) then
      IntDigits := Base;
  end;
  DecEmit := 0;
  if R.Count > 0 then
    DecEmit := R.Count - Base;
  if DecEmit > Info.DecPlaces then
    DecEmit := Info.DecPlaces;
  if DecEmit < Info.LastZero then
    DecEmit := Info.LastZero;
  Group := Info.Thousands and not Info.Scientific and (FS.ThousandSeparator <> #0);
  IntSeen := 0;
  DecSeen := 0;
  PointDone := False;
  I := First;
  while I <= Last do
  begin
    T := PictureToken(Fmt, I, Last, Lit, ExpDigits);
    if T = 'L' then
      Result := Result + Lit
    else if (T = 'E') or (T = 'e') then
    begin
      if ExpDigits < 1 then
        ExpDigits := 1;
      Result := Result + Lit + ExponentText(ExpValue, ExpDigits, T = 'E');
    end
    else if T = '.' then
    begin
      if not PointDone then
      begin
        PointDone := True;
        if Info.IntPlaces = 0 then               { no integer placeholder: the integer digits go before the point }
          for K := 0 to IntDigits - 1 do
          begin
            Result := Result + DigitAt(R, K);
            if Group and (IntDigits - 1 - K > 0) and ((IntDigits - 1 - K) mod 3 = 0) then
              Result := Result + FS.ThousandSeparator;
          end;
        if DecEmit > 0 then
          Result := Result + FS.DecimalSeparator;
      end;
    end
    else if (T = '0') or (T = '#') then
    begin
      if PointDone then
      begin
        if DecSeen < DecEmit then
          Result := Result + DigitAt(R, Base + DecSeen);
        DecSeen := DecSeen + 1;
      end
      else
      begin
        if IntSeen = 0 then                      { digits the picture has no placeholder for }
          for K := 0 to IntDigits - Info.IntPlaces - 1 do
          begin
            Result := Result + DigitAt(R, K);
            if Group and ((IntDigits - 1 - K) mod 3 = 0) then
              Result := Result + FS.ThousandSeparator;
          end;
        K := IntDigits - Info.IntPlaces + IntSeen;
        if (K >= 0) or (IntSeen >= Info.FirstZero) then
        begin
          if K >= 0 then
            Result := Result + DigitAt(R, K)
          else
            Result := Result + '0';
          K := Info.IntPlaces - 1 - IntSeen;     { integer positions to the right }
          if Group and (K > 0) and (K mod 3 = 0) then
            Result := Result + FS.ThousandSeparator;
        end;
        IntSeen := IntSeen + 1;
      end;
    end;
  end;
end;

function FormatFloat(const Fmt: string; Value: Double; const FS: TFormatSettings): string;
var
  R: TFloatDigits;
  Info: TPictureInfo;
  First, Last, Sec, Keep: Integer;
  Neg: Boolean;
begin
  _FloatDigitsPrecision(Value, 15, R);
  if R.IsNaN or R.IsInfinite then
  begin
    Result := FloatToStrF(Value, ffGeneral, 15, 0, FS);
    Exit;
  end;
  while (R.Count > 0) and (R.Digits[R.Count - 1] = 0) do
    R.Count := R.Count - 1;
  Neg := R.Negative and (R.Count > 0);
  Sec := PickPictureSection(Fmt, R.Count = 0, Neg, First, Last);
  if Last < First then
  begin
    Result := FloatToStrF(Value, ffGeneral, 15, 0, FS);
    Exit;
  end;
  ScanPicture(Fmt, First, Last, Info);
  if R.Count > 0 then
  begin
    if Info.Scientific then
      Keep := Info.IntPlaces + Info.DecPlaces
    else
      Keep := R.Exponent + Info.DecPlaces;
    if Keep < 0 then
      R.Count := 0
    else
      RoundDigitsHalfUp(R, Keep);
    if R.Count = 0 then
    begin                                        { rounded to zero: formatted as zero, without a sign }
      Neg := False;
      Sec := PickPictureSection(Fmt, True, False, First, Last);
      if Last < First then
      begin
        Result := '0';
        Exit;
      end;
      ScanPicture(Fmt, First, Last, Info);
    end;
  end;
  if R.Count = 0 then
    R.Exponent := 0;
  Result := RenderPicture(Fmt, First, Last, Info, R, FS);
  if Neg and (Sec = 0) then
    Result := '-' + Result;
end;

function FormatFloat(const Fmt: string; Value: Double): string;
begin
  Result := FormatFloat(Fmt, Value, FormatSettings);
end;

function TryStrToFloat(const S: string; out Value: Double; const FS: TFormatSettings): Boolean;
var
  T: string;
  I, Code: Integer;
  HasDigit: Boolean;
begin
  Result := False;
  T := Trim(S);
  HasDigit := False;
  for I := 1 to Length(T) do
    if T[I] = FS.DecimalSeparator then
      T[I] := '.'
    else if T[I] = '.' then
      Exit                                       { '.' is not the decimal separator of FS }
    else if (T[I] >= '0') and (T[I] <= '9') then
      HasDigit := True;
  if T = '' then
    Exit;
  Val(T, Value, Code);
  if Code <> 0 then
    Exit;
  { a decimal exponent beyond the Double range is a failure, not an infinity (RTL-238) }
  if HasDigit and ((Value > 1.7976931348623157E308) or (Value < -1.7976931348623157E308)) then
    Exit;
  Result := True;
end;

function TryStrToFloat(const S: string; out Value: Double): Boolean;
begin
  Result := TryStrToFloat(S, Value, FormatSettings);
end;

function StrToFloatDef(const S: string; const Default: Double; const FS: TFormatSettings): Double;
begin
  if not TryStrToFloat(S, Result, FS) then
    Result := Default;
end;

function StrToFloatDef(const S: string; const Default: Double): Double;
begin
  Result := StrToFloatDef(S, Default, FormatSettings);
end;

function StrToFloat(const S: string; const FS: TFormatSettings): Double;
begin
  if not TryStrToFloat(S, Result, FS) then
    ConvertError(SInvalidFloat, S);
end;

function StrToFloat(const S: string): Double;
begin
  Result := StrToFloat(S, FormatSettings);
end;

{ ------------------------------------------------------------------------------------------ date and time (6) }

function IsLeapYear(Year: Word): Boolean;
begin
  Result := (Year mod 4 = 0) and ((Year mod 100 <> 0) or (Year mod 400 = 0));
end;

function DaysInMonth(Year, Month: Word): Word;
begin
  case Month of
    2:
      if IsLeapYear(Year) then
        Result := 29
      else
        Result := 28;
    4, 6, 9, 11: Result := 30;
  else
    Result := 31;
  end;
end;

function TryEncodeDate(Year, Month, Day: Word; out Date: TDateTime): Boolean;
var
  Y1, Days, M: Integer;
begin
  Result := (Year >= 1) and (Year <= 9999) and (Month >= 1) and (Month <= 12) and (Day >= 1) and
    (Day <= DaysInMonth(Year, Month));
  if not Result then
    Exit;
  Y1 := Year - 1;
  Days := Y1 * 365 + Y1 div 4 - Y1 div 100 + Y1 div 400 + Day;
  for M := 1 to Month - 1 do
    Days := Days + DaysInMonth(Year, M);
  Date := Days - DateDelta;
end;

function TryEncodeTime(Hour, Min, Sec, MSec: Word; out Time: TDateTime): Boolean;
begin
  Result := (Hour < 24) and (Min < 60) and (Sec < 60) and (MSec < 1000);
  if Result then
    Time := (Hour * 3600000 + Min * 60000 + Sec * 1000 + MSec) / MSecsPerDay;
end;

function EncodeDate(Year, Month, Day: Word): TDateTime;
begin
  if not TryEncodeDate(Year, Month, Day, Result) then
    raise EConvertError.Create(SDateEncodeError);
end;

function EncodeTime(Hour, Min, Sec, MSec: Word): TDateTime;
begin
  if not TryEncodeTime(Hour, Min, Sec, MSec, Result) then
    raise EConvertError.Create(STimeEncodeError);
end;

{ the day number of DateTime counted from 0001-01-01 = 1 }
function DayNumber(const DateTime: TDateTime): Integer;
begin
  Result := Integer(Trunc(DateTime)) + DateDelta;
end;

function DecodeDateFully(const DateTime: TDateTime; var Year, Month, Day, DOW: Word): Boolean;
var
  T, Y400, Y100, Y4, Y1, M, L: Integer;
begin
  T := DayNumber(DateTime);
  if (T < 1) or (T > 3652059) then
  begin                                          { outside 0001-01-01 ... 9999-12-31 }
    Year := 0;
    Month := 0;
    Day := 0;
    DOW := 0;
    Result := False;
    Exit;
  end;
  DOW := T mod 7 + 1;
  T := T - 1;
  Y400 := T div 146097;
  T := T mod 146097;
  Y100 := T div 36524;
  T := T mod 36524;
  if Y100 = 4 then
  begin
    Y100 := 3;
    T := T + 36524;
  end;
  Y4 := T div 1461;
  T := T mod 1461;
  Y1 := T div 365;
  T := T mod 365;
  if Y1 = 4 then
  begin
    Y1 := 3;
    T := T + 365;
  end;
  Year := Y400 * 400 + Y100 * 100 + Y4 * 4 + Y1 + 1;
  M := 1;
  L := DaysInMonth(Year, M);
  while T >= L do
  begin
    T := T - L;
    M := M + 1;
    L := DaysInMonth(Year, M);
  end;
  Month := M;
  Day := T + 1;
  Result := IsLeapYear(Year);
end;

procedure DecodeDate(const DateTime: TDateTime; var Year, Month, Day: Word);
var
  DOW: Word;
begin
  DecodeDateFully(DateTime, Year, Month, Day, DOW);
end;

procedure DecodeTime(const DateTime: TDateTime; var Hour, Min, Sec, MSec: Word);
var
  MS: Int64;
begin
  MS := Round(Abs(Frac(DateTime)) * MSecsPerDay);
  if MS >= MSecsPerDay then
    MS := MSecsPerDay - 1;                       { RTL-242: no carry into the date }
  Hour := MS div 3600000;
  Min := (MS div 60000) mod 60;
  Sec := (MS div 1000) mod 60;
  MSec := MS mod 1000;
end;

function DayOfWeek(const DateTime: TDateTime): Word;
begin
  Result := DayNumber(DateTime) mod 7 + 1;
end;

function IncMonth(const DateTime: TDateTime; NumberOfMonths: Integer): TDateTime;
var
  Y, M, D: Word;
  Months: Integer;
  T: Double;
begin
  DecodeDate(DateTime, Y, M, D);
  Months := Y * 12 + (M - 1) + NumberOfMonths;
  Y := Months div 12;
  M := Months mod 12 + 1;
  if D > DaysInMonth(Y, M) then
    D := DaysInMonth(Y, M);
  Result := EncodeDate(Y, M, D);
  T := Abs(Frac(DateTime));
  if Result >= 0 then
    Result := Result + T
  else
    Result := Result - T;
end;

procedure DateTimeToSystemTime(const DateTime: TDateTime; var SystemTime: TSystemTime);
var
  DOW: Word;
begin
  DecodeDateFully(DateTime, SystemTime.wYear, SystemTime.wMonth, SystemTime.wDay, DOW);
  SystemTime.wDayOfWeek := DOW - 1;
  DecodeTime(DateTime, SystemTime.wHour, SystemTime.wMinute, SystemTime.wSecond, SystemTime.wMilliseconds);
end;

function SystemTimeToDateTime(const SystemTime: TSystemTime): TDateTime;
begin
  Result := EncodeDate(SystemTime.wYear, SystemTime.wMonth, SystemTime.wDay);
  if Result >= 0 then
    Result := Result + EncodeTime(SystemTime.wHour, SystemTime.wMinute, SystemTime.wSecond, SystemTime.wMilliseconds)
  else
    Result := Result - EncodeTime(SystemTime.wHour, SystemTime.wMinute, SystemTime.wSecond, SystemTime.wMilliseconds);
end;

{ ---------------------------------------------------------------------------------------------- the clock (6) }

function Now: TDateTime;
var
  ST: TSystemTime;
begin
  GetLocalTime(ST);
  Result := SystemTimeToDateTime(ST);
end;

function Date: TDateTime;
var
  ST: TSystemTime;
begin
  GetLocalTime(ST);
  Result := EncodeDate(ST.wYear, ST.wMonth, ST.wDay);
end;

function Time: TDateTime;
var
  ST: TSystemTime;
begin
  GetLocalTime(ST);
  Result := EncodeTime(ST.wHour, ST.wMinute, ST.wSecond, ST.wMilliseconds);
end;

function CurrentYear: Word;
var
  ST: TSystemTime;
begin
  GetLocalTime(ST);
  Result := ST.wYear;
end;

procedure Sleep(Milliseconds: Cardinal);
begin
  Windows.Sleep(Milliseconds);
end;

function GetTickCount64: UInt64;
begin
  Result := Windows.GetTickCount64;
end;

function GetEnvironmentVariable(const Name: string): string;
var
  Len: DWORD;
begin
  Result := '';
  if Name = '' then
    Exit;
  SetLength(Result, 256);
  Len := GetEnvironmentVariableW(PChar(Name), PChar(Result), 256);
  if Len > 256 then
  begin
    { too small: Len is the size needed, terminator included }
    SetLength(Result, Len);
    Len := GetEnvironmentVariableW(PChar(Name), PChar(Result), Len);
  end;
  SetLength(Result, Len);
end;

{ ------------------------------------------------------------------------------------ FormatDateTime (RTL-243) }

const
  MaxDateExpansionDepth = 2;           { RTL-244 }

{ V in decimal, left-padded with zeros to Width digits }
function PadNum(V: Integer; Width: Integer): string;
begin
  Result := IntToStr(V);
  while Length(Result) < Width do
    Result := '0' + Result;
end;

function UpChar(C: Char): Char;
begin
  if (C >= 'a') and (C <= 'z') then
    Result := Char(Ord(C) - 32)
  else
    Result := C;
end;

{ the text of F from I on starts with Pattern (ASCII case-insensitive) }
function MatchesAt(const F: string; I: Integer; const Pattern: string): Boolean;
var
  K: Integer;
begin
  Result := False;
  if I + Length(Pattern) - 1 > Length(F) then
    Exit;
  for K := 1 to Length(Pattern) do
    if UpChar(F[I + K - 1]) <> UpChar(Pattern[K]) then
      Exit;
  Result := True;
end;

{ the length of the AM/PM specifier at F[I] ('am/pm' 5, 'ampm' 4, 'a/p' 3), or 0 }
function AmPmLength(const F: string; I: Integer): Integer;
begin
  if MatchesAt(F, I, 'am/pm') then
    Result := 5
  else if MatchesAt(F, I, 'ampm') then
    Result := 4
  else if MatchesAt(F, I, 'a/p') then
    Result := 3
  else
    Result := 0;
end;

{ F contains an AM/PM specifier outside quoted text }
function HasAmPm(const F: string): Boolean;
var
  I: Integer;
  Q: Char;
begin
  Result := False;
  I := 1;
  while I <= Length(F) do
  begin
    if (F[I] = '''') or (F[I] = '"') then
    begin
      Q := F[I];
      I := I + 1;
      while (I <= Length(F)) and (F[I] <> Q) do
        I := I + 1;
    end
    else if AmPmLength(F, I) > 0 then
    begin
      Result := True;
      Exit;
    end;
    I := I + 1;
  end;
end;

{ appends DateTime formatted with F to R; F is a format at expansion depth Depth (the caller's format is depth 0) }
procedure AppendDateTime(var R: string; const F: string; DateTime: TDateTime; const FS: TFormatSettings; Depth: Integer);
var
  Y, Mo, D, H, Mi, S, MS: Word;
  I, Run, Take, Len: Integer;
  C, U, Q: Char;
  Twelve, AfterHour: Boolean;
begin
  if Depth > MaxDateExpansionDepth then
    Exit;
  DecodeDate(DateTime, Y, Mo, D);
  DecodeTime(DateTime, H, Mi, S, MS);
  Twelve := HasAmPm(F);
  AfterHour := False;
  I := 1;
  while I <= Length(F) do
  begin
    C := F[I];
    if (C = '''') or (C = '"') then
    begin
      Q := C;
      I := I + 1;
      while (I <= Length(F)) and (F[I] <> Q) do
      begin
        R := R + F[I];
        I := I + 1;
      end;
      I := I + 1;
      Continue;
    end;
    Len := AmPmLength(F, I);
    if Len > 0 then
    begin
      if Len = 4 then
      begin
        if H < 12 then
          R := R + FS.TimeAMString
        else
          R := R + FS.TimePMString;
      end
      else if H < 12 then
        R := R + Copy(F, I, Len div 2)
      else
        R := R + Copy(F, I + Len div 2 + 1, Len div 2);
      I := I + Len;
      AfterHour := False;
      Continue;
    end;
    U := UpChar(C);
    Run := 1;
    while (I + Run <= Length(F)) and (UpChar(F[I + Run]) = U) do
      Run := Run + 1;
    Take := 1;
    case U of
      'D':
        begin
          if Run > 6 then
            Take := 6
          else
            Take := Run;
          case Take of
            1: R := R + IntToStr(D);
            2: R := R + PadNum(D, 2);
            3: R := R + FS.ShortDayNames[DayOfWeek(DateTime)];
            4: R := R + FS.LongDayNames[DayOfWeek(DateTime)];
            5: AppendDateTime(R, FS.ShortDateFormat, DateTime, FS, Depth + 1);
          else
            AppendDateTime(R, FS.LongDateFormat, DateTime, FS, Depth + 1);
          end;
        end;
      'M':
        begin
          if Run > 4 then
            Take := 4
          else
            Take := Run;
          if AfterHour and (Take <= 2) then
            R := R + PadNum(Mi, Take)
          else
            case Take of
              1: R := R + IntToStr(Mo);
              2: R := R + PadNum(Mo, 2);
              3: R := R + FS.ShortMonthNames[Mo];
            else
              R := R + FS.LongMonthNames[Mo];
            end;
        end;
      'Y':
        begin
          if Run > 4 then
            Take := 4
          else
            Take := Run;
          if Take <= 2 then
            R := R + PadNum(Y mod 100, 2)
          else
            R := R + PadNum(Y, 4);
        end;
      'H':
        begin
          if Run > 2 then
            Take := 2
          else
            Take := Run;
          if Twelve then
          begin
            if H mod 12 = 0 then
              R := R + PadNum(12, Take)
            else
              R := R + PadNum(H mod 12, Take);
          end
          else
            R := R + PadNum(H, Take);
        end;
      'N':
        begin
          if Run > 2 then
            Take := 2
          else
            Take := Run;
          R := R + PadNum(Mi, Take);
        end;
      'S':
        begin
          if Run > 2 then
            Take := 2
          else
            Take := Run;
          R := R + PadNum(S, Take);
        end;
      'Z':
        begin
          if Run > 3 then
            Take := 3
          else
            Take := Run;
          if Take = 3 then
            R := R + PadNum(MS, 3)
          else
            R := R + IntToStr(MS);
        end;
      'T':
        begin
          if Run > 2 then
            Take := 2
          else
            Take := Run;
          if Take = 1 then
            AppendDateTime(R, FS.ShortTimeFormat, DateTime, FS, Depth + 1)
          else
            AppendDateTime(R, FS.LongTimeFormat, DateTime, FS, Depth + 1);
        end;
      'C':
        begin
          AppendDateTime(R, FS.ShortDateFormat, DateTime, FS, Depth + 1);
          if (H <> 0) or (Mi <> 0) or (S <> 0) or (MS <> 0) then
          begin
            R := R + ' ';
            AppendDateTime(R, FS.LongTimeFormat, DateTime, FS, Depth + 1);
          end;
        end;
      '/':
        R := R + FS.DateSeparator;
      ':':
        R := R + FS.TimeSeparator;
    else
      R := R + C;
    end;
    { m means minutes when the nearest preceding field specifier is h/hh; separators and literals do not count }
    case U of
      'H':
        AfterHour := True;
      'D', 'M', 'Y', 'N', 'S', 'Z', 'T', 'C':
        AfterHour := False;
    end;
    I := I + Take;
  end;
end;

procedure DateTimeToString(var Result: string; const Fmt: string; DateTime: TDateTime; const FS: TFormatSettings);
var
  R: string;
begin
  R := '';
  if Fmt = '' then
    AppendDateTime(R, 'c', DateTime, FS, 0)
  else
    AppendDateTime(R, Fmt, DateTime, FS, 0);
  Result := R;
end;

procedure DateTimeToString(var Result: string; const Fmt: string; DateTime: TDateTime);
begin
  DateTimeToString(Result, Fmt, DateTime, FormatSettings);
end;

function FormatDateTime(const Fmt: string; DateTime: TDateTime; const FS: TFormatSettings): string;
begin
  DateTimeToString(Result, Fmt, DateTime, FS);
end;

function FormatDateTime(const Fmt: string; DateTime: TDateTime): string;
begin
  DateTimeToString(Result, Fmt, DateTime, FormatSettings);
end;

function DateToStr(const DateTime: TDateTime; const FS: TFormatSettings): string;
begin
  Result := FormatDateTime(FS.ShortDateFormat, DateTime, FS);
end;

function DateToStr(const DateTime: TDateTime): string;
begin
  Result := DateToStr(DateTime, FormatSettings);
end;

function TimeToStr(const DateTime: TDateTime; const FS: TFormatSettings): string;
begin
  Result := FormatDateTime(FS.LongTimeFormat, DateTime, FS);
end;

function TimeToStr(const DateTime: TDateTime): string;
begin
  Result := TimeToStr(DateTime, FormatSettings);
end;

function DateTimeToStr(const DateTime: TDateTime; const FS: TFormatSettings): string;
begin
  Result := FormatDateTime('c', DateTime, FS);
end;

function DateTimeToStr(const DateTime: TDateTime): string;
begin
  Result := DateTimeToStr(DateTime, FormatSettings);
end;

{ ------------------------------------------------------------------------------- date/time parsing (RTL-245) }

function IsDigitChar(C: Char): Boolean;
begin
  Result := (C >= '0') and (C <= '9');
end;

{ 1 .. MaxDigits decimal digits of S from I on; I moves past every digit read (Digits counts them) }
function ScanNumber(const S: string; var I: Integer; MaxDigits: Integer; out Value, Digits: Integer): Boolean;
begin
  Value := 0;
  Digits := 0;
  while (I <= Length(S)) and IsDigitChar(S[I]) do
  begin
    if Digits < MaxDigits then
      Value := Value * 10 + (Ord(S[I]) - Ord('0'));
    Digits := Digits + 1;
    I := I + 1;
  end;
  Result := (Digits > 0) and (Digits <= MaxDigits);
end;

{ 'Y', 'M', 'D' in the order of their first occurrence in ShortDateFormat; 'YMD' when a letter is missing }
function DateOrder(const FS: TFormatSettings): string;
var
  I: Integer;
  U: Char;
begin
  Result := '';
  for I := 1 to Length(FS.ShortDateFormat) do
  begin
    U := UpChar(FS.ShortDateFormat[I]);
    if ((U = 'Y') or (U = 'M') or (U = 'D')) and (Pos(U, Result) = 0) then
      Result := Result + U;
  end;
  if Length(Result) <> 3 then
    Result := 'YMD';
end;

{ a date of 2 or 3 numeric parts from S[I] on; I ends after the last part (the caller checks what follows) }
function ScanDate(const S: string; var I: Integer; const FS: TFormatSettings; out Value: TDateTime): Boolean;
var
  Parts, Digits: array[0..2] of Integer;
  N, K, Y, M, D, YDigits, Start: Integer;
  Order: string;
  C: Char;
begin
  Value := 0;
  Result := False;
  N := 0;
  while True do
  begin
    if not ScanNumber(S, I, 4, Parts[N], Digits[N]) then
      Exit;
    N := N + 1;
    if (N = 3) or (I >= Length(S)) then
      Break;
    C := S[I];
    if ((C = FS.DateSeparator) or (C = '/') or (C = '-') or (C = '.')) and IsDigitChar(S[I + 1]) then
      I := I + 1
    else
      Break;
  end;
  if N < 2 then
    Exit;
  Order := DateOrder(FS);
  Y := CurrentYear;
  YDigits := 4;
  M := 0;
  D := 0;
  if N = 2 then
  begin
    if Pos('M', Order) < Pos('D', Order) then
    begin
      M := Parts[0];
      D := Parts[1];
    end
    else
    begin
      D := Parts[0];
      M := Parts[1];
    end;
  end
  else
    for K := 0 to 2 do
      case Order[K + 1] of
        'Y':
          begin
            Y := Parts[K];
            YDigits := Digits[K];
          end;
        'M': M := Parts[K];
      else
        D := Parts[K];
      end;
  if YDigits <= 2 then
  begin
    Start := CurrentYear - FS.TwoDigitYearCenturyWindow;
    Y := (Start div 100) * 100 + Y;
    if Y < Start then
      Y := Y + 100;
  end;
  Result := (Y >= 1) and (Y <= 9999) and TryEncodeDate(Word(Y), Word(M), Word(D), Value);
end;

{ the text Pattern (case-insensitive) is non-empty and equals S from I to the end }
function RestIs(const S: string; I: Integer; const Pattern: string): Boolean;
begin
  Result := (Pattern <> '') and (Length(S) - I + 1 = Length(Pattern)) and MatchesAt(S, I, Pattern);
end;

{ h[:n[:s[.z]]] [AM/PM] from S[I] to the end of S }
function ScanTime(const S: string; I: Integer; const FS: TFormatSettings; out Value: TDateTime): Boolean;
var
  Fields: array[0..2] of Integer;
  Count, MS, Digits: Integer;
  IsAm, IsPm: Boolean;
begin
  Value := 0;
  Result := False;
  Fields[1] := 0;
  Fields[2] := 0;
  MS := 0;
  if not ScanNumber(S, I, 2, Fields[0], Digits) then
    Exit;
  Count := 1;
  while (Count < 3) and (I <= Length(S)) and ((S[I] = FS.TimeSeparator) or (S[I] = ':')) do
  begin
    I := I + 1;
    if not ScanNumber(S, I, 2, Fields[Count], Digits) then
      Exit;
    Count := Count + 1;
  end;
  if (Count = 3) and (I <= Length(S)) and (S[I] = '.') then
  begin
    I := I + 1;
    if not ScanNumber(S, I, 3, MS, Digits) then
      Exit;
  end;
  while (I <= Length(S)) and (S[I] = ' ') do
    I := I + 1;
  if I <= Length(S) then
  begin
    IsAm := RestIs(S, I, FS.TimeAMString) or RestIs(S, I, 'AM');
    IsPm := not IsAm and (RestIs(S, I, FS.TimePMString) or RestIs(S, I, 'PM'));
    if not IsAm and not IsPm then
      Exit;
    if Fields[0] > 12 then
      Exit;
    if Fields[0] = 12 then
      Fields[0] := 0;
    if IsPm then
      Fields[0] := Fields[0] + 12;
  end;
  Result := TryEncodeTime(Word(Fields[0]), Word(Fields[1]), Word(Fields[2]), Word(MS), Value);
end;

function TryStrToDate(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean;
var
  T: string;
  I: Integer;
begin
  T := Trim(S);
  I := 1;
  Result := ScanDate(T, I, FS, Value) and (I > Length(T));
  if not Result then
    Value := 0;
end;

function TryStrToDate(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToDate(S, Value, FormatSettings);
end;

function TryStrToTime(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean;
begin
  Result := ScanTime(Trim(S), 1, FS, Value);
end;

function TryStrToTime(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToTime(S, Value, FormatSettings);
end;

{ a date, then optionally white space and a time; a time alone gives date 0 }
function TryStrToDateTime(const S: string; out Value: TDateTime; const FS: TFormatSettings): Boolean;
var
  T: string;
  I: Integer;
  D, Tm: TDateTime;
begin
  T := Trim(S);
  I := 1;
  Value := 0;
  if ScanDate(T, I, FS, D) then
  begin
    if I > Length(T) then
    begin
      Value := D;
      Result := True;
      Exit;
    end;
    Result := False;
    if (T[I] <> ' ') and (T[I] <> #9) then
      Exit;
    while (I <= Length(T)) and ((T[I] = ' ') or (T[I] = #9)) do
      I := I + 1;
    if not ScanTime(T, I, FS, Tm) then
      Exit;
    if D >= 0 then
      Value := D + Tm
    else
      Value := D - Tm;
    Result := True;
  end
  else
    Result := ScanTime(T, 1, FS, Value);
end;

function TryStrToDateTime(const S: string; out Value: TDateTime): Boolean;
begin
  Result := TryStrToDateTime(S, Value, FormatSettings);
end;

function StrToDate(const S: string; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToDate(S, Result, FS) then
    ConvertError(SInvalidDate, S);
end;

function StrToDate(const S: string): TDateTime;
begin
  Result := StrToDate(S, FormatSettings);
end;

function StrToTime(const S: string; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToTime(S, Result, FS) then
    ConvertError(SInvalidTime, S);
end;

function StrToTime(const S: string): TDateTime;
begin
  Result := StrToTime(S, FormatSettings);
end;

function StrToDateTime(const S: string; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToDateTime(S, Result, FS) then
    ConvertError(SInvalidDateTime, S);
end;

function StrToDateTime(const S: string): TDateTime;
begin
  Result := StrToDateTime(S, FormatSettings);
end;

function StrToDateDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToDate(S, Result, FS) then
    Result := Default;
end;

function StrToDateDef(const S: string; const Default: TDateTime): TDateTime;
begin
  Result := StrToDateDef(S, Default, FormatSettings);
end;

function StrToTimeDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToTime(S, Result, FS) then
    Result := Default;
end;

function StrToTimeDef(const S: string; const Default: TDateTime): TDateTime;
begin
  Result := StrToTimeDef(S, Default, FormatSettings);
end;

function StrToDateTimeDef(const S: string; const Default: TDateTime; const FS: TFormatSettings): TDateTime;
begin
  if not TryStrToDateTime(S, Result, FS) then
    Result := Default;
end;

function StrToDateTimeDef(const S: string; const Default: TDateTime): TDateTime;
begin
  Result := StrToDateTimeDef(S, Default, FormatSettings);
end;

{ ------------------------------------------------------------------------------------------------ Format (4) }

procedure FormatError(const Msg, Fmt: string);
begin
  raise EConvertError.CreateFmt(Msg, [Fmt]);
end;

{ RTL-223 's': at most Prec UTF-16 units, a surrogate pair cut by the limit is dropped whole }
function CutText(const S: string; Prec: Integer): string;
begin
  if (Prec < 0) or (Prec >= Length(S)) then
    Result := S
  else
  begin
    if (Prec > 0) and (S[Prec] >= #$D800) and (S[Prec] <= #$DBFF) then
      Prec := Prec - 1;
    Result := Copy(S, 1, Prec);
  end;
end;

function ZeroPad(const Digits: string; Prec: Integer): string;
begin
  Result := Digits;
  if Prec <= 32 then
    while Length(Result) < Prec do
      Result := '0' + Result;
end;

function Format(const Fmt: string; const Args: array of const; const FS: TFormatSettings): string;
var
  I, N, ArgIndex, Width, Prec, Num: Integer;
  LeftAlign, HasNum, HasStar: Boolean;
  C: Char;
  T: string;
  A: TVarRec;
  V: Double;
  I64: Int64;

  function TakeArg: TVarRec;
  begin
    if ArgIndex > High(Args) then
      FormatError(SArgumentMissing, Fmt);
    Result := Args[ArgIndex];
    ArgIndex := ArgIndex + 1;
  end;

  { number | '*' at I; HasNum False when neither is there }
  procedure ReadNumber;
  var
    Star: TVarRec;
  begin
    HasNum := False;
    HasStar := False;
    Num := 0;
    if (I <= N) and (Fmt[I] = '*') then
    begin
      I := I + 1;
      HasNum := True;
      HasStar := True;
      Star := TakeArg;
      if Star.VType <> vtInteger then
        FormatError(SInvalidFormat, Fmt);
      Num := Star.VInteger;
    end
    else
      while (I <= N) and (Fmt[I] >= '0') and (Fmt[I] <= '9') do
      begin
        HasNum := True;
        if Num < 100000000 then
          Num := Num * 10 + (Ord(Fmt[I]) - Ord('0'));
        I := I + 1;
      end;
  end;

  function FloatArg: Double;
  begin
    case A.VType of
      vtExtended: Result := A.VExtended^;
      vtInteger: Result := A.VInteger;
      vtInt64: Result := A.VInt64^;
    else
      begin
        FormatError(SInvalidFormat, Fmt);
        Result := 0;
      end;
    end;
  end;

  function IntArg: Int64;
  begin
    case A.VType of
      vtInteger: Result := A.VInteger;
      vtInt64: Result := A.VInt64^;
    else
      begin
        FormatError(SInvalidFormat, Fmt);
        Result := 0;
      end;
    end;
  end;

begin
  Result := '';
  N := Length(Fmt);
  ArgIndex := 0;
  I := 1;
  while I <= N do
  begin
    C := Fmt[I];
    if C <> '%' then
    begin
      Result := Result + C;
      I := I + 1;
      Continue;
    end;
    I := I + 1;
    if (I <= N) and (Fmt[I] = '%') then
    begin
      Result := Result + '%';
      I := I + 1;
      Continue;
    end;
    { [index ":"] ["-"] [width] ["." prec] type }
    ReadNumber;
    if HasNum and (I <= N) and (Fmt[I] = ':') then
    begin
      ArgIndex := Num;
      I := I + 1;
      HasNum := False;
    end;
    LeftAlign := False;
    if (I <= N) and (Fmt[I] = '-') then
    begin
      LeftAlign := True;
      I := I + 1;
    end;
    Width := 0;
    if not HasNum or HasStar then
    begin
      if not HasNum then
        ReadNumber;
      if HasNum then
        Width := Num;
    end
    else
      Width := Num;
    if Width < 0 then
    begin
      LeftAlign := True;
      Width := -Width;
    end;
    Prec := -1;
    if (I <= N) and (Fmt[I] = '.') then
    begin
      I := I + 1;
      ReadNumber;
      if not HasNum then
        FormatError(SInvalidFormat, Fmt);
      Prec := Num;
    end;
    if I > N then
      FormatError(SInvalidFormat, Fmt);
    C := Fmt[I];
    if (C >= 'A') and (C <= 'Z') then
      C := Char(Ord(C) + 32);
    I := I + 1;
    if Pos(C, 'duxefgnmps') = 0 then
      FormatError(SInvalidFormat, Fmt);
    A := TakeArg;
    case C of
      'd':
        begin
          I64 := IntArg;
          if I64 < 0 then
            T := '-' + ZeroPad(UIntToStr(UInt64(-(I64 + 1)) + 1), Prec)
          else
            T := ZeroPad(UIntToStr(UInt64(I64)), Prec);
        end;
      'u':
        begin
          I64 := IntArg;
          if A.VType = vtInteger then
            T := ZeroPad(UIntToStr(Cardinal(A.VInteger)), Prec)
          else
            T := ZeroPad(UIntToStr(UInt64(I64)), Prec);
        end;
      'x':
        begin
          I64 := IntArg;
          if Prec > 32 then
            Prec := 0;
          if A.VType = vtInteger then
            T := HexText(Cardinal(A.VInteger), Prec)
          else
            T := HexText(UInt64(I64), Prec);
        end;
      'e':
        begin
          V := FloatArg;
          if Prec < 0 then
            Prec := 15;
          T := FloatToStrF(V, ffExponent, Prec, 3, FS);
        end;
      'g':
        begin
          V := FloatArg;
          if Prec < 0 then
            Prec := 15;
          T := FloatToStrF(V, ffGeneral, Prec, 3, FS);
        end;
      'f', 'n', 'm':
        begin
          V := FloatArg;
          if Prec < 0 then
          begin
            if C = 'm' then
              Prec := FS.CurrencyDecimals
            else
              Prec := 2;
          end;
          if C = 'f' then
            T := FloatToStrF(V, ffFixed, 17, Prec, FS)
          else if C = 'n' then
            T := FloatToStrF(V, ffNumber, 17, Prec, FS)
          else
            T := FloatToStrF(V, ffCurrency, 17, Prec, FS);
        end;
      'p':
        if (A.VType = vtPointer) or (A.VType = vtObject) or (A.VType = vtClass) then
          T := HexText(UInt64(NativeUInt(A.VPointer)), 16)
        else
          FormatError(SInvalidFormat, Fmt);
    else
      begin                                      { 's' }
        case A.VType of
          vtUnicodeString: T := PString(@A.VUnicodeString)^;
          vtAnsiString: T := string(PAnsiString(@A.VAnsiString)^);
          vtString: T := string(A.VString^);
          vtWideChar: T := A.VWideChar;
          vtChar: T := Char(A.VChar);
          vtPWideChar: T := string(A.VPWideChar);
          vtPChar: T := string(A.VPChar);
        else
          FormatError(SInvalidFormat, Fmt);
        end;
        T := CutText(T, Prec);
      end;
    end;
    while Length(T) < Width do
      if LeftAlign then
        T := T + ' '
      else
        T := ' ' + T;
    Result := Result + T;
  end;
end;

function Format(const Fmt: string; const Args: array of const): string;
begin
  Result := Format(Fmt, Args, FormatSettings);
end;

procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const);
begin
  Result := Format(Fmt, Args, FormatSettings);
end;

procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const; const FS: TFormatSettings);
begin
  Result := Format(Fmt, Args, FS);
end;

function StrLFmt(Buffer: PChar; MaxBufLen: Cardinal; Fmt: PChar; const Args: array of const): PChar;
var
  S: string;
  I, N: Integer;
begin
  S := Format(string(Fmt), Args, FormatSettings);
  N := Length(S);
  if Cardinal(N) > MaxBufLen then
    N := Integer(MaxBufLen);
  for I := 0 to N - 1 do
    Buffer[I] := S[I + 1];
  Buffer[N] := #0;
  Result := Buffer;
end;

function StrFmt(Buffer, Fmt: PChar; const Args: array of const): PChar;
begin
  Result := StrLFmt(Buffer, MaxInt, Fmt, Args);
end;

function FormatBuf(var Buffer; BufLen: Cardinal; const Fmt; FmtLen: Cardinal; const Args: array of const;
  const FS: TFormatSettings): Cardinal;
var
  F, S: string;
  I, N: Integer;
begin
  SetLength(F, FmtLen);
  for I := 1 to Integer(FmtLen) do
    F[I] := PChar(@Fmt)[I - 1];
  S := Format(F, Args, FS);
  N := Length(S);
  if Cardinal(N) > BufLen then
    N := Integer(BufLen);
  for I := 0 to N - 1 do
    PChar(@Buffer)[I] := S[I + 1];
  Result := Cardinal(N);
end;

function FormatBuf(var Buffer; BufLen: Cardinal; const Fmt; FmtLen: Cardinal;
  const Args: array of const): Cardinal;
begin
  Result := FormatBuf(Buffer, BufLen, Fmt, FmtLen, Args, FormatSettings);
end;

{ -------------------------------------------------------------------------------------------- exceptions (2) }

constructor Exception.Create(const Msg: string);
begin
  FMessage := Msg;
end;

constructor Exception.CreateFmt(const Msg: string; const Args: array of const);
begin
  FMessage := Format(Msg, Args);
end;

constructor Exception.CreateHelp(const Msg: string; AHelpContext: Integer);
begin
  FMessage := Msg;
  FHelpContext := AHelpContext;
end;

constructor Exception.CreateFmtHelp(const Msg: string; const Args: array of const; AHelpContext: Integer);
begin
  FMessage := Format(Msg, Args);
  FHelpContext := AHelpContext;
end;

destructor Exception.Destroy;
begin
  FInnerException.Free;
  FInnerException := nil;
  inherited Destroy;
end;

function Exception.ToString: string;
begin
  Result := FMessage;
end;

function Exception.GetBaseException: Exception;
begin
  Result := Self;
  while Result.FInnerException <> nil do
    Result := Result.FInnerException;
end;

class procedure Exception.RaiseOuterException(E: Exception);
var
  Current: TObject;
begin
  Current := ExceptObject;
  if (Current <> nil) and (Current is Exception) and (Current <> E) then
    E.FInnerException := Exception(AcquireExceptionObject);
  raise E;
end;

procedure EStackOverflow.FreeInstance;
begin
  if AllowFree then
    inherited FreeInstance;
end;

procedure EHeapException.FreeInstance;
begin
  if AllowFree then
    inherited FreeInstance;
end;

var
  OutOfMemoryInstance: EOutOfMemory;
  InvalidPointerInstance: EInvalidPointer;
  StackOverflowInstance: EStackOverflow;

{ RTL-212: the exception for runtime error Code (the pre-allocated instance for 203 and 204) }
function RuntimeErrorException(Code: Integer): Exception;
var
  IO: EInOutError;
  Msg: string;
begin
  case Code of
    200: Result := EDivByZero.Create(SDivByZero);
    201: Result := ERangeError.Create(SRangeError);
    202: Result := StackOverflowInstance;
    203: Result := OutOfMemoryInstance;
    204: Result := InvalidPointerInstance;
    205: Result := EOverflow.Create(SOverflow);
    206: Result := EUnderflow.Create(SUnderflow);
    207: Result := EInvalidOp.Create(SInvalidOp);
    208: Result := EZeroDivide.Create(SZeroDivide);
    211: Result := EAbstractError.Create(SAbstractError);
    215: Result := EIntOverflow.Create(SIntOverflow);
    216: Result := EAccessViolation.Create(SAccessViolationError);
    218: Result := EPrivilege.Create(SPrivilege);
    219: Result := EInvalidCast.Create(SInvalidCast);
    228: Result := EIntfCastError.Create(SIntfCastError);
    230: Result := ESafecallException.Create(SSafecallException);
    231: Result := EDispatchError.Create(SDispatchError);
  else
    if Code < 200 then
    begin
      case Code of
        2: Msg := SFileNotFound;
        3: Msg := SPathNotFound;
        4: Msg := STooManyOpenFiles;
        5: Msg := SAccessDenied;
        6: Msg := SInvalidFileHandle;
        100: Msg := SEndOfFile;
        101: Msg := SDiskFull;
        102: Msg := SFileNotAssigned;
        103: Msg := SFileNotOpen;
        104: Msg := SFileNotOpenForInput;
        105: Msg := SFileNotOpenForOutput;
        106: Msg := SInvalidInput;
      else
        Msg := Format(SInOutError, [Code]);
      end;
      IO := EInOutError.Create(Msg);
      IO.ErrorCode := Code;
      Result := IO;
    end
    else
      Result := Exception.CreateFmt(SRuntimeError, [Code]);
  end;
end;

{ System.ErrorProc (ABI-101, RTL-212) }
procedure ErrorHandler(Code: Integer; Addr: Pointer);
begin
  raise RuntimeErrorException(Code) at Addr;
end;

{ System.ExceptObjProc (ABI-121, RTL-213): the exception object for an OS exception }
function MapOSException(ErrorCode: Integer; ExceptionRecord: Pointer): Pointer;
var
  R: PExceptionRecord;
  E: Exception;
  Access: string;
begin
  R := PExceptionRecord(ExceptionRecord);
  if R^.ExceptionCode = $C0000005 then
  begin
    case R^.ExceptionInformation[0] of
      1: Access := 'write';
      8: Access := 'execute';
    else
      Access := 'read';
    end;
    E := EAccessViolation.CreateFmt(SAccessViolation, ['$' + HexText(UInt64(NativeUInt(R^.ExceptionAddress)), 16),
      Access, '$' + HexText(UInt64(R^.ExceptionInformation[1]), 16)]);
  end
  else
    E := RuntimeErrorException(ErrorCode);
  if E is EExternal then
  begin
    EExternal(E).ExceptionCode := R^.ExceptionCode;
    EExternal(E).ExceptionAddress := R^.ExceptionAddress;
  end;
  Result := Pointer(E);
end;

{ ---------------------------------------------------------------------------------- bytes and encodings (7.5) }

type
  TUTF8Encoding = class(TEncoding)
  public
    function GetPreamble: TBytes; override;
    function GetBytes(const S: string): TBytes; override;
    function GetString(const Bytes: TBytes; Index, Count: Integer): string; overload; override;
    function EncodingName: string; override;
  end;

  TUnicodeEncoding = class(TEncoding)
  public
    function GetPreamble: TBytes; override;
    function GetBytes(const S: string): TBytes; override;
    function GetString(const Bytes: TBytes; Index, Count: Integer): string; overload; override;
    function EncodingName: string; override;
  end;

  TBigEndianUnicodeEncoding = class(TEncoding)
  public
    function GetPreamble: TBytes; override;
    function GetBytes(const S: string): TBytes; override;
    function GetString(const Bytes: TBytes; Index, Count: Integer): string; overload; override;
    function EncodingName: string; override;
  end;

  TASCIIEncoding = class(TEncoding)
  public
    function GetPreamble: TBytes; override;
    function GetBytes(const S: string): TBytes; override;
    function GetString(const Bytes: TBytes; Index, Count: Integer): string; overload; override;
    function EncodingName: string; override;
  end;

const
  EncUTF8 = 0;
  EncUnicode = 1;
  EncBigEndian = 2;
  EncASCII = 3;

var
  StdEncodings: array[EncUTF8..EncASCII] of Pointer;

{ RTL-258: created on first use; a thread that loses the compare-exchange frees its own instance }
function StdEncoding(Kind: Integer): TEncoding;
var
  E: TEncoding;
begin
  Result := TEncoding(StdEncodings[Kind]);
  if Result <> nil then
    Exit;
  case Kind of
    EncUTF8: E := TUTF8Encoding.Create;
    EncUnicode: E := TUnicodeEncoding.Create;
    EncBigEndian: E := TBigEndianUnicodeEncoding.Create;
  else
    E := TASCIIEncoding.Create;
  end;
  if AtomicCmpExchange(StdEncodings[Kind], Pointer(E), nil) <> nil then
    E.Free;
  Result := TEncoding(StdEncodings[Kind]);
end;

procedure FreeStdEncodings;
var
  I: Integer;
begin
  for I := EncUTF8 to EncASCII do
  begin
    TEncoding(StdEncodings[I]).Free;
    StdEncodings[I] := nil;
  end;
end;

procedure CheckBufferRange(const Bytes: TBytes; Index, Count: Integer);
begin
  if (Index < 0) or (Count < 0) or (Index > Length(Bytes) - Count) then
    raise EEncodingError.Create(SInvalidBufferRange);
end;

function MakeBytes(B0, B1, B2: Integer; Count: Integer): TBytes;
begin
  SetLength(Result, Count);
  if Count > 0 then
    Result[0] := B0;
  if Count > 1 then
    Result[1] := B1;
  if Count > 2 then
    Result[2] := B2;
end;

{ UTF-16 code units from Count bytes at Bytes[Index]; BigEndian selects the byte order. An odd trailing byte and
  unpaired surrogates become U+FFFD }
function Utf16Decode(const Bytes: TBytes; Index, Count: Integer; BigEndian: Boolean): string;
var
  I, N, Pairs: Integer;
  U, V: Word;

  function UnitAt(K: Integer): Word;
  begin
    if BigEndian then
      Result := Word(Bytes[Index + 2 * K]) shl 8 or Bytes[Index + 2 * K + 1]
    else
      Result := Word(Bytes[Index + 2 * K + 1]) shl 8 or Bytes[Index + 2 * K];
  end;

begin
  Pairs := Count div 2;
  SetLength(Result, Pairs + Count mod 2);
  N := 0;
  I := 0;
  while I < Pairs do
  begin
    U := UnitAt(I);
    I := I + 1;
    if (U >= $D800) and (U <= $DBFF) then
    begin
      if I < Pairs then
        V := UnitAt(I)
      else
        V := 0;
      if (V >= $DC00) and (V <= $DFFF) then
      begin
        N := N + 1;
        Result[N] := Char(U);
        U := V;
        I := I + 1;
      end
      else
        U := $FFFD;
    end
    else if (U >= $DC00) and (U <= $DFFF) then
      U := $FFFD;
    N := N + 1;
    Result[N] := Char(U);
  end;
  if Count mod 2 <> 0 then
  begin
    N := N + 1;
    Result[N] := #$FFFD;
  end;
  SetLength(Result, N);
end;

function Utf16Encode(const S: string; BigEndian: Boolean): TBytes;
var
  I: Integer;
  U: Word;
begin
  SetLength(Result, 2 * Length(S));
  for I := 1 to Length(S) do
  begin
    U := Ord(S[I]);
    if BigEndian then
    begin
      Result[2 * I - 2] := U shr 8;
      Result[2 * I - 1] := U and $FF;
    end
    else
    begin
      Result[2 * I - 2] := U and $FF;
      Result[2 * I - 1] := U shr 8;
    end;
  end;
end;

class function TEncoding.Default: TEncoding;
begin
  Result := StdEncoding(EncUTF8);
end;

class function TEncoding.ANSI: TEncoding;
begin
  Result := StdEncoding(EncUTF8);
end;

class function TEncoding.ASCII: TEncoding;
begin
  Result := StdEncoding(EncASCII);
end;

class function TEncoding.UTF8: TEncoding;
begin
  Result := StdEncoding(EncUTF8);
end;

class function TEncoding.Unicode: TEncoding;
begin
  Result := StdEncoding(EncUnicode);
end;

class function TEncoding.BigEndianUnicode: TEncoding;
begin
  Result := StdEncoding(EncBigEndian);
end;

{ the length of E's preamble when Buffer starts with it, else 0 (also for an empty preamble) }
function PreambleMatch(E: TEncoding; const Buffer: TBytes): Integer;
var
  P: TBytes;
  I: Integer;
begin
  P := E.GetPreamble;
  Result := 0;
  if (Length(P) = 0) or (Length(P) > Length(Buffer)) then
    Exit;
  for I := 0 to Length(P) - 1 do
    if P[I] <> Buffer[I] then
      Exit;
  Result := Length(P);
end;

class function TEncoding.GetBufferEncoding(const Buffer: TBytes; var AEncoding: TEncoding;
  ADefault: TEncoding): Integer;
var
  K: Integer;
begin
  if AEncoding <> nil then
  begin
    Result := PreambleMatch(AEncoding, Buffer);
    Exit;
  end;
  for K := EncUTF8 to EncBigEndian do
  begin
    Result := PreambleMatch(StdEncoding(K), Buffer);
    if Result > 0 then
    begin
      AEncoding := StdEncoding(K);
      Exit;
    end;
  end;
  if ADefault <> nil then
    AEncoding := ADefault
  else
    AEncoding := StdEncoding(EncUTF8);
  Result := 0;
end;

function TEncoding.GetString(const Bytes: TBytes): string;
begin
  Result := GetString(Bytes, 0, Length(Bytes));
end;

function TEncoding.GetByteCount(const S: string): Integer;
begin
  Result := Length(GetBytes(S));
end;

function TEncoding.GetCharCount(const Bytes: TBytes): Integer;
begin
  Result := Length(GetString(Bytes));
end;

function TUTF8Encoding.GetPreamble: TBytes;
begin
  Result := MakeBytes($EF, $BB, $BF, 3);
end;

function TUTF8Encoding.GetBytes(const S: string): TBytes;
var
  R: RawByteString;
begin
  R := UTF8Encode(S);
  SetLength(Result, Length(R));
  if Length(R) > 0 then
    Move(Pointer(R)^, Result[0], Length(R));
end;

function TUTF8Encoding.GetString(const Bytes: TBytes; Index, Count: Integer): string;
var
  R: RawByteString;
begin
  CheckBufferRange(Bytes, Index, Count);
  Result := '';
  if Count = 0 then
    Exit;
  SetLength(R, Count);
  Move(Bytes[Index], Pointer(R)^, Count);
  Result := UTF8ToString(R);
end;

function TUTF8Encoding.EncodingName: string;
begin
  Result := 'UTF-8';
end;

function TUnicodeEncoding.GetPreamble: TBytes;
begin
  Result := MakeBytes($FF, $FE, 0, 2);
end;

function TUnicodeEncoding.GetBytes(const S: string): TBytes;
begin
  Result := Utf16Encode(S, False);
end;

function TUnicodeEncoding.GetString(const Bytes: TBytes; Index, Count: Integer): string;
begin
  CheckBufferRange(Bytes, Index, Count);
  Result := Utf16Decode(Bytes, Index, Count, False);
end;

function TUnicodeEncoding.EncodingName: string;
begin
  Result := 'UTF-16LE';
end;

function TBigEndianUnicodeEncoding.GetPreamble: TBytes;
begin
  Result := MakeBytes($FE, $FF, 0, 2);
end;

function TBigEndianUnicodeEncoding.GetBytes(const S: string): TBytes;
begin
  Result := Utf16Encode(S, True);
end;

function TBigEndianUnicodeEncoding.GetString(const Bytes: TBytes; Index, Count: Integer): string;
begin
  CheckBufferRange(Bytes, Index, Count);
  Result := Utf16Decode(Bytes, Index, Count, True);
end;

function TBigEndianUnicodeEncoding.EncodingName: string;
begin
  Result := 'UTF-16BE';
end;

function TASCIIEncoding.GetPreamble: TBytes;
begin
  Result := nil;
end;

{ every UTF-16 code unit above 127 (each half of a surrogate pair too) becomes '?' }
function TASCIIEncoding.GetBytes(const S: string): TBytes;
var
  I: Integer;
begin
  SetLength(Result, Length(S));
  for I := 1 to Length(S) do
    if Ord(S[I]) < 128 then
      Result[I - 1] := Ord(S[I])
    else
      Result[I - 1] := Ord('?');
end;

function TASCIIEncoding.GetString(const Bytes: TBytes; Index, Count: Integer): string;
var
  I: Integer;
begin
  CheckBufferRange(Bytes, Index, Count);
  SetLength(Result, Count);
  for I := 1 to Count do
    if Bytes[Index + I - 1] < 128 then
      Result[I] := Char(Bytes[Index + I - 1])
    else
      Result[I] := #$FFFD;
end;

function TASCIIEncoding.EncodingName: string;
begin
  Result := 'US-ASCII';
end;

function BytesOf(const S: string): TBytes;
begin
  Result := StdEncoding(EncUTF8).GetBytes(S);
end;

function StringOf(const Bytes: TBytes): string;
begin
  Result := StdEncoding(EncUTF8).GetString(Bytes);
end;

{ ------------------------------------------------------------------------------------------- TStringBuilder (9) }

procedure RangeError(Value: Integer);
begin
  raise EArgumentOutOfRangeException.CreateFmt(SIndexOutOfRange, [Value]);
end;

constructor TStringBuilder.Create;
begin
  inherited Create;
  System.SetLength(FData, 16);
end;

constructor TStringBuilder.Create(ACapacity: Integer);
begin
  inherited Create;
  if ACapacity < 0 then
    RangeError(ACapacity);
  System.SetLength(FData, ACapacity);
end;

constructor TStringBuilder.Create(const Value: string);
begin
  inherited Create;
  if System.Length(Value) > 16 then
    System.SetLength(FData, System.Length(Value))
  else
    System.SetLength(FData, 16);
  Append(Value);
end;

function TStringBuilder.GetCapacity: Integer;
begin
  Result := System.Length(FData);
end;

function TStringBuilder.GetMaxCapacity: Integer;
begin
  Result := MaxInt;
end;

procedure TStringBuilder.SetCapacity(Value: Integer);
begin
  if Value < FLength then
    raise EArgumentOutOfRangeException.Create(SCapacityLessThanLength);
  System.SetLength(FData, Value);
end;

{ RTL-271 }
procedure TStringBuilder.Grow(Needed: Integer);
var
  C: Integer;
begin
  if Needed <= System.Length(FData) then
    Exit;
  C := System.Length(FData) * 2;
  if C < Needed then
    C := Needed;
  System.SetLength(FData, C);
end;

procedure TStringBuilder.SetLen(Value: Integer);
var
  I: Integer;
begin
  if Value < 0 then
    RangeError(Value);
  Grow(Value);
  for I := FLength + 1 to Value do
    FData[I] := #0;
  FLength := Value;
end;

function TStringBuilder.GetChars(Index: Integer): Char;
begin
  if (Index < 0) or (Index >= FLength) then
    RangeError(Index);
  Result := FData[Index + 1];
end;

procedure TStringBuilder.SetChars(Index: Integer; Value: Char);
begin
  if (Index < 0) or (Index >= FLength) then
    RangeError(Index);
  FData[Index + 1] := Value;
end;

function TStringBuilder.Append(const Value: string): TStringBuilder;
var
  N: Integer;
begin
  N := System.Length(Value);
  if N > 0 then
  begin
    Grow(FLength + N);
    Move(Pointer(Value)^, FData[FLength + 1], N * SizeOf(Char));
    FLength := FLength + N;
  end;
  Result := Self;
end;

function TStringBuilder.Append(Value: Char): TStringBuilder;
begin
  Grow(FLength + 1);
  FLength := FLength + 1;
  FData[FLength] := Value;
  Result := Self;
end;

function TStringBuilder.Append(Value: Char; RepeatCount: Integer): TStringBuilder;
var
  I: Integer;
begin
  if RepeatCount < 0 then
    RangeError(RepeatCount);
  Grow(FLength + RepeatCount);
  for I := 1 to RepeatCount do
    FData[FLength + I] := Value;
  FLength := FLength + RepeatCount;
  Result := Self;
end;

function TStringBuilder.Append(Value: Integer): TStringBuilder;
begin
  Result := Append(IntToStr(Value));
end;

function TStringBuilder.Append(Value: Int64): TStringBuilder;
begin
  Result := Append(IntToStr(Value));
end;

function TStringBuilder.Append(Value: Boolean): TStringBuilder;
begin
  if Value then
    Result := Append('True')
  else
    Result := Append('False');
end;

function TStringBuilder.Append(Value: Double): TStringBuilder;
begin
  Result := Append(FloatToStr(Value));
end;

function TStringBuilder.Append(Value: TObject): TStringBuilder;
begin
  if Value <> nil then
    Append(Value.ToString);
  Result := Self;
end;

function TStringBuilder.AppendFormat(const Fmt: string; const Args: array of const): TStringBuilder;
begin
  Result := Append(Format(Fmt, Args));
end;

function TStringBuilder.AppendLine: TStringBuilder;
begin
  Result := Append(sLineBreak);
end;

function TStringBuilder.AppendLine(const Value: string): TStringBuilder;
begin
  Append(Value);
  Result := Append(sLineBreak);
end;

function TStringBuilder.Insert(Index: Integer; const Value: string): TStringBuilder;
var
  N: Integer;
begin
  if (Index < 0) or (Index > FLength) then
    RangeError(Index);
  N := System.Length(Value);
  if N > 0 then
  begin
    Grow(FLength + N);
    if Index < FLength then
      Move(FData[Index + 1], FData[Index + N + 1], (FLength - Index) * SizeOf(Char));
    Move(Pointer(Value)^, FData[Index + 1], N * SizeOf(Char));
    FLength := FLength + N;
  end;
  Result := Self;
end;

function TStringBuilder.Remove(StartIndex, RemLength: Integer): TStringBuilder;
begin
  if (StartIndex < 0) or (StartIndex > FLength) then
    RangeError(StartIndex);
  if (RemLength < 0) or (RemLength > FLength - StartIndex) then
    RangeError(RemLength);
  if RemLength > 0 then
  begin
    if StartIndex + RemLength < FLength then
      Move(FData[StartIndex + RemLength + 1], FData[StartIndex + 1],
        (FLength - StartIndex - RemLength) * SizeOf(Char));
    FLength := FLength - RemLength;
  end;
  Result := Self;
end;

function TStringBuilder.Replace(const OldValue, NewValue: string): TStringBuilder;
var
  S: string;
begin
  Result := Self;
  if (OldValue = '') or (FLength = 0) then
    Exit;
  S := StringReplace(Copy(FData, 1, FLength), OldValue, NewValue, [rfReplaceAll]);
  FLength := 0;
  Append(S);
end;

function TStringBuilder.Replace(OldChar, NewChar: Char): TStringBuilder;
var
  I: Integer;
begin
  for I := 1 to FLength do
    if FData[I] = OldChar then
      FData[I] := NewChar;
  Result := Self;
end;

procedure TStringBuilder.Clear;
begin
  FLength := 0;
end;

function TStringBuilder.EnsureCapacity(ACapacity: Integer): Integer;
begin
  if ACapacity < 0 then
    RangeError(ACapacity);
  if ACapacity > System.Length(FData) then
    System.SetLength(FData, ACapacity);
  Result := System.Length(FData);
end;

function TStringBuilder.ToString: string;
begin
  Result := Copy(FData, 1, FLength);
end;

function TStringBuilder.ToString(StartIndex, StrLength: Integer): string;
begin
  if (StartIndex < 0) or (StartIndex > FLength) then
    RangeError(StartIndex);
  if (StrLength < 0) or (StrLength > FLength - StartIndex) then
    RangeError(StrLength);
  Result := Copy(FData, StartIndex + 1, StrLength);
end;

{ ---------------------------------------------------------------------------------- helper routines (2.4, 10) }

procedure Abort;
begin
  raise EAbort.Create(SOperationAborted);
end;

function SysErrorMessage(ErrorCode: Cardinal): string;
var
  Buffer: array[0..1023] of Char;
  Len: DWORD;
begin
  Len := FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM or FORMAT_MESSAGE_IGNORE_INSERTS, nil, ErrorCode, 0, @Buffer[0],
    Length(Buffer), nil);
  while (Len > 0) and (Buffer[Len - 1] <= ' ') do
    Len := Len - 1;
  SetString(Result, PChar(@Buffer[0]), Integer(Len));
end;

procedure RaiseLastOSError(LastError: Integer);
var
  E: EOSError;
begin
  if LastError <> 0 then
    E := EOSError.CreateFmt(SOSError, [LastError, SysErrorMessage(Cardinal(LastError))])
  else
    E := EOSError.Create(SUnkOSError);
  E.ErrorCode := Cardinal(LastError);
  raise E;
end;

procedure RaiseLastOSError;
begin
  RaiseLastOSError(Integer(Windows.GetLastError));
end;

function Win32Check(RetVal: LongBool): LongBool;
begin
  if not RetVal then
    RaiseLastOSError;
  Result := RetVal;
end;

procedure OutOfMemoryError;
begin
  raise OutOfMemoryInstance;
end;

function ExceptionText(ExceptObject: TObject; ExceptAddr: Pointer): string;
begin
  Result := Format(SExceptionInModule, [ExceptObject.ClassName, ExtractFileName(ParamStr(0)),
    IntToHex(NativeInt(ExceptAddr), 16)]);
  if ExceptObject is Exception then
    Result := Result + sLineBreak + Exception(ExceptObject).Message;
end;

function ExceptionErrorMessage(ExceptObject: TObject; ExceptAddr: Pointer; Buffer: PChar; Size: Integer): Integer;
var
  S: string;
  I: Integer;
begin
  Result := 0;
  if (Buffer = nil) or (Size <= 0) then
    Exit;
  S := ExceptionText(ExceptObject, ExceptAddr);
  Result := Length(S);
  if Result > Size - 1 then
    Result := Size - 1;
  for I := 0 to Result - 1 do
    Buffer[I] := S[I + 1];
  Buffer[Result] := #0;
end;

procedure ShowException(ExceptObject: TObject; ExceptAddr: Pointer);
var
  S: string;
begin
  S := ExceptionText(ExceptObject, ExceptAddr);
  if IsConsole or not Assigned(ShowErrorProc) then
    Writeln(ErrOutput, S)
  else
    ShowErrorProc(S);
end;

var
  ExitProcs: array of TProcedure;
  SavedExitProc: Pointer;

{ the System.ExitProc hook: the list, last added first, then the hook that was installed before AddExitProc }
procedure RunExitProcs;
var
  P: TProcedure;
begin
  while Length(ExitProcs) > 0 do
  begin
    P := ExitProcs[High(ExitProcs)];
    SetLength(ExitProcs, Length(ExitProcs) - 1);
    P;
  end;
  ExitProc := SavedExitProc;
end;

procedure FreeAndNil(var Obj);
var
  Temp: TObject;
begin
  Temp := TObject(Obj);
  Pointer(Obj) := nil;
  Temp.Free;
end;

procedure AddExitProc(Proc: TProcedure);
begin
  if Length(ExitProcs) = 0 then
  begin
    SavedExitProc := ExitProc;
    ExitProc := @RunExitProcs;
  end;
  SetLength(ExitProcs, Length(ExitProcs) + 1);
  ExitProcs[High(ExitProcs)] := Proc;
end;

function GetLastError: Cardinal;
begin
  Result := Windows.GetLastError;
end;

function CheckWin32Version(Major: Integer; Minor: Integer): Boolean;
begin
  Result := (Win32MajorVersion > Major) or ((Win32MajorVersion = Major) and (Win32MinorVersion >= Minor));
end;

var
  { filled by ReadVersion; stay 0 when RtlGetVersion fails (RTL-980) }
  OSServicePackMajor: Integer;
  OSServicePackMinor: Integer;
  OSProductType: Integer;
  OSArchitecture: TOSVersion.TArchitecture;

procedure ReadVersion;
var
  V: TOSVersionInfoExW;
  Info: TSystemInfo;
begin
  FillChar(V, SizeOf(V), 0);
  V.dwOSVersionInfoSize := SizeOf(V);
  if RtlGetVersion(POSVersionInfoW(@V)) = 0 then
  begin
    Win32MajorVersion := Integer(V.dwMajorVersion);
    Win32MinorVersion := Integer(V.dwMinorVersion);
    Win32BuildNumber := Integer(V.dwBuildNumber);
    OSServicePackMajor := V.wServicePackMajor;
    OSServicePackMinor := V.wServicePackMinor;
    OSProductType := V.wProductType;
  end;
  FillChar(Info, SizeOf(Info), 0);
  Info.wProcessorArchitecture := PROCESSOR_ARCHITECTURE_AMD64;
  GetNativeSystemInfo(Info);
  if Info.wProcessorArchitecture = PROCESSOR_ARCHITECTURE_INTEL then
    OSArchitecture := arIntelX86
  else
    OSArchitecture := arIntelX64;
end;

{ ------------------------------------------------------------------------------------ TOSVersion (os-version.md) }

class function TOSVersion.Architecture: TArchitecture;
begin
  Result := OSArchitecture;
end;

class function TOSVersion.Platform: TPlatform;
begin
  Result := pfWindows;
end;

class function TOSVersion.Major: Integer;
begin
  Result := Win32MajorVersion;
end;

class function TOSVersion.Minor: Integer;
begin
  Result := Win32MinorVersion;
end;

class function TOSVersion.Build: Integer;
begin
  Result := Win32BuildNumber;
end;

class function TOSVersion.ServicePackMajor: Integer;
begin
  Result := OSServicePackMajor;
end;

class function TOSVersion.ServicePackMinor: Integer;
begin
  Result := OSServicePackMinor;
end;

{ RTL-982: releases of version 10 are told apart by build number; product type 1 is a workstation }
class function TOSVersion.Name: string;
begin
  if Win32MajorVersion <> 10 then
    Result := 'Windows'
  else if OSProductType = VER_NT_WORKSTATION then
  begin
    if Win32BuildNumber >= 22000 then
      Result := 'Windows 11'
    else
      Result := 'Windows 10';
  end
  else if Win32BuildNumber >= 26100 then
    Result := 'Windows Server 2025'
  else if Win32BuildNumber >= 20348 then
    Result := 'Windows Server 2022'
  else if Win32BuildNumber >= 17763 then
    Result := 'Windows Server 2019'
  else
    Result := 'Windows Server';
end;

class function TOSVersion.Check(AMajor: Integer): Boolean;
begin
  Result := Win32MajorVersion >= AMajor;
end;

class function TOSVersion.Check(AMajor, AMinor: Integer): Boolean;
begin
  Result := (Win32MajorVersion > AMajor) or ((Win32MajorVersion = AMajor) and (Win32MinorVersion >= AMinor));
end;

class function TOSVersion.Check(AMajor, AMinor, AServicePackMajor: Integer): Boolean;
begin
  Result := (Win32MajorVersion > AMajor) or ((Win32MajorVersion = AMajor) and ((Win32MinorVersion > AMinor) or
    ((Win32MinorVersion = AMinor) and (OSServicePackMajor >= AServicePackMajor))));
end;

class function TOSVersion.CheckBuild(ABuild: Integer): Boolean;
begin
  Result := (Win32MajorVersion > 10) or ((Win32MajorVersion = 10) and ((Win32MinorVersion > 0) or
    (Win32BuildNumber >= ABuild)));
end;

class function TOSVersion.ToString: string;
begin
  Result := Name + ' (' + IntToStr(Win32MajorVersion) + '.' + IntToStr(Win32MinorVersion) + '.' +
    IntToStr(Win32BuildNumber) + ', ';
  if OSArchitecture = arIntelX86 then
    Result := Result + 'x86)'
  else
    Result := Result + 'x64)';
end;

{ System.AssertErrorProc (ABI-103, RTL-214) }
procedure AssertHandler(const Msg, FileName: string; Line: Integer; Addr: Pointer);
var
  M: string;
begin
  M := Msg;
  if M = '' then
    M := SAssertionFailed;
  raise EAssertionFailed.CreateFmt(SAssertError, [M, FileName, Line]) at Addr;
end;

{ System.AbstractErrorProc (ABI-103, RTL-133): EAbstractError, raised at the address in _AbstractError like the
  runtime error 211 that follows when no hook is set }
procedure AbstractHandler;
begin
  raise EAbstractError.Create(SAbstractError) at _ReturnAddress;
end;

{ System.ExceptProc (ABI-124, RTL-215): the report line; System then halts with 217 }
procedure UnhandledHandler(Obj: TObject; Addr: Pointer);
var
  S: string;
begin
  if Obj is Exception then
    S := Format(SUnhandledException, [Obj.ClassName, Exception(Obj).Message])
  else if Obj <> nil then
    S := Format(SUnhandledExceptionNoMsg, [Obj.ClassName])
  else
    S := Format(SUnhandledExceptionNoMsg, ['nil']);
  if IsConsole or not Assigned(ShowErrorProc) then
    Writeln(ErrOutput, S)
  else
    ShowErrorProc(S);
end;

initialization
  FormatSettings := InvariantFormatSettings;
  OutOfMemoryInstance := EOutOfMemory.Create(SOutOfMemory);
  InvalidPointerInstance := EInvalidPointer.Create(SInvalidPointer);
  StackOverflowInstance := EStackOverflow.Create(SStackOverflow);
  ErrorProc := @ErrorHandler;
  ExceptObjProc := @MapOSException;
  AssertErrorProc := @AssertHandler;
  ExceptProc := @UnhandledHandler;
  AbstractErrorProc := @AbstractHandler;
  ReadVersion;

finalization
  AbstractErrorProc := nil;
  ExceptProc := nil;
  AssertErrorProc := nil;
  ExceptObjProc := nil;
  ErrorProc := nil;
  OutOfMemoryInstance.AllowFree := True;
  OutOfMemoryInstance.Free;
  InvalidPointerInstance.AllowFree := True;
  InvalidPointerInstance.Free;
  StackOverflowInstance.AllowFree := True;
  StackOverflowInstance.Free;
  FreeStdEncodings;
end.
