{ Mini RTL for the M2.1/M2.2 golden tests (docs/spec/compiler/toolchain.md CMP-630, 631). Test-only, never shipped;
  replaced by rtl/src/System.pas (task C-149). Names and signatures follow docs/interfaces/rtl-abi.md section 9.

  System itself uses no FillChar/Move or aggregate copies: they become llvm.mem* calls, which call back into this unit.
  Memory is accessed through typed pointers with pointer arithmetic (POINTERMATH is on for the whole implementation,
  C-056); the heap is the process heap (HeapAlloc blocks are 16-byte aligned on x64). memcpy/memmove/memset/memcmp
  are byte loops (the compiler gives them the no-builtins attribute, CMP-610, so the loops do not become calls to themselves).
  Text I/O is unbuffered: the OS handle is stored in the first 8 bytes of the Text variable. Reals are formatted exactly
  (LANG-930...933) from their binary value with base-10^9 big-integer arithmetic in global buffers (single-threaded). }
unit System;

interface

var
  Input: Text;
  Output: Text;
  ErrOutput: Text;
  ExitCode: Integer;
  InOutRes: Integer;                         { ABI-132; one global here (the mini RTL starts no threads), per thread in the RTL }

procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
procedure _InitUnits;
procedure _Halt(Code: Integer); noreturn;
procedure _Halt0; noreturn;
procedure _RunError(Code: Integer); noreturn;
procedure _RangeError; noreturn;
procedure _OverflowError; noreturn;
procedure _DivZeroError; noreturn;
procedure _Assert(Msg, FileName: Pointer; Line: Integer); noreturn;
procedure _IOCheck;

{ text files (ABI-132, C-182): errors go to InOutRes, never raised here; the compiler calls _IOCheck after them under $I+ }
function IOResult: Integer;
procedure AssignFile(var F: Text; const Name: string);
procedure Reset(var F: Text);

function _GetMem(Size: NativeInt): Pointer;
procedure _FreeMem(P: Pointer);
function _ReallocMem(P: Pointer; Size: NativeInt): Pointer;
function _AllocZero(Size: NativeInt): Pointer;
function _LiveAllocCount: NativeInt;

procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
procedure _WriteLn(var T: Text);
procedure _WriteEnd(var T: Text);
function _ReadInt(var T: Text): Int64;
function _ReadUInt(var T: Text): UInt64;
function _ReadFloat(var T: Text): Double;
function _ReadWChar(var T: Text): WideChar;
procedure _ReadUStr(var T: Text; var S: Pointer);
procedure _ReadLn(var T: Text);
procedure _ReadEnd(var T: Text);
function _Eof(var T: Text): Boolean;
function _Eoln(var T: Text): Boolean;

procedure _UStrClr(var S: Pointer);
procedure _UStrAddRef(S: Pointer);
procedure _UStrAsg(var Dest: Pointer; Src: Pointer);
procedure _UStrFromWChar(var Dest: Pointer; C: WideChar);
procedure _UStrCat(var Dest: Pointer; A, B: Pointer);
procedure _UStrCatN(var Dest: Pointer; Parts: Pointer; Count: Integer);
function _UStrCmp(A, B: Pointer): Integer;
function _UStrEq(A, B: Pointer): Boolean;
procedure _UStrUnique(var S: Pointer);
procedure _UStrSetLen(var S: Pointer; NewLen: Integer);
procedure _UStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
procedure _UStrDelete(var S: Pointer; Index, Count: Integer);
procedure _UStrInsert(Src: Pointer; var Dest: Pointer; Index: Integer);
function _UStrPos(SubStr, S: Pointer; Offset: Integer): Integer;
procedure _UStrFromPWCharLen(var Dest: Pointer; P: PWideChar; Len: Integer);
procedure _UStrFromPWChar(var Dest: Pointer; P: PWideChar);

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
procedure _WriteAStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteSStr(var T: Text; S: Pointer; Width: Integer);
procedure _WritePWChar(var T: Text; P: PWideChar; Width: Integer);
procedure _StrUStr(var Dest: Pointer; S: Pointer; Width: Integer);

procedure _DynArrayClr(var A: Pointer; TI: Pointer);
procedure _DynArrayAddRef(A: Pointer);
procedure _DynArrayAsg(var Dest: Pointer; Src: Pointer; TI: Pointer);
procedure _DynArraySetLen(var A: Pointer; TI: Pointer; DimCount: NativeInt; Dims: Pointer);
procedure _DynArrayCopy(var Dest: Pointer; Src: Pointer; TI: Pointer; Index, Count: NativeInt);
procedure _Finalize(P: Pointer; TI: Pointer);
procedure _AddRef(P: Pointer; TI: Pointer);
procedure _CopyRecord(Dest, Src: Pointer; TI: Pointer);

procedure _StrInt(var Dest: Pointer; V: Int64; Width: Integer);
procedure _StrUInt(var Dest: Pointer; V: UInt64; Width: Integer);
procedure _StrFloat(var Dest: Pointer; V: Double; Width, Decimals: Integer);
function _ValInt(S: Pointer; var Code: Integer): Int64;
function _ValUInt(S: Pointer; var Code: Integer): UInt64;
function _ValFloat(S: Pointer; var Code: Integer): Double;

procedure _SetUnion(Dest, A, B: Pointer);
procedure _SetInter(Dest, A, B: Pointer);
procedure _SetDiff(Dest, A, B: Pointer);
procedure _SetSymDiff(Dest, A, B: Pointer);
function _SetEq(A, B: Pointer): Boolean;
function _SetLe(A, B: Pointer): Boolean;
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);

function _Round(X: Double): Int64;
function _Trunc(X: Double): Int64;
function _Int(X: Double): Double;
function _Frac(X: Double): Double;

function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memcpy';
function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memmove';
function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer; public name 'memset';
function memcmp(A, B: Pointer; N: NativeUInt): Integer; public name 'memcmp';
{ loop-idiom recognition turns a zero-terminated scan into these at -O2 (CMP-610, C-056) }
function strlen(S: PAnsiChar): NativeUInt; public name 'strlen';
function wcslen(S: PChar): NativeUInt; public name 'wcslen';

implementation

{$POINTERMATH ON}

type
  PByte = ^Byte;
  PInteger = ^Integer;
  PNativeInt = ^NativeInt;
  PPointer = ^Pointer;
  PUInt64 = ^UInt64;
  PWord = ^Word;

const
  kernel32 = 'kernel32.dll';
  CP_UTF8 = 65001;
  STD_INPUT_HANDLE = -10;
  STD_OUTPUT_HANDLE = -11;
  STD_ERROR_HANDLE = -12;
  HEAP_ZERO_MEMORY = 8;
  ScratchSize = 64;
  MaxUStrLength = $7FFFFFFE;                 { ABI-026: 2^31 - 2 elements }
  { exact float formatting (LANG-930...933): a Double is M * 2^E with M < 2^53 and -1074 <= E <= 971, so its exact decimal
    expansion has at most 767 digits; it is computed in base-10^9 limbs }
  LimbBase = 1000000000;
  MaxLimbs = 100;
  MaxDigits = 800;
  FloatTextSize = 400;
  MantissaMask = $FFFFFFFFFFFFF;
  ImplicitBit = $10000000000000;

var
  _fltused: Integer = $9875; public name '_fltused';
  Heap: NativeInt;
  LiveBlocks: NativeInt;                     { _LiveAllocCount (CMP-912) }
  Scratch: PByte;
  Units: PPointer;
  UnitEntries: NativeInt;
  Limbs: array[0..MaxLimbs - 1] of UInt64;   { least significant first, each < LimbBase }
  LimbCount: Integer;
  Digits: array[0..MaxDigits - 1] of Byte;   { exact decimal digits, most significant first, no leading zero }
  DigitCount: Integer;
  DigitScale: Integer;                       { |value| = Digits * 10^(-DigitScale) }
  Rounded: array[0..MaxDigits] of Byte;
  FloatText: array[0..FloatTextSize - 1] of Byte;
  FloatLen: Integer;

function GetStdHandle(Kind: Integer): NativeInt; external kernel32;
function GetProcessHeap: NativeInt; external kernel32;
function HeapAlloc(Heap: NativeInt; Flags: Cardinal; Size: NativeUInt): Pointer; external kernel32;
function HeapReAlloc(Heap: NativeInt; Flags: Cardinal; P: Pointer; Size: NativeUInt): Pointer; external kernel32;
function HeapFree(Heap: NativeInt; Flags: Cardinal; P: Pointer): Integer; external kernel32;
function WriteFile(H: NativeInt; Buffer: Pointer; Count: Integer; var Written: Integer; Overlapped: NativeInt): Integer; external kernel32;
function ReadFile(H: NativeInt; Buffer: Pointer; Count: Integer; var Done: Integer; Overlapped: NativeInt): Integer; external kernel32;
function WideCharToMultiByte(CodePage, Flags: Cardinal; Src: Pointer; SrcLen: Integer; Dest: Pointer; DestSize: Integer;
  DefaultChar, UsedDefault: NativeInt): Integer; external kernel32;
procedure ExitProcess(Code: Integer); external kernel32;
function CreateFileW(Name: Pointer; Access, Share: Cardinal; Security: NativeInt; Disposition, Flags: Cardinal;
  Template: NativeInt): NativeInt; external kernel32;
function GetLastError: Cardinal; external kernel32;


{ the OS handle in the first 8 bytes of a Text variable }
function HandleOf(var T: Text): NativeInt;
var
  P: PNativeInt;
begin
  P := @T;
  Result := P^;
end;

procedure SetHandle(var T: Text; H: NativeInt);
var
  P: PNativeInt;
begin
  P := @T;
  P^ := H;
end;

procedure WriteBytes(var T: Text; Buffer: Pointer; Count: Integer);
var
  Written: Integer;
begin
  if Count > 0 then
    WriteFile(HandleOf(T), Buffer, Count, Written, 0);
end;

{ LANG-915: Width - Len spaces before the text }
procedure WritePadding(var T: Text; Width, Len: Integer);
var
  N, Chunk, I: Integer;
begin
  N := Width - Len;
  while N > 0 do
  begin
    Chunk := N;
    if Chunk > ScratchSize then
      Chunk := ScratchSize;
    for I := 0 to Chunk - 1 do
      Scratch[I] := 32;
    WriteBytes(T, Scratch, Chunk);
    N := N - Chunk;
  end;
end;

procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
begin
  Units := UnitTable;
  UnitEntries := UnitCount;
  Heap := GetProcessHeap;
  Scratch := HeapAlloc(Heap, 0, ScratchSize);
  SetHandle(Input, GetStdHandle(STD_INPUT_HANDLE));
  SetHandle(Output, GetStdHandle(STD_OUTPUT_HANDLE));
  SetHandle(ErrOutput, GetStdHandle(STD_ERROR_HANDLE));
  ExitCode := 0;
end;

{ ABI-100: each entry is (Init, Final). Calling them needs procedural types (task C-161); the unit table holds only
  System, which has neither, so a non-null entry is reported as runtime error 204. }
procedure _InitUnits;
var
  I: NativeInt;
begin
  for I := 0 to 2 * UnitEntries - 1 do
    if Units[I] <> nil then
      _RunError(204);
end;

procedure _Halt0;
begin
  ExitProcess(ExitCode);
end;

procedure _Halt(Code: Integer);
begin
  ExitCode := Code;
  _Halt0;
end;

{ ABI-101 without ErrorProc (SysUtils is M2.4); the error address is not known yet and printed as zero }
procedure _RunError(Code: Integer);
begin
  Writeln(ErrOutput, 'Runtime error ', Code, ' at $0000000000000000');
  _Halt(Code);
end;

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

{ ABI-103 without AssertErrorProc (SysUtils is M2.4): '<Msg> (<FileName>, line <Line>)' on ErrOutput, then error 227 }
procedure _Assert(Msg, FileName: Pointer; Line: Integer);
begin
  if Msg = nil then
    Write(ErrOutput, 'Assertion failed')
  else
    _WriteUStr(ErrOutput, Msg, 0);
  Write(ErrOutput, ' (');
  _WriteUStr(ErrOutput, FileName, 0);
  Writeln(ErrOutput, ', line ', Line, ')');
  _RunError(227);
end;

{ ABI-132: nothing while InOutRes = 0; otherwise reset it and raise its old value as a runtime error }
procedure _IOCheck;
var
  Code: Integer;
begin
  if InOutRes <> 0 then
  begin
    Code := InOutRes;
    InOutRes := 0;
    _RunError(Code);
  end;
end;

function IOResult: Integer;
begin
  Result := InOutRes;
  InOutRes := 0;
end;

{ ------------------------------------------------------------------ text files (C-182; minimal: AssignFile + Reset for reading)
  The file name is kept as a #0-terminated UTF-16 copy on the process heap, its address at offset 24 of the Text variable
  (between the input fields at 8..23 and the input buffer from 32). The copy is not counted by _LiveAllocCount and is not
  freed (no CloseFile yet, C-166). A failed CreateFileW stores GetLastError in InOutRes (2 = file not found). }

const
  GENERIC_READ = $80000000;
  FILE_SHARE_READ = 1;
  OPEN_EXISTING = 3;
  FILE_ATTRIBUTE_NORMAL = $80;
  INVALID_HANDLE_VALUE = -1;

procedure AssignFile(var F: Text; const Name: string);
var
  State: PByte;
  NameCopy: PWord;
  Source: PWord;
  Len, I: Integer;
begin
  State := @F;
  for I := 0 to 31 do
    State[I] := 0;
  SetHandle(F, INVALID_HANDLE_VALUE);
  Len := Length(Name);
  NameCopy := HeapAlloc(Heap, 0, (Len + 1) * 2);
  if NameCopy = nil then
    _RunError(203);
  Source := PWord(PChar(Name));
  for I := 0 to Len - 1 do
    NameCopy[I] := Source[I];
  NameCopy[Len] := 0;
  PPointer(State + 24)^ := NameCopy;
end;

procedure Reset(var F: Text);
var
  State: PByte;
  H: NativeInt;
begin
  State := @F;
  H := CreateFileW(PPointer(State + 24)^, GENERIC_READ, FILE_SHARE_READ, 0, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, 0);
  if H = INVALID_HANDLE_VALUE then
  begin
    InOutRes := Integer(GetLastError);
    Exit;
  end;
  SetHandle(F, H);
  PInteger(State + 8)^ := 0;
  PInteger(State + 12)^ := 0;
  PInteger(State + 16)^ := 0;
  PInteger(State + 20)^ := 0;
end;

{ rtl-abi.md section 9.2: Size <= 0 gives nil; an exhausted heap is runtime error 203 }
function _GetMem(Size: NativeInt): Pointer;
begin
  Result := nil;
  if Size > 0 then
  begin
    Result := HeapAlloc(Heap, 0, Size);
    if Result = nil then
      _RunError(203);
    LiveBlocks := LiveBlocks + 1;
  end;
end;

procedure _FreeMem(P: Pointer);
begin
  if P <> nil then
  begin
    HeapFree(Heap, 0, P);
    LiveBlocks := LiveBlocks - 1;
  end;
end;

function _LiveAllocCount: NativeInt;
begin
  Result := LiveBlocks;
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
    Result := HeapReAlloc(Heap, 0, P, Size);
    if Result = nil then
      _RunError(203);
  end;
end;

function _AllocZero(Size: NativeInt): Pointer;
begin
  Result := nil;
  if Size > 0 then
  begin
    Result := HeapAlloc(Heap, HEAP_ZERO_MEMORY, Size);
    if Result = nil then
      _RunError(203);
    LiveBlocks := LiveBlocks + 1;
  end;
end;

{ S = payload of a UnicodeString (Length: Int32 at S - 12, rtl-abi.md section 3) or nil }
procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
var
  Len, Size: Integer;
  LenField: PInteger;
  Buffer: Pointer;
begin
  Len := 0;
  if S <> nil then
  begin
    LenField := S - 12;
    Len := LenField^;
  end;
  WritePadding(T, Width, Len);
  if Len > 0 then
  begin
    Buffer := _GetMem(3 * Len);
    Size := WideCharToMultiByte(CP_UTF8, 0, S, Len, Buffer, 3 * Len, 0, 0);
    WriteBytes(T, Buffer, Size);
    _FreeMem(Buffer);
  end;
end;

procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
var
  Size: Integer;
  Unit16: PChar;
begin
  WritePadding(T, Width, 1);
  Unit16 := PChar(Scratch);
  Unit16^ := C;
  Size := WideCharToMultiByte(CP_UTF8, 0, Scratch, 1, Scratch + 8, 8, 0, 0);
  WriteBytes(T, Scratch + 8, Size);
end;

procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
var
  Byte8: PAnsiChar;
begin
  WritePadding(T, Width, 1);
  Byte8 := PAnsiChar(Scratch);
  Byte8^ := C;
  WriteBytes(T, Scratch, 1);
end;

{ digits are built backwards from the end of Scratch; Pos = first used byte }
procedure PutDigit(var Pos: Integer; Digit: Integer);
begin
  Pos := Pos - 1;
  Scratch[Pos] := 48 + Digit;
end;

{ the decimal text of V in Scratch[Result..ScratchSize - 1] }
function FormatInt(V: Int64): Integer;
var
  Digit: Int64;
  Negative: Boolean;
begin
  Negative := V < 0;
  Result := ScratchSize;
  repeat
    Digit := V mod 10;
    if Digit < 0 then
      Digit := -Digit;
    PutDigit(Result, Digit);
    V := V div 10;
  until V = 0;
  if Negative then
  begin
    Result := Result - 1;
    Scratch[Result] := 45;
  end;
end;

function FormatUInt(V: UInt64): Integer;
begin
  Result := ScratchSize;
  repeat
    PutDigit(Result, V mod 10);
    V := V div 10;
  until V = 0;
end;

procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
var
  Pos: Integer;
begin
  Pos := FormatInt(V);
  WritePadding(T, Width, ScratchSize - Pos);
  WriteBytes(T, Scratch + Pos, ScratchSize - Pos);
end;

procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
var
  Pos: Integer;
begin
  Pos := FormatUInt(V);
  WritePadding(T, Width, ScratchSize - Pos);
  WriteBytes(T, Scratch + Pos, ScratchSize - Pos);
end;

procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
begin
  if V then
    Write(T, 'TRUE':Width)
  else
    Write(T, 'FALSE':Width);
end;

procedure _WriteLn(var T: Text);
begin
  Scratch[0] := 13;
  Scratch[1] := 10;
  WriteBytes(T, Scratch, 2);
end;

{ unbuffered: nothing to flush }
procedure _WriteEnd(var T: Text);
begin
end;

{ ------------------------------------------------------------------ UnicodeString (rtl-abi.md section 3, 9.3)
  S points at the first character of a block: RefCount (Int32) at S - 16, Length at S - 12, CodePage (Word) at S - 8,
  ElemSize at S - 6, reserved Int32 at S - 4. nil is ''. RefCount -1 marks a static literal: never counted, never freed.
  Single-threaded test RTL: reference counts are plain increments (the real RTL uses interlocked ones, ABI-022). }

function UStrLength(S: Pointer): Integer;
var
  LenField: PInteger;
begin
  Result := 0;
  if S <> nil then
  begin
    LenField := S - 12;
    Result := LenField^;
  end;
end;

{ a heap block for Len > 0 characters, RefCount 1, terminator set, characters not initialised (ABI-020); error 203 past ABI-026 }
function NewUStr(Len: Integer): Pointer;
var
  Block: PByte;
  Header: PInteger;
  Words: PWord;
  Chars: PChar;
begin
  if Len > MaxUStrLength then
    _RunError(203);
  Block := _GetMem(16 + (NativeInt(Len) + 1) * 2);
  Header := PInteger(Block);
  Header[0] := 1;
  Header[1] := Len;
  Words := PWord(Block + 8);
  Words[0] := 1200;
  Words[1] := 2;
  Header[3] := 0;
  Result := Block + 16;
  Chars := Result;
  Chars[Len] := #0;
end;

procedure ReleaseUStr(S: Pointer);
var
  Count: PInteger;
begin
  if S <> nil then
  begin
    Count := S - 16;
    if Count^ > 0 then
    begin
      Count^ := Count^ - 1;
      if Count^ = 0 then
        _FreeMem(S - 16);
    end;
  end;
end;

{ Dest := P, which already carries its reference; the old value is released afterwards (P may be built from it) }
procedure SetUStr(var Dest: Pointer; P: Pointer);
var
  Old: Pointer;
begin
  Old := Dest;
  Dest := P;
  ReleaseUStr(Old);
end;

procedure _UStrAddRef(S: Pointer);
var
  Count: PInteger;
begin
  if S <> nil then
  begin
    Count := S - 16;
    if Count^ > 0 then
      Count^ := Count^ + 1;
  end;
end;

procedure _UStrClr(var S: Pointer);
begin
  SetUStr(S, nil);
end;

