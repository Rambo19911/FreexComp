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
procedure _Assert(Msg, FileName: Pointer; Line: Integer); noreturn;
procedure _IOCheck;

function _GetMem(Size: NativeInt): Pointer;
procedure _FreeMem(P: Pointer);
function _ReallocMem(P: Pointer; Size: NativeInt): Pointer;
function _AllocZero(Size: NativeInt): Pointer;

procedure _WriteUStr(var T: Text; S: Pointer; Width: Integer);
procedure _WriteWChar(var T: Text; C: WideChar; Width: Integer);
procedure _WriteAChar(var T: Text; C: AnsiChar; Width: Integer);
procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
procedure _WriteUInt(var T: Text; V: UInt64; Width: Integer);
procedure _WriteBool(var T: Text; V: Boolean; Width: Integer);
procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
procedure _WriteLn(var T: Text);
procedure _WriteEnd(var T: Text);

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

const
  kernel32 = 'kernel32.dll';
  CP_UTF8 = 65001;
  STD_OUTPUT_HANDLE = -11;
  STD_ERROR_HANDLE = -12;
  HEAP_ZERO_MEMORY = 8;
  ScratchSize = 64;
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
function WideCharToMultiByte(CodePage, Flags: Cardinal; Src: Pointer; SrcLen: Integer; Dest: Pointer; DestSize: Integer;
  DefaultChar, UsedDefault: NativeInt): Integer; external kernel32;
procedure ExitProcess(Code: Integer); external kernel32;


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

{ InOutRes arrives with task C-166; no helper records an I/O error yet }
procedure _IOCheck;
begin
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
  end;
end;

procedure _FreeMem(P: Pointer);
begin
  if P <> nil then
    HeapFree(Heap, 0, P);
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

procedure _WriteInt(var T: Text; V: Int64; Width: Integer);
var
  Pos: Integer;
  Digit: Int64;
  Negative: Boolean;
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
    Scratch[Pos] := 45;
  end;
  WritePadding(T, Width, ScratchSize - Pos);
  WriteBytes(T, Scratch + Pos, ScratchSize - Pos);
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

{ CMP-410: Decimals < 0 = scientific in Width (the compiler passes 23 when no width is given), else fixed; LANG-932 specials }
procedure _WriteFloat(var T: Text; V: Double; Width, Decimals: Integer);
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

end.