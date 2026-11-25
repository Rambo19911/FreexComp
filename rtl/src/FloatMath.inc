// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ---------------------------------------------------------------- transcendental functions (system.md RTL-041)
  Own implementation from standard numerical methods; the polynomial coefficients are minimax fits computed for
  this RTL (Remez exchange on the reduced intervals named below). Every routine works on Double with the
  default SSE2 environment (round to nearest, exceptions masked; RTL-042) and needs no fused multiply-add.

  Sin, Cos: x = n * Pi/2 + r with |r| <= Pi/4. For |x| > Pi/4 the reduction is exact-in-integers (Payne-Hanek):
    the significand of x times a 224-bit window of the bits of 2/Pi, so the remainder is right for every Double.
    sin r = r + r^3 S(r^2), cos r = 1 - r^2/2 + r^4 C(r^2) on |r| <= Pi/4, with the tail of r folded in.
  ArcTan: atan |x| = atan c + atan t with c in (0, 1/2, 1, 3/2, infinity) and |t| <= 7/16;
    atan t = t + t Q(t^2).
  Exp: x = k ln 2 + r, |r| <= ln2 / 2 (Cody-Waite: ln 2 in a 42-bit head and a tail); e^r through the
    rational form 1 + 2r / (R(r^2) - r) with R(z) = 2 + z P(z) ~ r coth(r/2); result * 2^k in two steps.
  Ln: x = 2^k (1 + f) with 1 + f in [sqrt(2)/2, sqrt(2)); s = f / (2 + f); ln(1+f) = 2 atanh s,
    = f - (f^2/2 - s (f^2/2 + R(s^2))) with R(z) = z L(z). }

{ the constants and kernels are in FloatKernels.inc, included before this file }

function Sin(X: Double): Double;
var
  Bits: UInt64;
  Y0, Y1: Double;
begin
  Bits := DoubleBits(X) and not UInt64(SignMask);
  if Bits >= ExpMask then
    Result := X - X                                   { NaN, and NaN for an infinity }
  else if Bits < $3E40000000000000 then
    Result := X                                       { |X| < 2^-27: sin X = X to the last bit }
  else if BitsDouble(Bits) <= PiO4 then
    Result := KernelSin(X, 0)
  else
    case ReducePiO2(X, Y0, Y1) of
      0: Result := KernelSin(Y0, Y1);
      1: Result := KernelCos(Y0, Y1);
      2: Result := -KernelSin(Y0, Y1);
    else
      Result := -KernelCos(Y0, Y1);
    end;
end;

function Cos(X: Double): Double;
var
  Bits: UInt64;
  Y0, Y1: Double;
begin
  Bits := DoubleBits(X) and not UInt64(SignMask);
  if Bits >= ExpMask then
    Result := X - X
  else if Bits < $3E40000000000000 then
    Result := 1.0
  else if BitsDouble(Bits) <= PiO4 then
    Result := KernelCos(X, 0)
  else
    case ReducePiO2(X, Y0, Y1) of
      0: Result := KernelCos(Y0, Y1);
      1: Result := -KernelSin(Y0, Y1);
      2: Result := -KernelCos(Y0, Y1);
    else
      Result := KernelSin(Y0, Y1);
    end;
end;

function ArcTan(X: Double): Double;
var
  Bits: UInt64;
  A, T, Z, Q, Head, Tail: Double;
  Reduced: Boolean;
begin
  Bits := DoubleBits(X) and not UInt64(SignMask);
  if Bits > ExpMask then
  begin
    Result := X + X;                                  { NaN }
    Exit;
  end;
  A := BitsDouble(Bits);
  if Bits >= $4410000000000000 then
  begin
    Result := PiO2Head + PiO2Tail;                    { |X| >= 2^66 (and infinity): Pi/2 }
    if X < 0 then
      Result := -Result;
    Exit;
  end;
  if Bits < $3E40000000000000 then
  begin
    Result := X;                                      { |X| < 2^-27 }
    Exit;
  end;
  Reduced := True;
  Head := 0;
  Tail := 0;
  if A < 0.4375 then
  begin
    Reduced := False;
    T := A;
  end
  else if A < 0.6875 then
  begin
    T := (2.0 * A - 1.0) / (2.0 + A);                 { atan A = atan 1/2 + atan T }
    Head := AtnHalfHead;
    Tail := AtnHalfTail;
  end
  else if A < 1.1875 then
  begin
    T := (A - 1.0) / (A + 1.0);
    Head := AtnOneHead;
    Tail := AtnOneTail;
  end
  else if A < 2.4375 then
  begin
    T := (A - 1.5) / (1.0 + 1.5 * A);
    Head := AtnThreeHalvesHead;
    Tail := AtnThreeHalvesTail;
  end
  else
  begin
    T := -1.0 / A;
    Head := PiO2Head;
    Tail := PiO2Tail;
  end;
  Z := T * T;
  Q := Z * (AtnC1 + Z * (AtnC2 + Z * (AtnC3 + Z * (AtnC4 + Z * (AtnC5 + Z * (AtnC6 + Z * (AtnC7 + Z * (AtnC8 +
    Z * (AtnC9 + Z * (AtnC10 + Z * AtnC11))))))))));
  if Reduced then
    Result := Head - ((-(T * Q) - Tail) - T)
  else
    Result := T + T * Q;
  if X < 0 then
    Result := -Result;