procedure _UStrAsg(var Dest: Pointer; Src: Pointer);
begin
  _UStrAddRef(Src);
  SetUStr(Dest, Src);
end;

procedure _UStrFromWChar(var Dest: Pointer; C: WideChar);
var
  P: Pointer;
  Chars: PChar;
begin
  P := NewUStr(1);
  Chars := P;
  Chars[0] := C;
  SetUStr(Dest, P);
end;

procedure CopyChars(Dest, Src: PChar; Count: Integer);
var
  I: Integer;
begin
  for I := 0 to Count - 1 do
    Dest[I] := Src[I];
end;

{ Dest := A + B; a part that is '' gives the other one shared (ABI-021); Dest may be A or B }
procedure _UStrCat(var Dest: Pointer; A, B: Pointer);
var
  LA, LB: Integer;
  P: Pointer;
  Chars: PChar;
begin
  LA := UStrLength(A);
  LB := UStrLength(B);
  if LA = 0 then
    _UStrAsg(Dest, B)
  else if LB = 0 then
    _UStrAsg(Dest, A)
  else
  begin
    if LA > MaxUStrLength - LB then
      _RunError(203);
    P := NewUStr(LA + LB);
    Chars := P;
    CopyChars(Chars, A, LA);
    CopyChars(Chars + LA, B, LB);
    SetUStr(Dest, P);
  end;
end;

{ Dest := Parts[0] + ... + Parts[Count - 1] (Parts: PPointer; the declared Pointer avoids a PPointer type in the interface) }
procedure _UStrCatN(var Dest: Pointer; Parts: Pointer; Count: Integer);
var
  List: PPointer;
  Total, Len, At, I, NonEmpty: Integer;
  Only, P: Pointer;
  Chars: PChar;
begin
  List := Parts;
  Total := 0;
  NonEmpty := 0;
  Only := nil;
  for I := 0 to Count - 1 do
  begin
    Len := UStrLength(List[I]);
    if Len > 0 then
    begin
      if Total > MaxUStrLength - Len then
        _RunError(203);
      Total := Total + Len;
      NonEmpty := NonEmpty + 1;
      Only := List[I];
    end;
  end;
  if NonEmpty <= 1 then
    _UStrAsg(Dest, Only)
  else
  begin
    P := NewUStr(Total);
    Chars := P;
    At := 0;
    for I := 0 to Count - 1 do
    begin
      Len := UStrLength(List[I]);
      CopyChars(Chars + At, List[I], Len);
      At := At + Len;
    end;
    SetUStr(Dest, P);
  end;
end;

{ ordinal comparison of UTF-16 code units; on a common prefix the shorter string is smaller }
function _UStrCmp(A, B: Pointer): Integer;
var
  LA, LB, N, I: Integer;
  X, Y: PChar;
begin
  LA := UStrLength(A);
  LB := UStrLength(B);
  N := LA;
  if LB < N then
    N := LB;
  X := A;
  Y := B;
  for I := 0 to N - 1 do
    if X[I] <> Y[I] then
      Exit(Ord(X[I]) - Ord(Y[I]));
  Result := LA - LB;
end;

function _UStrEq(A, B: Pointer): Boolean;
begin
  if A = B then
    Result := True
  else if UStrLength(A) <> UStrLength(B) then
    Result := False
  else
    Result := _UStrCmp(A, B) = 0;
end;

{ afterwards S is nil or a heap block with RefCount 1: a shared or static block is copied first (copy-on-write, LANG-241) }
procedure _UStrUnique(var S: Pointer);
var
  Count: PInteger;
  Len: Integer;
  P: Pointer;
begin
  if S <> nil then
  begin
    Count := S - 16;
    if Count^ <> 1 then
    begin
      Len := UStrLength(S);
      P := NewUStr(Len);
      CopyChars(P, S, Len);
      SetUStr(S, P);
    end;
  end;
end;

{ ------------------------------------------------------------------ reals (intrinsics.md LANG-930...933, rtl-abi.md 9.9/9.10) }

function DoubleBits(X: Double): UInt64;
var
  P: PUInt64;
begin
  P := @X;
  Result := P^;
end;

function BitsDouble(Bits: UInt64): Double;
var
  D: Double;
  P: PUInt64;
begin
  P := @D;
  P^ := Bits;
  Result := D;
end;

{ Limbs := Limbs * M (M <= 1220703125, so Limb * M + Carry stays below 2^63) }
procedure MulLimbs(M: UInt64);
var
  I: Integer;
  T, Carry: UInt64;
begin
  Carry := 0;
  for I := 0 to LimbCount - 1 do
  begin
    T := Limbs[I] * M + Carry;
    Limbs[I] := T mod LimbBase;
    Carry := T div LimbBase;
  end;
  while Carry <> 0 do
  begin
    Limbs[LimbCount] := Carry mod LimbBase;
    Carry := Carry div LimbBase;
    LimbCount := LimbCount + 1;
  end;
end;

{ the exact decimal expansion of M * 2^E (M > 0) into Digits/DigitCount/DigitScale: for E < 0, M * 2^E = M * 5^-E / 10^-E }
procedure ExactDigits(M: UInt64; E: Integer);
var
  I, J, K: Integer;
  L: UInt64;
  Group: array[0..8] of Byte;
begin
  LimbCount := 0;
  while M <> 0 do
  begin
    Limbs[LimbCount] := M mod LimbBase;
    M := M div LimbBase;
    LimbCount := LimbCount + 1;
  end;
  DigitScale := 0;
  if E >= 0 then
  begin
    while E >= 29 do
    begin
      MulLimbs(536870912);
      E := E - 29;
    end;
    if E > 0 then
      MulLimbs(UInt64(1) shl E);
  end
  else
  begin
    DigitScale := -E;
    K := -E;
    while K >= 13 do
    begin
      MulLimbs(1220703125);
      K := K - 13;
    end;
    L := 1;
    for I := 1 to K do
      L := L * 5;
    if L > 1 then
      MulLimbs(L);
  end;
  DigitCount := 0;
  for I := LimbCount - 1 downto 0 do
  begin
    L := Limbs[I];
    for J := 8 downto 0 do
    begin
      Group[J] := L mod 10;
      L := L div 10;
    end;
    for J := 0 to 8 do
      if (DigitCount > 0) or (Group[J] <> 0) then
      begin
        Digits[DigitCount] := Group[J];
        DigitCount := DigitCount + 1;
      end;
  end;
end;

{ Rounded[0..Keep-1] := the first Keep (>= 0) digits rounded half away from zero (exact: the next digit decides);
  True when the rounding carried out of the first digit (Rounded is then all zero) }
function RoundDigits(Keep: Integer): Boolean;
var
  I: Integer;
begin
  for I := 0 to Keep - 1 do
    if I < DigitCount then
      Rounded[I] := Digits[I]
    else
      Rounded[I] := 0;
  Result := (Keep < DigitCount) and (Digits[Keep] >= 5);
  I := Keep - 1;
  while Result and (I >= 0) do
  begin
    if Rounded[I] = 9 then
      Rounded[I] := 0
    else
    begin
      Rounded[I] := Rounded[I] + 1;
      Result := False;
    end;
    I := I - 1;
  end;
end;

procedure PutChar(C: Integer);
begin
  FloatText[FloatLen] := C;
  FloatLen := FloatLen + 1;
end;

{ splits a Double into sign, mantissa and binary exponent; False for zero }
function Decompose(Bits: UInt64; var M: UInt64; var E: Integer): Boolean;
var
  BExp: Integer;
begin
  BExp := (Bits shr 52) and $7FF;
  M := Bits and MantissaMask;
  if BExp = 0 then
    E := -1074
  else
  begin
    M := M or ImplicitBit;
    E := BExp - 1075;
  end;
  Result := M <> 0;
end;

{ LANG-930: sign, d.(D digits), E, exponent sign, 4 exponent digits; D = min(max(Width - 9, 1), 16) }
procedure FormatScientific(Bits: UInt64; Width: Integer);
var
  D, Exp, I: Integer;
  M: UInt64;
  E: Integer;
begin
  D := Width - 9;
  if D < 1 then
    D := 1;
  if D > 16 then
    D := 16;
  Exp := 0;
  if Decompose(Bits, M, E) then
  begin
    ExactDigits(M, E);
    Exp := DigitCount - 1 - DigitScale;
    if RoundDigits(D + 1) then
    begin
      Rounded[0] := 1;
      Exp := Exp + 1;
    end;
  end
  else
    for I := 0 to D do
      Rounded[I] := 0;
  if (Bits shr 63 <> 0) and (M <> 0) then
    PutChar(45)
  else
    PutChar(32);
  PutChar(48 + Rounded[0]);
  PutChar(46);
  for I := 1 to D do
    PutChar(48 + Rounded[I]);
  PutChar(69);
  if Exp < 0 then
  begin
    PutChar(45);
    Exp := -Exp;
  end
  else
    PutChar(43);
  PutChar(48 + Exp div 1000);
  PutChar(48 + Exp div 100 mod 10);
  PutChar(48 + Exp div 10 mod 10);
  PutChar(48 + Exp mod 10);
end;

{ LANG-931: |v| rounded half away from zero to Decimals places; '-' only when a rounded digit is not zero }
procedure FormatFixed(Bits: UInt64; Decimals: Integer);
var
  Keep, Len, I: Integer;
  M: UInt64;
  E: Integer;
begin
  if Decimals > 40 then
    Decimals := 40;
  Len := 0;
  if Decompose(Bits, M, E) then
  begin
    ExactDigits(M, E);
    Keep := DigitCount - DigitScale + Decimals;
    if Keep >= 0 then
    begin
      Len := Keep;
      if RoundDigits(Keep) then
      begin
        Rounded[Keep] := 0;                  { 10^Keep: a one followed by Keep zeros (Keep may be 0) }
        Rounded[0] := 1;
        Len := Keep + 1;
      end;
    end;
  end;
  { Rounded[0] is never 0 when Len > 0, so Len = 0 means the rounded value is zero }
  if (Bits shr 63 <> 0) and (Len > 0) then
    PutChar(45);
  if Len > Decimals then
    for I := 0 to Len - Decimals - 1 do
      PutChar(48 + Rounded[I])
  else
    PutChar(48);
  if Decimals > 0 then
  begin
    PutChar(46);
    for I := Len - Decimals to Len - 1 do
      if I >= 0 then
        PutChar(48 + Rounded[I])
      else
        PutChar(48);
  end;
end;

{ CMP-410: Decimals < 0 = scientific in Width (the compiler passes 23 when no width is given), else fixed; LANG-932 specials;
  the text in FloatText[0..FloatLen - 1], unpadded }
