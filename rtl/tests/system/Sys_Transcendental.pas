// covers: RTL-041, RTL-042
// opc-run: variants 1 2 3 4
program Sys_Transcendental;
{ Sin, Cos, ArcTan, Exp, Ln (system.md section 5.2, RTL-041). Variant 1: 20 vectors per function, each result
  within 1 ulp of the correctly rounded value (computed with an independent high-precision evaluation and frozen
  here as bit patterns), then the special values. Variants 2, 3, 4: Ln(0), Ln(-1), Ln(-0) are runtime error 207.
  Arguments go through variables so the compiler cannot fold them. }
type
  PUInt64 = ^UInt64;
  PDouble = ^Double;
  TVec = record
    X, Y: UInt64;
  end;

const
  Vectors: array[0..99] of TVec = (
  { Sin }
  (X: $3FE0000000000000; Y: $3FDEAEE8744B05F0),   { 0.5 -> 0.479425538604203 }
  (X: $3FF0000000000000; Y: $3FEAED548F090CEE),   { 1.0 -> 0.8414709848078965 }
  (X: $C000000000000000; Y: $BFED18F6EAD1B446),   { -2.0 -> -0.9092974268256817 }
  (X: $4008000000000000; Y: $3FC210386DB6D55B),   { 3.0 -> 0.1411200080598672 }
  (X: $3FE921FB54442D18; Y: $3FE6A09E667F3BCC),   { 0.7853981633974483 -> 0.7071067811865475 }
  (X: $3FF921FB54442D18; Y: $3FF0000000000000),   { 1.5707963267948966 -> 1.0 }
  (X: $400921FB54442D18; Y: $3CA1A62633145C07),   { 3.141592653589793 -> 1.2246467991473532e-16 }
  (X: $401921FB54442D18; Y: $BCB1A62633145C07),   { 6.283185307179586 -> -2.4492935982947064e-16 }
  (X: $3EE4F8B588E368F1; Y: $3EE4F8B588E1E8A2),   { 1e-05 -> 9.999999999833334e-06 }
  (X: $81A56E1FC2F8F359; Y: $81A56E1FC2F8F359),   { -1e-300 -> -1e-300 }
  (X: $0000000000000001; Y: $0000000000000001),   { 5e-324 -> 5e-324 }
  (X: $4059000000000000; Y: $BFE03425B78C4DB8),   { 100.0 -> -0.5063656411097588 }
  (X: $412E848000000000; Y: $BFD6664B2568D867),   { 1000000.0 -> -0.34999350217129294 }
  (X: $4480F0CF064DD592; Y: $BFEB453AB76BF397),   { 1e+22 -> -0.8522008497671888 }
  (X: $7FEFFFFFFFFFFFFF; Y: $3F7452FC98B34E97),   { 1.7976931348623157e+308 -> 0.004961954789184062 }
  (X: $7506AC5B262CA1FF; Y: $3FF0000000000000),   { 5.319372648326541e+255 -> 1.0 }
  (X: $C30C6BF526340000; Y: $BFEB76F88136CEBA),   { -1000000000000000.0 -> -0.8582727931702359 }
  (X: $4076300000000000; Y: $BEFF9BD0307D1DE3),   { 355.0 -> -3.014435335948845e-05 }
  (X: $40F9639000000000; Y: $BEF40EFDF1EB8DE7),   { 103993.0 -> -1.9129335778423752e-05 }
  (X: $413921FB54442D18; Y: $BDD1A62633145C07),   { 1647099.3291652855 -> -6.420676210313675e-11 }
  { Cos }
  (X: $3FE0000000000000; Y: $3FEC1528065B7D50),   { 0.5 -> 0.8775825618903728 }
  (X: $BFF0000000000000; Y: $3FE14A280FB5068C),   { -1.0 -> 0.5403023058681398 }
  (X: $4000000000000000; Y: $BFDAA22657537205),   { 2.0 -> -0.4161468365471424 }
  (X: $4008000000000000; Y: $BFEFAE04BE85E5D2),   { 3.0 -> -0.9899924966004454 }
  (X: $3FE921FB54442D18; Y: $3FE6A09E667F3BCD),   { 0.7853981633974483 -> 0.7071067811865476 }
  (X: $3FF921FB54442D18; Y: $3C91A62633145C07),   { 1.5707963267948966 -> 6.123233995736766e-17 }
  (X: $400921FB54442D18; Y: $BFF0000000000000),   { 3.141592653589793 -> -1.0 }
  (X: $401921FB54442D18; Y: $3FF0000000000000),   { 6.283185307179586 -> 1.0 }
  (X: $3EE4F8B588E368F1; Y: $3FEFFFFFFFF920C8),   { 1e-05 -> 0.99999999995 }
  (X: $3FE6666666666666; Y: $3FE87996529F9D93),   { 0.7 -> 0.7648421872844885 }
  (X: $0000000000000001; Y: $3FF0000000000000),   { 5e-324 -> 1.0 }
  (X: $C059000000000000; Y: $3FEB981DBF665FDF),   { -100.0 -> 0.8623188722876839 }
  (X: $412E848000000000; Y: $3FEDF9DF9906D32C),   { 1000000.0 -> 0.9367521275331447 }
  (X: $4480F0CF064DD592; Y: $3FE0BE2CEF01C8F4),   { 1e+22 -> 0.523214785395139 }
  (X: $7FEFFFFFFFFFFFFF; Y: $BFEFFFE62ECFAB75),   { 1.7976931348623157e+308 -> -0.9999876894265599 }
  (X: $7506AC5B262CA1FF; Y: $BC214AE72E6BA22F),   { 5.319372648326541e+255 -> -4.687165924254628e-19 }
  (X: $430C6BF526340000; Y: $BFE06C154609D33F),   { 1000000000000000.0 -> -0.5131937377869703 }
  (X: $4076300000000000; Y: $BFEFFFFFFFC18E4C),   { 355.0 -> -0.999999999545659 }
  (X: $40E979C000000000; Y: $3ED719A47D413C35),   { 52174.0 -> 5.507508792203807e-06 }
  (X: $7FE0000000000000; Y: $BFEA719F26C232BF),   { 8.98846567431158e+307 -> -0.826369834614148 }
  { ArcTan }
  (X: $3BC79CA10C924223; Y: $3BC79CA10C924223),   { 1e-20 -> 1e-20 }
  (X: $3FB999999999999A; Y: $3FB983E282E2CC4D),   { 0.1 -> 0.09966865249116204 }
  (X: $3FDC000000000000; Y: $3FDA64EEC3CC23FD),   { 0.4375 -> 0.4124104415973873 }
  (X: $3FDBFFFFFFFFFFFF; Y: $3FDA64EEC3CC23FC),   { 0.43749999999999994 -> 0.41241044159738727 }
  (X: $3FE0000000000000; Y: $3FDDAC670561BB4F),   { 0.5 -> 0.4636476090008061 }
  (X: $3FE6000000000000; Y: $3FE345F01CCE37BB),   { 0.6875 -> 0.6022873461349642 }
  (X: $3FE6666666666666; Y: $3FE38B112D7BD4AD),   { 0.7 -> 0.6107259643892086 }
  (X: $3FF0000000000000; Y: $3FE921FB54442D18),   { 1.0 -> 0.7853981633974483 }
  (X: $BFF0000000000000; Y: $BFE921FB54442D18),   { -1.0 -> -0.7853981633974483 }
  (X: $3FF3000000000000; Y: $3FEBDE70ED439FE7),   { 1.1875 -> 0.870903457075653 }
  (X: $3FF8000000000000; Y: $3FEF730BD281F69B),   { 1.5 -> 0.982793723247329 }
  (X: $4003800000000000; Y: $3FF2E75728833A54),   { 2.4375 -> 1.1814796049617557 }
  (X: $4008000000000000; Y: $3FF3FC176B7A8560),   { 3.0 -> 1.2490457723982544 }
  (X: $4024000000000000; Y: $3FF789BD2C160054),   { 10.0 -> 1.4711276743037347 }
  (X: $4202A05F20000000; Y: $3FF921FB543D4DE0),   { 10000000000.0 -> 1.5707963266948965 }
  (X: $C376345785D8A000; Y: $BFF921FB54442D18),   { -1e+17 -> -1.5707963267948966 }
  (X: $7E37E43C8800759C; Y: $3FF921FB54442D18),   { 1e+300 -> 1.5707963267948966 }
  (X: $BFD3333333333333; Y: $BFD2A73A661EAF06),   { -0.3 -> -0.2914567944778671 }
  (X: $0000000000000001; Y: $0000000000000001),   { 5e-324 -> 5e-324 }
  (X: $7FEFFFFFFFFFFFFF; Y: $3FF921FB54442D18),   { 1.7976931348623157e+308 -> 1.5707963267948966 }
  { Exp }
  (X: $3FB999999999999A; Y: $3FF1AEC7B35A00D4),   { 0.1 -> 1.1051709180756477 }
  (X: $BFB999999999999A; Y: $3FECF46D99D52B3A),   { -0.1 -> 0.9048374180359595 }
  (X: $3FF0000000000000; Y: $4005BF0A8B145769),   { 1.0 -> 2.718281828459045 }
  (X: $BFF0000000000000; Y: $3FD78B56362CEF38),   { -1.0 -> 0.36787944117144233 }
  (X: $3FD62E42FEFA39EF; Y: $3FF6A09E667F3BCC),   { 0.34657359027997264 -> 1.414213562373095 }
  (X: $4024000000000000; Y: $40D5829DCF950560),   { 10.0 -> 22026.465794806718 }
  (X: $C024000000000000; Y: $3F07CD79B5647C9B),   { -10.0 -> 4.5399929762484854e-05 }
  (X: $4059000000000000; Y: $48F3494A9B171BF5),   { 100.0 -> 2.6881171418161356e+43 }
  (X: $4085E00000000000; Y: $7F0D945DF4F8EC8E),   { 700.0 -> 1.0142320547350045e+304 }
  (X: $40862E42FEFA39EF; Y: $7FEFFFFFFFFFFF2A),   { 709.782712893384 -> 1.7976931348622732e+308 }
  (X: $C085E00000000000; Y: $00D14F2B0FB9307F),   { -700.0 -> 9.85967654375977e-305 }
  (X: $C086232BDD7ABCD2; Y: $001000000000007C),   { -708.3964185322641 -> 2.2250738585072626e-308 }
  (X: $C086800000000000; Y: $0000000993B4DC95),   { -720.0 -> 2.0322308024e-313 }
  (X: $C0874910D52D3051; Y: $0000000000000001),   { -745.1332191019411 -> 5e-324 }
  (X: $C087491EB851EB85; Y: $0000000000000000),   { -745.14 -> 0.0 }
  (X: $3DDB7CDFD9D7BDBB; Y: $3FF000000006DF38),   { 1e-10 -> 1.0000000001 }
  (X: $01A56E1FC2F8F359; Y: $3FF0000000000000),   { 1e-300 -> 1.0 }
  (X: $3E10000000000000; Y: $3FF0000000400000),   { 9.313225746154785e-10 -> 1.0000000009313226 }
  (X: $8000000000000001; Y: $3FF0000000000000),   { -5e-324 -> 1.0 }
  (X: $4004000000000000; Y: $40285D6FD931E0BB),   { 2.5 -> 12.182493960703473 }
  { Ln }
  (X: $4000000000000000; Y: $3FE62E42FEFA39EF),   { 2.0 -> 0.6931471805599453 }
  (X: $3FE0000000000000; Y: $BFE62E42FEFA39EF),   { 0.5 -> -0.6931471805599453 }
  (X: $4024000000000000; Y: $40026BB1BBB55516),   { 10.0 -> 2.302585092994046 }
  (X: $3FB999999999999A; Y: $C0026BB1BBB55515),   { 0.1 -> -2.3025850929940455 }
  (X: $4005BF0A8B145769; Y: $3FF0000000000000),   { 2.718281828459045 -> 1.0 }
  (X: $3FF0000000000001; Y: $3CAFFFFFFFFFFFFF),   { 1.0000000000000002 -> 2.2204460492503128e-16 }
  (X: $3FEFFFFFFFFFFFFF; Y: $BCA0000000000000),   { 0.9999999999999999 -> -1.1102230246251565e-16 }
  (X: $3FF6A09E667F3BCD; Y: $3FD62E42FEFA39F0),   { 1.4142135623730951 -> 0.3465735902799727 }
  (X: $3FE6A09E667F3BCD; Y: $BFD62E42FEFA39EE),   { 0.7071067811865476 -> -0.3465735902799726 }
  (X: $7E37E43C8800759C; Y: $4085963447F87FB5),   { 1e+300 -> 690.7755278982137 }
  (X: $01A56E1FC2F8F359; Y: $C085963447F87FB5),   { 1e-300 -> -690.7755278982137 }
  (X: $0000000000000001; Y: $C0874385446D71C3),   { 5e-324 -> -744.4400719213812 }
  (X: $0010000000000000; Y: $C086232BDD7ABCD2),   { 2.2250738585072014e-308 -> -708.3964185322641 }
  (X: $7FEFFFFFFFFFFFFF; Y: $40862E42FEFA39EF),   { 1.7976931348623157e+308 -> 709.782712893384 }
  (X: $4008000000000000; Y: $3FF193EA7AAD030B),   { 3.0 -> 1.0986122886681098 }
  (X: $4202A05F20000000; Y: $4037069E2AA2AA5B),   { 10000000000.0 -> 23.025850929940457 }
  (X: $3FF8000000000000; Y: $3FD9F323ECBF984C),   { 1.5 -> 0.4054651081081644 }
  (X: $3FE8000000000000; Y: $BFD269621134DB92),   { 0.75 -> -0.2876820724517809 }
  (X: $40934A456D5CFAAD; Y: $401C7951D51791D7),   { 1234.5678 -> 7.118476228297786 }
  (X: $0018000000000000; Y: $C0861FED78FD24DF)    { 3.337610787760802e-308 -> -707.9909534241559 });

