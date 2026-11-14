{ System - the implicit unit of every FreexComp program (docs: rtl/system.md, interfaces/rtl-abi.md).
  Win32 imports are declared privately here (RTL-801); their names are listed in rtl/winapi/def/kernel32.def.
  Win32 reference: learn.microsoft.com pages of the imported functions and of the PE format (optional header). }
unit System;

interface

type
  THandle = NativeUInt;
  PByte = ^Byte;
  PWord = ^Word;
  PInteger = ^Integer;
  PCardinal = ^Cardinal;
  PNativeInt = ^NativeInt;
  PNativeUInt = ^NativeUInt;
  PPointer = ^Pointer;

var
  Input: Text;
  Output: Text;
  ErrOutput: Text;
  ExitCode: Integer;
  ExitProc: Pointer;
  ErrorAddr: Pointer;
  // TODO(C-161): ErrorProc: procedure(Code: Integer; Addr: Pointer) (ABI-101); declared as a Pointer until then
  ErrorProc: Pointer;
  IsConsole: Boolean;
  IsLibrary: Boolean;
  HInstance: THandle;
  MainThreadID: Cardinal;
  CmdLine: PChar;
  RandSeed: Cardinal;                  { state of Random (RTL-040); 0 at start-up }

{ compiler support face (rtl-abi.md section 9.1) }
procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
procedure _InitUnits;
procedure _Halt(Code: Integer); noreturn;
procedure _Halt0; noreturn;
procedure _RunError(Code: Integer); noreturn;
procedure _RangeError; noreturn;
procedure _OverflowError; noreturn;
procedure _DivZeroError; noreturn;
procedure _InvalidCast; noreturn;
procedure _IOCheck;

{ memory (rtl-abi.md section 9.2, system.md section 7) }
function _GetMem(Size: NativeInt): Pointer;
procedure _FreeMem(P: Pointer);
function _ReallocMem(P: Pointer; Size: NativeInt): Pointer;
function _AllocZero(Size: NativeInt): Pointer;
function _LiveAllocCount: NativeInt;
function AllocMem(Size: NativeInt): Pointer;

{ UnicodeString helpers (rtl-abi.md section 3, 9.3; system.md section 8). A string value is nil ('') or points at the
  first element of a block with the TStrRec header in front; RefCount -1 marks a static literal block. Every helper
  taking var Dest accepts Dest aliasing an operand (ABI-033). }
procedure _UStrClr(var S: Pointer);
procedure _UStrAddRef(S: Pointer);
procedure _UStrAsg(var Dest: Pointer; Src: Pointer);
function _UStrNew(Len: Integer): Pointer;
procedure _UStrFromPWCharLen(var Dest: Pointer; P: PWideChar; Len: Integer);
procedure _UStrFromPWChar(var Dest: Pointer; P: PWideChar);
procedure _UStrFromWChar(var Dest: Pointer; C: WideChar);
procedure _UStrCat(var Dest: Pointer; A, B: Pointer);
procedure _UStrCatN(var Dest: Pointer; Parts: PPointer; Count: Integer);
function _UStrCmp(A, B: Pointer): Integer;
function _UStrEq(A, B: Pointer): Boolean;
procedure _UStrUnique(var S: Pointer);
procedure _UStrSetLen(var S: Pointer; NewLen: Integer);
procedure _UStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
procedure _UStrDelete(var S: Pointer; Index, Count: Integer);
procedure _UStrInsert(Src: Pointer; var Dest: Pointer; Index: Integer);
function _UStrPos(SubStr, S: Pointer; Offset: Integer): Integer;

{ AnsiString family and ShortString (rtl-abi.md section 3, 9.4; system.md RTL-078). Byte strings share the TStrRec
  header (ElemSize 1); their bytes are always UTF-8 (ABI-027): a code page argument is stored in new blocks (0 is
  stored as 65001) and never changes how bytes are read. A ShortString argument is the address of its length byte.
  Comparison is bytewise, a common prefix puts the shorter first. }
procedure _AStrClr(var S: Pointer);
procedure _AStrAddRef(S: Pointer);
procedure _AStrAsg(var Dest: Pointer; Src: Pointer);
procedure _AStrFromPCharLen(var Dest: Pointer; P: PAnsiChar; Len: Integer; CodePage: Word);
procedure _AStrCat(var Dest: Pointer; A, B: Pointer);
function _AStrCmp(A, B: Pointer): Integer;
procedure _AStrUnique(var S: Pointer);
procedure _AStrSetLen(var S: Pointer; NewLen: Integer; CodePage: Word);
procedure _AStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
procedure _UStrFromAStr(var Dest: Pointer; Src: Pointer);
procedure _AStrFromUStr(var Dest: Pointer; Src: Pointer; CodePage: Word);
procedure _UStrFromSStr(var Dest: Pointer; Src: Pointer);
procedure _SStrFromUStr(Dest: Pointer; MaxLen: Integer; Src: Pointer);
procedure _SStrAsg(Dest: Pointer; MaxLen: Integer; Src: Pointer);
function _SStrCmp(A, B: Pointer): Integer;
{ Str of Booleans, strings and characters (lead note C-063): Dest := S left-padded with spaces to Width UTF-16 units }
procedure _StrUStr(var Dest: Pointer; S: Pointer; Width: Integer);

{ dynamic arrays and managed values (rtl-abi.md section 4, 9.5, 10; system.md section 9). TI is the type-info block of
  the dynamic array type (_DynArray*) or of the value type (_Finalize, _AddRef, _Copy*); managed kinds: UnicodeString,
  AnsiString, dynamic array, and records / static arrays containing them. _DynArraySetLen takes DimCount lengths,
  outermost first; the remaining ones are applied to every element (a negative length is runtime error 201). }
procedure _DynArrayClr(var A: Pointer; TI: Pointer);
procedure _DynArrayAddRef(A: Pointer);
procedure _DynArrayAsg(var Dest: Pointer; Src: Pointer; TI: Pointer);
procedure _DynArraySetLen(var A: Pointer; TI: Pointer; DimCount: NativeInt; Dims: PNativeInt);
procedure _DynArrayCopy(var Dest: Pointer; Src: Pointer; TI: Pointer; Index, Count: NativeInt);
procedure _Finalize(P: Pointer; TI: Pointer);
procedure _FinalizeArray(P: Pointer; TI: Pointer; Count: NativeInt);
procedure _AddRef(P: Pointer; TI: Pointer);
procedure _AddRefArray(P: Pointer; TI: Pointer; Count: NativeInt);
procedure _CopyRecord(Dest, Src: Pointer; TI: Pointer);
procedure _CopyArray(Dest, Src: Pointer; TI: Pointer; Count: NativeInt);

const
  CP_ACP = 0;
  CP_UTF8 = 65001;
  CP_UTF16 = 1200;
  DefaultSystemCodePage = 65001;       { RTL-014, ABI-027: never the OS code page }

{ public string routines (system.md section 5.3); Copy, Pos, Delete, Insert, SetLength, Concat and UpCase are compiler
  intrinsics lowered to the helpers above }
procedure UniqueString(var S: string);
function StringOfChar(Ch: Char; Count: Integer): string;
procedure SetString(var S: string; Buffer: PChar; Len: Integer);
{ TODO(compiler): the RawByteString overloads of these three (R-017) wait for overload resolution between compatible
  candidates (E2900 today); an AnsiString argument is converted to string until then }
function StringRefCount(const S: string): Integer;
function StringCodePage(const S: string): Word;
function StringElementSize(const S: string): Word;
function UTF8Encode(const S: string): RawByteString;            { code page 65001 }
function UTF8ToString(const S: RawByteString): string;
function UTF8Decode(const S: RawByteString): string;

{ big sets: 32-byte operands, bit k = bit k mod 8 of byte k div 8 (rtl-abi.md ABI-050, section 9.8) }
procedure _SetUnion(Dest, A, B: Pointer);
procedure _SetInter(Dest, A, B: Pointer);
procedure _SetDiff(Dest, A, B: Pointer);
procedure _SetSymDiff(Dest, A, B: Pointer);
function _SetEq(A, B: Pointer): Boolean;
function _SetLe(A, B: Pointer): Boolean;
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);

{ real conversions (rtl-abi.md section 9.10, system.md section 5.2); Sqrt is llvm.sqrt in the compiler }
function _Trunc(X: Double): Int64;
function _Round(X: Double): Int64;
function _Int(X: Double): Double;
function _Frac(X: Double): Double;

{ transcendental functions (system.md section 5.2, RTL-041); Ln(X <= 0) is runtime error 207 }
function Sin(X: Double): Double;
function Cos(X: Double): Double;
function ArcTan(X: Double): Double;
function Exp(X: Double): Double;
function Ln(X: Double): Double;

{ pseudo-random numbers (system.md section 5.2, RTL-040): RandSeed := RandSeed * 134775813 + 1 (mod 2^32) per draw }
function Random: Double; overload;                     { new RandSeed * 2^-32, in [0, 1) }
function Random(Range: Integer): Integer; overload;    { in [0, Range); 0 without a draw when Range <= 0 }
procedure Randomize;                                   { RandSeed from the tick count and the performance counter }