procedure FormatFloat(V: Double; Width, Decimals: Integer);
var
  Bits: UInt64;
begin
  Bits := DoubleBits(V);
  FloatLen := 0;
  if (Bits shr 52) and $7FF = $7FF then
  begin
    if Bits and MantissaMask <> 0 then
    begin
      PutChar(78);
      PutChar(65);
      PutChar(78);
    end
    else
    begin
      if Bits shr 63 <> 0 then
        PutChar(45);
      PutChar(73);
      PutChar(78);
      PutChar(70);
    end;
  end
  else if Decimals < 0 then
    FormatScientific(Bits, Width)
  else
    FormatFixed(Bits, Decimals);
end;

procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
begin
  FormatFloat(V, Width, Decimals);
  WritePadding(T, Width, FloatLen);
  WriteBytes(T, @FloatText, FloatLen);
end;

{ rtl-abi.md 9.10: toward zero; |X| >= 2^63 (except -2^63), INF and NaN are runtime error 207 }
function _Trunc(X: Double): Int64;
var
  Bits, M: UInt64;
  BExp: Integer;
begin
  Bits := DoubleBits(X);
  BExp := (Bits shr 52) and $7FF;
  if BExp >= 1086 then
  begin
    if Bits = $C3E0000000000000 then
      Exit(Low(Int64));
    _RunError(207);
  end;
  if BExp < 1023 then
    Exit(0);
  M := (Bits and MantissaMask) or ImplicitBit;
  if BExp >= 1075 then
    M := M shl (BExp - 1075)
  else
    M := M shr (1075 - BExp);
  if Bits shr 63 <> 0 then
    Result := -Int64(M)
  else
    Result := Int64(M);
end;

{ to nearest, ties to even; the same range rule as _Trunc }
function _Round(X: Double): Int64;
var
  Bits, M, Q, Rest, Half: UInt64;
  BExp, Shift: Integer;
begin
  Bits := DoubleBits(X);
  BExp := (Bits shr 52) and $7FF;
  if BExp >= 1086 then
  begin
    if Bits = $C3E0000000000000 then
      Exit(Low(Int64));
    _RunError(207);
  end;
  if BExp < 1022 then
    Exit(0);
  M := (Bits and MantissaMask) or ImplicitBit;
  if BExp >= 1075 then
    M := M shl (BExp - 1075)
  else
  begin
    Shift := 1075 - BExp;
    Q := M shr Shift;
    Rest := M and ((UInt64(1) shl Shift) - 1);
    Half := UInt64(1) shl (Shift - 1);
    if (Rest > Half) or ((Rest = Half) and (Q and 1 <> 0)) then
      Q := Q + 1;
    M := Q;
  end;
  if Bits shr 63 <> 0 then
    Result := -Int64(M)
  else
    Result := Int64(M);
end;

{ the integral part toward zero, by clearing the fraction bits (keeps the sign; INF and NaN unchanged) }
function _Int(X: Double): Double;
var
  Bits: UInt64;
  BExp: Integer;
begin
  Bits := DoubleBits(X);
  BExp := (Bits shr 52) and $7FF;
  if BExp >= 1075 then
    Exit(X);
  if BExp < 1023 then
    Bits := Bits and (UInt64(1) shl 63)
  else
    Bits := Bits and not ((UInt64(1) shl (1075 - BExp)) - 1);
  Result := BitsDouble(Bits);
end;

function _Frac(X: Double): Double;
begin
  Result := X - _Int(X);
end;

{ ------------------------------------------------------------------ string intrinsics (intrinsics.md section 1, rtl-abi.md 9.3; C-062)
  Every edit builds a new block from the old characters before the old value is released, so Dest may be (or share) the
  source operand. }

{ ABI-020: a unique block of NewLen characters keeping min(old, new) of the old ones; NewLen <= 0 gives '' }
procedure _UStrSetLen(var S: Pointer; NewLen: Integer);
var
  Keep: Integer;
  P: Pointer;
begin
  if NewLen <= 0 then
  begin
    _UStrClr(S);
    Exit;
  end;
  Keep := UStrLength(S);
  if Keep > NewLen then
    Keep := NewLen;
  P := NewUStr(NewLen);
  CopyChars(P, S, Keep);
  SetUStr(S, P);
end;

{ Count characters of S from the 0-based Start as a new block (Count > 0) }
function SubUStr(S: Pointer; Start, Count: Integer): Pointer;
var
  Chars: PChar;
begin
  Chars := S;
  Result := NewUStr(Count);
  CopyChars(Result, Chars + Start, Count);
end;

{ 1-based; Index < 1 counts from 1, Count is clamped to the end, nothing left gives '' }
procedure _UStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
var
  Len: Integer;
begin
  Len := UStrLength(S);
  if Index < 1 then
    Index := 1;
  if (Index > Len) or (Count <= 0) then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  if Count > Len - Index + 1 then
    Count := Len - Index + 1;
  if Count = Len then
    _UStrAsg(Dest, S)
  else
    SetUStr(Dest, SubUStr(S, Index - 1, Count));
end;

{ no-op unless 1 <= Index <= Length and Count > 0; Count is clamped to the end }
procedure _UStrDelete(var S: Pointer; Index, Count: Integer);
var
  Len: Integer;
  P: Pointer;
  Chars, Old: PChar;
begin
  Len := UStrLength(S);
  if (Index < 1) or (Index > Len) or (Count <= 0) then
    Exit;
  if Count > Len - Index + 1 then
    Count := Len - Index + 1;
  if Count = Len then
  begin
    _UStrClr(S);
    Exit;
  end;
  P := NewUStr(Len - Count);
  Chars := P;
  Old := S;
  CopyChars(Chars, Old, Index - 1);
  CopyChars(Chars + (Index - 1), Old + (Index - 1 + Count), Len - Index + 1 - Count);
  SetUStr(S, P);
end;

{ Index < 1 inserts at the front, past Length + 1 appends; an empty Src changes nothing }
procedure _UStrInsert(Src: Pointer; var Dest: Pointer; Index: Integer);
var
  LS, LD: Integer;
  P: Pointer;
  Chars, Old, Part: PChar;
begin
  LS := UStrLength(Src);
  if LS = 0 then
    Exit;
  LD := UStrLength(Dest);
  if Index < 1 then
    Index := 1;
  if Index > LD + 1 then
    Index := LD + 1;
  if LS > MaxUStrLength - LD then
    _RunError(203);
  P := NewUStr(LD + LS);
  Chars := P;
  Old := Dest;
  Part := Src;
  CopyChars(Chars, Old, Index - 1);
  CopyChars(Chars + (Index - 1), Part, LS);
  CopyChars(Chars + (Index - 1 + LS), Old + (Index - 1), LD - Index + 1);
  SetUStr(Dest, P);
end;

{ the first 1-based index >= Offset where SubStr occurs in S, else 0; an empty SubStr gives 0; Offset < 1 counts from 1 }
function _UStrPos(SubStr, S: Pointer; Offset: Integer): Integer;
var
  LS, L, I, J: Integer;
  X, Y: PChar;
begin
  Result := 0;
  LS := UStrLength(SubStr);
  L := UStrLength(S);
  if Offset < 1 then
    Offset := 1;
  if LS = 0 then
    Exit;
  X := SubStr;
  Y := S;
  I := Offset - 1;
  while I <= L - LS do
  begin
    J := 0;
    while (J < LS) and (Y[I + J] = X[J]) do
      J := J + 1;
    if J = LS then
      Exit(I + 1);
    I := I + 1;
  end;
end;

{ Dest := Width - Count spaces (when positive), then the Count ASCII bytes at Text (LANG-915 padding) }
procedure SetAsciiUStr(var Dest: Pointer; Text: PByte; Count, Width: Integer);
var
  Pad, I: Integer;
  P: Pointer;
  Chars: PChar;
begin
  Pad := Width - Count;
  if Pad < 0 then
    Pad := 0;
  if Pad + Count = 0 then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  P := NewUStr(Pad + Count);
  Chars := P;
  for I := 0 to Pad - 1 do
    Chars[I] := ' ';
  for I := 0 to Count - 1 do
    Chars[Pad + I] := Chr(Text[I]);
  SetUStr(Dest, P);
end;

{ LANG-951: exactly the text Write produces }
procedure _StrInt(var Dest: Pointer; V: Int64; Width: Integer);
var
  Pos: Integer;
begin
  Pos := FormatInt(V);
  SetAsciiUStr(Dest, Scratch + Pos, ScratchSize - Pos, Width);
end;

procedure _StrUInt(var Dest: Pointer; V: UInt64; Width: Integer);
var
  Pos: Integer;
begin
  Pos := FormatUInt(V);
  SetAsciiUStr(Dest, Scratch + Pos, ScratchSize - Pos, Width);
end;

{ LANG-950 integer syntax: leading spaces, an optional sign, decimal digits or $ / 0x and hex digits, nothing after. The
  magnitude (Overflow when it exceeds UInt64); Code = 1-based index of the first offending character, Length + 1 when the
  text ends before a digit, else 0 }
function ValMagnitude(S: Pointer; var Code: Integer; var Negative, Overflow: Boolean): UInt64;
var
  Len, I, Count: Integer;
  Base, Digit: UInt64;
  Chars: PChar;
  C: Char;
begin
  Result := 0;
  Negative := False;
  Overflow := False;
  Len := UStrLength(S);
  Chars := S;
  I := 0;
  while (I < Len) and (Chars[I] = ' ') do
    I := I + 1;
  if (I < Len) and ((Chars[I] = '+') or (Chars[I] = '-')) then
  begin
    Negative := Chars[I] = '-';
    I := I + 1;
  end;
  Base := 10;
  if (I < Len) and (Chars[I] = '$') then
  begin
    Base := 16;
    I := I + 1;
  end
  else if (I + 1 < Len) and (Chars[I] = '0') and ((Chars[I + 1] = 'x') or (Chars[I + 1] = 'X')) then
  begin
    Base := 16;
    I := I + 2;
  end;
  Count := 0;
  while I < Len do
  begin
    C := UpCase(Chars[I]);
    if (C >= '0') and (C <= '9') then
      Digit := Ord(C) - 48
    else if (Base = 16) and (C >= 'A') and (C <= 'F') then
      Digit := Ord(C) - 55
    else
    begin
      Code := I + 1;
      Exit(0);
    end;
    if Result > (High(UInt64) - Digit) div Base then
      Overflow := True
    else
      Result := Result * Base + Digit;
    Count := Count + 1;
    I := I + 1;
  end;
  if Count = 0 then
  begin
    Code := Len + 1;
    Exit(0);
  end;
  Code := 0;