var
  D: Double;
  S: Single;
  Failed: Integer;
  P: PChar;

function BitsOf(X: Double): UInt64;
begin
  Result := PUInt64(@X)^;
end;

function FromBits(B: UInt64): Double;
begin
  Result := PDouble(@B)^;
end;

procedure WriteHex(B: UInt64);
const
  HexDigits: array[0..15] of Char = ('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F');
var
  I: Integer;
begin
  Write('$');
  for I := 15 downto 0 do
    Write(HexDigits[(B shr (4 * I)) and 15]);
end;

{ position of a finite double on the integer line of all doubles, so that neighbours differ by 1 }
function OrderKey(B: UInt64): Int64;
begin
  if (B shr 63) <> 0 then
    Result := -Int64(B and $7FFFFFFFFFFFFFFF)
  else
    Result := Int64(B);
end;

procedure WriteName(Fn: Integer);
begin
  case Fn of
    0: Write('Sin');
    1: Write('Cos');
    2: Write('ArcTan');
    3: Write('Exp');
  else
    Write('Ln');
  end;
end;

function Apply(Fn: Integer; X: Double): Double;
begin
  case Fn of
    0: Result := Sin(X);
    1: Result := Cos(X);
    2: Result := ArcTan(X);
    3: Result := Exp(X);
  else
    Result := Ln(X);
  end;