{ exact decimal digits of a binary float (system.md RTL-100); used by the float Write/Str helpers and SysUtils }
const
  FloatDigitsMax = 768;                { a Double's exact expansion has at most 767 significant digits }

type
  TFloatDigits = record
    Count: Integer;                    { digits in Digits[0..Count-1]; 0 for zero, infinity and NaN }
    Exponent: Integer;                 { value = 0.D0 D1 ... D(Count-1) x 10^Exponent }
    Negative: Boolean;                 { the sign bit (also set for -0, -Inf and a NaN with the sign bit) }
    IsNaN: Boolean;
    IsInfinite: Boolean;
    Digits: array[0..FloatDigitsMax - 1] of Byte;   { values 0..9, Digits[0] <> 0 }
  end;

{ shortest digits that read back (round-half-even) to the same Double, or to the same Single when IsSingle (V must
  then hold a Single value); of the shortest candidates the one nearest to V, a tie going to the even digit }
procedure _FloatDigitsShortest(V: Double; IsSingle: Boolean; var R: TFloatDigits);
{ the exact value rounded to Precision significant digits (clipped to 1..FloatDigitsMax) with round-half-even;
  Count = Precision, trailing zeros included }
procedure _FloatDigitsPrecision(V: Double; Precision: Integer; var R: TFloatDigits);

{ text output (rtl-abi.md section 9.9) }
procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteAStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteSStr(var T: Text; S: Pointer; Width: Integer);
procedure _WritePWChar(var T: Text; P: PWideChar; Width: Integer);
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
procedure _WriteLn(var T: Text);
procedure _WriteEnd(var T: Text);

{ Str and Val (rtl-abi.md section 9.10, lead note C-062; system.md RTL-079, LANG-950/951). Str writes exactly the text
  Write would write into Dest. Val accepts leading spaces, a sign, decimal digits or $ / 0x / 0X hex digits (reals:
  digits [. digits] [e [sign] digits], also . digits, INF and NAN); no trailing characters. On failure the result is
  0 and Code the 1-based index of the first offending character (Length + 1 when the text ends too early); a value
  outside Int64 / UInt64 gives Code = Length(S). _ValFloat is correctly rounded (half-even). }
procedure _StrInt(var Dest: Pointer; V: Int64; Width: Integer);
procedure _StrUInt(var Dest: Pointer; V: UInt64; Width: Integer);
procedure _StrFloat(var Dest: Pointer; V: Double; Width, Decimals: Integer);
function _ValInt(S: Pointer; var Code: Integer): Int64;
function _ValUInt(S: Pointer; var Code: Integer): UInt64;
function _ValFloat(S: Pointer; var Code: Integer): Double;

{ symbols LLVM-generated code may reference, exact link names (rtl-abi.md section 9.2, RTL-065); the compiler gives
  them the no-builtins attribute (CMP-610), so their loops are not turned back into calls to themselves }
function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memcpy';
function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memmove';
function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer; public name 'memset';
function memcmp(A, B: Pointer; N: NativeUInt): Integer; public name 'memcmp';
function strlen(S: PAnsiChar): NativeUInt; public name 'strlen';
function wcslen(S: PWideChar): NativeUInt; public name 'wcslen';

implementation

{$POINTERMATH ON}

{$I AbiLayout.inc}                     { TStrRec, TDynArrayRec, TTextRec (RTL-029) }

const
  kernel32 = 'kernel32.dll';
  STD_INPUT_HANDLE = $FFFFFFF6;        { (DWORD)-10 }
  STD_OUTPUT_HANDLE = $FFFFFFF5;       { (DWORD)-11 }
  STD_ERROR_HANDLE = $FFFFFFF4;        { (DWORD)-12 }
  INVALID_HANDLE_VALUE = $FFFFFFFFFFFFFFFF;
  IMAGE_SUBSYSTEM_WINDOWS_CUI = 3;
  PeHeaderOffsetField = $3C;           { IMAGE_DOS_HEADER.e_lfanew }
  PeSubsystemOffset = 92;              { signature 4 + file header 20 + optional header offset 68 }

type
  { rtl-abi.md ABI-100: one unit-table entry }
  TUnitEntry = record
    Init: Pointer;
    Final: Pointer;
  end;
  PUnitEntry = ^TUnitEntry;

function GetStdHandle(nStdHandle: Cardinal): THandle; stdcall; external kernel32 name 'GetStdHandle';
function GetModuleHandleW(lpModuleName: PWideChar): THandle; stdcall; external kernel32 name 'GetModuleHandleW';
function GetCommandLineW: PWideChar; stdcall; external kernel32 name 'GetCommandLineW';
function GetCurrentThreadId: Cardinal; stdcall; external kernel32 name 'GetCurrentThreadId';
procedure ExitProcess(uExitCode: Cardinal); stdcall; external kernel32 name 'ExitProcess';
function WriteFile(hFile: THandle; lpBuffer: Pointer; nNumberOfBytesToWrite: Cardinal; var lpNumberOfBytesWritten: Cardinal;
  lpOverlapped: Pointer): LongBool; stdcall; external kernel32 name 'WriteFile';
function WriteConsoleW(hConsoleOutput: THandle; lpBuffer: Pointer; nNumberOfCharsToWrite: Cardinal;
  var lpNumberOfCharsWritten: Cardinal; lpReserved: Pointer): LongBool; stdcall; external kernel32 name 'WriteConsoleW';
function GetConsoleMode(hConsoleHandle: THandle; var lpMode: Cardinal): LongBool; stdcall;
  external kernel32 name 'GetConsoleMode';
function GetTickCount: Cardinal; stdcall; external kernel32 name 'GetTickCount';
function QueryPerformanceCounter(var lpPerformanceCount: Int64): LongBool; stdcall;
  external kernel32 name 'QueryPerformanceCounter';
function GetProcessHeap: THandle; stdcall; external kernel32 name 'GetProcessHeap';
function HeapAlloc(hHeap: THandle; dwFlags: Cardinal; dwBytes: NativeUInt): Pointer; stdcall; external kernel32 name 'HeapAlloc';
function HeapFree(hHeap: THandle; dwFlags: Cardinal; lpMem: Pointer): LongBool; stdcall; external kernel32 name 'HeapFree';
function HeapReAlloc(hHeap: THandle; dwFlags: Cardinal; lpMem: Pointer; dwBytes: NativeUInt): Pointer; stdcall;
  external kernel32 name 'HeapReAlloc';

var
  _fltused: Integer = $9875; public name '_fltused';   { referenced by objects that use floating point }
  ProcessHeap: THandle;
  Units: PUnitEntry;
  Entries: NativeInt;
  InitCount: NativeInt;
  ShutdownStep: Integer;               { ABI-104: last step started; each step runs at most once }

function TextRec(var T: Text): PTextRec;
begin
  Result := PTextRec(@T);
end;

{ ---------------------------------------------------------------- memory manager (RTL-060 ... RTL-064) }
{ The default manager maps onto the process heap, whose blocks are 16-byte aligned on Win64 (RTL-061).
  TODO(C-161): TMemoryManager, GetMemoryManager, SetMemoryManager and IsMemoryManagerSet (RTL-062) need procedural
  types; every allocation already goes through the four routines below, which will dispatch through the manager. }

const
  HEAP_ZERO_MEMORY = $00000008;

var
  BlocksAllocated: NativeInt;          { RTL-064 counters }
  BlocksFreed: NativeInt;

function HeapAllocate(Flags: Cardinal; Size: NativeInt): Pointer;
begin
  Result := nil;
  if Size <= 0 then
    Exit;
  Result := HeapAlloc(ProcessHeap, Flags, Size);
  if Result = nil then
    _RunError(203);
  AtomicIncrement(BlocksAllocated);    { RTL-064: interlocked counters }
end;

function _GetMem(Size: NativeInt): Pointer;
begin
  Result := HeapAllocate(0, Size);
end;

function _AllocZero(Size: NativeInt): Pointer;
begin
  Result := HeapAllocate(HEAP_ZERO_MEMORY, Size);
end;

function AllocMem(Size: NativeInt): Pointer;
begin
  Result := HeapAllocate(HEAP_ZERO_MEMORY, Size);
end;

procedure _FreeMem(P: Pointer);
begin
  if P = nil then
    Exit;
  if not HeapFree(ProcessHeap, 0, P) then
    _RunError(204);
  AtomicIncrement(BlocksFreed);
end;

function _ReallocMem(P: Pointer; Size: NativeInt): Pointer;
begin
  if P = nil then
    Result := _GetMem(Size)
  else if Size <= 0 then
  begin
    _FreeMem(P);
    Result := nil;
  end
  else
  begin
    Result := HeapReAlloc(ProcessHeap, 0, P, Size);
    if Result = nil then
      _RunError(203);                  { the block stays where it was; the process ends }
  end;
end;

function _LiveAllocCount: NativeInt;
begin
  Result := BlocksAllocated - BlocksFreed;
end;

{ ---------------------------------------------------------------- UnicodeString (rtl-abi.md section 3, 9.3) }
{ Blocks are 16 + (Length + 1) * 2 bytes from _GetMem (ABI-020); length 0 is always nil (ABI-021); counts change with
  atomic increments and decrements (ABI-022, RTL-071); RefCount -1 is a literal: never counted, never freed, copied
  before any write (ABI-025). Every routine reads all of its operands before it releases the old Dest value. }

const
  UStrCodePage = 1200;
  UStrElemSize = 2;
  MaxStrLen = $7FFFFFFE;               { ABI-026: 2^31 - 2 elements }

function StrRec(S: Pointer): PStrRec;
begin
  Result := PStrRec(PByte(S) - SizeOf(TStrRec));
end;

function StrLength(S: Pointer): Integer;
begin
  Result := 0;
  if S <> nil then
    Result := StrRec(S)^.Length;
end;

{ a new block of Len (> 0) elements, RefCount 1, terminator set, elements undefined }
function NewUStr(Len: NativeInt): Pointer;
var
  R: PStrRec;
begin
  if Len > MaxStrLen then
    _RunError(203);
  R := _GetMem(SizeOf(TStrRec) + (Len + 1) * UStrElemSize);
  R^.RefCount := 1;
  R^.Length := Len;
  R^.CodePage := UStrCodePage;
  R^.ElemSize := UStrElemSize;
  R^.Reserved := 0;
  Result := PByte(R) + SizeOf(TStrRec);
  PWord(Result)[Len] := 0;
end;

procedure Release(S: Pointer);
var
  R: PStrRec;
begin
  if S = nil then
    Exit;
  R := StrRec(S);
  if R^.RefCount < 0 then
    Exit;                              { literal }
  if AtomicDecrement(R^.RefCount) = 0 then
    _FreeMem(R);
end;

{ Dest := S, taking over S's reference; the old Dest is released last }
procedure Replace(var Dest: Pointer; S: Pointer);
var
  Old: Pointer;
begin
  Old := Dest;
  Dest := S;
  Release(Old);
end;

procedure _UStrClr(var S: Pointer);
begin
  Replace(S, nil);
end;

procedure _UStrAddRef(S: Pointer);
var
  R: PStrRec;
begin
  if S = nil then
    Exit;
  R := StrRec(S);
  if R^.RefCount >= 0 then
    AtomicIncrement(R^.RefCount);
end;

procedure _UStrAsg(var Dest: Pointer; Src: Pointer);
begin
  _UStrAddRef(Src);
  Replace(Dest, Src);
end;

function _UStrNew(Len: Integer): Pointer;
begin
  Result := nil;
  if Len > 0 then
    Result := NewUStr(Len);
end;

procedure _UStrFromPWCharLen(var Dest: Pointer; P: PWideChar; Len: Integer);
var
  N: Pointer;
begin
  N := nil;
  if Len > 0 then
  begin
    N := NewUStr(Len);
    if P <> nil then
      Move(P^, N^, NativeInt(Len) * UStrElemSize);
  end;
  Replace(Dest, N);
end;

procedure _UStrFromPWChar(var Dest: Pointer; P: PWideChar);
var
  Len: NativeUInt;
begin
  Len := 0;
  if P <> nil then
    Len := wcslen(P);
  if Len > MaxStrLen then
    _RunError(203);
  _UStrFromPWCharLen(Dest, P, Integer(Len));
end;

procedure _UStrFromWChar(var Dest: Pointer; C: WideChar);
var
  N: Pointer;
begin
  N := NewUStr(1);
  PWideChar(N)^ := C;
  Replace(Dest, N);
end;

{ RTL-074: with Dest = A and A unique, the block grows in place (ReallocMem); B = A is read from the moved block }
procedure _UStrCat(var Dest: Pointer; A, B: Pointer);
var
  LA, LB: Integer;
  N: Pointer;
  R: PStrRec;
begin
  LA := StrLength(A);
  LB := StrLength(B);
  if LB = 0 then
    _UStrAsg(Dest, A)
  else if LA = 0 then
    _UStrAsg(Dest, B)
  else
  begin
    if NativeInt(LA) + LB > MaxStrLen then
      _RunError(203);
    if (Dest = A) and (StrRec(A)^.RefCount = 1) then
    begin
      R := _ReallocMem(StrRec(A), SizeOf(TStrRec) + (NativeInt(LA) + LB + 1) * UStrElemSize);
      N := PByte(R) + SizeOf(TStrRec);
      if B = A then
        B := N;
      R^.Length := LA + LB;
      Move(B^, PWord(N)[LA], NativeInt(LB) * UStrElemSize);
      PWord(N)[LA + LB] := 0;
      Dest := N;
    end
    else
    begin
      N := NewUStr(NativeInt(LA) + LB);
      Move(A^, N^, NativeInt(LA) * UStrElemSize);
      Move(B^, PWord(N)[LA], NativeInt(LB) * UStrElemSize);
      Replace(Dest, N);
    end;
  end;
end;

procedure _UStrCatN(var Dest: Pointer; Parts: PPointer; Count: Integer);
var
  Total: NativeInt;
  I, Len, At: Integer;
  N: Pointer;
begin
  Total := 0;
  for I := 0 to Count - 1 do
    Total := Total + StrLength(Parts[I]);
  N := nil;
  if Total > 0 then
  begin
    N := NewUStr(Total);
    At := 0;
    for I := 0 to Count - 1 do
    begin
      Len := StrLength(Parts[I]);
      if Len > 0 then
        Move(Parts[I]^, PWord(N)[At], NativeInt(Len) * UStrElemSize);
      At := At + Len;
    end;
  end;
  Replace(Dest, N);
end;

{ RTL-076: ordinal by UTF-16 code unit; a common prefix puts the shorter string first }
function _UStrCmp(A, B: Pointer): Integer;
var
  LA, LB, I, N: Integer;
  X, Y: PWord;
begin
  Result := 0;
  if A = B then
    Exit;
  LA := StrLength(A);
  LB := StrLength(B);
  N := LA;
  if LB < N then
    N := LB;
  X := A;
  Y := B;
  for I := 0 to N - 1 do
    if X[I] <> Y[I] then
    begin
      Result := Integer(X[I]) - Integer(Y[I]);
      Exit;
    end;
  if LA < LB then
    Result := -1
  else if LA > LB then
    Result := 1;
end;

function _UStrEq(A, B: Pointer): Boolean;
var
  I, Len: Integer;
  X, Y: PWord;
begin
  Result := True;
  if A = B then
    Exit;
  Result := False;
  Len := StrLength(A);
  if Len <> StrLength(B) then
    Exit;
  X := A;
  Y := B;
  for I := 0 to Len - 1 do
    if X[I] <> Y[I] then
      Exit;
  Result := True;
end;

{ a private copy of S with NewLen (> 0) elements: the first min(old, NewLen) are copied }
function CopyResized(S: Pointer; NewLen: Integer): Pointer;
var
  Keep: Integer;
begin
  Result := NewUStr(NewLen);
  Keep := StrLength(S);
  if Keep > NewLen then
    Keep := NewLen;
  if Keep > 0 then
    Move(S^, Result^, NativeInt(Keep) * UStrElemSize);
end;

{ RTL-072: a shared or literal block is copied before a write }
procedure _UStrUnique(var S: Pointer);
begin
  if (S <> nil) and (StrRec(S)^.RefCount <> 1) then
    Replace(S, CopyResized(S, StrLength(S)));
end;

{ RTL-073: a unique block is reallocated with exactly NewLen + 1 elements; a shared one is copied }
procedure _UStrSetLen(var S: Pointer; NewLen: Integer);
var
  R: PStrRec;
begin
  if NewLen <= 0 then
    _UStrClr(S)
  else if (S <> nil) and (StrRec(S)^.RefCount = 1) then
  begin
    if NewLen > MaxStrLen then
      _RunError(203);
    R := _ReallocMem(StrRec(S), SizeOf(TStrRec) + (NativeInt(NewLen) + 1) * UStrElemSize);
    R^.Length := NewLen;
    S := PByte(R) + SizeOf(TStrRec);
    PWord(S)[NewLen] := 0;
  end
  else
    Replace(S, CopyResized(S, NewLen));
end;

{ system.md section 5.3 Copy: Index < 1 counts from 1 with Count unchanged; past the end or Count <= 0 is ''; Count is
  clipped to the end. Always a new block (or nil). }
procedure _UStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
var
  Len: Integer;
  N: Pointer;
begin
  Len := StrLength(S);
  if Index < 1 then
    Index := 1;
  N := nil;
  if (Index <= Len) and (Count > 0) then
  begin
    if Count > Len - Index + 1 then
      Count := Len - Index + 1;
    N := NewUStr(Count);
    Move(PWord(S)[Index - 1], N^, NativeInt(Count) * UStrElemSize);
  end;
  Replace(Dest, N);
end;

{ no-op for Index < 1, Index > Length or Count <= 0; Count clipped; the result is a new unique block }
procedure _UStrDelete(var S: Pointer; Index, Count: Integer);
var
  Len, Rest: Integer;
  N: Pointer;
begin
  Len := StrLength(S);
  if (Index < 1) or (Index > Len) or (Count <= 0) then
    Exit;
  if Count > Len - Index + 1 then
    Count := Len - Index + 1;
  N := nil;
  if Count < Len then
  begin
    N := NewUStr(Len - Count);
    Move(S^, N^, NativeInt(Index - 1) * UStrElemSize);
    Rest := Len - (Index - 1) - Count;
    Move(PWord(S)[Index - 1 + Count], PWord(N)[Index - 1], NativeInt(Rest) * UStrElemSize);
  end;
  Replace(S, N);
end;

{ Index < 1 inserts at the front, Index > Length appends; an empty Src changes nothing; Src may be Dest's value }
procedure _UStrInsert(Src: Pointer; var Dest: Pointer; Index: Integer);
var
  LS, LD: Integer;
  N: Pointer;
begin
  LS := StrLength(Src);
  if LS = 0 then
    Exit;
  LD := StrLength(Dest);
  if NativeInt(LS) + LD > MaxStrLen then
    _RunError(203);
  if Index < 1 then
    Index := 1
  else if Index > LD then
    Index := LD + 1;
  N := NewUStr(NativeInt(LS) + LD);
  if Index > 1 then
    Move(Dest^, N^, NativeInt(Index - 1) * UStrElemSize);
  Move(Src^, PWord(N)[Index - 1], NativeInt(LS) * UStrElemSize);
  if Index <= LD then
    Move(PWord(Dest)[Index - 1], PWord(N)[Index - 1 + LS], NativeInt(LD - Index + 1) * UStrElemSize);
  Replace(Dest, N);
end;

{ first position >= Offset (Offset < 1 counts from 1) of SubStr in S, ordinal; 0 when absent or SubStr = '' }
function _UStrPos(SubStr, S: Pointer; Offset: Integer): Integer;
var
  LSub, Len, I, J: Integer;
  X, Y: PWord;
begin
  Result := 0;
  LSub := StrLength(SubStr);
  Len := StrLength(S);
  if Offset < 1 then
    Offset := 1;
  if (LSub = 0) or (LSub > Len) then
    Exit;
  X := S;
  Y := SubStr;
  for I := Offset - 1 to Len - LSub do
  begin
    J := 0;
    while (J < LSub) and (X[I + J] = Y[J]) do
      J := J + 1;
    if J = LSub then
    begin
      Result := I + 1;
      Exit;
    end;
  end;
end;

{ bytes of two counted byte sequences, unsigned; equal prefixes order by length }
function CompareBytes(X, Y: PByte; LX, LY: Integer): Integer;
var
  I, N: Integer;
begin
  N := LX;
  if LY < N then
    N := LY;
  for I := 0 to N - 1 do
    if X[I] <> Y[I] then
    begin
      Result := Integer(X[I]) - Integer(Y[I]);
      Exit;
    end;
  Result := 0;
  if LX < LY then
    Result := -1
  else if LX > LY then
    Result := 1;
end;

function _AStrCmp(A, B: Pointer): Integer;
begin
  Result := 0;
  if A <> B then
    Result := CompareBytes(A, B, StrLength(A), StrLength(B));
end;

{ a ShortString is a length byte followed by that many bytes }
function _SStrCmp(A, B: Pointer): Integer;
var
  X, Y: PByte;
begin
  X := A;
  Y := B;
  Result := CompareBytes(X + 1, Y + 1, X[0], Y[0]);
end;

{ ---------------------------------------------------------------- AnsiString family and ShortString (rtl-abi.md 9.4) }
{ Byte-string blocks are 16 + Length + 1 bytes (ABI-020) with ElemSize 1; counting, literals and copy-on-write work as
  for UnicodeString (the header is the same, so Release / Replace / _UStrAddRef serve both). The bytes are UTF-8 whatever
  the code page says (ABI-027, RTL-078). }

{ the code point of the UTF-8 sequence at P[I], advancing I; a byte that does not start a complete, shortest,
  non-surrogate sequence is one U+FFFD (system.md RTL-078) }
function NextUtf8(P: PByte; Len: Integer; var I: Integer): Cardinal;
var
  B, Lo, Hi: Cardinal;
  Need, K: Integer;
begin
  B := P[I];
  I := I + 1;
  Result := B;
  if B < $80 then
    Exit;
  Result := $FFFD;
  Lo := $80;
  Hi := $BF;
  if (B >= $C2) and (B <= $DF) then
  begin
    Need := 1;
    B := B and $1F;
  end
  else if (B >= $E0) and (B <= $EF) then
  begin
    Need := 2;
    if B = $E0 then
      Lo := $A0                        { no overlong forms }
    else if B = $ED then
      Hi := $9F;                       { no surrogates }
    B := B and $0F;
  end
  else if (B >= $F0) and (B <= $F4) then
  begin
    Need := 3;
    if B = $F0 then
      Lo := $90
    else if B = $F4 then
      Hi := $8F;                       { nothing above U+10FFFF }
    B := B and $07;
  end
  else
    Exit;
  if I + Need > Len then
    Exit;
  for K := 0 to Need - 1 do
  begin
    if (P[I + K] < Lo) or (P[I + K] > Hi) then
      Exit;
    B := (B shl 6) or (P[I + K] and $3F);
    Lo := $80;
    Hi := $BF;
  end;
  I := I + Need;
  Result := B;
end;

const
  AStrElemSize = 1;
  ShortStrMax = 255;

{ a new byte block of Len (> 0) bytes, RefCount 1, terminator set, bytes undefined; code page 0 is stored as 65001 }
function NewAStr(Len: NativeInt; CodePage: Word): Pointer;
var
  R: PStrRec;
begin
  if Len > MaxStrLen then
    _RunError(203);
  if CodePage = CP_ACP then
    CodePage := DefaultSystemCodePage;
  R := _GetMem(SizeOf(TStrRec) + Len + 1);
  R^.RefCount := 1;
  R^.Length := Len;
  R^.CodePage := CodePage;
  R^.ElemSize := AStrElemSize;
  R^.Reserved := 0;
  Result := PByte(R) + SizeOf(TStrRec);
  PByte(Result)[Len] := 0;
end;

{ a private copy of S with NewLen (> 0) bytes and S's code page (CodePage when S is nil); min(old, NewLen) copied }
function ACopyResized(S: Pointer; NewLen: Integer; CodePage: Word): Pointer;
var
  Keep: Integer;
begin
  if S <> nil then
    CodePage := StrRec(S)^.CodePage;
  Result := NewAStr(NewLen, CodePage);
  Keep := StrLength(S);
  if Keep > NewLen then
    Keep := NewLen;
  if Keep > 0 then
    Move(S^, Result^, Keep);
end;

procedure _AStrClr(var S: Pointer);
begin
  Replace(S, nil);
end;

procedure _AStrAddRef(S: Pointer);
begin
  _UStrAddRef(S);
end;

procedure _AStrAsg(var Dest: Pointer; Src: Pointer);
begin
  _UStrAddRef(Src);
  Replace(Dest, Src);
end;

procedure _AStrFromPCharLen(var Dest: Pointer; P: PAnsiChar; Len: Integer; CodePage: Word);
var
  N: Pointer;
begin
  N := nil;
  if Len > 0 then
  begin
    N := NewAStr(Len, CodePage);
    if P <> nil then
      Move(P^, N^, Len);
  end;
  Replace(Dest, N);
end;

{ Dest := A + B with the code page of the first non-nil operand (lead note C-063); an empty operand shares the other
  one's block; with Dest = A and A unique the block grows in place }
procedure _AStrCat(var Dest: Pointer; A, B: Pointer);
var
  LA, LB: Integer;
  N: Pointer;
  R: PStrRec;
begin
  LA := StrLength(A);
  LB := StrLength(B);
  if LB = 0 then
    _AStrAsg(Dest, A)
  else if LA = 0 then
    _AStrAsg(Dest, B)
  else
  begin
    if NativeInt(LA) + LB > MaxStrLen then
      _RunError(203);
    if (Dest = A) and (StrRec(A)^.RefCount = 1) then
    begin
      R := _ReallocMem(StrRec(A), SizeOf(TStrRec) + NativeInt(LA) + LB + 1);
      N := PByte(R) + SizeOf(TStrRec);
      if B = A then
        B := N;
      R^.Length := LA + LB;
      Move(B^, PByte(N)[LA], LB);
      PByte(N)[LA + LB] := 0;
      Dest := N;
    end
    else
    begin
      N := NewAStr(NativeInt(LA) + LB, StrRec(A)^.CodePage);
      Move(A^, N^, LA);
      Move(B^, PByte(N)[LA], LB);
      Replace(Dest, N);
    end;
  end;
end;

procedure _AStrUnique(var S: Pointer);
begin
  if (S <> nil) and (StrRec(S)^.RefCount <> 1) then
    Replace(S, ACopyResized(S, StrLength(S), CP_ACP));
end;

{ as _UStrSetLen; a block keeps its code page, a new one (S was nil) gets CodePage }
procedure _AStrSetLen(var S: Pointer; NewLen: Integer; CodePage: Word);
var
  R: PStrRec;
begin
  if NewLen <= 0 then
    _AStrClr(S)
  else if (S <> nil) and (StrRec(S)^.RefCount = 1) then
  begin
    if NewLen > MaxStrLen then
      _RunError(203);
    R := _ReallocMem(StrRec(S), SizeOf(TStrRec) + NativeInt(NewLen) + 1);
    R^.Length := NewLen;
    S := PByte(R) + SizeOf(TStrRec);
    PByte(S)[NewLen] := 0;
  end
  else
    Replace(S, ACopyResized(S, NewLen, CodePage));
end;

{ as _UStrCopy, in bytes; the result has S's code page }
procedure _AStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
var
  Len: Integer;
  N: Pointer;
begin
  Len := StrLength(S);
  if Index < 1 then
    Index := 1;
  N := nil;
  if (Index <= Len) and (Count > 0) then
  begin
    if Count > Len - Index + 1 then
      Count := Len - Index + 1;
    N := NewAStr(Count, StrRec(S)^.CodePage);
    Move(PByte(S)[Index - 1], N^, Count);
  end;
  Replace(Dest, N);
end;

{ the UTF-8 encoding of Len UTF-16 units at Src, written to Dest (when not nil) up to Max bytes: a sequence that does
  not fit completely is left out with everything after it. A surrogate pair is one 4-byte sequence, a lone surrogate
  U+FFFD (EF BF BD, RTL-078). Returns the number of bytes. }
function Utf8FromUnits(Src: PWord; Len: Integer; Dest: PByte; Max: NativeInt): NativeInt;
var
  I, N: Integer;
  C: Cardinal;
begin
  Result := 0;
  I := 0;
  while I < Len do
  begin
    C := Src[I];
    I := I + 1;
    if (C >= $D800) and (C <= $DBFF) and (I < Len) and (Src[I] >= $DC00) and (Src[I] <= $DFFF) then
    begin
      C := $10000 + ((C - $D800) shl 10) + (Cardinal(Src[I]) - $DC00);
      I := I + 1;
    end
    else if (C >= $D800) and (C <= $DFFF) then
      C := $FFFD;
    if C < $80 then
      N := 1
    else if C < $800 then
      N := 2
    else if C < $10000 then
      N := 3
    else
      N := 4;
    if Result + N > Max then
      Exit;
    if Dest <> nil then
      case N of
        1:
          Dest[Result] := Byte(C);
        2:
          begin
            Dest[Result] := Byte($C0 or (C shr 6));
            Dest[Result + 1] := Byte($80 or (C and $3F));
          end;
        3:
          begin
            Dest[Result] := Byte($E0 or (C shr 12));
            Dest[Result + 1] := Byte($80 or ((C shr 6) and $3F));
            Dest[Result + 2] := Byte($80 or (C and $3F));
          end;
      else
        Dest[Result] := Byte($F0 or (C shr 18));
        Dest[Result + 1] := Byte($80 or ((C shr 12) and $3F));
        Dest[Result + 2] := Byte($80 or ((C shr 6) and $3F));
        Dest[Result + 3] := Byte($80 or (C and $3F));
      end;
    Result := Result + N;
  end;
end;

{ Dest := the UTF-16 decoding of Len UTF-8 bytes at P (NextUtf8: invalid bytes become U+FFFD) }
procedure UStrFromUtf8(var Dest: Pointer; P: PByte; Len: Integer);
var
  I, Units: Integer;
  C: Cardinal;
  N: Pointer;
  W: PWord;
begin
  Units := 0;
  I := 0;
  while I < Len do
    if NextUtf8(P, Len, I) >= $10000 then
      Units := Units + 2
    else
      Units := Units + 1;
  N := nil;
  if Units > 0 then
  begin
    N := NewUStr(Units);
    W := N;
    Units := 0;
    I := 0;
    while I < Len do
    begin
      C := NextUtf8(P, Len, I);
      if C >= $10000 then
      begin
        W[Units] := Word($D800 + ((C - $10000) shr 10));
        W[Units + 1] := Word($DC00 + ((C - $10000) and $3FF));
        Units := Units + 2;
      end
      else
      begin
        W[Units] := Word(C);
        Units := Units + 1;
      end;
    end;
  end;
  Replace(Dest, N);
end;

procedure _UStrFromAStr(var Dest: Pointer; Src: Pointer);
begin
  UStrFromUtf8(Dest, Src, StrLength(Src));
end;

procedure _AStrFromUStr(var Dest: Pointer; Src: Pointer; CodePage: Word);
var
  Len: Integer;
  Bytes: NativeInt;
  N: Pointer;
begin
  Len := StrLength(Src);
  N := nil;
  Bytes := Utf8FromUnits(Src, Len, nil, High(NativeInt));
  if Bytes > 0 then
  begin
    N := NewAStr(Bytes, CodePage);
    Utf8FromUnits(Src, Len, N, Bytes);
  end;
  Replace(Dest, N);
end;

procedure _UStrFromSStr(var Dest: Pointer; Src: Pointer);
var
  P: PByte;
begin
  P := Src;
  UStrFromUtf8(Dest, P + 1, P[0]);
end;

function ShortCapacity(MaxLen: Integer): Integer;
begin
  Result := MaxLen;
  if Result > ShortStrMax then
    Result := ShortStrMax
  else if Result < 0 then
    Result := 0;
end;

procedure _SStrFromUStr(Dest: Pointer; MaxLen: Integer; Src: Pointer);
var
  P: PByte;
begin
  P := Dest;
  P[0] := Byte(Utf8FromUnits(Src, StrLength(Src), P + 1, ShortCapacity(MaxLen)));
end;

{ the number of bytes of P[0..Len-1] kept when at most Max (< Len) fit: Max, moved back to the start of a UTF-8
  sequence that would be cut (RTL-078); bytes that are no valid sequence are cut anywhere }
function Utf8Cut(P: PByte; Max: Integer): Integer;
var
  J, Need: Integer;
begin
  Result := Max;
  if (P[Max] and $C0) <> $80 then
    Exit;                              { the first dropped byte starts a sequence }
  J := Max - 1;
  while (J >= 0) and (Max - J <= 3) and ((P[J] and $C0) = $80) do
    J := J - 1;
  if (J < 0) or (P[J] < $C0) then
    Exit;
  if P[J] >= $F0 then
    Need := 4
  else if P[J] >= $E0 then
    Need := 3
  else
    Need := 2;
  if J + Need > Max then
    Result := J;
end;

procedure _SStrAsg(Dest: Pointer; MaxLen: Integer; Src: Pointer);
var
  D, S: PByte;
  Len: Integer;
begin
  D := Dest;
  S := Src;
  Len := S[0];
  MaxLen := ShortCapacity(MaxLen);
  if Len > MaxLen then
    Len := Utf8Cut(S + 1, MaxLen);
  Move(S[1], D[1], Len);
  D[0] := Byte(Len);
end;

procedure _StrUStr(var Dest: Pointer; S: Pointer; Width: Integer);
var
  Len, I: Integer;
  N: Pointer;
begin
  Len := StrLength(S);
  if Width <= Len then
    _UStrAsg(Dest, S)
  else
  begin
    N := NewUStr(Width);
    for I := 0 to Width - Len - 1 do
      PWord(N)[I] := 32;
    if Len > 0 then
      Move(S^, PWord(N)[Width - Len], NativeInt(Len) * UStrElemSize);
    Replace(Dest, N);
  end;
end;

{ ---------------------------------------------------------------- dynamic arrays, managed values (rtl-abi.md 4, 9.5) }
{ A block is 16 + Length * ElSize bytes (ABI-040), RefCount > 0, counted with atomic increments (RTL-080); the last
  release finalises the managed elements in index order, then frees the block (RTL-085). Writing an element never
  copies; SetLength and Copy return unique blocks (ABI-042). }

const
  KindUString = 9;                     { rtl-abi.md section 10 kind values }
  KindAString = 10;
  KindRecord = 12;
  KindArray = 13;
  KindDynArray = 14;

function DynRec(A: Pointer): PDynArrayRec;
begin
  Result := PDynArrayRec(PByte(A) - SizeOf(TDynArrayRec));
end;

function DynLength(A: Pointer): NativeInt;
begin
  Result := 0;
  if A <> nil then
    Result := DynRec(A)^.Length;
end;

procedure ReleaseDynArray(A: Pointer; TI: Pointer);
var
  R: PDynArrayRec;
  El: Pointer;
begin
  if A = nil then
    Exit;
  R := DynRec(A);
  if AtomicDecrement(R^.RefCount) = 0 then
  begin
    El := PDynArrayTypeInfo(TI)^.ManagedElType;
    if El <> nil then
      _FinalizeArray(A, El, R^.Length);
    _FreeMem(R);
  end;
end;

procedure _DynArrayClr(var A: Pointer; TI: Pointer);
var
  Old: Pointer;
begin
  Old := A;
  A := nil;
  ReleaseDynArray(Old, TI);
end;

procedure _DynArrayAddRef(A: Pointer);
begin
  if A <> nil then
    AtomicIncrement(DynRec(A)^.RefCount);
end;

procedure _DynArrayAsg(var Dest: Pointer; Src: Pointer; TI: Pointer);
var
  Old: Pointer;
begin
  _DynArrayAddRef(Src);
  Old := Dest;
  Dest := Src;
  ReleaseDynArray(Old, TI);
end;

{ the payload size of Len elements; more than a block can hold is runtime error 203 }
function DynPayload(Len, ElSize: NativeInt): NativeInt;
begin
  if (ElSize > 0) and (Len > (High(NativeInt) - SizeOf(TDynArrayRec)) div ElSize) then
    _RunError(203);
  Result := Len * ElSize;
end;

{ a new block of Len (> 0) elements, RefCount 1; zero-filled when Zero }
function NewDynArray(Len, ElSize: NativeInt; Zero: Boolean): Pointer;
var
  R: PDynArrayRec;
  Size: NativeInt;
begin
  Size := SizeOf(TDynArrayRec) + DynPayload(Len, ElSize);
  if Zero then
    R := _AllocZero(Size)
  else
    R := _GetMem(Size);
  R^.RefCount := 1;
  R^.Reserved := 0;
  R^.Length := Len;
  Result := PByte(R) + SizeOf(TDynArrayRec);
end;

{ RTL-081 for one dimension (NewLen >= 0): a unique block is resized in place (dropped elements finalised, new ones
  zeroed), a shared one is copied (kept managed elements gain a reference) and released; the result is unique }
procedure SetDynLength(var A: Pointer; TI: Pointer; NewLen: NativeInt);
var
  Info: PDynArrayTypeInfo;
  R: PDynArrayRec;
  OldLen, Keep: NativeInt;
  N, Old: Pointer;
begin
  if NewLen = 0 then
  begin
    _DynArrayClr(A, TI);
    Exit;
  end;
  Info := TI;
  OldLen := DynLength(A);
  if (A <> nil) and (DynRec(A)^.RefCount = 1) then
  begin
    if NewLen = OldLen then
      Exit;
    if (NewLen < OldLen) and (Info^.ManagedElType <> nil) then
      _FinalizeArray(PByte(A) + NewLen * Info^.ElSize, Info^.ManagedElType, OldLen - NewLen);
    R := _ReallocMem(DynRec(A), SizeOf(TDynArrayRec) + DynPayload(NewLen, Info^.ElSize));
    R^.Length := NewLen;
    A := PByte(R) + SizeOf(TDynArrayRec);
    if NewLen > OldLen then
      memset(PByte(A) + OldLen * Info^.ElSize, 0, (NewLen - OldLen) * Info^.ElSize);
  end
  else
  begin
    N := NewDynArray(NewLen, Info^.ElSize, True);
    Keep := OldLen;
    if Keep > NewLen then
      Keep := NewLen;
    if Keep > 0 then
    begin
      Move(A^, N^, Keep * Info^.ElSize);
      if Info^.ManagedElType <> nil then
        _AddRefArray(N, Info^.ManagedElType, Keep);
    end;
    Old := A;
    A := N;
    ReleaseDynArray(Old, TI);
  end;
end;

{ every length is checked before anything changes; inner dimensions are set on every element, so the result is
  rectangular and every inner array unique (RTL-081) }
procedure _DynArraySetLen(var A: Pointer; TI: Pointer; DimCount: NativeInt; Dims: PNativeInt);
var
  I: NativeInt;
  Inner: Pointer;
begin
  if DimCount < 1 then
    Exit;
  for I := 0 to DimCount - 1 do
    if Dims[I] < 0 then
      _RunError(201);
  SetDynLength(A, TI, Dims[0]);
  if DimCount > 1 then
  begin
    Inner := PDynArrayTypeInfo(TI)^.ManagedElType;
    for I := 0 to Dims[0] - 1 do
      _DynArraySetLen(PPointer(A)[I], Inner, DimCount - 1, Dims + 1);
  end;
end;

{ RTL-082 with the clamping of Copy for strings, 0-based: Index < 0 counts from 0 with Count unchanged; nothing at or
  past the end or Count <= 0 gives nil; Count is clipped to the end }
procedure _DynArrayCopy(var Dest: Pointer; Src: Pointer; TI: Pointer; Index, Count: NativeInt);
var
  Info: PDynArrayTypeInfo;
  Len: NativeInt;
  N, Old: Pointer;
begin
  Info := TI;
  Len := DynLength(Src);
  if Index < 0 then
    Index := 0;
  N := nil;
  if (Index < Len) and (Count > 0) then
  begin
    if Count > Len - Index then
      Count := Len - Index;
    N := NewDynArray(Count, Info^.ElSize, False);
    Move(PByte(Src)[Index * Info^.ElSize], N^, Count * Info^.ElSize);
    if Info^.ManagedElType <> nil then
      _AddRefArray(N, Info^.ManagedElType, Count);
  end;
  Old := Dest;
  Dest := N;
  ReleaseDynArray(Old, TI);
end;

{ Count consecutive values of type TI at P: every managed part released and set to nil (nil parts are skipped) }
procedure _FinalizeArray(P: Pointer; TI: Pointer; Count: NativeInt);
var
  I, J: NativeInt;
  Rec: PRecordTypeInfo;
  F: PManagedField;
  Arr: PArrayTypeInfo;
  Q: PByte;
begin
  if Count <= 0 then
    Exit;
  case PByte(TI)^ of
    KindUString, KindAString:
      for I := 0 to Count - 1 do
        _UStrClr(PPointer(P)[I]);
    KindDynArray:
      for I := 0 to Count - 1 do
        _DynArrayClr(PPointer(P)[I], TI);
    KindRecord:
      begin
        Rec := TI;
        Q := P;
        for I := 0 to Count - 1 do
        begin
          F := Rec^.ManagedFields;
          for J := 0 to Rec^.ManagedCount - 1 do
            _FinalizeArray(Q + F[J].Offset, F[J].TypeInfo, 1);
          Q := Q + Rec^.Size;
        end;
      end;
    KindArray:
      begin
        Arr := TI;
        if Arr^.ElType <> nil then
          _FinalizeArray(P, Arr^.ElType, Count * Arr^.ElCount);
      end;
  end;
end;

procedure _Finalize(P: Pointer; TI: Pointer);
begin
  _FinalizeArray(P, TI, 1);
end;

{ one more reference on every managed part of Count values of type TI at P (after a raw copy) }
procedure _AddRefArray(P: Pointer; TI: Pointer; Count: NativeInt);
var
  I, J: NativeInt;
  Rec: PRecordTypeInfo;
  F: PManagedField;
  Arr: PArrayTypeInfo;
  Q: PByte;
begin
  if Count <= 0 then
    Exit;
  case PByte(TI)^ of
    KindUString, KindAString:
      for I := 0 to Count - 1 do
        _UStrAddRef(PPointer(P)[I]);
    KindDynArray:
      for I := 0 to Count - 1 do
        _DynArrayAddRef(PPointer(P)[I]);
    KindRecord:
      begin
        Rec := TI;
        Q := P;
        for I := 0 to Count - 1 do
        begin
          F := Rec^.ManagedFields;
          for J := 0 to Rec^.ManagedCount - 1 do
            _AddRefArray(Q + F[J].Offset, F[J].TypeInfo, 1);
          Q := Q + Rec^.Size;
        end;
      end;
    KindArray:
      begin
        Arr := TI;
        if Arr^.ElType <> nil then
          _AddRefArray(P, Arr^.ElType, Count * Arr^.ElCount);
      end;
  end;
end;

procedure _AddRef(P: Pointer; TI: Pointer);
begin
  _AddRefArray(P, TI, 1);
end;

{ the size of one value of type TI }
function ManagedSize(TI: Pointer): NativeInt;
begin
  case PByte(TI)^ of
    KindRecord:
      Result := PRecordTypeInfo(TI)^.Size;
    KindArray:
      Result := PArrayTypeInfo(TI)^.Size;
  else
    Result := SizeOf(Pointer);
  end;
end;

{ Dest[i] := Src[i] for Count values of type TI with reference counting. Dest = Src is a no-op; otherwise the ranges
  must not overlap. Records and static arrays: the source parts gain their references before the old destination
  parts are released, then all bytes are copied. }
procedure _CopyArray(Dest, Src: Pointer; TI: Pointer; Count: NativeInt);
var
  I: NativeInt;
begin
  if (Count <= 0) or (Dest = Src) then
    Exit;
  case PByte(TI)^ of
    KindUString, KindAString:
      for I := 0 to Count - 1 do
        _UStrAsg(PPointer(Dest)[I], PPointer(Src)[I]);
    KindDynArray:
      for I := 0 to Count - 1 do
        _DynArrayAsg(PPointer(Dest)[I], PPointer(Src)[I], TI);
  else
    _AddRefArray(Src, TI, Count);
    _FinalizeArray(Dest, TI, Count);
    Move(Src^, Dest^, Count * ManagedSize(TI));
  end;
end;

procedure _CopyRecord(Dest, Src: Pointer; TI: Pointer);
begin
  _CopyArray(Dest, Src, TI, 1);
end;

{ ---------------------------------------------------------------- public string routines (system.md section 5.3) }
{ The value of a string variable is read and written as a Pointer through its address. }

procedure UniqueString(var S: string);
begin
  _UStrUnique(PPointer(@S)^);
end;

function StringOfChar(Ch: Char; Count: Integer): string;
var
  P: PWideChar;
  I: Integer;
begin
  _UStrSetLen(PPointer(@Result)^, Count);
  P := PPointer(@Result)^;
  for I := 0 to Count - 1 do
    P[I] := Ch;
end;

procedure SetString(var S: string; Buffer: PChar; Len: Integer);
begin
  _UStrFromPWCharLen(PPointer(@S)^, Buffer, Len);
end;

function StringRefCount(const S: string): Integer;
var
  P: Pointer;
begin
  P := PPointer(@S)^;
  Result := 0;
  if P <> nil then
    Result := StrRec(P)^.RefCount;
end;

function StringCodePage(const S: string): Word;
var
  P: Pointer;
begin
  P := PPointer(@S)^;
  Result := UStrCodePage;
  if P <> nil then
    Result := StrRec(P)^.CodePage;
end;

function StringElementSize(const S: string): Word;
var
  P: Pointer;
begin
  P := PPointer(@S)^;
  Result := UStrElemSize;
  if P <> nil then
    Result := StrRec(P)^.ElemSize;
end;

function UTF8Encode(const S: string): RawByteString;
begin
  _AStrFromUStr(PPointer(@Result)^, PPointer(@S)^, CP_UTF8);
end;

function UTF8ToString(const S: RawByteString): string;
begin
  _UStrFromAStr(PPointer(@Result)^, PPointer(@S)^);
end;

function UTF8Decode(const S: RawByteString): string;
begin
  _UStrFromAStr(PPointer(@Result)^, PPointer(@S)^);
end;

{ ---------------------------------------------------------------- text files: buffer and encoder (RTL-090 ... 093) }
{ Console kind: the buffer holds UTF-16 code units, written with WriteConsoleW. Byte-stream kind: the buffer holds
  UTF-8, written with WriteFile. Absent kind: output is discarded. }

procedure FlushText(var R: TTextRec);
var
  Written: Cardinal;
begin
  if R.BufPos <= 0 then
    Exit;
  if R.Kind = skConsole then
    WriteConsoleW(R.Handle, R.Buffer, R.BufPos div 2, Written, nil)
  else if R.Kind = skBytes then
    WriteFile(R.Handle, R.Buffer, R.BufPos, Written, nil);
  // TODO(R-032): a failed write sets InOutRes := 101 unless the pipe is closed (RTL-094)
  R.BufPos := 0;
end;

procedure PutByte(var R: TTextRec; B: Cardinal);
begin
  if R.BufPos >= TextBufSize then
    FlushText(R);
  R.Buffer[R.BufPos] := Byte(B);
  R.BufPos := R.BufPos + 1;
end;

procedure PutCodePoint(var R: TTextRec; C: Cardinal);
begin
  if C < $80 then
    PutByte(R, C)
  else if C < $800 then
  begin
    PutByte(R, $C0 or (C shr 6));
    PutByte(R, $80 or (C and $3F));
  end
  else if C < $10000 then
  begin
    PutByte(R, $E0 or (C shr 12));
    PutByte(R, $80 or ((C shr 6) and $3F));
    PutByte(R, $80 or (C and $3F));
  end
  else
  begin
    PutByte(R, $F0 or (C shr 18));
    PutByte(R, $80 or ((C shr 12) and $3F));
    PutByte(R, $80 or ((C shr 6) and $3F));
    PutByte(R, $80 or (C and $3F));
  end;
end;

{ one UTF-16 code unit; a high surrogate waits in PendingHigh for its low half, also across statements (RTL-093) }
procedure PutUnit(var R: TTextRec; U: Cardinal);
begin
  if R.Kind = skAbsent then
    Exit;
  if R.Kind = skConsole then
  begin
    PutByte(R, U and $FF);
    PutByte(R, U shr 8);
    Exit;
  end;
  if R.PendingHigh <> #0 then
  begin
    if (U >= $DC00) and (U <= $DFFF) then
    begin
      PutCodePoint(R, $10000 + ((Ord(R.PendingHigh) - $D800) shl 10) + (U - $DC00));
      R.PendingHigh := #0;
      Exit;
    end;
    R.PendingHigh := #0;
    PutCodePoint(R, $FFFD);
  end;
  if (U >= $D800) and (U <= $DBFF) then
    R.PendingHigh := WideChar(U)
  else if (U >= $DC00) and (U <= $DFFF) then
    PutCodePoint(R, $FFFD)
  else
    PutCodePoint(R, U);
end;

procedure PutSpaces(var R: TTextRec; Count: Integer);
begin
  while Count > 0 do
  begin
    PutUnit(R, 32);
    Count := Count - 1;
  end;
end;

{ ABI-104 step 4: an unpaired high surrogate left at the end is U+FFFD; then everything is written }
procedure FinishText(var T: Text);
var
  R: PTextRec;
begin
  R := TextRec(T);
  if R^.Buffer = nil then
    Exit;
  if R^.PendingHigh <> #0 then
  begin
    R^.PendingHigh := #0;
    PutCodePoint(R^, $FFFD);
  end;
  FlushText(R^);
end;

{ RTL-090: kind and buffer of a standard stream }
procedure OpenStandard(var T: Text; Which: Cardinal; Mode: TTextMode);
var
  R: PTextRec;
  ConsoleMode: Cardinal;
begin
  R := TextRec(T);
  R^.Handle := GetStdHandle(Which);
  R^.Mode := Mode;
  if (R^.Handle = 0) or (R^.Handle = INVALID_HANDLE_VALUE) then
    R^.Kind := skAbsent
  else if GetConsoleMode(R^.Handle, ConsoleMode) then
    R^.Kind := skConsole
  else
    R^.Kind := skBytes;
  R^.CodePage := 65001;
  R^.Buffer := _GetMem(TextBufSize);
end;

{ ---------------------------------------------------------------- start-up (ABI-105) }

{ the image's own PE optional header: Subsystem = 3 is a console program }
function ImageIsConsole: Boolean;
var
  Base: PByte;
  LfaNew: PInteger;
  Subsystem: PWord;
begin
  Base := PByte(HInstance);
  LfaNew := PInteger(Base + PeHeaderOffsetField);
  Subsystem := PWord(Base + LfaNew^ + PeSubsystemOffset);
  Result := Subsystem^ = IMAGE_SUBSYSTEM_WINDOWS_CUI;
end;

procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
begin
  Units := UnitTable;
  Entries := UnitCount;
  ProcessHeap := GetProcessHeap;
  HInstance := GetModuleHandleW(nil);
  MainThreadID := GetCurrentThreadId;
  CmdLine := GetCommandLineW;
  IsConsole := ImageIsConsole;
  // TODO(C-174): ABI-105 sets MXCSR = $1F80 (the Win64 default at process start, so nothing changes until then)
  OpenStandard(Input, STD_INPUT_HANDLE, tmInput);
  OpenStandard(Output, STD_OUTPUT_HANDLE, tmOutput);
  OpenStandard(ErrOutput, STD_ERROR_HANDLE, tmOutput);
  ExitCode := 0;
end;

{ ABI-100: Init routines in table order, counting the completed ones }
procedure _InitUnits;
var
  I: NativeInt;
begin
  for I := 0 to Entries - 1 do
  begin
    // TODO(C-161): call Units[I].Init through a procedural type. Until then no unit with an initialization part
    // can be linked (only program + System exist before C-068), so a non-nil entry is an invalid pointer operation.
    if Units[I].Init <> nil then
      _RunError(204);
    InitCount := I + 1;
  end;
end;

{ ---------------------------------------------------------------- shutdown (ABI-104) }

procedure _Halt0;
begin
  if ShutdownStep < 1 then
  begin
    ShutdownStep := 1;
    // TODO(C-161): call the ExitProc chain (R-010); ExitProc cannot be called before procedural types exist
    ExitProc := nil;
  end;
  if ShutdownStep < 2 then
  begin
    ShutdownStep := 2;
    // TODO(C-161): call Final of entries InitCount-1 ... 0 (R-010); _InitUnits guarantees there is none yet
    InitCount := 0;
  end;
  if ShutdownStep < 3 then
    ShutdownStep := 3;                 { leak report: R-077 }
  if ShutdownStep < 4 then
  begin
    ShutdownStep := 4;
    FinishText(Output);
    FinishText(ErrOutput);
  end;
  ExitProcess(ExitCode);
end;

procedure _Halt(Code: Integer);
begin
  ExitCode := Code;
  _Halt0;
end;

{ RTL-131/132, ABI-101 }
procedure _RunError(Code: Integer);
var
  R: PTextRec;
  Address: NativeUInt;
  Shift: Integer;
  Digit: Cardinal;
begin
  // TODO(C-173): ErrorAddr := _ReturnAddress (the caller's address); it stays nil until the intrinsic exists
  ErrorAddr := nil;
  // TODO(C-161): if ErrorProc <> nil then call ErrorProc(Code, ErrorAddr) (it normally raises and does not return);
  // a procedural variable cannot be called before C-161, so the fatal path is always taken
  ExitCode := Code;
  FinishText(Output);
  // TODO(C-161): with IsConsole = False and ShowErrorProc set, pass the text to ShowErrorProc instead
  R := TextRec(ErrOutput);
  Write(ErrOutput, 'Runtime error ', Code, ' at $');
  Address := NativeUInt(ErrorAddr);
  Shift := 60;
  while Shift >= 0 do
  begin
    Digit := (Address shr Shift) and $F;
    if Digit < 10 then
      PutUnit(R^, 48 + Digit)
    else
      PutUnit(R^, 55 + Digit);         { 'A' - 10 }
    Shift := Shift - 4;
  end;
  Writeln(ErrOutput);
  _Halt(Code);
end;

{ error paths of the inline checks the compiler emits (RTL-143, ABI-102) }
procedure _RangeError;
begin
  _RunError(201);
end;

procedure _OverflowError;
begin
  _RunError(215);
end;

procedure _DivZeroError;
begin
  _RunError(200);
end;

procedure _InvalidCast;
begin
  _RunError(219);
end;

{ ABI-132: the compiler calls this after every text-I/O statement in $I+ state }
procedure _IOCheck;
begin
  // TODO(R-032): read and reset the per-thread InOutRes; no helper records an I/O error before R-031
end;

{ ---------------------------------------------------------------- big sets (rtl-abi.md section 9.8) }
{ Dest may be the same block as A or B: every byte is read before the same byte of Dest is written. }

const
  SetBytes = 32;

procedure _SetUnion(Dest, A, B: Pointer);
var
  D, X, Y: PByte;
  I: Integer;
begin
  D := Dest;
  X := A;
  Y := B;
  for I := 0 to SetBytes - 1 do
    D[I] := X[I] or Y[I];
end;

procedure _SetInter(Dest, A, B: Pointer);
var
  D, X, Y: PByte;
  I: Integer;
begin
  D := Dest;
  X := A;
  Y := B;
  for I := 0 to SetBytes - 1 do
    D[I] := X[I] and Y[I];
end;

procedure _SetDiff(Dest, A, B: Pointer);
var
  D, X, Y: PByte;
  I: Integer;
begin
  D := Dest;
  X := A;
  Y := B;
  for I := 0 to SetBytes - 1 do
    D[I] := X[I] and not Y[I];
end;

procedure _SetSymDiff(Dest, A, B: Pointer);
var
  D, X, Y: PByte;
  I: Integer;
begin
  D := Dest;
  X := A;
  Y := B;
  for I := 0 to SetBytes - 1 do
    D[I] := X[I] xor Y[I];
end;

function _SetEq(A, B: Pointer): Boolean;
var
  X, Y: PByte;
  I: Integer;
begin
  X := A;
  Y := B;
  Result := False;
  for I := 0 to SetBytes - 1 do
    if X[I] <> Y[I] then
      Exit;
  Result := True;
end;

{ A <= B: no element of A is missing from B }
function _SetLe(A, B: Pointer): Boolean;
var
  X, Y: PByte;
  I: Integer;
begin
  X := A;
  Y := B;
  Result := False;
  for I := 0 to SetBytes - 1 do
    if X[I] and not Y[I] <> 0 then
      Exit;
  Result := True;
end;

{ Lo > Hi is tested first: the compiler clips the bounds under $R- so that this is the only case outside 0..255 }
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);
var
  D: PByte;
  K: Integer;
begin
  if Lo > Hi then
    Exit;
  if (Lo < 0) or (Hi > 255) then
    _RangeError;
  D := Dest;
  for K := Lo to Hi do
    D[K shr 3] := D[K shr 3] or (1 shl (K and 7));
end;

{ ---------------------------------------------------------------- float digits (RTL-100) }
{ Free-format digit generation with exact big-integer arithmetic (the classic method: the value and the half-gaps
  to its neighbours are the ratios R/S, MPlus/S, MMinus/S, scaled by a power of ten so that the first digit comes
  out of 10R/S). Fixed precision uses the same scaling and rounds the remainder half-even. The numbers stay below
  2^1140 here; decimal-to-binary conversion (_ValFloat) needs up to about 2^3750, hence 128 limbs. }

const
  BigLimbs = 128;

type
  TBig = record
    Len: Integer;                      { limbs in use, W[Len-1] <> 0; 0 for the value 0 }
    W: array[0..BigLimbs - 1] of Cardinal;
  end;
  PUInt64 = ^UInt64;

procedure BigSet(var A: TBig; V: UInt64);
begin
  A.W[0] := Cardinal(V and $FFFFFFFF);
  A.W[1] := Cardinal(V shr 32);
  if A.W[1] <> 0 then
    A.Len := 2
  else if A.W[0] <> 0 then
    A.Len := 1
  else
    A.Len := 0;
end;

procedure BigMulSmall(var A: TBig; M: Cardinal);
var
  I: Integer;
  Carry: UInt64;
begin
  Carry := 0;
  for I := 0 to A.Len - 1 do
  begin
    Carry := UInt64(A.W[I]) * M + Carry;
    A.W[I] := Cardinal(Carry and $FFFFFFFF);
    Carry := Carry shr 32;
  end;
  if Carry <> 0 then
  begin
    A.W[A.Len] := Cardinal(Carry);
    A.Len := A.Len + 1;
  end;
end;

procedure BigMulPow10(var A: TBig; N: Integer);
var
  M: Cardinal;
begin
  while N >= 9 do
  begin
    BigMulSmall(A, 1000000000);
    N := N - 9;
  end;
  M := 1;
  while N > 0 do
  begin
    M := M * 10;
    N := N - 1;
  end;
  BigMulSmall(A, M);
end;

{ A := A * 2^N, N >= 0 }
procedure BigShl(var A: TBig; N: Integer);
var
  Words, Bits, I: Integer;
begin
  if A.Len = 0 then
    Exit;
  Words := N shr 5;
  Bits := N and 31;
  if Bits = 0 then
    for I := A.Len - 1 downto 0 do
      A.W[I + Words] := A.W[I]
  else
  begin
    A.W[A.Len + Words] := A.W[A.Len - 1] shr (32 - Bits);
    for I := A.Len - 1 downto 1 do
      A.W[I + Words] := Cardinal(((UInt64(A.W[I]) shl Bits) or (A.W[I - 1] shr (32 - Bits))) and $FFFFFFFF);
    A.W[Words] := Cardinal((UInt64(A.W[0]) shl Bits) and $FFFFFFFF);
    A.Len := A.Len + 1;
  end;
  for I := 0 to Words - 1 do
    A.W[I] := 0;
  A.Len := A.Len + Words;
  if A.W[A.Len - 1] = 0 then
    A.Len := A.Len - 1;
end;

function BigCmp(const A, B: TBig): Integer;
var
  I: Integer;
begin
  Result := 0;
  if A.Len <> B.Len then
  begin
    if A.Len < B.Len then
      Result := -1
    else
      Result := 1;
    Exit;
  end;
  for I := A.Len - 1 downto 0 do
    if A.W[I] <> B.W[I] then
    begin
      if A.W[I] < B.W[I] then
        Result := -1
      else
        Result := 1;
      Exit;
    end;
end;

procedure BigAdd(var A: TBig; const B: TBig);
var
  I, N: Integer;
  Sum: UInt64;
begin
  N := A.Len;
  if B.Len > N then
    N := B.Len;
  Sum := 0;
  for I := 0 to N - 1 do
  begin
    if I < A.Len then
      Sum := Sum + A.W[I];
    if I < B.Len then
      Sum := Sum + B.W[I];
    A.W[I] := Cardinal(Sum and $FFFFFFFF);
    Sum := Sum shr 32;
  end;
  A.Len := N;
  if Sum <> 0 then
  begin
    A.W[N] := Cardinal(Sum);
    A.Len := N + 1;
  end;
end;

{ A := A - B, A >= B }
procedure BigSub(var A: TBig; const B: TBig);
var
  I: Integer;
  D: Int64;
  Borrow: Int64;
begin
  Borrow := 0;
  for I := 0 to A.Len - 1 do
  begin
    D := Int64(A.W[I]) - Borrow;
    if I < B.Len then
      D := D - B.W[I];
    if D < 0 then
    begin
      D := D + $100000000;
      Borrow := 1;
    end
    else
      Borrow := 0;
    A.W[I] := Cardinal(D);
  end;
  while (A.Len > 0) and (A.W[A.Len - 1] = 0) do
    A.Len := A.Len - 1;
end;

{ Compare(A + B, C) }
function BigCmpSum(const A, B, C: TBig): Integer;
var
  T: TBig;
begin
  T := A;
  BigAdd(T, B);
  Result := BigCmp(T, C);
end;

{ next digit: R := 10 R mod S, result 10 R div S (0..9 while R < S) }
function BigNextDigit(var R: TBig; const S: TBig): Integer;
begin
  BigMulSmall(R, 10);
  Result := 0;
  while BigCmp(R, S) >= 0 do
  begin
    BigSub(R, S);
    Result := Result + 1;
  end;
end;

{ estimate of the decimal exponent K with 10^(K-1) <= F * 2^E < 10^K, possibly one off (log10(2) ~ 1233/4096) }
function EstimateExponent(F: UInt64; E: Integer): Integer;
var
  Bits: Integer;
begin
  Bits := 0;
  while F <> 0 do
  begin
    Bits := Bits + 1;
    F := F shr 1;
  end;
  Result := ((E + Bits - 1) * 1233) div 4096 + 1;
end;

{ R/S := F * 2^E, multiplied by 10^-K for the estimated K }
procedure ScaleValue(F: UInt64; E, K: Integer; var R, S: TBig);
begin
  BigSet(R, F);
  BigSet(S, 1);
  if E >= 0 then
    BigShl(R, E)
  else
    BigShl(S, -E);
  if K >= 0 then
    BigMulPow10(S, K)
  else
    BigMulPow10(R, -K);
end;

{ sign, NaN, infinity, zero; False when V is finite and not zero, with V = F * 2^E, F < 2^Precision, E >= MinE }
function DecodeFloat(V: Double; IsSingle: Boolean; var R: TFloatDigits; var F: UInt64; var E, MinE: Integer;
  var Hidden: UInt64): Boolean;
var
  Bits: UInt64;
  S: Single;
  Bits32: Cardinal;
  BiasedExp: Integer;
  Frac: UInt64;
  MaxExp, Bias, FracBits: Integer;
begin
  if IsSingle then
  begin
    S := V;
    Bits32 := PCardinal(@S)^;
    R.Negative := (Bits32 shr 31) <> 0;
    BiasedExp := (Bits32 shr 23) and $FF;
    Frac := Bits32 and $7FFFFF;
    MaxExp := $FF;
    Bias := 150;
    FracBits := 23;
  end
  else
  begin
    Bits := PUInt64(@V)^;
    R.Negative := (Bits shr 63) <> 0;
    BiasedExp := (Bits shr 52) and $7FF;
    Frac := Bits and $FFFFFFFFFFFFF;
    MaxExp := $7FF;
    Bias := 1075;
    FracBits := 52;
  end;
  R.Count := 0;
  R.Exponent := 0;
  R.IsNaN := (BiasedExp = MaxExp) and (Frac <> 0);
  R.IsInfinite := (BiasedExp = MaxExp) and (Frac = 0);
  Hidden := UInt64(1) shl FracBits;
  MinE := 1 - Bias;
  if BiasedExp = 0 then
  begin
    F := Frac;
    E := MinE;
  end
  else
  begin
    F := Frac or Hidden;
    E := BiasedExp - Bias;
  end;
  Result := (BiasedExp <> MaxExp) and (F <> 0);
end;

procedure _FloatDigitsShortest(V: Double; IsSingle: Boolean; var R: TFloatDigits);
var
  F, Hidden: UInt64;
  E, MinE, K, Digit, C: Integer;
  Even, Unequal, Low, High: Boolean;
  Rm, S, MPlus, MMinus, T: TBig;
begin
  if not DecodeFloat(V, IsSingle, R, F, E, MinE, Hidden) then
    Exit;
  Even := (F and 1) = 0;               { the reader rounds half-even: an even F also owns both boundaries }
  Unequal := (F = Hidden) and (E > MinE);   { the gap below a power of two is half the gap above }
  K := EstimateExponent(F, E);
  { 2 V = R/S with half-gaps MPlus/S, MMinus/S; with unequal gaps everything is doubled once more }
  if Unequal then
  begin
    ScaleValue(F * 4, E, K, Rm, S);
    BigSet(MPlus, 2);
    BigSet(MMinus, 1);
    BigShl(S, 2);
  end
  else
  begin
    ScaleValue(F * 2, E, K, Rm, S);
    BigSet(MPlus, 1);
    BigSet(MMinus, 1);
    BigShl(S, 1);
  end;
  if E >= 0 then
  begin
    BigShl(MPlus, E);
    BigShl(MMinus, E);
  end;
  if K < 0 then
  begin
    BigMulPow10(MPlus, -K);
    BigMulPow10(MMinus, -K);
  end;
  { fix K: the upper boundary (R + MPlus)/S must lie below 1 (or at 1 when it is excluded) and not below 1/10 }
  while True do
  begin
    C := BigCmpSum(Rm, MPlus, S);
    if (C > 0) or (Even and (C = 0)) then
    begin
      BigMulSmall(S, 10);
      K := K + 1;
    end
    else
      Break;
  end;
  while True do
  begin
    T := Rm;
    BigAdd(T, MPlus);
    BigMulSmall(T, 10);
    C := BigCmp(T, S);
    if (C < 0) or (not Even and (C = 0)) then
    begin
      BigMulSmall(Rm, 10);
      BigMulSmall(MPlus, 10);
      BigMulSmall(MMinus, 10);
      K := K - 1;
    end
    else
      Break;
  end;
  R.Exponent := K;
  repeat
    Digit := BigNextDigit(Rm, S);
    BigMulSmall(MPlus, 10);
    BigMulSmall(MMinus, 10);
    C := BigCmp(Rm, MMinus);
    Low := (C < 0) or (Even and (C = 0));        { the digits so far are inside the interval }
    C := BigCmpSum(Rm, MPlus, S);
    High := (C > 0) or (Even and (C = 0));       { the digits so far plus one unit are inside the interval }
    if Low and High then
    begin
      T := Rm;
      BigShl(T, 1);
      C := BigCmp(T, S);
      if (C > 0) or ((C = 0) and ((Digit and 1) <> 0)) then
        Digit := Digit + 1;
    end
    else if High then
      Digit := Digit + 1;
    R.Digits[R.Count] := Digit;
    R.Count := R.Count + 1;
  until Low or High;
end;

{ V rounded to Count significant digits, or with Fixed to Count digits after the decimal point (then fewer than one
  significant digit can remain: a value below the rounding unit gives zero or one unit). HalfEven selects the tie rule;
  otherwise ties go away from zero. Count = digits produced (missing digits up to the requested position are 0). }
procedure RoundedDigits(V: Double; Count: Integer; Fixed, HalfEven: Boolean; var R: TFloatDigits);
var
  F, Hidden: UInt64;
  E, MinE, K, I, C, Precision: Integer;
  Rm, S, T: TBig;
  Up: Boolean;
begin
  if not DecodeFloat(V, False, R, F, E, MinE, Hidden) then
    Exit;
  K := EstimateExponent(F, E);
  ScaleValue(F, E, K, Rm, S);
  { fix K: 1/10 <= R/S < 1 }
  while BigCmp(Rm, S) >= 0 do
  begin
    BigMulSmall(S, 10);
    K := K + 1;
  end;
  while True do
  begin
    T := Rm;
    BigMulSmall(T, 10);
    if BigCmp(T, S) >= 0 then
      Break;
    Rm := T;
    K := K - 1;
  end;
  { now V = 0.D0 D1 ... x 10^K exactly }
  Precision := Count;
  if Fixed then
    Precision := K + Count;
  if Precision < 0 then
    Exit;                              { below a tenth of the rounding unit: zero }
  if Precision > FloatDigitsMax then
    Precision := FloatDigitsMax;
  for I := 0 to Precision - 1 do
    R.Digits[I] := BigNextDigit(Rm, S);
  R.Count := Precision;
  R.Exponent := K;
  BigShl(Rm, 1);
  C := BigCmp(Rm, S);                  { remainder against half a unit of the last digit }
  Up := C > 0;
  if C = 0 then
    Up := not HalfEven or ((Precision > 0) and ((R.Digits[Precision - 1] and 1) <> 0));
  if Up then
  begin
    I := Precision - 1;
    while (I >= 0) and (R.Digits[I] = 9) do
    begin
      R.Digits[I] := 0;
      I := I - 1;
    end;
    if I < 0 then
    begin
      R.Digits[0] := 1;                { 9.99 -> 10.0: the digits are 1 0 0, one decade higher }
      R.Exponent := K + 1;
      if R.Count = 0 then
        R.Count := 1;
    end
    else
      R.Digits[I] := R.Digits[I] + 1;
  end
  else if Precision = 0 then
    R.Exponent := 0;
end;

procedure _FloatDigitsPrecision(V: Double; Precision: Integer; var R: TFloatDigits);
begin
  if Precision < 1 then
    Precision := 1
  else if Precision > FloatDigitsMax then
    Precision := FloatDigitsMax;
  RoundedDigits(V, Precision, False, True, R);
end;

{ ---------------------------------------------------------------- Write helpers (system.md section 10.2) }
{ Field width: right-aligned, padded with spaces counted in UTF-16 code units (LANG-915); Width <= 0 pads nothing. }

procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
var
  R: PTextRec;
  P: PWord;
  Len, I: Integer;
begin
  R := TextRec(T);
  Len := 0;
  if S <> nil then
    Len := PStrRec(PByte(S) - SizeOf(TStrRec))^.Length;
  PutSpaces(R^, Width - Len);
  P := PWord(S);
  for I := 0 to Len - 1 do
    PutUnit(R^, P[I]);
end;

{ a nil PWideChar writes nothing but the padding }
procedure _WritePWChar(var T: Text; P: PWideChar; Width: Integer);
var
  R: PTextRec;
  Len, I: NativeInt;
begin
  R := TextRec(T);
  Len := 0;
  if P <> nil then
    Len := wcslen(P);
  if Len < Width then
    PutSpaces(R^, Width - Integer(Len));
  for I := 0 to Len - 1 do
    PutUnit(R^, Ord(P[I]));
end;

{ Len bytes of UTF-8 as text; the field width counts the UTF-16 code units of the decoded text }
procedure WriteUtf8(var T: Text; P: PByte; Len, Width: Integer);
var
  R: PTextRec;
  I, Units: Integer;
  C: Cardinal;
begin
  R := TextRec(T);
  Units := 0;
  I := 0;
  while I < Len do
  begin
    if NextUtf8(P, Len, I) >= $10000 then
      Units := Units + 2
    else
      Units := Units + 1;
  end;
  PutSpaces(R^, Width - Units);
  I := 0;
  while I < Len do
  begin
    C := NextUtf8(P, Len, I);
    if C >= $10000 then
    begin
      PutUnit(R^, $D800 + ((C - $10000) shr 10));
      PutUnit(R^, $DC00 + ((C - $10000) and $3FF));
    end
    else
      PutUnit(R^, C);
  end;
end;

{ every AnsiString code page is UTF-8 here: 65001, and $FFFF / 0 are read as UTF-8 (ABI-027, RTL-078) }
procedure _WriteAStr(var T: Text; S: Pointer; Width: Integer);
begin
  WriteUtf8(T, S, StrLength(S), Width);
end;

procedure _WriteSStr(var T: Text; S: Pointer; Width: Integer);
var
  P: PByte;
begin
  P := S;
  WriteUtf8(T, P + 1, P[0], Width);
end;

procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
var
  R: PTextRec;
begin
  R := TextRec(T);
  PutSpaces(R^, Width - 1);
  PutUnit(R^, Ord(C));
end;

{ ABI-027: an AnsiChar is widened by zero extension (byte b -> U+00b) }
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
var
  R: PTextRec;
begin
  R := TextRec(T);
  PutSpaces(R^, Width - 1);
  PutUnit(R^, Ord(C));
end;

procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
var
  Digits: array[0..23] of Byte;
  Pos, I: Integer;
  R: PTextRec;
begin
  Pos := 24;
  repeat
    Pos := Pos - 1;
    Digits[Pos] := 48 + V mod 10;
    V := V div 10;
  until V = 0;
  R := TextRec(T);
  PutSpaces(R^, Width - (24 - Pos));
  for I := Pos to 23 do
    PutUnit(R^, Digits[I]);
end;

procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
var
  Digits: array[0..23] of Byte;
  Pos, I: Integer;
  Digit: Int64;
  Negative: Boolean;
  R: PTextRec;
begin
  Negative := V < 0;
  Pos := 24;
  repeat
    Digit := V mod 10;                 { works in the negative range too, so Low(Int64) needs no special case }
    if Digit < 0 then
      Digit := -Digit;
    Pos := Pos - 1;
    Digits[Pos] := 48 + Digit;
    V := V div 10;
  until V = 0;
  if Negative then
  begin
    Pos := Pos - 1;
    Digits[Pos] := 45;
  end;
  R := TextRec(T);
  PutSpaces(R^, Width - (24 - Pos));
  for I := Pos to 23 do
    PutUnit(R^, Digits[I]);
end;

procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
var
  R: PTextRec;
begin
  R := TextRec(T);
  if V then
  begin
    PutSpaces(R^, Width - 4);
    PutUnit(R^, Ord('T'));
    PutUnit(R^, Ord('R'));
    PutUnit(R^, Ord('U'));
    PutUnit(R^, Ord('E'));
  end
  else
  begin
    PutSpaces(R^, Width - 5);
    PutUnit(R^, Ord('F'));
    PutUnit(R^, Ord('A'));
    PutUnit(R^, Ord('L'));
    PutUnit(R^, Ord('S'));
    PutUnit(R^, Ord('E'));
  end;
end;

{ ---------------------------------------------------------------- real conversions (system.md section 5.2) }
{ Worked on the bit pattern X = (-1)^s * F * 2^(BiasedExp - 1075), so no floating-point rounding is involved. }

type
  PDouble = ^Double;

const
  FracMask = $000FFFFFFFFFFFFF;
  SignMask = $8000000000000000;
  MinInt64Bits = $C3E0000000000000;    { -2^63, the only value with |X| >= 2^63 inside Int64 }

{ toward zero, or to nearest with ties to even; runtime error 207 outside Int64, for infinities and NaN }
function IntegerOf(X: Double; Nearest: Boolean): Int64;
var
  Bits, F, Q, Rest, Half: UInt64;
  BiasedExp, Shift: Integer;
begin
  Bits := PUInt64(@X)^;
  BiasedExp := (Bits shr 52) and $7FF;
  if BiasedExp >= 1023 + 63 then
  begin
    if Bits <> MinInt64Bits then
      _RunError(207);
    Result := Int64(UInt64(SignMask));
    Exit;
  end;
  F := Bits and FracMask;
  if BiasedExp <> 0 then
    F := F or (UInt64(1) shl 52);
  Shift := 1075 - BiasedExp;
  if Shift <= 0 then
    Q := F shl (-Shift)
  else if Shift >= 64 then
    Q := 0                             { |X| < 2^-11: truncates and rounds to 0 }
  else
  begin
    Q := F shr Shift;
    if Nearest then
    begin
      Rest := F and ((UInt64(1) shl Shift) - 1);
      Half := UInt64(1) shl (Shift - 1);
      if (Rest > Half) or ((Rest = Half) and ((Q and 1) <> 0)) then
        Q := Q + 1;
    end;
  end;
  if (Bits shr 63) <> 0 then
    Result := Int64(UInt64(0) - Q)
  else
    Result := Int64(Q);
end;

function _Trunc(X: Double): Int64;
begin
  Result := IntegerOf(X, False);
end;

function _Round(X: Double): Int64;
begin
  Result := IntegerOf(X, True);
end;

{ the fraction bits below the binary point are cleared; |X| < 1 keeps only the sign (Int(-0.5) = -0) }
function _Int(X: Double): Double;
var
  Bits: UInt64;
  BiasedExp: Integer;
begin
  Bits := PUInt64(@X)^;
  BiasedExp := (Bits shr 52) and $7FF;
  if BiasedExp >= 1075 then
  begin
    Result := X;                       { already integral, or an infinity or NaN }
    Exit;
  end;
  if BiasedExp < 1023 then
    Bits := Bits and SignMask
  else
    Bits := Bits and not ((UInt64(1) shl (1075 - BiasedExp)) - 1);
  Result := PDouble(@Bits)^;
end;

function _Frac(X: Double): Double;
begin
  Result := X - _Int(X);
end;

{$I FloatMath.inc}                     { Sin, Cos, ArcTan, Exp, Ln }

{ ---------------------------------------------------------------- Random (RTL-040) }

function NextSeed: Cardinal;
begin
  RandSeed := Cardinal((UInt64(RandSeed) * 134775813 + 1) and $FFFFFFFF);
  Result := RandSeed;
end;

function Random: Double;
begin
  Result := NextSeed * 2.3283064365386963e-10;       { 2^-32, exact }
end;

function Random(Range: Integer): Integer;
begin
  if Range <= 0 then
    Result := 0
  else
    Result := Integer((UInt64(NextSeed) * UInt64(Range)) shr 32);
end;

procedure Randomize;
var
  Counter: Int64;
begin
  Counter := 0;
  QueryPerformanceCounter(Counter);
  RandSeed := GetTickCount xor Cardinal(Counter and $FFFFFFFF);
end;

{ ---------------------------------------------------------------- float text (LANG-930 ... 933) }
{ Decimals < 0: scientific, Width - 9 fraction digits (1..16); otherwise fixed with Decimals (at most 40) places.
  Both round half away from zero on the exact value. }

const
  FloatTextMax = 400;                  { sign + 309 integer digits + point + 40 decimals, with room to spare }

type
  TFloatText = record
    Len: Integer;
    C: array[0..FloatTextMax - 1] of Byte;
  end;

procedure AddChar(var X: TFloatText; C: Integer);
begin
  X.C[X.Len] := C;
  X.Len := X.Len + 1;
end;

{ digit I of R (0 beyond the generated ones) }
function DigitAt(const R: TFloatDigits; I: Integer): Integer;
begin
  Result := 0;
  if (I >= 0) and (I < R.Count) then
    Result := R.Digits[I];
end;

procedure ScientificText(V: Double; Width: Integer; var X: TFloatText);
var
  R: TFloatDigits;
  Fraction, I, E: Integer;
begin
  Fraction := Width - 9;
  if Fraction < 1 then
    Fraction := 1
  else if Fraction > 16 then
    Fraction := 16;
  RoundedDigits(V, Fraction + 1, False, False, R);
  if R.Negative and (R.Count > 0) then
    AddChar(X, Ord('-'))
  else
    AddChar(X, Ord(' '));              { -0 prints with a space }
  AddChar(X, 48 + DigitAt(R, 0));
  AddChar(X, Ord('.'));
  for I := 1 to Fraction do
    AddChar(X, 48 + DigitAt(R, I));
  AddChar(X, Ord('E'));
  E := 0;
  if R.Count > 0 then
    E := R.Exponent - 1;
  if E < 0 then
  begin
    AddChar(X, Ord('-'));
    E := -E;
  end
  else
    AddChar(X, Ord('+'));
  AddChar(X, 48 + E div 1000);
  AddChar(X, 48 + (E div 100) mod 10);
  AddChar(X, 48 + (E div 10) mod 10);
  AddChar(X, 48 + E mod 10);
end;

procedure FixedText(V: Double; Decimals: Integer; var X: TFloatText);
var
  R: TFloatDigits;
  I: Integer;
begin
  if Decimals > 40 then
    Decimals := 40;
  RoundedDigits(V, Decimals, True, False, R);
  if R.Negative and (R.Count > 0) then  { a value that rounds to zero has no minus sign }
    AddChar(X, Ord('-'));
  if (R.Count = 0) or (R.Exponent <= 0) then
    AddChar(X, Ord('0'))
  else
    for I := 0 to R.Exponent - 1 do
      AddChar(X, 48 + DigitAt(R, I));
  if Decimals > 0 then
  begin
    AddChar(X, Ord('.'));
    for I := 1 to Decimals do
      if R.Count = 0 then
        AddChar(X, Ord('0'))
      else
        AddChar(X, 48 + DigitAt(R, R.Exponent + I - 1));
  end;
end;

{ the text of Write(V:Width:Decimals) before padding; Decimals < 0 is the scientific form }
procedure FloatText(V: Double; Width, Decimals: Integer; var X: TFloatText);
var
  Bits: UInt64;
begin
  X.Len := 0;
  Bits := PUInt64(@V)^;
  if (Bits and $7FF0000000000000) = $7FF0000000000000 then
  begin
    { LANG-932: NAN, INF, -INF }
    if (Bits and $000FFFFFFFFFFFFF) <> 0 then
    begin
      AddChar(X, Ord('N'));
      AddChar(X, Ord('A'));
      AddChar(X, Ord('N'));
    end
    else
    begin
      if (Bits shr 63) <> 0 then
        AddChar(X, Ord('-'));
      AddChar(X, Ord('I'));
      AddChar(X, Ord('N'));
      AddChar(X, Ord('F'));
    end;
  end
  else if Decimals < 0 then
    ScientificText(V, Width, X)
  else
    FixedText(V, Decimals, X);
end;

procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
var
  X: TFloatText;
  I: Integer;
  P: PTextRec;
begin
  FloatText(V, Width, Decimals, X);
  P := TextRec(T);
  PutSpaces(P^, Width - X.Len);
  for I := 0 to X.Len - 1 do
    PutUnit(P^, X.C[I]);
end;

{ ---------------------------------------------------------------- Str (LANG-951: exactly the text Write produces) }

{ Dest := X right-aligned in Width (padded with spaces on the left) }
procedure TextToStr(var Dest: Pointer; const X: TFloatText; Width: Integer);
var
  N: Pointer;
  Pad, I: Integer;
  P: PWord;
begin
  Pad := 0;
  if Width > X.Len then
    Pad := Width - X.Len;
  N := nil;
  if Pad + X.Len > 0 then
  begin
    N := NewUStr(Pad + X.Len);
    P := N;
    for I := 0 to Pad - 1 do
      P[I] := 32;
    for I := 0 to X.Len - 1 do
      P[Pad + I] := X.C[I];
  end;
  Replace(Dest, N);
end;

procedure UIntText(V: UInt64; var X: TFloatText);
var
  Digits: array[0..19] of Byte;
  N, I: Integer;
begin
  N := 0;
  repeat
    Digits[N] := 48 + V mod 10;
    V := V div 10;
    N := N + 1;
  until V = 0;
  for I := N - 1 downto 0 do
    AddChar(X, Digits[I]);
end;

procedure _StrInt(var Dest: Pointer; V: Int64; Width: Integer);
var
  X: TFloatText;
begin
  X.Len := 0;
  if V < 0 then
  begin
    AddChar(X, Ord('-'));
    UIntText(UInt64(0) - UInt64(V), X);  { the magnitude, also of Low(Int64) }
  end
  else
    UIntText(V, X);
  TextToStr(Dest, X, Width);
end;

procedure _StrUInt(var Dest: Pointer; V: UInt64; Width: Integer);
var
  X: TFloatText;
begin
  X.Len := 0;
  UIntText(V, X);
  TextToStr(Dest, X, Width);
end;

procedure _StrFloat(var Dest: Pointer; V: Double; Width, Decimals: Integer);
var
  X: TFloatText;
begin
  FloatText(V, Width, Decimals, X);
  TextToStr(Dest, X, Width);
end;

{ ---------------------------------------------------------------- Val (RTL-079, LANG-950) }

const
  ChPlus = 43;
  ChMinus = 45;
  ChDot = 46;
  ChSpace = 32;

{ value of the digit C (decimal, or hex when Hex), -1 when it is none }
function DigitValue(C: Cardinal; Hex: Boolean): Integer;
begin
  Result := -1;
  if (C >= 48) and (C <= 57) then
    Result := Integer(C) - 48
  else if Hex and (C >= 65) and (C <= 70) then
    Result := Integer(C) - 55
  else if Hex and (C >= 97) and (C <= 102) then
    Result := Integer(C) - 87;
end;

{ leading spaces (U+0020 only), then an optional sign; I is the index after them }
procedure ValPrefix(P: PWord; Len: Integer; var I: Integer; var Negative: Boolean);
begin
  I := 0;
  while (I < Len) and (P[I] = ChSpace) do
    I := I + 1;
  Negative := False;
  if (I < Len) and ((P[I] = ChPlus) or (P[I] = ChMinus)) then
  begin
    Negative := P[I] = ChMinus;
    I := I + 1;
  end;
end;

{ the magnitude of an integer text, Code <> 0 on a syntax error; Over when it needs more than 64 bits }
function ValMagnitude(S: Pointer; var Code: Integer; var Negative, Hex, Over: Boolean): UInt64;
var
  P: PWord;
  Len, I, Digit: Integer;
begin
  Result := 0;
  P := S;
  Len := StrLength(S);
  ValPrefix(P, Len, I, Negative);
  Hex := False;
  Over := False;
  if (I < Len) and (P[I] = 36) then                  { $ }
  begin
    Hex := True;
    I := I + 1;
  end
  else if (I + 1 < Len) and (P[I] = 48) and ((P[I + 1] = 120) or (P[I + 1] = 88)) then   { 0x 0X }
  begin
    Hex := True;
    I := I + 2;
  end;
  if I >= Len then
  begin
    Code := Len + 1;
    Exit;
  end;
  while I < Len do
  begin
    Digit := DigitValue(P[I], Hex);
    if Digit < 0 then
    begin
      Code := I + 1;
      Result := 0;
      Exit;
    end;
    if Hex then
    begin
      if (Result shr 60) <> 0 then
        Over := True;
      Result := (Result shl 4) or UInt64(Digit);
    end
    else
    begin
      if (Result > 1844674407370955161) or ((Result = 1844674407370955161) and (Digit > 5)) then
        Over := True;
      Result := Result * 10 + UInt64(Digit);
    end;
    I := I + 1;
  end;
  Code := 0;
end;

{ decimal: -2^63 .. 2^63 - 1; hex digits fill the 64 bits ($FFFFFFFFFFFFFFFF = -1) and a sign negates them }
function _ValInt(S: Pointer; var Code: Integer): Int64;
var
  M: UInt64;
  Negative, Hex, Over: Boolean;
begin
  Result := 0;
  M := ValMagnitude(S, Code, Negative, Hex, Over);
  if Code <> 0 then
    Exit;
  if not Hex and ((M > UInt64(SignMask)) or (not Negative and (M = UInt64(SignMask)))) then
    Over := True;
  if Over then
  begin
    Code := StrLength(S);
    Exit;
  end;
  if Negative then
    M := UInt64(0) - M;
  Result := Int64(M);
end;

{ a minus sign is accepted only before zero }
function _ValUInt(S: Pointer; var Code: Integer): UInt64;
var
  M: UInt64;
  Negative, Hex, Over: Boolean;
begin
  Result := 0;
  M := ValMagnitude(S, Code, Negative, Hex, Over);
  if Code <> 0 then
    Exit;
  if Over or (Negative and (M <> 0)) then
  begin
    Code := StrLength(S);
    Exit;
  end;
  Result := M;
end;

{ Correctly rounded decimal -> binary (LANG-943). The significant digits D (at most ValDigitsMax; later non-zero
  digits are kept as one final digit 1, which is exact for rounding because every halfway point between two Doubles
  has at most 767 significant digits) and the exponent E10 give the exact ratio V = R / S of big integers. With
  V = Q * 2^E, 2^52 <= Q < 2^53 (E = -1074 for subnormals), Q is the integer quotient rounded half-even by the
  remainder. }

const
  ValDigitsMax = 780;
  ValExponentMax = 100000;             { larger exponents only overflow or underflow }

function BigBitLength(const A: TBig): Integer;
var
  Top: Cardinal;
begin
  Result := 0;
  if A.Len = 0 then
    Exit;
  Result := (A.Len - 1) * 32;
  Top := A.W[A.Len - 1];
  while Top <> 0 do
  begin
    Result := Result + 1;
    Top := Top shr 1;
  end;
end;

{ Compare(A * 2^ShiftA, B * 2^ShiftB), shifts >= 0 }
function BigCmpShifted(const A: TBig; ShiftA: Integer; const B: TBig; ShiftB: Integer): Integer;
var
  X, Y: TBig;
begin
  X := A;
  Y := B;
  BigShl(X, ShiftA);
  BigShl(Y, ShiftB);
  Result := BigCmp(X, Y);
end;

{ D = Digits[0..Count-1] (Digits[0] <> 0), value D * 10^E10 }
function DecimalToDouble(Digits: PByte; Count, E10: Integer; Negative: Boolean): Double;
var
  R, S, T, Small: TBig;
  I, E, B: Integer;
  Q, Bits: UInt64;
begin
  Bits := 0;
  if Count + E10 > 310 then
    Bits := $7FF0000000000000          { at least 10^310: infinity }
  else if Count + E10 > -324 then      { otherwise below 10^-324, which rounds to zero }
  begin
    BigSet(R, 0);
    for I := 0 to Count - 1 do
    begin
      BigMulSmall(R, 10);
      BigSet(Small, Digits[I]);
      BigAdd(R, Small);
    end;
    BigSet(S, 1);
    if E10 >= 0 then
      BigMulPow10(R, E10)
    else
      BigMulPow10(S, -E10);
    E := BigBitLength(R) - BigBitLength(S) - 53;   { R / (S * 2^E) lies in (2^52, 2^54) }
    if E + 53 >= 0 then
      I := BigCmpShifted(R, 0, S, E + 53)
    else
      I := BigCmpShifted(R, -(E + 53), S, 0);
    if I >= 0 then
      E := E + 1;
    if E < -1074 then
      E := -1074;
    if E >= 0 then
      BigShl(S, E)
    else
      BigShl(R, -E);
    Q := 0;                            { Q := R div S (below 2^53), R := R mod S }
    for B := 53 downto 0 do
    begin
      T := S;
      BigShl(T, B);
      if BigCmp(R, T) >= 0 then
      begin
        BigSub(R, T);
        Q := Q or (UInt64(1) shl B);
      end;
    end;
    BigShl(R, 1);
    I := BigCmp(R, S);
    if (I > 0) or ((I = 0) and ((Q and 1) <> 0)) then
      Q := Q + 1;
    if Q = (UInt64(1) shl 53) then
    begin
      Q := UInt64(1) shl 52;
      E := E + 1;
    end;
    if E > 971 then
      Bits := $7FF0000000000000
    else if Q >= (UInt64(1) shl 52) then
      Bits := (UInt64(E + 1075) shl 52) or (Q and FracMask)
    else
      Bits := Q;                       { subnormal: E = -1074, biased exponent 0 }
  end;
  if Negative then
    Bits := Bits or SignMask;
  Result := BitsDouble(Bits);
end;

{ the word W (upper case) at P[I..] up to the end, case-insensitive; otherwise Code = the offending index }
function MatchWord(P: PWord; Len, I: Integer; const W: string; var Code: Integer): Boolean;
var
  K: Integer;
  C: Cardinal;
begin
  Result := False;
  for K := 1 to Length(W) do
  begin
    if I >= Len then
    begin
      Code := Len + 1;
      Exit;
    end;
    C := P[I];
    if (C >= 97) and (C <= 122) then
      C := C - 32;
    if C <> Ord(W[K]) then
    begin
      Code := I + 1;
      Exit;
    end;
    I := I + 1;
  end;
  if I < Len then
  begin
    Code := I + 1;
    Exit;
  end;
  Result := True;
end;

function _ValFloat(S: Pointer; var Code: Integer): Double;
var
  P: PWord;
  Len, I, Count, E10, Exponent, Digit, Seen: Integer;
  Negative, NegativeExponent, Sticky, Fraction: Boolean;
  Digits: array[0..ValDigitsMax] of Byte;
  C: Cardinal;
begin
  Result := 0;
  P := S;
  Len := StrLength(S);
  ValPrefix(P, Len, I, Negative);
  if I >= Len then
  begin
    Code := Len + 1;
    Exit;
  end;
  C := P[I];
  if (C = 73) or (C = 105) or (C = 78) or (C = 110) then   { I i N n }
  begin
    if (C = 73) or (C = 105) then
    begin
      if MatchWord(P, Len, I, 'INF', Code) then
        Result := BitsDouble($7FF0000000000000)
      else
        Exit;
    end
    else if MatchWord(P, Len, I, 'NAN', Code) then
      Result := BitsDouble($7FF8000000000000)
    else
      Exit;
    Code := 0;
    if Negative then
      Result := -Result;
    Exit;
  end;
  Count := 0;
  E10 := 0;
  Seen := 0;
  Sticky := False;
  Fraction := False;
  while I < Len do
  begin
    C := P[I];
    if (C = ChDot) and not Fraction then
    begin
      Fraction := True;
      I := I + 1;
      if I >= Len then
      begin
        Code := Len + 1;               { a point needs a digit after it }
        Exit;
      end;
      if DigitValue(P[I], False) < 0 then
      begin
        Code := I + 1;
        Exit;
      end;
      Continue;
    end;
    Digit := DigitValue(C, False);
    if Digit < 0 then
      Break;
    Seen := Seen + 1;
    if (Count = 0) and (Digit = 0) then
    begin
      if Fraction then
        E10 := E10 - 1;
    end
    else if Count < ValDigitsMax then
    begin
      Digits[Count] := Digit;
      Count := Count + 1;
      if Fraction then
        E10 := E10 - 1;
    end
    else
    begin
      if not Fraction then
        E10 := E10 + 1;
      if Digit <> 0 then
        Sticky := True;
    end;
    I := I + 1;
  end;
  if Seen = 0 then
  begin
    Code := I + 1;
    Exit;
  end;
  if (I < Len) and ((P[I] = 69) or (P[I] = 101)) then   { E e }
  begin
    I := I + 1;
    NegativeExponent := False;
    if (I < Len) and ((P[I] = ChPlus) or (P[I] = ChMinus)) then
    begin
      NegativeExponent := P[I] = ChMinus;
      I := I + 1;
    end;
    if I >= Len then
    begin
      Code := Len + 1;
      Exit;
    end;
    if DigitValue(P[I], False) < 0 then
    begin
      Code := I + 1;
      Exit;
    end;
    Exponent := 0;
    while (I < Len) and (DigitValue(P[I], False) >= 0) do
    begin
      if Exponent < ValExponentMax then
        Exponent := Exponent * 10 + DigitValue(P[I], False);
      I := I + 1;
    end;
    if NegativeExponent then
      E10 := E10 - Exponent
    else
      E10 := E10 + Exponent;
  end;
  if I < Len then
  begin
    Code := I + 1;
    Exit;
  end;
  Code := 0;
  if Count = 0 then
  begin
    if Negative then
      Result := BitsDouble(SignMask);
    Exit;
  end;
  if Sticky then
  begin
    Digits[Count] := 1;
    Count := Count + 1;
    E10 := E10 - 1;
  end;
  Result := DecimalToDouble(@Digits[0], Count, E10, Negative);
end;

{ RTL-092: CR LF on every kind }
procedure _WriteLn(var T: Text);
var
  R: PTextRec;
begin
  R := TextRec(T);
  PutUnit(R^, 13);
  PutUnit(R^, 10);
end;

{ end of a Write/Writeln statement: every file that is not a disk file is flushed (LANG-914, ABI _WriteEnd).
  Disk files (R-031) carry a name; the standard streams have none. }
procedure _WriteEnd(var T: Text);
var
  R: PTextRec;
begin
  R := TextRec(T);
  if (R^.Kind = skConsole) or (R^.Name[0] = #0) then
    FlushText(R^);
end;

{ ---------------------------------------------------------------- memory primitives (RTL-065) }
{ Move/FillChar are compiler intrinsics lowered to llvm.memmove/llvm.memset, which call memmove/memset.
  TODO(C-176): a negative Count reaches them as NativeUInt >= 2^63; it is treated as 0 ("Count <= 0 does nothing")
  until the compiler guards the count itself. }

const
  CountLimit = $7FFFFFFFFFFFFFFF;

function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer;
begin
  Result := memmove(Dest, Src, N);
end;

function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer;
var
  D, S: PByte;
  I: NativeUInt;
begin
  Result := Dest;
  if (N = 0) or (N > CountLimit) then
    Exit;
  D := Dest;
  S := Src;
  if D < S then
  begin
    I := 0;
    while I < N do
    begin
      D[I] := S[I];
      I := I + 1;
    end;
  end
  else if D > S then
  begin
    I := N;
    while I > 0 do                     { Dest above Src: copy from the end so overlapping bytes are read first }
    begin
      I := I - 1;
      D[I] := S[I];
    end;
  end;
end;

function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer;
var
  D: PByte;
  I: NativeUInt;
begin
  Result := Dest;
  if N > CountLimit then
    Exit;
  D := Dest;
  I := 0;
  while I < N do
  begin
    D[I] := C and $FF;
    I := I + 1;
  end;
end;

{ bytes compared as unsigned values; the sign of the first difference }
function memcmp(A, B: Pointer; N: NativeUInt): Integer;
var
  X, Y: PByte;
  I: NativeUInt;
begin
  Result := 0;
  X := A;
  Y := B;
  I := 0;
  while I < N do
  begin
    if X[I] <> Y[I] then
    begin
      Result := Integer(X[I]) - Integer(Y[I]);
      Exit;
    end;
    I := I + 1;
  end;
end;

function strlen(S: PAnsiChar): NativeUInt;
begin
  Result := 0;
  while S[Result] <> #0 do
    Result := Result + 1;
end;

function wcslen(S: PWideChar): NativeUInt;
begin
  Result := 0;
  while S[Result] <> #0 do
    Result := Result + 1;
end;

end.
