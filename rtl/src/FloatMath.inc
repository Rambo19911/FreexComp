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

const
  ExpMask = $7FF0000000000000;

  { minimax polynomial coefficients }
  SinC1 = -0.16666666666666632;        { (sin r)/r - 1 on z = r^2 in (0, 0.617] }
  SinC2 = 0.008333333333322411;
  SinC3 = -0.0001984126982980585;
  SinC4 = 2.755731369120978e-06;
  SinC5 = -2.5050758000116794e-08;
  SinC6 = 1.5896788746458855e-10;
  CosC2 = 0.041666666666666644;        { cos r - 1 + r^2/2, powers z^2 .. z^7, z in (0, 0.617] }
  CosC3 = -0.0013888888888881456;
  CosC4 = 2.480158729400149e-05;
  CosC5 = -2.7557315632599126e-07;
  CosC6 = 2.0875893187371182e-09;
  CosC7 = -1.1368210885359575e-11;
  AtnC1 = -0.3333333333333306;         { (atan t)/t - 1 on z = t^2 in (0, 0.1915] }
  AtnC2 = 0.19999999999906304;
  AtnC3 = -0.14285714275031977;
  AtnC4 = 0.11111110513091116;
  AtnC5 = -0.09090889787137323;
  AtnC6 = 0.07691916876136007;
  AtnC7 = -0.06661472863799472;
  AtnC8 = 0.058360915593572434;
  AtnC9 = -0.04986747295139954;
  AtnC10 = 0.03674974898179896;
  AtnC11 = -0.016493979651494187;
  LnC1 = 0.6666666666666734;           { 2 atanh(s)/s - 2 on z = s^2 in (0, 0.02944] }
  LnC2 = 0.39999999999414343;
  LnC3 = 0.2857142874246908;
  LnC4 = 0.22222198564130213;
  LnC5 = 0.1818356483073271;
  LnC6 = 0.1531403681380657;
  LnC7 = 0.14796095085859337;
  ExpC1 = 0.16666666666666602;         { r coth(r/2) - 2 on z = r^2 in (0, 0.1202] }
  ExpC2 = -0.0027777777777013633;
  ExpC3 = 6.61375632093802e-05;
  ExpC4 = -1.6533901674955887e-06;
  ExpC5 = 4.138117047030772e-08;

  { split constants: Head has trailing zero bits where a product with it must be exact }
  Ln2Head = 0.6931471805598903;        { ln 2 rounded to 42 bits }
  Ln2Tail = 5.497923018708371e-14;
  InvLn2 = 1.4426950408889634;
  PiO2Head = 1.5707963267948966;
  PiO2Tail = 6.123233995736766e-17;
  PiO4 = 0.7853981633974483;
  AtnHalfHead = 0.4636476090008061;
  AtnHalfTail = 2.2698777452961687e-17;
  AtnOneHead = 0.7853981633974483;
  AtnOneTail = 3.061616997868383e-17;
  AtnThreeHalvesHead = 0.982793723247329;
  AtnThreeHalvesTail = 1.3903311031230998e-17;
  ExpOverflow = 709.782712893384;      { largest x with a finite e^x }
  ExpUnderflow = -746.0;               { e^x < 2^-1075 below: rounds to +0 }
  DekkerSplit = 134217729.0;           { 2^27 + 1 }

  { bits 1 .. 1344 of 2/Pi (2/Pi = 0.b1 b2 b3 ... in binary), most significant bit first }
  TwoOverPi: array[0..41] of Cardinal = (
    $A2F9836E, $4E441529, $FC2757D1, $F534DDC0, $DB629599, $3C439041,
    $FE5163AB, $DEBBC561, $B7246E3A, $424DD2E0, $06492EEA, $09D1921C,
    $FE1DEB1C, $B129A73E, $E88235F5, $2EBB4484, $E99C7026, $B45F7E41,
    $3991D639, $835339F4, $9C845F8B, $BDF9283B, $1FF897FF, $DE05980F,
    $EF2F118B, $5A0A6D1F, $6D367ECF, $27CB09B7, $4F463F66, $9E5FEA2D,
    $7527BAC7, $EBE5F17B, $3D0739F7, $8A5292EA, $6BFB5FB1, $1F8D5D08,
    $56033046, $FC7B6BAB, $F0CFBC20, $9AF4361D, $A9E39161, $5EE61B08);

function DoubleBits(X: Double): UInt64;
begin
  Result := PUInt64(@X)^;
end;

function BitsDouble(Bits: UInt64): Double;
begin
  Result := PDouble(@Bits)^;
end;

function ToDouble(I: Int64): Double;
begin
  Result := I;
end;

{ 2^E for -1022 <= E <= 1023 }
function Pow2(E: Integer): Double;
begin
  Result := BitsDouble(UInt64(E + 1023) shl 52);
end;

{ bits P .. P+31 of 2/Pi as one word (bit P most significant); bits with an index below 1 are 0 }
function TwoOverPiWord(P: Integer): Cardinal;
var
  Q, J, Sh: Integer;
begin
  Q := P - 1;
  if Q <= -32 then
    Result := 0
  else if Q < 0 then
    Result := TwoOverPi[0] shr (-Q)
  else
  begin
    J := Q shr 5;
    Sh := Q and 31;
    Result := Cardinal((((UInt64(TwoOverPi[J]) shl 32) or TwoOverPi[J + 1]) shr (32 - Sh)) and $FFFFFFFF);
  end;
end;

