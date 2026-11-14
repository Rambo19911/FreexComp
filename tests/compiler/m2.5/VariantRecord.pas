// covers: LANG-261, ABI-061
program VariantRecord;

type
  TKind = (kInt, kReal, kText);
  TValue = record
    Name: string;
    case Kind: TKind of
      kInt: (I: Integer);
      kReal: (R: Double);
      kText: (C: array[0..3] of AnsiChar; Len: Byte);
  end;
  TOverlay = record
    case Integer of
      0: (B0, B1, B2, B3: Byte);
      1: (W0, W1: Word);
      2: (L: Cardinal);
  end;
  TNested = record
    Tag: Byte;
    case Byte of
      0: (A: Word;
          case Inner: Byte of
            0: (X: Int64);
            1: (Y: Byte));
      1: (Z: Integer);
  end;
  TVar = record
    case VType: Byte of
      0: (VInteger: Integer);
      1: (VPointer: Pointer);
  end;

procedure Show(const V: TValue);
begin
  case V.Kind of
    kInt: Writeln(V.Name, ' int ', V.I);
    kReal: Writeln(V.Name, ' real ', V.R:0:2);
    kText: Writeln(V.Name, ' text ', V.C[0], ' ', V.Len);
  end;
end;

const
  KO: TOverlay = (W0: $0201; W1: $0403);
  KN: TNested = (Tag: 2; A: 300; Inner: 1; Y: 7);

var
  V: TValue;
  O: TOverlay;
  N: TNested;
  T: TVar;
  P: ^TValue;
begin
  Writeln(SizeOf(TValue), ' ', SizeOf(TOverlay), ' ', SizeOf(TNested), ' ', SizeOf(TVar));
  V.Name := 'a';
  V.Kind := kInt;
  V.I := 42;
  Show(V);
  V.Kind := kReal;
  V.R := 2.5;
  Show(V);
  V.Kind := kText;
  V.C[0] := 'x';
  V.Len := 3;
  Show(V);
  O.L := $04030201;
  Writeln(O.B0, ' ', O.B1, ' ', O.B2, ' ', O.B3, ' ', O.W0, ' ', O.W1);
  O.W1 := 0;
  Writeln(O.L);
  N.Tag := 1;
  N.A := 7;
  N.Inner := 0;
  N.X := 0;
  N.Y := 9;
  Writeln(N.A, ' ', N.X, ' ', N.Z);
  T.VType := 1;
  T.VPointer := nil;
  T.VInteger := -1;
  Writeln(T.VInteger, ' ', NativeInt(T.VPointer));
  New(P);
  P^.Name := 'heap';
  P^.Kind := kInt;
  P^.I := 5;
  Show(P^);
  Dispose(P);
  Writeln(KO.L, ' ', KO.B3, ' ', KN.Tag, ' ', KN.A, ' ', KN.Inner, ' ', KN.X);
end.
