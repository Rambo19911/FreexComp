{ Math - floating-point and integer helpers on top of System (docs: rtl/other-units.md section 2, RTL-610).
  Domain errors raise EInvalidOp ('Invalid floating point operation') unless stated; arguments and results are Double
  unless stated. The transcendental functions reuse the kernels of System (FloatKernels.inc). }
unit Math;

interface

uses
  SysUtils, Types, RTLConsts;

type
  EInvalidArgument = class(EMathError);

var
  { LANG-342 forbids 0/0 and 1/0 in constant expressions: these three are variables set at initialization from their bit
    patterns (quiet NaN $7FF8000000000000, $7FF0000000000000, $FFF0000000000000); never assign them }
  NaN: Double;
  Infinity: Double;
  NegInfinity: Double;

const
  MinDouble = 2.2250738585072014E-308;      { the smallest normal Double }
  MaxDouble = 1.7976931348623157E+308;
  MinSingle = 1.1754943508222875E-38;       { the smallest normal Single }
  MaxSingle = 3.4028234663852886E+38;

{ ------------------------------------------------------------------------------------------------- Min / Max }
{ a NaN operand gives the other operand }
function Min(A, B: Integer): Integer; overload;
function Min(A, B: Int64): Int64; overload;
function Min(A, B: UInt64): UInt64; overload;
function Min(A, B: Double): Double; overload;
function Max(A, B: Integer): Integer; overload;
function Max(A, B: Int64): Int64; overload;
function Max(A, B: UInt64): UInt64; overload;
function Max(A, B: Double): Double; overload;
{ an empty array raises EInvalidArgument }
function MinIntValue(const Data: array of Integer): Integer;
function MaxIntValue(const Data: array of Integer): Integer;
function MinValue(const Data: array of Double): Double;
function MaxValue(const Data: array of Double): Double;

{ ------------------------------------------------------------------------------------------------ range, sign }
{ Value < AMin -> AMin, Value > AMax -> AMax; AMin is applied last when AMin > AMax }
function EnsureRange(Value, AMin, AMax: Integer): Integer; overload;
function EnsureRange(Value, AMin, AMax: Int64): Int64; overload;
function EnsureRange(Value, AMin, AMax: Double): Double; overload;
function InRange(Value, AMin, AMax: Integer): Boolean; overload;
function InRange(Value, AMin, AMax: Int64): Boolean; overload;
function InRange(Value, AMin, AMax: Double): Boolean; overload;
function Sign(Value: Integer): TValueSign; overload;
function Sign(Value: Int64): TValueSign; overload;
{ Sign(NaN) raises EInvalidOp; Sign(-0.0) = 0 }
function Sign(Value: Double): TValueSign; overload;

{ ----------------------------------------------------------------------------------------------- rounding }
{ a result outside the integer type, an infinity or NaN raises EInvalidOp }
function Floor(X: Double): Integer;
function Ceil(X: Double): Integer;
function Floor64(X: Double): Int64;
function Ceil64(X: Double): Int64;
{ Digit in -37 .. 37 (else EInvalidOp): Round(Value / 10^Digit) * 10^Digit, ties to even. For Digit < 0 the
  scaling multiplies by 10^-Digit (exact for -22 .. -1), which is the same value when exact arithmetic is used }
function RoundTo(Value: Double; Digit: Integer): Double;
{ as RoundTo, ties away from zero }
function SimpleRoundTo(Value: Double; Digit: Integer = -2): Double;

{ -------------------------------------------------------------------------------------- powers and logarithms }
{ Exponent = 0 -> 1; Base = 0 and Exponent > 0 -> 0; an integral Exponent with |Exponent| <= 2^31 -> repeated
  squaring; a negative Base with a non-integral Exponent raises EInvalidOp; else Exp(Exponent * Ln(Base)) }
function Power(Base, Exponent: Double): Double;
{ repeated squaring; a negative Exponent gives the reciprocal }
function IntPower(Base: Double; Exponent: Integer): Double;
{ Sqrt(X^2 + Y^2) without intermediate overflow or underflow; an infinite operand gives +Inf }
function Hypot(X, Y: Double): Double;
{ X <= 0 (and Base <= 0 for LogN, X <= -1 for LnXP1) raises EInvalidOp; Log10 is exact for 10^-22 .. 10^22, Log2 for
  every power of two }
