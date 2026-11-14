// covers: LANG-226, LANG-232
program RealSpecialValues;
var
  Zero, One, PosInf, NegInf, NaN, D: Double;
  S: Single;
begin
  Zero := 0;
  One := 1;
  PosInf := One / Zero;
  NegInf := -One / Zero;
  NaN := Zero / Zero;

  { division by zero gives infinities and NaN without an error (exceptions masked) }
  Writeln(PosInf > 1e308, ' ', NegInf < -1e308, ' ', PosInf = -NegInf, ' ', PosInf + 1 = PosInf, ' ', PosInf - PosInf = PosInf - PosInf);

  { every ordered comparison with NaN is False, <> is True }
  Writeln(NaN = NaN, ' ', NaN <> NaN, ' ', NaN < One, ' ', NaN <= One, ' ', NaN > One, ' ', NaN >= One);
  Writeln(One = NaN, ' ', One <> NaN, ' ', NaN < PosInf, ' ', NaN > NegInf);
  D := NaN;
  if not (D = D) then
    Writeln('NaN is not equal to itself');
  D := NaN * 0;
  Writeln(D <> D);

  { -0.0 equals 0.0, but 1 / -0.0 is -INF }
  D := -Zero;
  Writeln(D = Zero, ' ', One / D = NegInf);

  { Single overflow at run time gives an infinity; NaN survives the conversion }
  S := 3e38;
  S := S * 10;
  Writeln(S = PosInf, ' ', S > 3e38);
  S := NaN;
  Writeln(S = S, ' ', S <> S);
end.