end;

procedure CheckVectors;
var
  I: Integer;
  Got: UInt64;
  Dist: Int64;
begin
  for I := 0 to 99 do
  begin
    D := FromBits(Vectors[I].X);
    Got := BitsOf(Apply(I div 20, D));
    Dist := OrderKey(Got) - OrderKey(Vectors[I].Y);
    if Dist < 0 then
      Dist := -Dist;
    if Dist <= 1 then
    begin
      Write('ok ');
      WriteName(I div 20);
      Writeln(' ', I mod 20 + 1);
    end
    else
    begin
      Failed := Failed + 1;
      Write('FAIL ');
      WriteName(I div 20);
      Write(' ', I mod 20 + 1, ': x ');
      WriteHex(Vectors[I].X);
      Write(' expected ');
      WriteHex(Vectors[I].Y);
      Write(' got ');
      WriteHex(Got);
      Writeln;
    end;
  end;
end;

{ NaN of any sign and payload prints as NAN, everything else as its exact bit pattern }
procedure Show(V: Double);
var
  B: UInt64;
begin
  B := BitsOf(V);
  if (B and $7FFFFFFFFFFFFFFF) > $7FF0000000000000 then
    Write('NAN')
  else
    WriteHex(B);
  Writeln;
end;

procedure Specials;
var
  Zero, Inf, NaN, NegZero: Double;