function Log10(X: Double): Double;
function Log2(X: Double): Double;
function LogN(Base, X: Double): Double;
{ Ln(1 + X), accurate for small X }
function LnXP1(X: Double): Double;

{ --------------------------------------------------------------------------------------------- trigonometry }
function Tan(X: Double): Double;
function Cotan(X: Double): Double;
{ outside [-1, 1] raises EInvalidOp }
function ArcSin(X: Double): Double;
function ArcCos(X: Double): Double;
{ the angle of (X, Y) in [-Pi, Pi]; ArcTan2(0, 0) = 0 }
function ArcTan2(Y, X: Double): Double;
procedure SinCos(Theta: Double; var S, C: Double);
function Sinh(X: Double): Double;
function Cosh(X: Double): Double;
function Tanh(X: Double): Double;
function ArcSinh(X: Double): Double;
{ X < 1 raises EInvalidOp }
function ArcCosh(X: Double): Double;
{ |X| > 1 raises EInvalidOp; ArcTanh(+-1) = +-Inf }
function ArcTanh(X: Double): Double;
{ D * Pi / 180, R * 180 / Pi }
function DegToRad(D: Double): Double;
function RadToDeg(R: Double): Double;

{ ------------------------------------------------------------------------------------------- float tests }
function IsNan(V: Double): Boolean;
function IsInfinite(V: Double): Boolean;
{ Epsilon = 0: Max(Min(|A|, |B|) * 1E-12, 1E-12); equal values (also infinities) are always the same }
function SameValue(A, B: Double; Epsilon: Double = 0): Boolean;
{ Epsilon = 0: 1E-12 }
function IsZero(A: Double; Epsilon: Double = 0): Boolean;
{ 0 when SameValue(A, B, Epsilon), else -1 / 1 }
function CompareValue(A, B: Double; Epsilon: Double = 0): TValueRelationship;

{ --------------------------------------------------------------------------------------- integers, random }
procedure DivMod(Dividend: Integer; Divisor: Word; var Result, Remainder: Word);
function IfThen(Cond: Boolean; ATrue: Integer; AFalse: Integer = 0): Integer; overload;
function IfThen(Cond: Boolean; ATrue: Int64; AFalse: Int64 = 0): Int64; overload;
function IfThen(Cond: Boolean; ATrue: Double; AFalse: Double = 0): Double; overload;
{ Random(Abs(AFrom - ATo)) + Min(AFrom, ATo) }
function RandomRange(AFrom, ATo: Integer): Integer;

{ ---------------------------------------------------------------------------------------------- statistics }
function Sum(const Data: array of Double): Double;
function SumInt(const Data: array of Integer): Int64;
{ an empty array raises EInvalidArgument }
function Mean(const Data: array of Double): Double;

implementation

{ ------------------------------------------------------------------------------------------------- errors }

procedure InvalidOp;
begin
  raise EInvalidOp.Create(SInvalidOp);
end;

procedure InvalidArgument;
begin
  raise EInvalidArgument.Create(SInvalidArgument);
end;

const
  FracMask = $000FFFFFFFFFFFFF;        { names FloatKernels.inc expects }
  SignMask = $8000000000000000;

{$I FloatKernels.inc}                  { DoubleBits, BitsDouble, Pow2, ReducePiO2, KernelSin, KernelCos }

{ ------------------------------------------------------------------------------------------------- Min / Max }

function Min(A, B: Integer): Integer;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: Int64): Int64;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: UInt64): UInt64;
begin
  if A < B then
    Result := A
  else
    Result := B;
end;

function Min(A, B: Double): Double;
begin
  if IsNan(A) or (B < A) then
    Result := B
  else
    Result := A;
end;

function Max(A, B: Integer): Integer;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Max(A, B: Int64): Int64;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Max(A, B: UInt64): UInt64;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

