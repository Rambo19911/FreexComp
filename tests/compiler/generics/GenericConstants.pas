// covers: LANG-780, LANG-784
program GenericConstants;

type
  TR<T> = record
    A: T;
    B: Byte;
  end;

  TK<T> = class
  public
    const X = 7;
    type
      TEn = (eA, eB, eC);
      TSub = 2..5;
  end;

  TD = class(TK<Integer>)
  end;

  TOuter = class
  public
    type
      TInner<T> = record
        V: T;
        W: T;
      end;
  end;

  TDerivedOuter = class(TOuter)
  end;

  // a constant of a generic body naming an instance of its parameter: folded per instance
  TG<T> = class
  public
    const S = SizeOf(TR<T>);
    type
      TBytes = array[0..SizeOf(TR<T>) - 1] of Byte;
  end;

  TSized = array[0..SizeOf(TR<Word>) - 1] of Byte;
  TByEnum = array[Low(TK<Byte>.TEn)..High(GenericConstants.TK<Byte>.TEn)] of string;
  TBySub = array[TD.TSub] of Integer;

const
  SizeR = SizeOf(TR<Int64>);
  SizeQ = SizeOf(GenericConstants.TR<Byte>);
  KX = TK<Integer>.X;
  KXQ = GenericConstants.TK<string>.X;
  DX = TD.X;
  HighEn = Ord(High(TK<Integer>.TEn));
  LowSub = Low(TK<Byte>.TSub);
  HighSub = High(TD.TSub);
  SizeInner = SizeOf(TOuter.TInner<Word>);
  SizeInnerQ = SizeOf(GenericConstants.TOuter.TInner<Int64>);
  SizeInnerD = SizeOf(TDerivedOuter.TInner<Byte>);
  Mixed = TK<Integer>.X * 2 + SizeOf(TR<Byte>);
  TypedSize: Integer = SizeOf(TR<Int64>) + TK<Byte>.X;
  LastEn: TK<Integer>.TEn = High(TK<Integer>.TEn);

function Twice(N: Integer = SizeOf(TOuter.TInner<Integer>) + TD.X): Integer;
begin
  Result := N * 2;
end;

function Classify(N: Integer): string;
begin
  case N of
    SizeOf(TR<Byte>): Result := 'size of TR<Byte>';
    TK<Integer>.X: Result := 'TK<Integer>.X';
    High(TK<Byte>.TSub) - 2..High(TD.TSub): Result := 'in TSub upper part';
  else
    Result := 'other';
  end;
end;

procedure Check(const Name: string; Constant, Statement: Integer);
begin
  if Constant = Statement then
    Writeln(Name, ' = ', Constant)
  else
    Writeln(Name, ' MISMATCH ', Constant, ' <> ', Statement);
end;

var
  S: TSized;
  E: TByEnum;
  B: TBySub;
  I: TK<Byte>.TEn;
  GB: TG<Byte>.TBytes;
  GL: TG<Int64>.TBytes;
begin
  Check('SizeR', SizeR, SizeOf(TR<Int64>));
  Check('SizeQ', SizeQ, SizeOf(GenericConstants.TR<Byte>));
  Check('KX', KX, TK<Integer>.X);
  Check('KXQ', KXQ, GenericConstants.TK<string>.X);
  Check('DX', DX, TD.X);
  Check('HighEn', HighEn, Ord(High(TK<Integer>.TEn)));
  Check('LowSub', LowSub, Low(TK<Byte>.TSub));
  Check('HighSub', HighSub, High(TD.TSub));
  Check('SizeInner', SizeInner, SizeOf(TOuter.TInner<Word>));
  Check('SizeInnerQ', SizeInnerQ, SizeOf(GenericConstants.TOuter.TInner<Int64>));
  Check('SizeInnerD', SizeInnerD, SizeOf(TDerivedOuter.TInner<Byte>));
  Check('Mixed', Mixed, TK<Integer>.X * 2 + SizeOf(TR<Byte>));
  Check('TypedSize', TypedSize, SizeOf(TR<Int64>) + TK<Byte>.X);
  Check('LastEn', Ord(LastEn), Ord(High(TK<Integer>.TEn)));
  Check('Length(TSized)', Length(S), SizeOf(TR<Word>));
  Check('Length(TByEnum)', Length(E), Ord(High(TK<Byte>.TEn)) + 1);
  Check('Low(TBySub)', Low(B), Low(TD.TSub));
  Check('Length(TBySub)', Length(B), High(TD.TSub) - Low(TD.TSub) + 1);
  Check('Twice', Twice, (SizeOf(TOuter.TInner<Integer>) + TD.X) * 2);
  Check('TG<Byte>.S', TG<Byte>.S, SizeOf(TR<Byte>));
  Check('TG<Int64>.S', TG<Int64>.S, SizeOf(TR<Int64>));
  Check('Length(TG<Byte>.TBytes)', Length(GB), SizeOf(TR<Byte>));
  Check('Length(TG<Int64>.TBytes)', Length(GL), SizeOf(TR<Int64>));
  for I := Low(TByEnum) to High(TByEnum) do
    E[I] := 'e' + Chr(Ord('A') + Ord(I));
  for I := Low(TK<Byte>.TEn) to High(GenericConstants.TK<Byte>.TEn) do
    Write(E[I]);
  Writeln;
  Writeln(Classify(2));
  Writeln(Classify(7));
  Writeln(Classify(3));
  Writeln(Classify(5));
  Writeln(Classify(9));
end.
