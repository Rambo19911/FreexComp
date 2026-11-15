// phase: 7
// covers: RTL-670, RTL-671
program Gen_Comparers;
{ other-units.md section 8.1: TComparer<T>.Default / TEqualityComparer<T>.Default chosen from TypeInfo(T) -
  ordinals by value (signed and unsigned), floats by value with NaN first and NaN = NaN, string ordinal, classes and
  pointers by address, unmanaged records by their bytes, dynamic arrays by length then bytes; a record with a
  managed field raises ENotSupportedException; FNV-1a 32-bit hashes; Construct with a comparison and a hasher. }
uses
  SysUtils, Generics.Defaults, TestKit;

type
  TColour = (clRed, clGreen, clBlue);
  TPoint2 = record
    X, Y: Integer;
  end;
  TNamed = record
    Name: string;
  end;
  TInts = array of Integer;

function Sign(I: Integer): Integer;
begin
  if I < 0 then
    Result := -1
  else if I > 0 then
    Result := 1
  else
    Result := 0;
end;

function Ints(const A: array of Integer): TInts;
var
  I: Integer;
begin
  SetLength(Result, Length(A));
  for I := 0 to High(A) do
    Result[I] := A[I];
end;

function ByLength(const Left, Right: string): Integer;
begin
  Result := Length(Left) - Length(Right);
end;

function SameLength(const Left, Right: string): Boolean;
begin
  Result := Length(Left) = Length(Right);
end;

function LengthHash(const Value: string): Integer;
begin
  Result := Length(Value) * 10;
end;

function DefaultError: string;
begin
  Result := '';
  try
    TComparer<TNamed>.Default;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

function EqualityError: string;
begin
  Result := '';
  try
    TEqualityComparer<TNamed>.Default;
  except
    on E: Exception do
      Result := E.ClassName + ': ' + E.Message;
  end;
end;

var
  CI: IComparer<Integer>;
  CC: IComparer<Cardinal>;
  C64: IComparer<UInt64>;
  CE: IComparer<TColour>;
  CD: IComparer<Double>;
  CS: IComparer<string>;
  CO: IComparer<TObject>;
  CR: IComparer<TPoint2>;
  CA: IComparer<TInts>;
  EI: IEqualityComparer<Integer>;
  ED: IEqualityComparer<Double>;
  ES: IEqualityComparer<string>;
  ER: IEqualityComparer<TPoint2>;
  A, B: TObject;
  P, Q: TPoint2;
  X, Y: TInts;
  NaN, Zero: Double;