function Max(A, B: Double): Double;
begin
  if IsNan(A) or (B > A) then
    Result := B
  else
    Result := A;
end;

function MinIntValue(const Data: array of Integer): Integer;
var
  I: Integer;
begin
  if Length(Data) = 0 then
    InvalidArgument;
  Result := Data[0];
  for I := 1 to High(Data) do
    if Data[I] < Result then
      Result := Data[I];
end;

function MaxIntValue(const Data: array of Integer): Integer;
var
  I: Integer;
begin
  if Length(Data) = 0 then
    InvalidArgument;
  Result := Data[0];
  for I := 1 to High(Data) do
    if Data[I] > Result then
      Result := Data[I];
end;

function MinValue(const Data: array of Double): Double;
var
  I: Integer;
begin
  if Length(Data) = 0 then
    InvalidArgument;
  Result := Data[0];
  for I := 1 to High(Data) do
    Result := Min(Result, Data[I]);
end;

function MaxValue(const Data: array of Double): Double;
var
  I: Integer;
begin
  if Length(Data) = 0 then
    InvalidArgument;
  Result := Data[0];
  for I := 1 to High(Data) do
    Result := Max(Result, Data[I]);
end;

{ ------------------------------------------------------------------------------------------------ range, sign }

function EnsureRange(Value, AMin, AMax: Integer): Integer;
begin
  Result := Value;
  if Result > AMax then
    Result := AMax;
  if Result < AMin then
    Result := AMin;
end;

function EnsureRange(Value, AMin, AMax: Int64): Int64;
begin
  Result := Value;
  if Result > AMax then
    Result := AMax;
  if Result < AMin then
    Result := AMin;
end;

function EnsureRange(Value, AMin, AMax: Double): Double;
begin
  Result := Value;
  if Result > AMax then
    Result := AMax;
  if Result < AMin then
    Result := AMin;
end;

function InRange(Value, AMin, AMax: Integer): Boolean;
begin
  Result := (Value >= AMin) and (Value <= AMax);
end;

function InRange(Value, AMin, AMax: Int64): Boolean;
begin
  Result := (Value >= AMin) and (Value <= AMax);
end;

function InRange(Value, AMin, AMax: Double): Boolean;
begin
  Result := (Value >= AMin) and (Value <= AMax);
end;

function Sign(Value: Integer): TValueSign;
begin
  if Value < 0 then
    Result := -1
  else if Value > 0 then
    Result := 1
  else
    Result := 0;
end;

function Sign(Value: Int64): TValueSign;
begin
  if Value < 0 then
    Result := -1
  else if Value > 0 then
    Result := 1
  else
    Result := 0;
end;

function Sign(Value: Double): TValueSign;
begin
  if IsNan(Value) then
    InvalidOp;
  if Value < 0 then
    Result := -1
  else if Value > 0 then
    Result := 1
  else
    Result := 0;
end;

{ ----------------------------------------------------------------------------------------------- rounding }

const
  TwoPow31 = 2147483648.0;
  TwoPow52 = 4503599627370496.0;
  TwoPow63 = 9223372036854775808.0;

{ floor / ceiling as a Double for |X| < 2^63 (Trunc is exact there) }
function FloorD(X: Double): Double;
begin
  Result := Trunc(X);
  if Result > X then
    Result := Result - 1;
end;

function CeilD(X: Double): Double;
begin
  Result := Trunc(X);
  if Result < X then
    Result := Result + 1;
end;

function Floor(X: Double): Integer;
begin
  if not ((X >= -TwoPow31) and (X < TwoPow31)) then
    InvalidOp;
  Result := Trunc(FloorD(X));
end;

function Ceil(X: Double): Integer;
begin
  if not ((X > -TwoPow31 - 1) and (X <= TwoPow31 - 1)) then
    InvalidOp;
  Result := Trunc(CeilD(X));
end;

function Floor64(X: Double): Int64;
begin
  if not ((X >= -TwoPow63) and (X < TwoPow63)) then
    InvalidOp;
  Result := Trunc(X);
  if Result > X then
    Result := Result - 1;
