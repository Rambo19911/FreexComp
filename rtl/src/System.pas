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

const
  kernel32 = 'kernel32.dll';
  STD_INPUT_HANDLE = $FFFFFFF6;            { (DWORD)-10 }
  STD_OUTPUT_HANDLE = $FFFFFFF5;           { (DWORD)-11 }
  STD_ERROR_HANDLE = $FFFFFFF4;            { (DWORD)-12 }
  CP_UTF8 = 65001;
  IMAGE_SUBSYSTEM_WINDOWS_CUI = 3;
  PeHeaderOffsetField = $3C;           { IMAGE_DOS_HEADER.e_lfanew }
  PeSubsystemOffset = 92;              { signature 4 + file header 20 + optional header offset 68 }

type
  { RTL-127: the private view of a Text variable; R-006 completes the field list }
  TTextRec = record
    Handle: THandle;
  end;
  PTextRec = ^TTextRec;

  { rtl-abi.md section 3.1, ABI-020: header in front of the first element (RTL-029) }
  TStrRec = record
    RefCount: Integer;
    Length: Integer;
    CodePage: Word;
    ElemSize: Word;
    Reserved: Integer;
  end;
  PStrRec = ^TStrRec;

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
function WideCharToMultiByte(CodePage, dwFlags: Cardinal; lpWideCharStr: PWideChar; cchWideChar: Integer;
  lpMultiByteStr: PAnsiChar; cbMultiByte: Integer; lpDefaultChar: PAnsiChar; lpUsedDefaultChar: Pointer): Integer;
  stdcall; external kernel32 name 'WideCharToMultiByte';
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

{ typed pointers of different types meet through Pointer (LANG-267) }
// TODO(C-057): replace by typecasts
function AsPointer(P: Pointer): Pointer;
begin
  Result := P;
end;

function TextRec(var T: Text): PTextRec;
begin
  Result := AsPointer(@T);
end;

{ ---------------------------------------------------------------- start-up (ABI-105) }

{ the image's own PE optional header: Subsystem = 3 is a console program }
function ImageIsConsole: Boolean;
var
  Base: PByte;
  Field: PPointer;
  LfaNew: PInteger;
  Subsystem: PWord;
begin
  // TODO(C-057): Pointer(HInstance) once typecasts exist; read the handle's bits as a pointer meanwhile
  Field := AsPointer(@HInstance);
  Base := Field^;
  LfaNew := AsPointer(Base + PeHeaderOffsetField);
  Subsystem := AsPointer(Base + LfaNew^ + PeSubsystemOffset);
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
  // TODO(MXCSR): ABI-105 sets MXCSR = $1F80; no compiler intrinsic exists yet. It is the Win64 default at process start.
  TextRec(Input)^.Handle := GetStdHandle(STD_INPUT_HANDLE);
  TextRec(Output)^.Handle := GetStdHandle(STD_OUTPUT_HANDLE);
  TextRec(ErrOutput)^.Handle := GetStdHandle(STD_ERROR_HANDLE);
  ExitCode := 0;
end;

{ ABI-100: Init routines in table order, counting the completed ones }
procedure _InitUnits;
var
  I: NativeInt;
begin
  for I := 0 to Entries - 1 do
  begin
    // TODO(C-161): call UnitTable[I].Init through a procedural type. Until then no unit with an initialization part
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
    ShutdownStep := 4;                 { flush Output and ErrOutput: output is unbuffered until R-006 }
  ExitProcess(ExitCode);
end;

procedure _Halt(Code: Integer);
begin
  ExitCode := Code;
  _Halt0;
end;

{ ABI-101 without ErrorProc and without the error address (R-008) }
procedure _RunError(Code: Integer);
begin
  Writeln(ErrOutput, 'Runtime error ', Code, ' at $0000000000000000');
  _Halt(Code);
end;

{ ABI-132: the compiler calls this after every text-I/O statement in $I+ state }
procedure _IOCheck;
begin
  // TODO(R-032): read and reset the per-thread InOutRes; no helper records an I/O error before R-031
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

{ ---------------------------------------------------------------- text output }
{ Provisional, unbuffered byte-stream output (UTF-8 through WriteFile) so that programs can print before R-006
  delivers the buffered Text implementation with console detection. }

procedure WriteBytes(var T: Text; Buffer: Pointer; Count: Integer);
var
  Written: Cardinal;
begin
  if Count > 0 then
    WriteFile(TextRec(T)^.Handle, Buffer, Count, Written, nil);
end;

procedure WritePadding(var T: Text; Width, Len: Integer);
var
  Space: array[0..0] of Byte;          { an array: a scalar written before "@Space" draws a false hint H2002 }
begin
  Space[0] := 32;
  while Width > Len do
  begin
    WriteBytes(T, @Space[0], 1);
    Width := Width - 1;
  end;
end;

procedure WriteUtf16(var T: Text; P: PWideChar; Len, Width: Integer);
var
  Buffer: PAnsiChar;
  Size: Integer;
begin
  WritePadding(T, Width, Len);
  if Len <= 0 then
    Exit;
  Buffer := _GetMem(3 * Len);
  Size := WideCharToMultiByte(CP_UTF8, 0, P, Len, Buffer, 3 * Len, nil, nil);
  WriteBytes(T, Buffer, Size);
  _FreeMem(Buffer);
end;

procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
var
  Header: PStrRec;
  Elements: PByte;
  Len: Integer;
begin
  Len := 0;
  if S <> nil then
  begin
    Elements := S;
    Header := AsPointer(Elements - SizeOf(TStrRec));
    Len := Header^.Length;
  end;
  WriteUtf16(T, S, Len, Width);
end;

procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
begin
  WriteUtf16(T, @C, 1, Width);
end;

procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
begin
  WritePadding(T, Width, 1);
  WriteBytes(T, @C, 1);
end;

procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
var
  Digits: array[0..23] of Byte;
  Pos: Integer;
  Digit: Int64;
  Negative: Boolean;
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
  WritePadding(T, Width, 24 - Pos);
  WriteBytes(T, @Digits[Pos], 24 - Pos);
end;

procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
var
  Digits: array[0..23] of Byte;
  Pos: Integer;
begin
  Pos := 24;
  repeat
    Pos := Pos - 1;
    Digits[Pos] := 48 + V mod 10;
    V := V div 10;
  until V = 0;
  WritePadding(T, Width, 24 - Pos);
  WriteBytes(T, @Digits[Pos], 24 - Pos);
end;

procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
begin
  if V then
    Write(T, 'TRUE':Width)
  else
    Write(T, 'FALSE':Width);
end;

procedure _WriteLn(var T: Text);
var
  LineEnd: array[0..1] of Byte;
begin
  LineEnd[0] := 13;
  LineEnd[1] := 10;
  WriteBytes(T, @LineEnd[0], 2);
end;

procedure _WriteEnd(var T: Text);
begin
  { unbuffered: nothing to flush (R-006) }
end;

{ ---------------------------------------------------------------- memory primitives (RTL-065) }
{ Move/FillChar are compiler intrinsics lowered to llvm.memmove/llvm.memset, which call memmove/memset. A count
  of NativeUInt >= 2^63 can only come from a negative Count ("Count <= 0 does nothing") and is treated as 0. }

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
      Result := X[I];
      Result := Result - Y[I];
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