begin
  Section('ordinals (RTL-671)');
  CI := TComparer<Integer>.Default;
  CheckEqI(-1, Sign(CI.Compare(1, 2)), 'Integer less');
  CheckEqI(1, Sign(CI.Compare(2, -5)), 'Integer greater');
  CheckEqI(0, CI.Compare(7, 7), 'Integer equal');
  CheckEqI(-1, Sign(CI.Compare(Low(Integer), High(Integer))), 'Integer extremes');
  CC := TComparer<Cardinal>.Default;
  CheckEqI(1, Sign(CC.Compare($FFFFFFFF, 1)), 'Cardinal unsigned');
  C64 := TComparer<UInt64>.Default;
  CheckEqI(1, Sign(C64.Compare(UInt64($FFFFFFFFFFFFFFFF), 1)), 'UInt64 unsigned');
  CE := TComparer<TColour>.Default;
  CheckEqI(-1, Sign(CE.Compare(clRed, clBlue)), 'enumeration by ordinal');

  Section('floats');
  NaN := 0;
  NaN := NaN / NaN;
  Zero := 0;
  CD := TComparer<Double>.Default;
  CheckEqI(-1, Sign(CD.Compare(1.5, 2.5)), 'Double less');
  CheckEqI(-1, Sign(CD.Compare(NaN, -1E300)), 'NaN sorts first');
  CheckEqI(1, Sign(CD.Compare(-1E300, NaN)), 'NaN sorts first (right)');
  CheckEqI(0, CD.Compare(NaN, NaN), 'NaN compares equal to NaN');
  ED := TEqualityComparer<Double>.Default;
  CheckEqB(True, ED.Equals(NaN, NaN), 'NaN = NaN for equality');
  CheckEqB(True, ED.Equals(Zero, -Zero), '-0 = 0');
  CheckEqI(ED.GetHashCode(Zero), ED.GetHashCode(-Zero), '-0 hashes like 0');
  CheckEqI(-1679724187, ED.GetHashCode(0), 'hash of 0.0 = FNV-1a of its 8 bytes');

  Section('strings');
  CS := TComparer<string>.Default;
  CheckEqI(-1, Sign(CS.Compare('B', 'a')), 'ordinal (B before a)');
  CheckEqI(-1, Sign(CS.Compare('ab', 'abc')), 'prefix first');
  CheckEqI(0, CS.Compare('x', 'x'), 'equal');
  ES := TEqualityComparer<string>.Default;
  CheckEqI(Integer($811C9DC5), ES.GetHashCode(''), 'hash of '''' = $811C9DC5');
  CheckEqI(723832900, ES.GetHashCode('a'), 'hash over the UTF-16 bytes');
  CheckEqI(-1373726339, ES.GetHashCode('abc'), 'hash of abc');
  CheckEqB(False, ES.Equals('a', 'A'), 'case-sensitive');

  Section('classes, records, dynamic arrays');
  A := TObject.Create;
  B := TObject.Create;
  CO := TComparer<TObject>.Default;
  if NativeUInt(Pointer(A)) < NativeUInt(Pointer(B)) then
    CheckEqI(-1, Sign(CO.Compare(A, B)), 'objects by address')
  else
    CheckEqI(1, Sign(CO.Compare(A, B)), 'objects by address');
  CheckEqI(0, CO.Compare(A, A), 'same object');
  A.Free;
  B.Free;
  P.X := 1;
  P.Y := 2;
  Q := P;
  CR := TComparer<TPoint2>.Default;
  CheckEqI(0, CR.Compare(P, Q), 'equal records by bytes');
  Q.Y := 3;
  Check(CR.Compare(P, Q) <> 0, 'different records');
  ER := TEqualityComparer<TPoint2>.Default;
  CheckEqB(False, ER.Equals(P, Q), 'record equality');
  CheckEqS('ENotSupportedException: No default comparer for type TNamed', DefaultError,
    'record with a managed field (comparer)');
  CheckEqS('ENotSupportedException: No default comparer for type TNamed', EqualityError,
    'record with a managed field (equality comparer)');
  X := Ints([5, 6]);
  Y := Ints([1, 2, 3]);
  CA := TComparer<TInts>.Default;
  CheckEqI(-1, Sign(CA.Compare(X, Y)), 'dynamic arrays by length first');
  Y := Ints([5, 7]);
  Check(CA.Compare(X, Y) <> 0, 'then by bytes');
  Y := Ints([5, 6]);
  CheckEqI(0, CA.Compare(X, Y), 'equal contents');

  Section('hashes');
  EI := TEqualityComparer<Integer>.Default;
  CheckEqI(1268118805, EI.GetHashCode(0), 'Integer 0');
  CheckEqI(-76958204, EI.GetHashCode(1), 'Integer 1');
  CheckEqB(True, EI.Equals(3, 3), 'Integer equality');

  Section('Construct');
  CS := TComparer<string>.Construct(ByLength);
  CheckEqI(-1, Sign(CS.Compare('zz', 'aaa')), 'comparison function used');
  ES := TEqualityComparer<string>.Construct(SameLength, LengthHash);
  CheckEqB(True, ES.Equals('ab', 'xy'), 'equality function used');
  CheckEqI(30, ES.GetHashCode('abc'), 'hasher used');
  Done;
end.