begin
  Zero := 0;
  NegZero := -Zero;
  Inf := 1 / Zero;
  NaN := Zero / Zero;
  Write('Sin(+0) = ');
  Show(Sin(Zero));
  Write('Sin(-0) = ');
  Show(Sin(NegZero));
  Write('Sin(+Inf) = ');
  Show(Sin(Inf));
  Write('Sin(NaN) = ');
  Show(Sin(NaN));
  Write('Cos(-0) = ');
  Show(Cos(NegZero));
  Write('Cos(-Inf) = ');
  Show(Cos(-Inf));
  Write('Cos(NaN) = ');
  Show(Cos(NaN));
  Write('ArcTan(-0) = ');
  Show(ArcTan(NegZero));
  Write('ArcTan(+Inf) = ');
  Show(ArcTan(Inf));
  Write('ArcTan(-Inf) = ');
  Show(ArcTan(-Inf));
  Write('ArcTan(NaN) = ');
  Show(ArcTan(NaN));
  Write('Exp(+0) = ');
  Show(Exp(Zero));
  Write('Exp(-0) = ');
  Show(Exp(NegZero));
  Write('Exp(+Inf) = ');
  Show(Exp(Inf));
  Write('Exp(-Inf) = ');
  Show(Exp(-Inf));
  D := 710;
  Write('Exp(710) = ');
  Show(Exp(D));
  D := -1000;
  Write('Exp(-1000) = ');
  Show(Exp(D));
  Write('Exp(NaN) = ');
  Show(Exp(NaN));
  D := 1;
  Write('Ln(1) = ');
  Show(Ln(D));
  Write('Ln(+Inf) = ');
  Show(Ln(Inf));
  Write('Ln(NaN) = ');
  Show(Ln(NaN));
  S := 0.5;
  D := 0.5;
  Writeln('Single argument: ', BitsOf(Sin(S)) = BitsOf(Sin(D)));
end;

begin
  P := CmdLine;
  while P^ <> #0 do
    Inc(P);
  Dec(P);
  case P^ of
    '1':
      begin
        CheckVectors;
        Specials;
        Writeln('failed ', Failed);
      end;
    '2':
      begin
        D := 0;
        Writeln(Ln(D));
      end;
    '3':
      begin
        D := -1;
        Writeln(Ln(D));
      end;
    '4':
      begin
        D := 0;
        D := -D;
        Writeln(Ln(D));
      end;
  end;
end.