end;

{ LANG-950: a value outside Int64 fails with Code = Length (the index of the last digit) and 0 }
function _ValInt(S: Pointer; var Code: Integer): Int64;
var
  M: UInt64;
  Negative, Overflow: Boolean;
begin
  Result := 0;
  M := ValMagnitude(S, Code, Negative, Overflow);
  if Code <> 0 then
    Exit;
  if Negative then
  begin
    if Overflow or (M > UInt64($8000000000000000)) then
      Code := UStrLength(S)
    else
      Result := -Int64(M);
  end
  else if Overflow or (M > UInt64($7FFFFFFFFFFFFFFF)) then
    Code := UStrLength(S)
  else
    Result := Int64(M);
end;

function _ValUInt(S: Pointer; var Code: Integer): UInt64;
var
  Negative, Overflow: Boolean;
begin
  Result := ValMagnitude(S, Code, Negative, Overflow);
  if (Code = 0) and (Overflow or (Negative and (Result <> 0))) then
  begin
    Code := UStrLength(S);
    Result := 0;
  end;
end;

{ case-insensitive Word (upper case) at Chars[I..] }
function HasWord(Chars: PChar; I, Len: Integer; const W: string): Boolean;
var
  K: Integer;
begin
  Result := I + Length(W) <= Len;
  K := 0;
  while Result and (K < Length(W)) do
  begin
    Result := UpCase(Chars[I + K]) = W[K + 1];
    K := K + 1;
  end;
end;

{ LANG-943 syntax after leading spaces: [+-] digits [. digits] [(e|E) [+-] digits], INF, NAN. Test RTL: correctly rounded only
  when at most 19 significant digits give a mantissa <= 2^53 and the decimal exponent is within 22 (one exact rounding);
  otherwise approximate (the real RTL rounds correctly, LANG-943). }
function _ValFloat(S: Pointer; var Code: Integer): Double;
var
  Len, I, Exp, Scale, ExpValue, Digits: Integer;
  Chars: PChar;
  Negative, ExpNegative: Boolean;
  M: UInt64;
  Power: Double;
begin
  Result := 0.0;
  Len := UStrLength(S);
  Chars := S;
  I := 0;
  while (I < Len) and (Chars[I] = ' ') do
    I := I + 1;
  Negative := False;
  if (I < Len) and ((Chars[I] = '+') or (Chars[I] = '-')) then
  begin
    Negative := Chars[I] = '-';
    I := I + 1;
  end;
  if HasWord(Chars, I, Len, 'INF') and (I + 3 = Len) then
  begin
    Code := 0;
    if Negative then
      Exit(BitsDouble($FFF0000000000000));
    Exit(BitsDouble($7FF0000000000000));
  end;
  if HasWord(Chars, I, Len, 'NAN') and (I + 3 = Len) then
  begin
    Code := 0;
    Exit(BitsDouble($7FF8000000000000));
  end;
  M := 0;
  Scale := 0;
  Digits := 0;
  { integer digits }
  while (I < Len) and (Chars[I] >= '0') and (Chars[I] <= '9') do
  begin
    if M < 1000000000000000000 then
      M := M * 10 + UInt64(Ord(Chars[I]) - 48)
    else
      Scale := Scale + 1;
    Digits := Digits + 1;
    I := I + 1;
  end;
  if Digits = 0 then
  begin
    if I < Len then Code := I + 1 else Code := Len + 1;
    Exit(0.0);
  end;
  if (I < Len) and (Chars[I] = '.') then
  begin
    I := I + 1;
    Digits := 0;
    while (I < Len) and (Chars[I] >= '0') and (Chars[I] <= '9') do
    begin
      if M < 1000000000000000000 then
      begin
        M := M * 10 + UInt64(Ord(Chars[I]) - 48);
        Scale := Scale - 1;
      end;
      Digits := Digits + 1;
      I := I + 1;
    end;
    if Digits = 0 then
    begin
      if I < Len then Code := I + 1 else Code := Len + 1;
      Exit(0.0);
    end;
  end;
  if (I < Len) and ((Chars[I] = 'e') or (Chars[I] = 'E')) then
  begin
    I := I + 1;
    ExpNegative := False;
    if (I < Len) and ((Chars[I] = '+') or (Chars[I] = '-')) then
    begin
      ExpNegative := Chars[I] = '-';
      I := I + 1;
    end;
    ExpValue := 0;
    Digits := 0;
    while (I < Len) and (Chars[I] >= '0') and (Chars[I] <= '9') do
    begin
      if ExpValue < 100000 then
        ExpValue := ExpValue * 10 + (Ord(Chars[I]) - 48);
      Digits := Digits + 1;
      I := I + 1;
    end;
    if Digits = 0 then
    begin
      if I < Len then Code := I + 1 else Code := Len + 1;
      Exit(0.0);
    end;
    if ExpNegative then
      ExpValue := -ExpValue;
    Scale := Scale + ExpValue;
  end;
  if I < Len then
  begin
    Code := I + 1;
    Exit(0.0);
  end;
  Code := 0;
  Result := M;
  Exp := Scale;
  if Exp < 0 then
    Exp := -Exp;
  Power := 1.0;
  if M <> 0 then
  begin
    if Exp <= 22 then
    begin
      while Exp > 0 do
      begin
        Power := Power * 10.0;
        Exp := Exp - 1;
      end;
      if Scale < 0 then
        Result := Result / Power
      else
        Result := Result * Power;
    end
    else
      while Exp > 0 do
      begin
        if Scale < 0 then
          Result := Result / 10.0
        else
          Result := Result * 10.0;
        Exp := Exp - 1;
      end;
  end;
  if Negative then
    Result := -Result;
end;

{ LANG-951: exactly the text Write produces }
procedure _StrFloat(var Dest: Pointer; V: Double; Width, Decimals: Integer);
begin
  FormatFloat(V, Width, Decimals);
  SetAsciiUStr(Dest, @FloatText, FloatLen, Width);
end;

{ Big sets (rtl-abi.md section 9.8): 32 bytes, bit K in byte K shr 3; combined byte by byte, so Dest may be A or B
  (ABI-033). Op: 0 union, 1 intersection, 2 difference, 3 symmetric difference. }
procedure SetCombine(Dest, A, B: PByte; Op: Integer);
var
  I: Integer;
begin
  for I := 0 to 31 do
    case Op of
      0: Dest[I] := A[I] or B[I];
      1: Dest[I] := A[I] and B[I];
      2: Dest[I] := A[I] and not B[I];
    else
      Dest[I] := A[I] xor B[I];
    end;
end;

procedure _SetUnion(Dest, A, B: Pointer);
begin
  SetCombine(Dest, A, B, 0);
end;

procedure _SetInter(Dest, A, B: Pointer);
begin
  SetCombine(Dest, A, B, 1);
end;

procedure _SetDiff(Dest, A, B: Pointer);
begin
  SetCombine(Dest, A, B, 2);
end;

procedure _SetSymDiff(Dest, A, B: Pointer);
begin
  SetCombine(Dest, A, B, 3);
end;

function _SetEq(A, B: Pointer): Boolean;
var
  I: Integer;
  X, Y: PByte;
begin
  X := A;
  Y := B;
  Result := True;
  for I := 0 to 31 do
    if X[I] <> Y[I] then
      Result := False;
end;

{ A <= B: no bit of A outside B }
function _SetLe(A, B: Pointer): Boolean;
var
  I: Integer;
  X, Y: PByte;
begin
  X := A;
  Y := B;
  Result := True;
  for I := 0 to 31 do
    if X[I] and not Y[I] <> 0 then
      Result := False;
end;

{ Dest += [Lo..Hi]; Lo > Hi adds nothing, otherwise both bounds must lie in 0..255 (runtime error 201) }
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);
var
  K: Integer;
  Bits: PByte;
begin
  if Lo > Hi then
    Exit;
  if (Lo < 0) or (Hi > 255) then
    _RunError(201);
  Bits := Dest;
  for K := Lo to Hi do
    Bits[K shr 3] := Bits[K shr 3] or (1 shl (K and 7));
end;

function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer;
var
  D, S: PByte;
  I: NativeUInt;
begin
  D := Dest;
  S := Src;
  I := 0;
  while I < N do
  begin
    D[I] := S[I];
    I := I + 1;
  end;
  Result := Dest;
end;

{ overlapping blocks: copies backwards when Dest lies above Src }
function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer;
var
  D, S: PByte;
  I: NativeUInt;
begin
  D := Dest;
  S := Src;
  if D > S then
  begin
    I := N;
    while I > 0 do
    begin
      I := I - 1;
      D[I] := S[I];
    end;
  end
  else
  begin
    I := 0;
    while I < N do
    begin
      D[I] := S[I];
      I := I + 1;
    end;
  end;
  Result := Dest;
end;

function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer;
var
  D: PByte;
  I: NativeUInt;
begin
  D := Dest;
  I := 0;
  while I < N do
  begin
    D[I] := C;
    I := I + 1;
  end;
  Result := Dest;
end;

function memcmp(A, B: Pointer; N: NativeUInt): Integer;
var
  X, Y: PByte;
  I: NativeUInt;
begin
  X := A;
  Y := B;
  Result := 0;
  I := 0;
  while (Result = 0) and (I < N) do
  begin
    Result := X[I] - Y[I];
    I := I + 1;
  end;
end;

function strlen(S: PAnsiChar): NativeUInt;
begin
  Result := 0;
  while S[Result] <> #0 do
    Result := Result + 1;
end;

function wcslen(S: PChar): NativeUInt;
begin
  Result := 0;
  while S[Result] <> #0 do
    Result := Result + 1;
end;

{ ------------------------------------------------------------------ AnsiString family, ShortString, PChar (rtl-abi.md 3, 9.3, 9.4; C-063)
  AnsiString blocks have the UnicodeString header with ElemSize 1 and the code page at S - 8 (65001 or $FFFF, ABI-027); the
  reference counting is shared with UnicodeString (ReleaseUStr, _UStrAddRef). ShortString: byte 0 = length, then UTF-8 bytes.
  UTF-8 is decoded by hand: an invalid byte becomes U+FFFD and is skipped alone; a lone surrogate is encoded as '?'. }

procedure _UStrFromPWCharLen(var Dest: Pointer; P: PWideChar; Len: Integer);
var
  R: Pointer;
begin
  if Len <= 0 then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  R := NewUStr(Len);
  CopyChars(R, P, Len);
  SetUStr(Dest, R);
end;

