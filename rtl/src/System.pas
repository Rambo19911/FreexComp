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
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
procedure _WriteLn(var T: Text);
procedure _WriteEnd(var T: Text);

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
  // TODO(C-168): AtomicIncrement once the atomic intrinsics exist (RTL-064 asks for interlocked counters)
  BlocksAllocated := BlocksAllocated + 1;
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
  // TODO(C-168): AtomicIncrement
  BlocksFreed := BlocksFreed + 1;
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
  2^1140, so 40 limbs suffice. }

const
  BigLimbs = 40;

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

procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
var
  X: TFloatText;
  Bits: UInt64;
  I: Integer;
  P: PTextRec;
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
  P := TextRec(T);
  PutSpaces(P^, Width - X.Len);
  for I := 0 to X.Len - 1 do
    PutUnit(P^, X.C[I]);
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
