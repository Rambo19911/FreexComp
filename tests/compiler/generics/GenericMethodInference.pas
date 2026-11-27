// covers: LANG-979, LANG-714, LANG-701
program GenericMethodInference;

type
  IComparer<T> = interface
    function Compare(const A, B: T): Integer;
  end;

  TComparison<T> = reference to function(const A, B: T): Integer;

  TIntComparer = class(TInterfacedObject, IComparer<Integer>)
    function Compare(const A, B: Integer): Integer;
  end;

  TBox<T> = class
    Value: T;
    Size: Integer;
  end;

  TNameBox = class(TBox<string>)
  end;

  TUtil = class
    class procedure Swap<T>(var A, B: T); static;
    class function Max<T>(const A, B: T; const Comparer: IComparer<T>): T; static;
    class function MaxBy<T>(const A, B: T; const Compare: TComparison<T>): T; static;
    class function IndexOf<T>(const Values: array of T; const Item: T): Integer; static;
    class function Sum<T>(const Values: array of T): T; static;
    class function First<T>(const Values: TArray<T>): T; static;
    class function Count<T>(const Box: TBox<T>): Integer; static;
    class procedure Fill<K, V>(const Key: K; out Value: V); static;
  end;

  TCounter = class
    Calls: Integer;
    procedure Exchange<T>(var A, B: T);
  end;

  TScale = record
    Steps: Integer;
    procedure Advance<T>(var Value: T; const Step: T);
  end;

function TIntComparer.Compare(const A, B: Integer): Integer;
begin
  Result := A - B;
end;

class procedure TUtil.Swap<T>(var A, B: T);
var
  Temp: T;
begin
  Temp := A;
  A := B;
  B := Temp;
end;

class function TUtil.Max<T>(const A, B: T; const Comparer: IComparer<T>): T;
begin
  if Comparer.Compare(A, B) >= 0 then
    Result := A
  else
    Result := B;
end;

class function TUtil.MaxBy<T>(const A, B: T; const Compare: TComparison<T>): T;
begin
  if Compare(A, B) >= 0 then
    Result := A
  else
    Result := B;
end;

class function TUtil.IndexOf<T>(const Values: array of T; const Item: T): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 0 to High(Values) do
    if Values[I] = Item then
    begin
      Result := I;
      Exit;
    end;
end;

class function TUtil.Sum<T>(const Values: array of T): T;
var
  I: Integer;
begin
  Result := Values[0];
  for I := 1 to High(Values) do
    Result := Result + Values[I];
end;

class function TUtil.First<T>(const Values: TArray<T>): T;
begin
  Result := Values[0];
end;

class function TUtil.Count<T>(const Box: TBox<T>): Integer;
begin
  Result := Box.Size * SizeOf(T);
end;

class procedure TUtil.Fill<K, V>(const Key: K; out Value: V);
begin
  Value := Default(V);
  Writeln('Fill: key of ', SizeOf(K), ', value of ', SizeOf(V));
end;

procedure TCounter.Exchange<T>(var A, B: T);
begin
  TUtil.Swap(A, B);
  Inc(Calls);
end;

procedure TScale.Advance<T>(var Value: T; const Step: T);
begin
  Value := Value + Step;
  Inc(Steps);
end;

var
  I, J, N: Integer;
  B: Byte;
  L: Int64;
  D: Double;
  S, U: string;
  Names: array[0..2] of string;
  Ints: array of Integer;
  Reals: TArray<Double>;
  Cmp: IComparer<Integer>;
  ByLength: TComparison<string>;
  IntBox: TBox<Integer>;
  ByteBox: TBox<Byte>;
  NameBox: TNameBox;
  Counter: TCounter;
  Scale: TScale;
