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

{ text output (rtl-abi.md section 9.9) }
procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
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
