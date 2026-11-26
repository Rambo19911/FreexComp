// covers: LANG-778, LANG-780
program GenericNestedInType;

type
  TOuter = class
  public
    type
      TBox = class
      public
        function Describe: string;
      end;

      TBox<T> = class
      public
        Value: T;
        constructor Create(const AValue: T);
        function Describe: string;
        class function Make(const AValue: T): TBox<T>;
      end;

      TBox<A, B> = class
      public
        First: A;
        Second: B;
        function Describe: string;
      end;
  public
    function Wrap(N: Integer): TBox<Integer>;
  end;

  TR = record
    Tag: Integer;
    type
      TCell<T> = record
        Item: T;
        function Show: string;
      end;
  end;

  TA<T> = class
  public
    type
      TB<U> = class
      public
        Left: T;
        Right: U;
        function Pair: string;
      end;
  end;

function TOuter.TBox.Describe: string;
begin
  Result := 'plain box';
end;

constructor TOuter.TBox<T>.Create(const AValue: T);
begin
  inherited Create;
  Value := AValue;
end;

function TOuter.TBox<T>.Describe: string;
begin
  Result := 'box of ' + ClassName;
end;

class function TOuter.TBox<T>.Make(const AValue: T): TBox<T>;
begin
  Result := TBox<T>.Create(AValue);
end;

function TOuter.TBox<A, B>.Describe: string;
begin
  Result := 'pair box ' + ClassName;
end;

function TOuter.Wrap(N: Integer): TBox<Integer>;
begin
  Result := TBox<Integer>.Create(N);
end;

function TR.TCell<T>.Show: string;
begin
  Result := 'cell';
end;

function TA<T>.TB<U>.Pair: string;
begin
  Result := ClassName;
end;

procedure Run;
var
  O: TOuter;
  P: TOuter.TBox;
  B: TOuter.TBox<Integer>;
  S: TOuter.TBox<string>;
  Two: TOuter.TBox<Integer, string>;
  C: TR.TCell<Integer>;
  AB: TA<Integer>.TB<string>;
  Other: TA<Byte>.TB<string>;
  C1, C2: TClass;
begin
  O := TOuter.Create;
  P := TOuter.TBox.Create;
  WriteLn(P.Describe);
  P.Free;
  B := O.Wrap(7);
  WriteLn(B.Describe, ' ', B.Value);
  C1 := B.ClassType;
  B.Free;
  B := TOuter.TBox<Integer>.Make(8);
  WriteLn(B.Value);
  C2 := TOuter.TBox<Integer>;
  WriteLn(C1 = C2, ' ', C1 = B.ClassType);
  B.Free;
  S := TOuter.TBox<string>.Create('text');
  WriteLn(S.Value, ' ', S.Describe);
  WriteLn(TClass(TOuter.TBox<string>) <> TClass(TOuter.TBox<Integer>));
  S.Free;
  Two := TOuter.TBox<Integer, string>.Create;
  Two.First := 1;
  Two.Second := 'two';
  WriteLn(Two.Describe, ' ', Two.First, ' ', Two.Second);
  Two.Free;
  C.Item := 5;
  WriteLn(C.Show, ' ', C.Item, ' ', SizeOf(TR.TCell<Int64>));
  AB := TA<Integer>.TB<string>.Create;
  AB.Left := 3;
  AB.Right := 'three';
  WriteLn(AB.Pair, ' ', AB.Left, ' ', AB.Right);
  Other := TA<Byte>.TB<string>.Create;
  WriteLn(TClass(TA<Integer>.TB<string>) <> TClass(TA<Byte>.TB<string>));
  WriteLn(TClass(TA<Integer>.TB<string>) = AB.ClassType);
  WriteLn(TypeInfo(TOuter.TBox<Integer>) = TypeInfo(TOuter.TBox<Integer>));
  WriteLn(TypeInfo(TA<Integer>.TB<string>) <> TypeInfo(TA<Byte>.TB<string>));
  Other.Free;
  AB.Free;
  O.Free;
end;

var
  Live: NativeInt;
begin
  Live := _LiveAllocCount;
  Run;
  WriteLn('leaks ', _LiveAllocCount - Live);
end.