end;

function Ceil64(X: Double): Int64;
begin
  if not ((X >= -TwoPow63) and (X < TwoPow63)) then
    InvalidOp;
  Result := Trunc(X);
  if Result < X then
    Result := Result + 1;
end;

{ X rounded to an integral value, ties to even; |X| >= 2^52, infinities and NaN are returned unchanged }
function RoundEven(X: Double): Double;
begin
  if (X > -TwoPow52) and (X < TwoPow52) then
    Result := Round(X)
  else
    Result := X;
end;

{ X rounded to an integral value, ties away from zero }
function RoundAway(X: Double): Double;
begin
  if (X > -TwoPow52) and (X < TwoPow52) then
  begin
    if X < 0 then
      Result := Trunc(X - 0.5)
    else
      Result := Trunc(X + 0.5);
  end
  else
    Result := X;
end;

function Pow10(N: Integer): Double;
var
  I: Integer;
begin
  Result := 1;
  for I := 1 to N do
    Result := Result * 10;
end;

function ScaledRound(Value: Double; Digit: Integer; Away: Boolean): Double;
var
  P: Double;
begin
  if (Digit < -37) or (Digit > 37) then
    InvalidOp;
  if Digit < 0 then
  begin
    if not ((Value > -TwoPow52) and (Value < TwoPow52)) then
    begin
      Result := Value;                 { already integral, an infinity or NaN }
      Exit;
    end;
    P := Pow10(-Digit);
    if Away then
      Result := RoundAway(Value * P) / P
    else
      Result := RoundEven(Value * P) / P;
  end
  else
  begin
    P := Pow10(Digit);
    if Away then
      Result := RoundAway(Value / P) * P
    else
      Result := RoundEven(Value / P) * P;
  end;
end;

function RoundTo(Value: Double; Digit: Integer): Double;
begin
  Result := ScaledRound(Value, Digit, False);
end;

function SimpleRoundTo(Value: Double; Digit: Integer): Double;
begin
  Result := ScaledRound(Value, Digit, True);
end;

{ ------------------------------------------------------------------------------------------- float tests }

function IsNan(V: Double): Boolean;
begin
  Result := V <> V;
end;

function IsInfinite(V: Double): Boolean;
begin
  Result := (V = Infinity) or (V = NegInfinity);
end;

function SameValue(A, B: Double; Epsilon: Double): Boolean;
begin
  if Epsilon = 0 then
    Epsilon := Max(Min(Abs(A), Abs(B)) * 1E-12, 1E-12);
  Result := (A = B) or (Abs(A - B) <= Epsilon);
end;

function IsZero(A: Double; Epsilon: Double): Boolean;
begin
  if Epsilon = 0 then
    Epsilon := 1E-12;
  Result := Abs(A) <= Epsilon;
end;

function CompareValue(A, B: Double; Epsilon: Double): TValueRelationship;
begin
  if SameValue(A, B, Epsilon) then
    Result := EqualsValue
  else if A < B then
    Result := LessThanValue
  else
    Result := GreaterThanValue;
end;

{ --------------------------------------------------------------------------------------- integers, random }

procedure DivMod(Dividend: Integer; Divisor: Word; var Result, Remainder: Word);
begin
  Result := Word(Dividend div Divisor);
  Remainder := Word(Dividend mod Divisor);
end;

function IfThen(Cond: Boolean; ATrue: Integer; AFalse: Integer): Integer;
begin
  if Cond then
    Result := ATrue
  else
    Result := AFalse;
end;

function IfThen(Cond: Boolean; ATrue: Int64; AFalse: Int64): Int64;
begin
  if Cond then
    Result := ATrue
  else
    Result := AFalse;
end;

function IfThen(Cond: Boolean; ATrue: Double; AFalse: Double): Double;
begin
  if Cond then
    Result := ATrue
  else
    Result := AFalse;
end;

function RandomRange(AFrom, ATo: Integer): Integer;
begin
  Result := Random(Abs(AFrom - ATo)) + Min(AFrom, ATo);
end;