procedure _UStrFromPWChar(var Dest: Pointer; P: PWideChar);
var
  Len: Integer;
begin
  Len := 0;
  if P <> nil then
    while P[Len] <> #0 do
      Len := Len + 1;
  _UStrFromPWCharLen(Dest, P, Len);
end;

{ a heap block for Len > 0 bytes, RefCount 1, terminator set }
function NewAStr(Len: Integer; CodePage: Word): Pointer;
var
  Block: PByte;
  Header: PInteger;
  Words: PWord;
begin
  if Len > MaxUStrLength then
    _RunError(203);
  Block := _GetMem(16 + NativeInt(Len) + 1);
  Header := PInteger(Block);
  Header[0] := 1;
  Header[1] := Len;
  Words := PWord(Block + 8);
  if CodePage = 0 then
    CodePage := CP_UTF8;
  Words[0] := CodePage;
  Words[1] := 1;
  Header[3] := 0;
  Result := Block + 16;
  Block[16 + Len] := 0;
end;

function AStrCodePage(S: Pointer): Word;
var
  Words: PWord;
begin
  Result := CP_UTF8;
  if S <> nil then
  begin
    Words := S - 8;
    Result := Words^;
  end;
end;

procedure CopyBytes(Dest, Src: PByte; Count: Integer);
var
  I: Integer;
begin
  for I := 0 to Count - 1 do
    Dest[I] := Src[I];
end;

procedure _AStrClr(var S: Pointer);
begin
  _UStrClr(S);
end;

procedure _AStrAddRef(S: Pointer);
begin
  _UStrAddRef(S);
end;

procedure _AStrAsg(var Dest: Pointer; Src: Pointer);
begin
  _UStrAsg(Dest, Src);
end;

procedure _AStrFromPCharLen(var Dest: Pointer; P: PAnsiChar; Len: Integer; CodePage: Word);
var
  R: Pointer;
begin
  if Len <= 0 then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  R := NewAStr(Len, CodePage);
  CopyBytes(R, PByte(P), Len);
  SetUStr(Dest, R);
end;

procedure _AStrCat(var Dest: Pointer; A, B: Pointer);
var
  LA, LB: Integer;
  P: Pointer;
  Bytes: PByte;
begin
  LA := UStrLength(A);
  LB := UStrLength(B);
  if LA = 0 then
    _UStrAsg(Dest, B)
  else if LB = 0 then
    _UStrAsg(Dest, A)
  else
  begin
    if LA > MaxUStrLength - LB then
      _RunError(203);
    P := NewAStr(LA + LB, AStrCodePage(A));
    Bytes := P;
    CopyBytes(Bytes, A, LA);
    CopyBytes(Bytes + LA, B, LB);
    SetUStr(Dest, P);
  end;
end;

{ bytewise (unsigned); on a common prefix the shorter string is smaller }
function _AStrCmp(A, B: Pointer): Integer;
var
  LA, LB, N, I: Integer;
  X, Y: PByte;
begin
  LA := UStrLength(A);
  LB := UStrLength(B);
  N := LA;
  if LB < N then
    N := LB;
  X := A;
  Y := B;
  for I := 0 to N - 1 do
    if X[I] <> Y[I] then
      Exit(Integer(X[I]) - Integer(Y[I]));
  Result := LA - LB;
end;

procedure _AStrUnique(var S: Pointer);
var
  Count: PInteger;
  Len: Integer;
  P: Pointer;
begin
  if S <> nil then
  begin
    Count := S - 16;
    if Count^ <> 1 then
    begin
      Len := UStrLength(S);
      P := NewAStr(Len, AStrCodePage(S));
      CopyBytes(P, S, Len);
      SetUStr(S, P);
    end;
  end;
end;

{ keeps min(old, new) bytes; always a new unique block (new bytes are zero) }
procedure _AStrSetLen(var S: Pointer; NewLen: Integer; CodePage: Word);
var
  Keep, I: Integer;
  P: Pointer;
  Bytes: PByte;
begin
  if NewLen <= 0 then
  begin
    _UStrClr(S);
    Exit;
  end;
  Keep := UStrLength(S);
  if Keep > NewLen then
    Keep := NewLen;
  P := NewAStr(NewLen, CodePage);
  Bytes := P;
  CopyBytes(Bytes, S, Keep);
  for I := Keep to NewLen - 1 do
    Bytes[I] := 0;
  SetUStr(S, P);
end;

{ 1-based bytes; clamps like _UStrCopy }
procedure _AStrCopy(var Dest: Pointer; S: Pointer; Index, Count: Integer);
var
  Len: Integer;
  P: Pointer;
  Bytes: PByte;
begin
  Len := UStrLength(S);
  if Index < 1 then
    Index := 1;
  if (Index > Len) or (Count <= 0) then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  if Count > Len - Index + 1 then
    Count := Len - Index + 1;
  if Count = Len then
    _UStrAsg(Dest, S)
  else
  begin
    P := NewAStr(Count, AStrCodePage(S));
    Bytes := S;
    CopyBytes(P, Bytes + Index - 1, Count);
    SetUStr(Dest, P);
  end;
end;

{ UTF-8 → UTF-16: the number of code units for Len bytes at Src; written to Dest unless it is nil }
function DecodeUtf8(Src: PByte; Len: Integer; Dest: PWord): Integer;
var
  I, Need, K: Integer;
  B: Integer;
  CodePoint: Integer;
  Ok: Boolean;
begin
  Result := 0;
  I := 0;
  while I < Len do
  begin
    B := Src[I];
    Need := 0;
    CodePoint := B;
    if B >= $F0 then
    begin
      Need := 3;
      CodePoint := B and 7;
    end
    else if B >= $E0 then
    begin
      Need := 2;
      CodePoint := B and 15;
    end
    else if B >= $C2 then
    begin
      Need := 1;
      CodePoint := B and 31;
    end;
    Ok := (B < $80) or ((Need > 0) and (B <= $F4) and (I + Need < Len + 1));
    if Ok and (Need > 0) then
      for K := 1 to Need do
        if Ok and (I + K < Len) and ((Src[I + K] and $C0) = $80) then
          CodePoint := (CodePoint shl 6) or (Src[I + K] and 63)
        else
          Ok := False;
    if Ok and (((Need = 2) and ((CodePoint < $800) or ((CodePoint >= $D800) and (CodePoint <= $DFFF))))
      or ((Need = 3) and ((CodePoint < $10000) or (CodePoint > $10FFFF)))) then
      Ok := False;
    if not Ok then
    begin
      CodePoint := $FFFD;
      Need := 0;
    end;
    I := I + Need + 1;
    if CodePoint >= $10000 then
    begin
      if Dest <> nil then
      begin
        Dest[Result] := $D800 + ((CodePoint - $10000) shr 10);
        Dest[Result + 1] := $DC00 + ((CodePoint - $10000) and $3FF);
      end;
      Result := Result + 2;
    end
    else
    begin
      if Dest <> nil then
        Dest[Result] := CodePoint;
      Result := Result + 1;
    end;
  end;
end;

{ UTF-16 → UTF-8: the number of bytes for Len code units at Src; written to Dest unless it is nil; a lone surrogate gives '?' }
function EncodeUtf8(Src: PWord; Len: Integer; Dest: PByte): Integer;
var
  I, CodePoint, Size: Integer;
begin
  Result := 0;
  I := 0;
  while I < Len do
  begin
    CodePoint := Src[I];
    I := I + 1;
    if (CodePoint >= $D800) and (CodePoint <= $DBFF) and (I < Len) and (Src[I] >= $DC00) and (Src[I] <= $DFFF) then
    begin
      CodePoint := $10000 + ((CodePoint - $D800) shl 10) + (Src[I] - $DC00);
      I := I + 1;
    end
    else if (CodePoint >= $D800) and (CodePoint <= $DFFF) then
      CodePoint := 63;
    if CodePoint < $80 then
      Size := 1
    else if CodePoint < $800 then
      Size := 2
    else if CodePoint < $10000 then
      Size := 3
    else
      Size := 4;
    if Dest <> nil then
    begin
      if Size = 1 then
        Dest[Result] := CodePoint
      else if Size = 2 then
      begin
        Dest[Result] := $C0 or (CodePoint shr 6);
        Dest[Result + 1] := $80 or (CodePoint and 63);
      end
      else if Size = 3 then
      begin
        Dest[Result] := $E0 or (CodePoint shr 12);
        Dest[Result + 1] := $80 or ((CodePoint shr 6) and 63);
        Dest[Result + 2] := $80 or (CodePoint and 63);
      end
      else
      begin
        Dest[Result] := $F0 or (CodePoint shr 18);
        Dest[Result + 1] := $80 or ((CodePoint shr 12) and 63);
        Dest[Result + 2] := $80 or ((CodePoint shr 6) and 63);
        Dest[Result + 3] := $80 or (CodePoint and 63);
      end;
    end;
    Result := Result + Size;
  end;
end;

{ Dest := the UnicodeString decoded from Len UTF-8 bytes }
procedure SetDecoded(var Dest: Pointer; Src: PByte; Len: Integer);
var
  Units: Integer;
  P: Pointer;
begin
  Units := DecodeUtf8(Src, Len, nil);
  if Units = 0 then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  P := NewUStr(Units);
  DecodeUtf8(Src, Len, P);
  SetUStr(Dest, P);
end;

procedure _UStrFromAStr(var Dest: Pointer; Src: Pointer);
begin
  SetDecoded(Dest, Src, UStrLength(Src));
end;

procedure _AStrFromUStr(var Dest: Pointer; Src: Pointer; CodePage: Word);
var
  Size: Integer;
  P: Pointer;
begin
  Size := EncodeUtf8(Src, UStrLength(Src), nil);
  if Size = 0 then
  begin
    _UStrClr(Dest);
    Exit;
  end;
  P := NewAStr(Size, CodePage);
  EncodeUtf8(Src, UStrLength(Src), P);
  SetUStr(Dest, P);
end;

procedure _UStrFromSStr(var Dest: Pointer; Src: Pointer);
var
  Bytes: PByte;
begin
  Bytes := Src;
  SetDecoded(Dest, Bytes + 1, Bytes[0]);
end;

{ UTF-8, cut at MaxLen bytes without splitting a sequence }
procedure _SStrFromUStr(Dest: Pointer; MaxLen: Integer; Src: Pointer);
var
  Size: Integer;
  Buffer, Bytes: PByte;