{ |X| > Pi/4, finite: X = N * Pi/2 + (Y0 + Y1) with |Y0 + Y1| <= Pi/4 (to about 2^-100 relative); returns N mod 4 }
function ReducePiO2(X: Double; var Y0, Y1: Double): Integer;
var
  Bits, M, A, B: UInt64;
  E, I0, I, J, L, Q: Integer;
  W, Pr: array[0..6] of Cardinal;    { least significant word first }
  MW: array[0..1] of Cardinal;
  T, Carry: UInt64;
  Neg, NegX: Boolean;
  Hi, Lo, P, PHi, PLo, AHi, ALo, BHi, BLo, C, Err, S: Double;
begin
  Bits := DoubleBits(X);
  NegX := (Bits shr 63) <> 0;
  M := (Bits and FracMask) or (UInt64(1) shl 52);
  E := Integer((Bits shr 52) and $7FF) - 1075;     { |X| = M * 2^E; X is normal here }
  I0 := E - 1;                                    { bits of 2/Pi before I0 only add multiples of 4 }
  for I := 0 to 6 do
    W[6 - I] := TwoOverPiWord(I0 + 32 * I);
  MW[0] := Cardinal(M and $FFFFFFFF);
  MW[1] := Cardinal(M shr 32);
  for I := 0 to 6 do
    Pr[I] := 0;
  for J := 0 to 1 do
  begin
    Carry := 0;
    for I := 0 to 6 - J do
    begin
      T := UInt64(W[I]) * MW[J] + Pr[I + J] + Carry;
      Pr[I + J] := Cardinal(T and $FFFFFFFF);
      Carry := T shr 32;
    end;
  end;
  { X * 2/Pi mod 4 = Pr * 2^-222: bits 222, 223 are the quadrant, bits 0 .. 221 the fraction }
  Q := Integer(Pr[6] shr 30);
  Pr[6] := Pr[6] and $3FFFFFFF;
  Neg := (Pr[6] and $20000000) <> 0;
  if Neg then
  begin
    { fraction >= 1/2: one more quadrant, remainder 2^222 - fraction, negative }
    Q := Q + 1;
    Carry := 1;
    for I := 0 to 6 do
    begin
      T := UInt64(not Pr[I]) + Carry;
      Pr[I] := Cardinal(T and $FFFFFFFF);
      Carry := T shr 32;
    end;
    Pr[6] := Pr[6] and $3FFFFFFF;
  end;
  { leading bit L of the 222-bit magnitude; A = bits L-63 .. L, B = the 64 bits below }
  L := 221;
  while (L >= 0) and (((Pr[L shr 5] shr (L and 31)) and 1) = 0) do
    L := L - 1;
  if L < 0 then
  begin
    Y0 := 0;
    Y1 := 0;
  end
  else
  begin
    A := 0;
    B := 0;
    for I := L downto L - 127 do
    begin
      if I >= 0 then
        T := (Pr[I shr 5] shr (I and 31)) and 1
      else
        T := 0;
      if I > L - 64 then
        A := (A shl 1) or T
      else
        B := (B shl 1) or T;
    end;
    { magnitude = (A * 2^64 + B) * 2^(L - 127 - 222); as a double-double Hi + Lo }
    Hi := ToDouble(Int64(A shr 11)) * Pow2(L - 127 + 75 - 222);
    Lo := (ToDouble(Int64(A and $7FF)) * 9007199254740992.0 + ToDouble(Int64(B shr 11))) * Pow2(L - 127 + 11 - 222);
    { (Hi + Lo) * (PiO2Head + PiO2Tail), the head product exact (Dekker) }
    P := Hi * PiO2Head;
    C := DekkerSplit * Hi;
    AHi := C - (C - Hi);
    ALo := Hi - AHi;
    C := DekkerSplit * PiO2Head;
    BHi := C - (C - PiO2Head);
    BLo := PiO2Head - BHi;
    Err := ((AHi * BHi - P) + AHi * BLo + ALo * BHi) + ALo * BLo;
    PHi := P;
    PLo := Err + (Hi * PiO2Tail + Lo * PiO2Head);
    S := PHi + PLo;
    Y1 := PLo - (S - PHi);
    Y0 := S;
    if Neg then
    begin
      Y0 := -Y0;
      Y1 := -Y1;
    end;
  end;
  if NegX then
  begin
    Y0 := -Y0;
    Y1 := -Y1;
    Q := -Q;
  end;
  Result := Q and 3;
end;

{ sin(X + Y) for |X + Y| <= Pi/4 (about), |Y| <= ulp(X)/2 }
function KernelSin(X, Y: Double): Double;
var
  Z, V, R: Double;
begin
  Z := X * X;
  V := Z * X;
  R := SinC2 + Z * (SinC3 + Z * (SinC4 + Z * (SinC5 + Z * SinC6)));
  Result := X - ((Z * (0.5 * Y - V * R) - Y) - V * SinC1);
end;

{ cos(X + Y) for |X + Y| <= Pi/4 (about), |Y| <= ulp(X)/2 }
function KernelCos(X, Y: Double): Double;
var
  Z, R, HZ, W: Double;
begin
  Z := X * X;
  R := Z * Z * (CosC2 + Z * (CosC3 + Z * (CosC4 + Z * (CosC5 + Z * (CosC6 + Z * CosC7)))));
  HZ := 0.5 * Z;
  W := 1.0 - HZ;
  Result := W + (((1.0 - W) - HZ) + (R - X * Y));
end;

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