{ ---------------------------------------------------------------------------------------------- statistics }

function Sum(const Data: array of Double): Double;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to High(Data) do
    Result := Result + Data[I];
end;

function SumInt(const Data: array of Integer): Int64;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to High(Data) do
    Result := Result + Data[I];
end;

function Mean(const Data: array of Double): Double;
begin
  if Length(Data) = 0 then
    InvalidArgument;
  Result := Sum(Data) / Length(Data);
end;

{ -------------------------------------------------------------------------------------- powers and logarithms }

const
  Ln2 = 0.6931471805599453;
  Ln10 = 2.302585092994046;
  PiHead = 3.141592653589793;          { 2 * PiO2Head, 2 * PiO2Tail }
  PiTail = 1.2246467991473532e-16;
  TwoPow28 = 268435456.0;
  TwoM28 = 3.725290298461914e-09;      { 2^-28 }
  ExpMaxArg = 709.782712893384;        { the largest X with a finite Exp(X) }

function PowInt(Base: Double; N: Int64): Double;
var
  M: UInt64;
  B: Double;
begin
  if N < 0 then
    M := UInt64(-N)
  else
    M := UInt64(N);
  B := Base;
  Result := 1;
  while M <> 0 do
  begin
    if (M and 1) <> 0 then
      Result := Result * B;
    M := M shr 1;
    if M <> 0 then
      B := B * B;
  end;
  if N < 0 then
    Result := 1 / Result;
end;

function Power(Base, Exponent: Double): Double;
begin
  if Exponent = 0 then
    Result := 1
  else if (Base = 0) and (Exponent > 0) then
    Result := 0
  else if (Frac(Exponent) = 0) and (Abs(Exponent) <= TwoPow31) then
    Result := PowInt(Base, Trunc(Exponent))
  else if Base < 0 then
  begin
    InvalidOp;
    Result := 0;
  end
  else
    Result := Exp(Exponent * Ln(Base));
end;

function IntPower(Base: Double; Exponent: Integer): Double;
begin
  Result := PowInt(Base, Exponent);
end;

{ the operands are scaled by a power of two when their squares could overflow or underflow }
function Hypot(X, Y: Double): Double;
var
  A, B, T: Double;
begin
  A := Abs(X);
  B := Abs(Y);
  if IsInfinite(A) or IsInfinite(B) then
  begin
    Result := Infinity;
    Exit;
  end;
  if A < B then
  begin
    T := A;
    A := B;
    B := T;
  end;
  if A > Pow2(500) then
    Result := Sqrt(Sqr(A * Pow2(-600)) + Sqr(B * Pow2(-600))) * Pow2(600)
  else if A < Pow2(-500) then
    Result := Sqrt(Sqr(A * Pow2(600)) + Sqr(B * Pow2(600))) * Pow2(-600)
  else
    Result := Sqrt(A * A + B * B);
end;

procedure CheckLogArg(X: Double);
begin
  if X <= 0 then
    InvalidOp;
end;

function Log10(X: Double): Double;
var
  K: Integer;
  P: Double;
begin
  CheckLogArg(X);
  Result := Ln(X) / Ln10;
  if Abs(Result) <= 22.5 then
  begin
    K := Round(Result);
    if Abs(Result - K) < 1E-9 then
    begin
      if K >= 0 then
        P := PowInt(10, K)
      else
        P := 1 / PowInt(10, -K);
      if P = X then
        Result := K;
      end;
  end;
end;

{ X = 2^K * M with M in [sqrt(2)/2, sqrt(2)): K + Ln(M) / Ln 2 }
function Log2(X: Double): Double;
var
  Bits: UInt64;
  K: Integer;
  M: Double;
begin
  CheckLogArg(X);
  Bits := DoubleBits(X);
  if Bits >= ExpMask then
  begin
    Result := X + X;                   { NaN, +Inf }
    Exit;
  end;
  K := 0;
  if Bits < $0010000000000000 then
  begin
    X := X * 18014398509481984.0;      { subnormal: 2^54 }
    Bits := DoubleBits(X);
    K := -54;
  end;
  K := K + Integer(Bits shr 52) - 1023;
  if (Bits and FracMask) > $6A09E667F3BCD then
  begin
    K := K + 1;
    M := BitsDouble((Bits and FracMask) or $3FE0000000000000);
  end
  else
    M := BitsDouble((Bits and FracMask) or $3FF0000000000000);
  Result := K + Ln(M) / Ln2;