begin
  Bytes := Dest;
  Size := EncodeUtf8(Src, UStrLength(Src), nil);
  if Size = 0 then
  begin
    Bytes[0] := 0;
    Exit;
  end;
  Buffer := _GetMem(Size + 1);
  EncodeUtf8(Src, UStrLength(Src), Buffer);
  if Size > MaxLen then
  begin
    Size := MaxLen;
    while (Size > 0) and ((Buffer[Size] and $C0) = $80) do
      Size := Size - 1;
  end;
  CopyBytes(Bytes + 1, Buffer, Size);
  Bytes[0] := Size;
  _FreeMem(Buffer);
end;

procedure _SStrAsg(Dest: Pointer; MaxLen: Integer; Src: Pointer);
var
  Len: Integer;
  D, S: PByte;
begin
  D := Dest;
  S := Src;
  Len := S[0];
  if Len > MaxLen then
    Len := MaxLen;
  CopyBytes(D + 1, S + 1, Len);
  D[0] := Len;
end;

function _SStrCmp(A, B: Pointer): Integer;
var
  LA, LB, N, I: Integer;
  X, Y: PByte;
begin
  X := A;
  Y := B;
  LA := X[0];
  LB := Y[0];
  N := LA;
  if LB < N then
    N := LB;
  for I := 1 to N do
    if X[I] <> Y[I] then
      Exit(Integer(X[I]) - Integer(Y[I]));
  Result := LA - LB;
end;

{ Len UTF-8 bytes, padded per LANG-915 by their number of UTF-16 code units }
procedure WriteUtf8(var T: Text; Bytes: PByte; Len, Width: Integer);
begin
  WritePadding(T, Width, DecodeUtf8(Bytes, Len, nil));
  WriteBytes(T, Bytes, Len);
end;

procedure _WriteAStr(var T: Text; S: Pointer; Width: Integer);
begin
  WriteUtf8(T, S, UStrLength(S), Width);
end;

procedure _WriteSStr(var T: Text; S: Pointer; Width: Integer);
var
  Bytes: PByte;
begin
  Bytes := S;
  WriteUtf8(T, Bytes + 1, Bytes[0], Width);
end;

procedure _WritePWChar(var T: Text; P: PWideChar; Width: Integer);
var
  Len, Size: Integer;
  Buffer: Pointer;
begin
  Len := 0;
  if P <> nil then
    while P[Len] <> #0 do
      Len := Len + 1;
  WritePadding(T, Width, Len);
  if Len > 0 then
  begin
    Buffer := _GetMem(3 * Len);
    Size := EncodeUtf8(PWord(P), Len, Buffer);
    WriteBytes(T, Buffer, Size);
    _FreeMem(Buffer);
  end;
end;

{ LANG-951 for strings and Booleans (C-063): S padded to Width with leading spaces }
procedure _StrUStr(var Dest: Pointer; S: Pointer; Width: Integer);
var
  Len, Pad, I: Integer;
  P: Pointer;
  Chars: PChar;
begin
  Len := UStrLength(S);
  Pad := Width - Len;
  if Pad <= 0 then
  begin
    _UStrAsg(Dest, S);
    Exit;
  end;
  P := NewUStr(Pad + Len);
  Chars := P;
  for I := 0 to Pad - 1 do
    Chars[I] := ' ';
  CopyChars(Chars + Pad, S, Len);
  SetUStr(Dest, P);
end;

{ ------------------------------------------------------------------ dynamic arrays (rtl-abi.md 4, 9.5, 10; C-064)
  A points at element 0: RefCount (Int32) at A - 16, Length (NativeInt) at A - 8; nil is empty. TI is the tkDynArray block:
  Kind at 0, ElSize at 16, ElType at 24, ManagedElType at 32 (nil when the elements are not managed). Managed elements are
  strings (tkUString 9 / tkAString 10), dynamic arrays (tkDynArray 14), managed records (tkRecord 12) or static arrays
  (tkArray 13), handled by _AddRef/_Finalize (C-065). }

function DynLength(A: Pointer): NativeInt;
var
  Len: PNativeInt;
begin
  Result := 0;
  if A <> nil then
  begin
    Len := A - 8;
    Result := Len^;
  end;
end;

function TIField(TI: Pointer; Offset: Integer): NativeInt;
var
  P: PNativeInt;
begin
  P := TI + Offset;
  Result := P^;
end;

{ C-065: managed values by type info kind (rtl-abi.md 10): strings (9, 10) and dynamic arrays (14) are one pointer; a
  tkRecord (12) block has Size at 16, ManagedCount at 24 and ManagedFields at 32 (entries of TypeInfo, Offset); a tkArray
  (13) block has Size at 16, the total ElCount at 24 and the innermost ElType at 32 }

function ValueSize(TI: Pointer): NativeInt;
var
  Kind: PByte;
begin
  Kind := TI;
  if (Kind^ = 12) or (Kind^ = 13) then
    Result := TIField(TI, 16)
  else
    Result := 8;
end;

{ +1 / release on each of Count managed values of type ElTI stored at P }
procedure AddRefValues(P: Pointer; ElTI: Pointer; Count: NativeInt);
var
  I, Size: NativeInt;
begin
  Size := ValueSize(ElTI);
  for I := 0 to Count - 1 do
    _AddRef(P + I * Size, ElTI);
end;

procedure FinalizeValues(P: Pointer; ElTI: Pointer; Count: NativeInt);
var
  I, Size: NativeInt;
begin
  Size := ValueSize(ElTI);
  for I := 0 to Count - 1 do
    _Finalize(P + I * Size, ElTI);
end;

{ the managed fields of a record: Count entries of (TypeInfo, Offset) }
procedure RecordFields(TI: Pointer; var Fields: PNativeInt; var Count: NativeInt);
begin
  Count := TIField(TI, 24);
  Fields := PNativeInt(TIField(TI, 32));
end;

{ one managed value of type TI at P is released and its references set to nil }
procedure _Finalize(P: Pointer; TI: Pointer);
var
  Kind: PByte;
  Slot: PPointer;
  Fields: PNativeInt;
  Count, I: NativeInt;
begin
  Kind := TI;
  Slot := P;
  case Kind^ of
    9: _UStrClr(Slot^);
    10: _AStrClr(Slot^);
    14: _DynArrayClr(Slot^, TI);
    12:
      begin
        RecordFields(TI, Fields, Count);
        for I := 0 to Count - 1 do
          _Finalize(P + Fields[2 * I + 1], Pointer(Fields[2 * I]));
      end;
    13:
      begin
        Count := TIField(TI, 24);
        FinalizeValues(P, Pointer(TIField(TI, 32)), Count);
      end;
  end;
end;

{ +1 on every reference inside one value of type TI at P (after a raw copy) }
procedure _AddRef(P: Pointer; TI: Pointer);
var
  Kind: PByte;
  Slot: PPointer;
  Fields: PNativeInt;
  Count, I: NativeInt;
begin
  Kind := TI;
  Slot := P;
  case Kind^ of
    9: _UStrAddRef(Slot^);
    10: _AStrAddRef(Slot^);
    14: _DynArrayAddRef(Slot^);
    12:
      begin
        RecordFields(TI, Fields, Count);
        for I := 0 to Count - 1 do
          _AddRef(P + Fields[2 * I + 1], Pointer(Fields[2 * I]));
      end;
    13:
      begin
        Count := TIField(TI, 24);
        AddRefValues(P, Pointer(TIField(TI, 32)), Count);
      end;
  end;
end;