end;

function Exp(X: Double): Double;
var
  Bits: UInt64;
  K: Integer;
  T, Hi, Lo, R, Z, C, Y: Double;
begin
  Bits := DoubleBits(X) and not UInt64(SignMask);
  if Bits >= ExpMask then
  begin
    if (Bits > ExpMask) or (X > 0) then
      Result := X + X                                 { NaN, +Inf }
    else
      Result := 0;                                    { -Inf }
    Exit;
  end;
  if X > ExpOverflow then
  begin
    Result := BitsDouble(ExpMask);                    { +Inf (RTL-042: no error) }
    Exit;
  end;
  if X < ExpUnderflow then
  begin
    Result := 0;
    Exit;
  end;
  if Bits < $3E30000000000000 then
  begin
    Result := 1.0 + X;                                { |X| < 2^-28 }
    Exit;
  end;
  if Bits < $3FD62E42FEFA39EF then                    { |X| < ln2 / 2 }
  begin
    K := 0;
    Hi := X;
    Lo := 0;
  end
  else
  begin
    T := X * InvLn2;
    if T < 0 then
      K := Trunc(T - 0.5)
    else
      K := Trunc(T + 0.5);
    Hi := X - K * Ln2Head;                            { exact: K * Ln2Head has at most 53 bits, Sterbenz }
    Lo := K * Ln2Tail;
  end;
  R := Hi - Lo;
  Z := R * R;
  C := R - Z * (ExpC1 + Z * (ExpC2 + Z * (ExpC3 + Z * (ExpC4 + Z * ExpC5))));
  Y := 1.0 - ((Lo - (R * C) / (2.0 - C)) - Hi);
  if K = 0 then
    Result := Y
  else if K > 1023 then
    Result := Y * Pow2(K - 1) * 2.0
  else if K < -1021 then
    Result := Y * Pow2(K + 1000) * Pow2(-1000)        { subnormal result: one rounding in the last product }
  else
    Result := Y * Pow2(K);
end;

function Ln(X: Double): Double;
var
  Bits, Frac: UInt64;
  K: Integer;
  F, S, Z, R, HFSq, DK: Double;
begin
  Bits := DoubleBits(X);
  if Bits >= ExpMask then
  begin
    if (Bits and not UInt64(SignMask)) > ExpMask then
      Result := X + X                                 { NaN }
    else if (Bits shr 63) = 0 then
      Result := X                                     { +Inf }
    else
      _RunError(207);                                 { -Inf and other negatives }
    Exit;
  end;
  if Bits = 0 then
    _RunError(207);                                   { +0 }
  K := 0;
  if Bits < $0010000000000000 then
  begin
    X := X * 18014398509481984.0;                     { subnormal: scale by 2^54 }
    Bits := DoubleBits(X);
    K := -54;
  end;
  K := K + Integer(Bits shr 52) - 1023;
  Frac := Bits and FracMask;
  if Frac > $6A09E667F3BCD then                       { significand > sqrt 2: use [sqrt(2)/2, 1) }
  begin
    K := K + 1;
    F := BitsDouble(Frac or $3FE0000000000000) - 1.0;
  end
  else
    F := BitsDouble(Frac or $3FF0000000000000) - 1.0;
  DK := K;
  if F = 0 then
  begin
    Result := DK * Ln2Head + DK * Ln2Tail;
    Exit;
  end;
  S := F / (2.0 + F);
  Z := S * S;
  R := Z * (LnC1 + Z * (LnC2 + Z * (LnC3 + Z * (LnC4 + Z * (LnC5 + Z * (LnC6 + Z * LnC7))))));
  HFSq := 0.5 * F * F;
  Result := DK * Ln2Head - ((HFSq - (S * (HFSq + R) + DK * Ln2Tail)) - F);
end;