begin
  { var parameters }
  I := 1;
  J := 2;
  TUtil.Swap<Integer>(I, J);
  Writeln('Swap<Integer>: ', I, ' ', J);
  TUtil.Swap(I, J);
  Writeln('Swap: ', I, ' ', J);
  S := 'left';
  U := 'right';
  TUtil.Swap(S, U);
  Writeln('Swap: ', S, ' ', U);

  { an instance of a generic interface: an interface variable, a class that implements it }
  Cmp := TIntComparer.Create;
  Writeln('Max<Integer>: ', TUtil.Max<Integer>(3, 7, Cmp));
  Writeln('Max: ', TUtil.Max(3, 7, Cmp));
  Writeln('Max: ', TUtil.Max(9, 4, TIntComparer.Create));

  { a method reference: an anonymous method takes no part in the inference, a variable does }
  Writeln('MaxBy<Integer>: ', TUtil.MaxBy<Integer>(3, 7, function(const A, B: Integer): Integer begin Result := A - B end));
  Writeln('MaxBy: ', TUtil.MaxBy(3, 7, function(const A, B: Integer): Integer begin Result := B - A end));
  ByLength := function(const A, B: string): Integer begin Result := Length(A) - Length(B) end;
  Writeln('MaxBy: ', TUtil.MaxBy(S, U, ByLength));

  { open arrays: a static array, a dynamic array, a bracket constructor, Slice }
  Names[0] := 'ann';
  Names[1] := 'bob';
  Names[2] := 'cy';
  Writeln('IndexOf<string>: ', TUtil.IndexOf<string>(Names, 'bob'));
  S := 'cy';
  Writeln('IndexOf: ', TUtil.IndexOf(Names, S));
  Ints := [4, 5, 6];
  Writeln('IndexOf: ', TUtil.IndexOf(Ints, 6));
  Writeln('IndexOf: ', TUtil.IndexOf([I, J, 9], 7));
  Writeln('Sum<Integer>: ', TUtil.Sum<Integer>([1, 2, 3]));
  Writeln('Sum: ', TUtil.Sum([1, 2, 3]));
  Writeln('Sum: ', TUtil.Sum(Ints));
  Writeln('Sum: ', TUtil.Sum(Slice(Ints, 2)));
  Writeln('Sum: ', TUtil.Sum(Names));

  { a dynamic array }
  Reals := [1.5, 2.5];
  Writeln('First<Double>: ', TUtil.First<Double>(Reals):0:1);
  Writeln('First: ', TUtil.First(Reals):0:1);

  { an instance of a generic class, also through a descendant }
  IntBox := TBox<Integer>.Create;
  IntBox.Size := 3;
  ByteBox := TBox<Byte>.Create;
  ByteBox.Size := 3;
  NameBox := TNameBox.Create;
  NameBox.Size := 0;
  Writeln('Count<Integer>: ', TUtil.Count<Integer>(IntBox));
  Writeln('Count: ', TUtil.Count(IntBox));
  Writeln('Count: ', TUtil.Count(ByteBox));
  Writeln('Count: ', TUtil.Count(NameBox));

  { two type parameters, one of them through an out parameter }
  B := 200;
  TUtil.Fill<Byte, Int64>(B, L);
  TUtil.Fill(B, L);
  TUtil.Fill(L, D);
  TUtil.Fill(1, B);
  Writeln('Filled: ', L, ' ', D:0:1, ' ', B);

  { an instance method and a record method }
  Counter := TCounter.Create;
  I := 10;
  J := 20;
  Counter.Exchange<Integer>(I, J);
  Counter.Exchange(I, J);
  Counter.Exchange(I, J);
  S := 'one';
  U := 'two';
  Counter.Exchange(S, U);
  Writeln('Exchange: ', I, ' ', J, ' ', S, ' ', U, ' in ', Counter.Calls, ' calls');
  Scale.Steps := 0;
  N := 1;
  Scale.Advance<Integer>(N, 2);
  Scale.Advance(N, 3);
  D := 0.5;
  Scale.Advance(D, 0.5);
  S := 'ab';
  Scale.Advance(S, U);
  Writeln('Advance: ', N, ' ', D:0:1, ' ', S, ' in ', Scale.Steps, ' steps');

  Counter.Free;
  NameBox.Free;
  ByteBox.Free;
  IntBox.Free;
end.
