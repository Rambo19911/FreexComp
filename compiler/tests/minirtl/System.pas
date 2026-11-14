{ Mini RTL for the M2.1 golden tests (docs/spec/compiler/toolchain.md CMP-630, 631). Test-only, never shipped;
  replaced by rtl/src/System.pas (task C-149). Names and signatures follow docs/interfaces/rtl-abi.md section 9.

  M2.1 has no pointers, arrays or typecasts yet, so every memory access goes through ntdll's RtlMoveMemory and
  RtlFillMemory, declared several times with by-reference parameters (all of them are `ptr` in the IR).
  Text I/O is unbuffered: the OS handle is stored in the first 8 bytes of the Text variable. }
unit System;

interface

var
  Output: Text;
  ErrOutput: Text;
  ExitCode: Integer;

procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
procedure _InitUnits;
procedure _Halt(Code: Integer); noreturn;
procedure _Halt0; noreturn;
procedure _RunError(Code: Integer); noreturn;
procedure _RangeError; noreturn;
procedure _OverflowError; noreturn;
procedure _DivZeroError; noreturn;
procedure _IOCheck;

procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
procedure _WriteLn(var T: Text);
procedure _WriteEnd(var T: Text);

procedure _SetUnion(Dest, A, B: Pointer);
procedure _SetInter(Dest, A, B: Pointer);
procedure _SetDiff(Dest, A, B: Pointer);
procedure _SetSymDiff(Dest, A, B: Pointer);
function _SetEq(A, B: Pointer): Boolean;
function _SetLe(A, B: Pointer): Boolean;
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);

function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memcpy';
function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer; public name 'memmove';
function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer; public name 'memset';
function memcmp(A, B: Pointer; N: NativeUInt): Integer; public name 'memcmp';

implementation

const
  kernel32 = 'kernel32.dll';
  ntdll = 'ntdll.dll';
  CP_UTF8 = 65001;
  STD_OUTPUT_HANDLE = -11;
  STD_ERROR_HANDLE = -12;
  ScratchSize = 64;

var
  _fltused: Integer = $9875; public name '_fltused';
  Heap: NativeInt;
  Scratch: Pointer;
  Units: Pointer;
  UnitEntries: NativeInt;

function GetStdHandle(Kind: Integer): NativeInt; external kernel32;
function GetProcessHeap: NativeInt; external kernel32;
function HeapAlloc(Heap: NativeInt; Flags: Cardinal; Size: NativeUInt): Pointer; external kernel32;
function HeapFree(Heap: NativeInt; Flags: Cardinal; P: Pointer): Integer; external kernel32;
function WriteFile(H: NativeInt; Buffer: Pointer; Count: Integer; var Written: Integer; Overlapped: NativeInt): Integer; external kernel32;
function WideCharToMultiByte(CodePage, Flags: Cardinal; Src: Pointer; SrcLen: Integer; Dest: Pointer; DestSize: Integer;
  DefaultChar, UsedDefault: NativeInt): Integer; external kernel32;
procedure ExitProcess(Code: Integer); external kernel32;

