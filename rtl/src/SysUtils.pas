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
  end;

var
  FormatSettings: TFormatSettings;

function InvariantFormatSettings: TFormatSettings;

{ ------------------------------------------------------------------------- integer and Boolean conversions (3) }
function IntToStr(Value: Integer): string; overload;
function IntToStr(Value: Int64): string; overload;
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
function StrToIntDef(const S: string; Default: Integer): Integer;
function StrToInt64Def(const S: string; Default: Int64): Int64;
function StrToUIntDef(const S: string; Default: Cardinal): Cardinal;
function StrToUInt64Def(const S: string; Default: UInt64): UInt64;
function BoolToStr(B: Boolean; UseBoolStrs: Boolean = False): string;
{ 'true' / 'false' (any case), else a number in FormatSettings: non-zero is True }
function TryStrToBool(const S: string; out Value: Boolean): Boolean;
function StrToBoolDef(const S: string; Default: Boolean): Boolean;

{ ------------------------------------------------------------------------------ case and comparison (7.1, 7.2) }
function UpperCase(const S: string): string;
function LowerCase(const S: string): string;
function CompareStr(const S1, S2: string): Integer;
function CompareText(const S1, S2: string): Integer;
function SameStr(const S1, S2: string): Boolean;
function SameText(const S1, S2: string): Boolean;
function CompareMem(P1, P2: Pointer; Length: Integer): Boolean;
function Trim(const S: string): string;
function TrimLeft(const S: string): string;
function TrimRight(const S: string): string;

type
  TSysCharSet = set of AnsiChar;
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
function StrToFloatDef(const S: string; const Default: Double; const FS: TFormatSettings): Double; overload;

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

{ ------------------------------------------------------------------------------------------------ Format (4) }
function Format(const Fmt: string; const Args: array of const): string; overload;
function Format(const Fmt: string; const Args: array of const; const FS: TFormatSettings): string; overload;
procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const); overload;
procedure FmtStr(var Result: string; const Fmt: string; const Args: array of const;
  const FS: TFormatSettings); overload;
{ PChar forms: Buffer must hold the whole result and its terminator (StrLFmt: at most MaxBufLen characters) }
function StrFmt(Buffer, Fmt: PChar; const Args: array of const): PChar;
function StrLFmt(Buffer: PChar; MaxBufLen: Cardinal; Fmt: PChar; const Args: array of const): PChar;

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

{ ------------------------------------------------------------------------- integer and Boolean conversions (3) }

function IntToStr(Value: Integer): string;
begin
  Str(Value, Result);
end;

function IntToStr(Value: Int64): string;
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
  { TODO(compiler): char literals in a set of AnsiChar need the casts (E2010 today) }
  Result := WrapText(Line, sLineBreak, [AnsiChar(' '), AnsiChar(#9), AnsiChar('-')], MaxCol);
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

function ExpandFileName(const FileName: string): string;
var
  N: DWORD;
  Buf: string;
begin
  SetLength(Buf, 260);
  N := GetFullPathNameW(PChar(FileName), 260, PChar(Buf), nil);
  if N > 260 then
  begin
    SetLength(Buf, N);
    N := GetFullPathNameW(PChar(FileName), N, PChar(Buf), nil);
  end;
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
    216: Result := EAccessViolation.Create('Access violation');
    218: Result := EPrivilege.Create(SPrivilege);
    219: Result := EInvalidCast.Create(SInvalidCast);
    228: Result := EIntfCastError.Create(SIntfCastError);
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

finalization
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
end.