end;

function LogN(Base, X: Double): Double;
begin
  CheckLogArg(Base);
  CheckLogArg(X);
  Result := Ln(X) / Ln(Base);
end;

{ Goldberg: Ln(U) * X / (U - 1) with U = 1 + X compensates the rounding of U }
function LnXP1(X: Double): Double;
var
  U: Double;
begin
  if X <= -1 then
    InvalidOp;
  U := 1 + X;
  if (U = 1) or IsNan(X) or IsInfinite(X) then
    Result := X
  else
    Result := Ln(U) * (X / (U - 1));
end;

{ Kahan: Exp(X) - 1, accurate for small X }
function ExpM1(X: Double): Double;
var
  U, M: Double;
begin
  if X > ExpMaxArg then
  begin
    Result := Exp(X);
    Exit;
  end;
  U := Exp(X);
  if U = 1 then
    Result := X
  else
  begin
    M := U - 1;
    if M = -1 then
      Result := -1
    else
      Result := M * X / Ln(U);
  end;
end;

{ --------------------------------------------------------------------------------------------- trigonometry }

function Tan(X: Double): Double;
var
  Bits: UInt64;
  Y0, Y1: Double;
begin
  Bits := DoubleBits(X) and not UInt64(SignMask);
  if Bits >= ExpMask then
    Result := X - X                    { NaN, and NaN for an infinity }
  else if Bits < $3E40000000000000 then
    Result := X                        { |X| < 2^-27 }
  else if BitsDouble(Bits) <= PiO4 then
    Result := KernelSin(X, 0) / KernelCos(X, 0)
  else if (ReducePiO2(X, Y0, Y1) and 1) = 0 then
    Result := KernelSin(Y0, Y1) / KernelCos(Y0, Y1)
  else
    Result := -KernelCos(Y0, Y1) / KernelSin(Y0, Y1);
end;

function Cotan(X: Double): Double;
begin
  Result := 1 / Tan(X);
end;

function ArcSin(X: Double): Double;
begin
  if Abs(X) > 1 then
    InvalidOp;
  Result := ArcTan2(X, Sqrt((1 - X) * (1 + X)));
end;

function ArcCos(X: Double): Double;
begin
  if Abs(X) > 1 then
    InvalidOp;
  Result := ArcTan2(Sqrt((1 - X) * (1 + X)), X);
end;

function Negative(X: Double): Boolean;
begin
  Result := (DoubleBits(X) shr 63) <> 0;
end;

{ Y with the sign of S }
function WithSign(Y, S: Double): Double;
begin
  if Negative(S) then
    Result := -Y
  else
    Result := Y;
end;

function ArcTan2(Y, X: Double): Double;
var
  Z: Double;
begin
  if IsNan(X) or IsNan(Y) then
    Result := X + Y
  else if Y = 0 then
  begin
    if X = 0 then
      Result := 0
    else if X > 0 then
      Result := Y
    else
      Result := WithSign(PiHead, Y);
  end
  else if X = 0 then
    Result := WithSign(PiO2Head, Y)
  else if IsInfinite(X) then
  begin
    if IsInfinite(Y) then
    begin
      if X > 0 then
        Result := WithSign(PiO4, Y)
      else
        Result := WithSign(3 * PiO4, Y);
    end
    else if X > 0 then
      Result := WithSign(0, Y)
    else
      Result := WithSign(PiHead, Y);
  end
  else if IsInfinite(Y) then
    Result := WithSign(PiO2Head, Y)
  else
  begin
    Z := ArcTan(Y / X);
    if X > 0 then
      Result := Z
    else if Y > 0 then
      Result := PiHead + (Z + PiTail)
    else
      Result := -PiHead + (Z - PiTail);
  end;
