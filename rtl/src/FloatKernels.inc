// SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
{ ------------------------------------------------- floating-point kernels shared by System and Math (FloatMath.inc)
  The constants, bit helpers, Payne-Hanek reduction (ReducePiO2) and the sin/cos kernels of the transcendental
  functions; the method is described in FloatMath.inc. The including unit declares FracMask, SignMask, PUInt64 and
  PDouble. }

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