{ Dest := Src for a managed record or static array: Src's references are added first (Src may share blocks with Dest),
  then Dest's are released and the bytes copied; Dest = Src does nothing }
procedure _CopyRecord(Dest, Src: Pointer; TI: Pointer);
begin
  if Dest = Src then
    Exit;
  _AddRef(Src, TI);
  _Finalize(Dest, TI);
  CopyBytes(Dest, Src, ValueSize(TI));
end;

{ a zero-filled block of Len > 0 elements of ElSize bytes, RefCount 1 }
function NewDynArray(Len, ElSize: NativeInt): Pointer;
var
  Block: PByte;
  Header: PInteger;
  LenField: PNativeInt;
begin
  Block := _AllocZero(16 + Len * ElSize);
  Header := PInteger(Block);
  Header[0] := 1;
  LenField := PNativeInt(Block + 8);
  LenField^ := Len;
  Result := Block + 16;
end;

procedure ReleaseDynArray(A: Pointer; TI: Pointer);
var
  Count: PInteger;
  ElTI: Pointer;
begin
  if A = nil then
    Exit;
  Count := A - 16;
  Count^ := Count^ - 1;
  if Count^ = 0 then
  begin
    ElTI := Pointer(TIField(TI, 32));
    if ElTI <> nil then
      FinalizeValues(A, ElTI, DynLength(A));
    _FreeMem(A - 16);
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
var
  Count: PInteger;
begin
  if A <> nil then
  begin
    Count := A - 16;
    Count^ := Count^ + 1;
  end;
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

{ Dims (PNativeInt in rtl-abi.md; Pointer here as for _UStrCatN): Dims[0] = new length of A (always a new unique block; kept elements moved when A was unique, copied with +1 when shared,
  new ones zero); the remaining dimensions are applied to every element (ABI-043) }
procedure _DynArraySetLen(var A: Pointer; TI: Pointer; DimCount: NativeInt; Dims: Pointer);
var
  NewLen, OldLen, Keep, ElSize, I: NativeInt;
  ElTI, P: Pointer;
  Count: PInteger;
  Slots: PPointer;
  Lengths: PNativeInt;
begin
  Lengths := Dims;
  NewLen := Lengths[0];
  if NewLen < 0 then
    _RangeError;
  if NewLen = 0 then
    _DynArrayClr(A, TI)
  else
  begin
    ElSize := TIField(TI, 16);
    ElTI := Pointer(TIField(TI, 32));
    OldLen := DynLength(A);
    Keep := OldLen;
    if Keep > NewLen then
      Keep := NewLen;
    P := NewDynArray(NewLen, ElSize);
    if A <> nil then
    begin
      CopyBytes(P, A, Keep * ElSize);
      Count := A - 16;
      if Count^ = 1 then
      begin
        { unique: the kept elements moved; finalise the dropped ones and free the block alone }
        if ElTI <> nil then
          FinalizeValues(A + Keep * ElSize, ElTI, OldLen - Keep);
        _FreeMem(A - 16);
      end
      else
      begin
        if ElTI <> nil then
          AddRefValues(P, ElTI, Keep);
        Count^ := Count^ - 1;
      end;
    end;
    A := P;
  end;
  if (DimCount > 1) and (A <> nil) then
  begin
    Slots := A;
    for I := 0 to DynLength(A) - 1 do
      _DynArraySetLen(Slots[I], Pointer(TIField(TI, 32)), DimCount - 1, Lengths + 1);
  end;
end;

{ 0-based; Index < 0 counts from 0, Count is clamped to the end, nothing left gives nil; always a new block }
procedure _DynArrayCopy(var Dest: Pointer; Src: Pointer; TI: Pointer; Index, Count: NativeInt);
var
  Len, ElSize: NativeInt;
  ElTI, P, Old: Pointer;
begin
  Len := DynLength(Src);
  if Index < 0 then
    Index := 0;
  if (Index >= Len) or (Count <= 0) then
  begin
    _DynArrayClr(Dest, TI);
    Exit;
  end;
  if Count > Len - Index then
    Count := Len - Index;
  ElSize := TIField(TI, 16);
  ElTI := Pointer(TIField(TI, 32));
  P := NewDynArray(Count, ElSize);
  CopyBytes(P, Src + Index * ElSize, Count * ElSize);
  if ElTI <> nil then
    AddRefValues(P, ElTI, Count);
  Old := Dest;
  Dest := P;
  ReleaseDynArray(Old, TI);
end;

{ ------------------------------------------------------------------ text input (rtl-abi.md 9.9, LANG-940...946; C-066)
  Input bytes are buffered in the Text variable after the handle: Pos (Integer) at 8, Len at 12, a pending low surrogate
  (Integer, 0 = none) at 16, the end-of-file flag at 20 and InBufferSize bytes from 32. Bytes are UTF-8 (ABI-131). A
  malformed number (LANG-942/943) is runtime error 106 right away: InOutRes and the deferred _IOCheck arrive with C-166. }

const
  InBufferSize = 512;

function InField(var T: Text; Offset: Integer): PInteger;
var
  State: PByte;
begin
  State := @T;
  Result := PInteger(State + Offset);
end;

{ the next input byte without consuming it, or -1 at end of file (the buffer is refilled with ReadFile when empty) }
function PeekByte(var T: Text): Integer;
var
  State: PByte;
  Pos, Len, Ended: PInteger;
  Done: Integer;
begin
  State := @T;
  Pos := InField(T, 8);
  Len := InField(T, 12);
  Ended := InField(T, 20);
  if (Pos^ >= Len^) and (Ended^ = 0) then
  begin
    Pos^ := 0;
    Len^ := 0;
    Done := 0;
    if (ReadFile(HandleOf(T), State + 32, InBufferSize, Done, 0) = 0) or (Done <= 0) then
      Ended^ := 1
    else
      Len^ := Done;
  end;
  if Pos^ < Len^ then
    Result := State[32 + Pos^]
  else
    Result := -1;
end;

{ consumes the byte PeekByte returned (only after a result >= 0) }
procedure SkipByte(var T: Text);
var
  Pos: PInteger;
begin
  Pos := InField(T, 8);
  Pos^ := Pos^ + 1;
end;

{ LANG-942: skips spaces, tabs, CR and LF; the next byte or -1 }
function SkipBlanks(var T: Text): Integer;
begin
  Result := PeekByte(T);
  while (Result = 32) or (Result = 9) or (Result = 13) or (Result = 10) do
  begin
    SkipByte(T);
    Result := PeekByte(T);
  end;
end;

function IsLineEnd(B: Integer): Boolean;
begin
  Result := (B < 0) or (B = 13) or (B = 10);
end;

{ a number cannot start in the middle of a surrogate pair }
procedure CheckNoPending(var T: Text);
var
  Pending: PInteger;
begin
  Pending := InField(T, 16);
  if Pending^ <> 0 then
    _RunError(106);
end;

{ LANG-942: [+|-] then decimal digits, or $ and hex digits; stops before the first byte that cannot continue the number.
  No digits or a magnitude beyond UInt64 is error 106; at end of file before any non-blank byte Present is False. }
function ReadMagnitude(var T: Text; var Negative, Present: Boolean): UInt64;
var
  B, Digits: Integer;
  Digit, Base: UInt64;
begin
  Result := 0;
  Negative := False;
  Present := True;
  CheckNoPending(T);
  B := SkipBlanks(T);
  if B < 0 then
  begin
    Present := False;
    Exit;
  end;
  if (B = 43) or (B = 45) then
  begin
    Negative := B = 45;
    SkipByte(T);
    B := PeekByte(T);
  end;
  Base := 10;
  if B = 36 then
  begin
    Base := 16;
    SkipByte(T);
    B := PeekByte(T);
  end;
  Digits := 0;
  while True do
  begin
    if (B >= 48) and (B <= 57) then
      Digit := B - 48
    else if (Base = 16) and (B >= 65) and (B <= 70) then
      Digit := B - 55
    else if (Base = 16) and (B >= 97) and (B <= 102) then
      Digit := B - 87
    else
      Break;
    if Result > (High(UInt64) - Digit) div Base then
      _RunError(106);
    Result := Result * Base + Digit;
    Digits := Digits + 1;
    SkipByte(T);
    B := PeekByte(T);
  end;
  if Digits = 0 then
    _RunError(106);
end;

function _ReadInt(var T: Text): Int64;
var
  M: UInt64;
  Negative, Present: Boolean;
begin
  M := ReadMagnitude(T, Negative, Present);
  Result := 0;
  if (not Present) or (M = 0) then
    Exit;
  if Negative then
  begin
    if M - 1 > UInt64(High(Int64)) then
      _RunError(106);
    Result := -Int64(M - 1) - 1;
  end
  else
  begin
    if M > UInt64(High(Int64)) then
      _RunError(106);
    Result := Int64(M);
  end;
end;

function _ReadUInt(var T: Text): UInt64;
var
  Negative, Present: Boolean;
begin
  Result := ReadMagnitude(T, Negative, Present);
  if Negative and (Result <> 0) then
    _RunError(106);
end;

{ LANG-943: the token (digits, '.', letters for exponents and INF/NAN, a sign first or after e/E) is converted by
  _ValFloat; anything it rejects is error 106; at end of file 0.0 }
function _ReadFloat(var T: Text): Double;
var
  Bytes: array[0..255] of Byte;
  Count, B, Code: Integer;
  Previous: Integer;
  S: Pointer;
begin
  Result := 0;
  CheckNoPending(T);
  B := SkipBlanks(T);
  if B < 0 then
    Exit;
  Count := 0;
  Previous := 0;
  while (B >= 0) and (((B >= 48) and (B <= 57)) or (B = 46) or ((B >= 65) and (B <= 90)) or ((B >= 97) and (B <= 122))
    or (((B = 43) or (B = 45)) and ((Count = 0) or (Previous = 69) or (Previous = 101)))) do
  begin
    if Count = 256 then
      _RunError(106);
    Bytes[Count] := B;
    Count := Count + 1;
    Previous := B;
    SkipByte(T);
    B := PeekByte(T);
  end;
  S := nil;
  SetAsciiUStr(S, PByte(@Bytes), Count, 0);
  Code := 0;
  Result := _ValFloat(S, Code);
  _UStrClr(S);
  if Code <> 0 then
    _RunError(106);
end;

{ LANG-944: the next UTF-16 unit without skipping anything (a CR, an LF); #26 at end of file; the low surrogate of a pair
  is kept for the next read }
function _ReadWChar(var T: Text): WideChar;
var
  Pending: PInteger;
  B, Need, Count: Integer;
  Bytes: array[0..3] of Byte;
  Units: array[0..3] of Word;
begin
  Pending := InField(T, 16);
  if Pending^ <> 0 then
  begin
    Result := WideChar(Pending^);
    Pending^ := 0;
    Exit;
  end;
  B := PeekByte(T);
  if B < 0 then
  begin
    Result := #26;
    Exit;
  end;
  SkipByte(T);
  Bytes[0] := B;
  Count := 1;
  Need := 0;
  if B >= $F0 then
    Need := 3
  else if B >= $E0 then
    Need := 2
  else if B >= $C0 then
    Need := 1;
  while Need > 0 do
  begin
    B := PeekByte(T);
    if (B < $80) or (B >= $C0) then
      Break;
    SkipByte(T);
    Bytes[Count] := B;
    Count := Count + 1;
    Need := Need - 1;
  end;
  if DecodeUtf8(PByte(@Bytes), Count, PWord(@Units)) >= 2 then
    Pending^ := Units[1];
  Result := WideChar(Units[0]);
end;

{ LANG-945: everything up to (not including) the next line terminator or the end of file }
procedure _ReadUStr(var T: Text; var S: Pointer);
var
  Buffer: PByte;
  Size, Count, B, Units, First: Integer;
  Pending: PInteger;
  P: Pointer;
  Chars: PChar;
begin
  Size := 64;
  Count := 0;
  Buffer := _GetMem(Size);
  B := PeekByte(T);
  while not IsLineEnd(B) do
  begin
    if Count = Size then
    begin
      Size := 2 * Size;
      Buffer := _ReallocMem(Buffer, Size);
    end;
    Buffer[Count] := B;
    Count := Count + 1;
    SkipByte(T);
    B := PeekByte(T);
  end;
  Pending := InField(T, 16);
  First := 0;
  if Pending^ <> 0 then
    First := 1;
  Units := First + DecodeUtf8(Buffer, Count, nil);
  if Units = 0 then
    _UStrClr(S)
  else
  begin
    P := NewUStr(Units);
    Chars := P;
    if First = 1 then
      Chars[0] := WideChar(Pending^);
    DecodeUtf8(Buffer, Count, PWord(Chars + First));
    SetUStr(S, P);
  end;
  Pending^ := 0;
  _FreeMem(Buffer);
end;

{ skips to and past the next line terminator (CR LF, LF or CR, LANG-941) }
procedure _ReadLn(var T: Text);
var
  B: Integer;
  Pending: PInteger;
begin
  Pending := InField(T, 16);
  Pending^ := 0;
  B := PeekByte(T);
  while not IsLineEnd(B) do
  begin
    SkipByte(T);
    B := PeekByte(T);
  end;
  if B = 13 then
  begin
    SkipByte(T);
    if PeekByte(T) = 10 then
      SkipByte(T);
  end
  else if B = 10 then
    SkipByte(T);
end;

procedure _ReadEnd(var T: Text);
begin
end;

function _Eof(var T: Text): Boolean;
var
  Pending: PInteger;
begin
  Pending := InField(T, 16);
  Result := (Pending^ = 0) and (PeekByte(T) < 0);
end;

function _Eoln(var T: Text): Boolean;
var
  Pending: PInteger;
begin
  Pending := InField(T, 16);
  Result := (Pending^ = 0) and IsLineEnd(PeekByte(T));
end;

end.