{ ntdll!RtlMoveMemory(Dest, Src, N) and RtlFillMemory(Dest, N, Fill) under the shapes this unit needs }
procedure MoveMemory(Dest, Src: Pointer; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure FillMemory(Dest: Pointer; N: NativeUInt; Fill: Integer); external ntdll name 'RtlFillMemory';
procedure StoreHandle(var Dest: Text; var Src: NativeInt; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure LoadHandle(var Dest: NativeInt; var Src: Text; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure PointerToInt(var Dest: NativeInt; var Src: Pointer; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure IntToPointer(var Dest: Pointer; var Src: NativeInt; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure LoadByte(var Dest: Byte; Src: Pointer; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure StoreByte(Dest: Pointer; var Src: Byte; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure StoreAnsiChar(Dest: Pointer; var Src: AnsiChar; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure StoreWideChar(Dest: Pointer; var Src: WideChar; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure LoadInteger(var Dest: Integer; Src: Pointer; N: NativeUInt); external ntdll name 'RtlMoveMemory';
procedure LoadPointer(var Dest: Pointer; Src: Pointer; N: NativeUInt); external ntdll name 'RtlMoveMemory';

function AddressOf(P: Pointer): NativeInt;
begin
  PointerToInt(Result, P, 8);
end;

function Offset(P: Pointer; Delta: NativeInt): Pointer;
var
  A: NativeInt;
begin
  PointerToInt(A, P, 8);
  A := A + Delta;
  IntToPointer(Result, A, 8);
end;

procedure WriteBytes(var T: Text; Buffer: Pointer; Count: Integer);
var
  H: NativeInt;
  Written: Integer;
begin
  if Count > 0 then
  begin
    LoadHandle(H, T, 8);
    WriteFile(H, Buffer, Count, Written, 0);
  end;
end;

{ LANG-915: Width - Len spaces before the text }
procedure WritePadding(var T: Text; Width, Len: Integer);
var
  N, Chunk: Integer;
begin
  N := Width - Len;
  while N > 0 do
  begin
    Chunk := N;
    if Chunk > ScratchSize then
      Chunk := ScratchSize;
    FillMemory(Scratch, Chunk, 32);
    WriteBytes(T, Scratch, Chunk);
    N := N - Chunk;
  end;
end;

procedure _Startup(UnitTable: Pointer; UnitCount: NativeInt);
var
  H: NativeInt;
begin
  Units := UnitTable;
  UnitEntries := UnitCount;
  Heap := GetProcessHeap;
  Scratch := HeapAlloc(Heap, 0, ScratchSize);
  H := GetStdHandle(STD_OUTPUT_HANDLE);
  StoreHandle(Output, H, 8);
  H := GetStdHandle(STD_ERROR_HANDLE);
  StoreHandle(ErrOutput, H, 8);
  ExitCode := 0;
end;

{ ABI-100: each entry is (Init, Final). Calling them needs procedural types (task C-161); the M2.1 unit table holds
  only System, which has neither, so a non-null entry is reported as runtime error 204. }
procedure _InitUnits;
var
  I: NativeInt;
  Entry: Pointer;
begin
  I := 0;
  while I < 2 * UnitEntries do
  begin
    LoadPointer(Entry, Offset(Units, 8 * I), 8);
    if AddressOf(Entry) <> 0 then
      _RunError(204);
    I := I + 1;
  end;
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

{ ABI-101 without ErrorProc (SysUtils is M2.4); the error address is not known in M2.1 and printed as zero }
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

{ InOutRes arrives with task C-166; no M2.1 helper records an I/O error }
procedure _IOCheck;
begin
end;

{ S = payload of a UnicodeString (Length: Int32 at S - 12, rtl-abi.md section 3) or nil }
procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
var
  Len, Size: Integer;
  Buffer: Pointer;
begin
  Len := 0;
  if AddressOf(S) <> 0 then
    LoadInteger(Len, Offset(S, -12), 4);
  WritePadding(T, Width, Len);
  if Len > 0 then
  begin
    Buffer := HeapAlloc(Heap, 0, 3 * Len);
    Size := WideCharToMultiByte(CP_UTF8, 0, S, Len, Buffer, 3 * Len, 0, 0);
    WriteBytes(T, Buffer, Size);
    HeapFree(Heap, 0, Buffer);
  end;
end;

procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
var
  Size: Integer;
begin
  WritePadding(T, Width, 1);
  StoreWideChar(Scratch, C, 2);
  Size := WideCharToMultiByte(CP_UTF8, 0, Scratch, 1, Offset(Scratch, 8), 8, 0, 0);
  WriteBytes(T, Offset(Scratch, 8), Size);
end;

procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
begin
  WritePadding(T, Width, 1);
  StoreAnsiChar(Scratch, C, 1);
  WriteBytes(T, Scratch, 1);
end;

{ digits are built backwards from the end of Scratch; Pos = first used byte }
procedure PutDigit(var Pos: Integer; Digit: Integer);
var
  B: Byte;
begin
  Pos := Pos - 1;
  B := 48 + Digit;
  StoreByte(Offset(Scratch, Pos), B, 1);
end;

procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
var
  Pos: Integer;
  Digit: Int64;
  Negative: Boolean;
  Minus: Byte;
begin
  Negative := V < 0;
  Pos := ScratchSize;
  repeat
    Digit := V mod 10;
    if Digit < 0 then
      Digit := -Digit;
    PutDigit(Pos, Digit);
    V := V div 10;
  until V = 0;
  if Negative then
  begin
    Pos := Pos - 1;
    Minus := 45;
    StoreByte(Offset(Scratch, Pos), Minus, 1);
  end;
  WritePadding(T, Width, ScratchSize - Pos);
  WriteBytes(T, Offset(Scratch, Pos), ScratchSize - Pos);
end;

procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
var
  Pos: Integer;
begin
  Pos := ScratchSize;
  repeat
    PutDigit(Pos, V mod 10);
    V := V div 10;
  until V = 0;
  WritePadding(T, Width, ScratchSize - Pos);
  WriteBytes(T, Offset(Scratch, Pos), ScratchSize - Pos);
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
  B: Byte;
begin
  B := 13;
  StoreByte(Scratch, B, 1);
  B := 10;
  StoreByte(Offset(Scratch, 1), B, 1);
  WriteBytes(T, Scratch, 2);
end;

{ unbuffered: nothing to flush }
procedure _WriteEnd(var T: Text);
begin
end;

{ Big sets (rtl-abi.md section 9.8): 32 bytes, bit K in byte K shr 3; combined byte by byte, so Dest may be A or B
  (ABI-033). Op: 0 union, 1 intersection, 2 difference, 3 symmetric difference. }
procedure SetCombine(Dest, A, B: Pointer; Op: Integer);
var
  I: Integer;
  X, Y: Byte;
begin
  for I := 0 to 31 do
  begin
    LoadByte(X, Offset(A, I), 1);
    LoadByte(Y, Offset(B, I), 1);
    case Op of
      0: X := X or Y;
      1: X := X and Y;
      2: X := X and not Y;
    else
      X := X xor Y;
    end;
    StoreByte(Offset(Dest, I), X, 1);
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
  X, Y: Byte;
begin
  Result := True;
  for I := 0 to 31 do
  begin
    LoadByte(X, Offset(A, I), 1);
    LoadByte(Y, Offset(B, I), 1);
    if X <> Y then
      Result := False;
  end;
end;

{ A <= B: no bit of A outside B }
function _SetLe(A, B: Pointer): Boolean;
var
  I: Integer;
  X, Y: Byte;
begin
  Result := True;
  for I := 0 to 31 do
  begin
    LoadByte(X, Offset(A, I), 1);
    LoadByte(Y, Offset(B, I), 1);
    if X and not Y <> 0 then
      Result := False;
  end;
end;

{ Dest += [Lo..Hi]; Lo > Hi adds nothing, otherwise both bounds must lie in 0..255 (runtime error 201) }
procedure _SetRange(Dest: Pointer; Lo, Hi: Integer);
var
  K: Integer;
  X: Byte;
begin
  if Lo > Hi then
    Exit;
  if (Lo < 0) or (Hi > 255) then
    _RunError(201);
  for K := Lo to Hi do
  begin
    LoadByte(X, Offset(Dest, K shr 3), 1);
    X := X or (1 shl (K and 7));
    StoreByte(Offset(Dest, K shr 3), X, 1);
  end;
end;

function memcpy(Dest, Src: Pointer; N: NativeUInt): Pointer;
begin
  MoveMemory(Dest, Src, N);
  Result := Dest;
end;

function memmove(Dest, Src: Pointer; N: NativeUInt): Pointer;
begin
  MoveMemory(Dest, Src, N);
  Result := Dest;
end;

function memset(Dest: Pointer; C: Integer; N: NativeUInt): Pointer;
begin
  FillMemory(Dest, N, C);
  Result := Dest;
end;

function memcmp(A, B: Pointer; N: NativeUInt): Integer;
var
  I: NativeUInt;
  X, Y: Byte;
begin
  Result := 0;
  I := 0;
  while (Result = 0) and (I < N) do
  begin
    LoadByte(X, Offset(A, I), 1);
    LoadByte(Y, Offset(B, I), 1);
    Result := X - Y;
    I := I + 1;
  end;
end;

end.