end;

procedure SinCos(Theta: Double; var S, C: Double);
begin
  S := Sin(Theta);
  C := Cos(Theta);
end;

function Sinh(X: Double): Double;
var
  A, T, W: Double;
begin
  A := Abs(X);
  if IsNan(X) or (A < TwoM28) then
  begin
    Result := X;
    Exit;
  end;
  if A < 22 then
  begin
    T := ExpM1(A);
    if A < 1 then
      Result := 0.5 * (2 * T - T * T / (T + 1))
    else
      Result := 0.5 * (T + T / (T + 1));
  end
  else if A <= ExpMaxArg then
    Result := 0.5 * Exp(A)
  else
  begin
    W := Exp(0.5 * A);
    Result := (0.5 * W) * W;           { overflows to +Inf beyond the range }
  end;
  Result := WithSign(Result, X);
end;

function Cosh(X: Double): Double;
var
  A, T, W: Double;
begin
  A := Abs(X);
  if IsNan(X) then
    Result := A
  else if A < 0.5 * Ln2 then
  begin
    T := ExpM1(A);
    W := 1 + T;
    Result := 1 + (T * T) / (W + W);
  end
  else if A < 22 then
  begin
    T := Exp(A);
    Result := 0.5 * T + 0.5 / T;
  end
  else if A <= ExpMaxArg then
    Result := 0.5 * Exp(A)
  else
  begin
    W := Exp(0.5 * A);
    Result := (0.5 * W) * W;
  end;
end;

function Tanh(X: Double): Double;
var
  A, T: Double;
begin
  A := Abs(X);
  if IsNan(X) or (A < TwoM28 * TwoM28) then
  begin
    Result := X;
    Exit;
  end;
  if A >= 22 then
    Result := 1
  else if A >= 1 then
  begin
    T := ExpM1(A + A);
    Result := 1 - 2 / (T + 2);
  end
  else
  begin
    T := ExpM1(-(A + A));
    Result := -T / (T + 2);
  end;
  Result := WithSign(Result, X);
end;

function ArcSinh(X: Double): Double;
var
  A, T: Double;
begin
  A := Abs(X);
  if IsNan(X) or IsInfinite(X) or (A < TwoM28) then
  begin
    Result := X;
    Exit;
  end;
  if A > TwoPow28 then
    Result := Ln(A) + Ln2
  else if A > 2 then
    Result := Ln(2 * A + 1 / (Sqrt(A * A + 1) + A))
  else
  begin
    T := A * A;
    Result := LnXP1(A + T / (1 + Sqrt(1 + T)));
  end;
  Result := WithSign(Result, X);
end;

function ArcCosh(X: Double): Double;
var
  T: Double;
begin
  if X < 1 then
    InvalidOp;
  if IsNan(X) or IsInfinite(X) then
    Result := X
  else if X > TwoPow28 then
    Result := Ln(X) + Ln2
  else if X > 2 then
    Result := Ln(2 * X - 1 / (X + Sqrt(X * X - 1)))
  else
  begin
    T := X - 1;
    Result := LnXP1(T + Sqrt(2 * T + T * T));
  end;
end;

function ArcTanh(X: Double): Double;
var
  A, T: Double;
begin
  A := Abs(X);
  if A > 1 then
    InvalidOp;
  if IsNan(X) or (A < TwoM28) then
  begin
    Result := X;
    Exit;
  end;
  if A = 1 then
    Result := Infinity
  else if A < 0.5 then
  begin
    T := A + A;
    Result := 0.5 * LnXP1(T + T * A / (1 - A));
  end
  else
    Result := 0.5 * LnXP1((A + A) / (1 - A));
  Result := WithSign(Result, X);
end;

function DegToRad(D: Double): Double;
begin
  Result := D * Pi / 180;
end;

function RadToDeg(R: Double): Double;
begin
  Result := R * 180 / Pi;
end;

initialization
  NaN := BitsDouble($7FF8000000000000);
  Infinity := BitsDouble($7FF0000000000000);
  NegInfinity := BitsDouble($FFF0000000000000);
end.
