// covers: LANG-982, LANG-983, LANG-984
program ErrGenericConstraintKinds;

type
  TBase = class
  end;

  TChild = class(TBase)
  end;

  TOther = class
  end;

  // its only Create takes an argument and has no overload: it hides the parameterless one of TObject
  TNoDefault = class
    constructor Create(A: Integer);
  end;

  // its parameterless Create is private: only the class itself constructs
  THidden = class
  private
    constructor Create;
  public
    class function Make: THidden;
  end;

  IGreeter = interface
    function Greet: string;
  end;

  IOther = interface
    procedure Other;
  end;

  TGreeter = class(TInterfacedObject, IGreeter)
    function Greet: string;
  end;

  TRec = record
    X: Integer;
  end;

  PRec = ^TRec;

  TFactory<T: constructor> = class
    class function Make: T;
  end;

  TNamer<T: TBase> = class
    Item: T;
  end;

  TStrict<T: TChild> = class
    Item: T;
  end;

  TGreeting<T: IGreeter> = class
    Item: T;
  end;

  TValue<T: record> = record
    Item: T;
  end;

  TObjList<T: class> = class
    Item: T;
  end;

  TBoth<T: TBase, IGreeter> = class
    Item: T;
  end;

  TMake<T: class, constructor> = class
    Item: T;
  end;

  TPairOf<A, B: class> = class
    First: A;
    Second: B;
  end;

  TMap<K: record; V: class> = class
    Key: K;
    Value: V;
  end;

  TUtil = class
    class procedure Keep<T: class>(const X: T); static;
  end;

  // constraint lists that nothing can satisfy, reported at the declaration (row 15)
  TBad1<T: class, record> = class
  end;

  TBad2<T: record, constructor> = class
  end;

  TBad3<T: record, TBase> = class
  end;

  TBad4<T: record, IGreeter> = class
  end;

  TBad5<T: TBase, TOther> = class
  end;

  // an item that cannot be combined with several earlier ones is reported once, at that item
  TBadMany<T: class, constructor, record> = class
  end;

  // a constraint type is a class or an interface type (row 16)
  TBad6<T: Integer> = class
  end;

  // a forward declaration and its completion agree in the constraints (row 17)
  TFwd<T: class> = class;

  TFwd<T> = class
  end;

constructor TNoDefault.Create(A: Integer);
begin
end;

constructor THidden.Create;
begin
end;

class function THidden.Make: THidden;
begin
  Result := Create;
end;

function TGreeter.Greet: string;
begin
  Result := 'hello';
end;

class function TFactory<T>.Make: T;
begin
  Result := T.Create;
end;

class procedure TUtil.Keep<T>(const X: T);
begin
end;

var
  // constructor: what the call X.Create resolves to must be a public constructor (rows 1-3)
  F1: TFactory<TNoDefault>;
  F2: TFactory<THidden>;
  F3: TFactory<Integer>;
  // a class type: the class itself or a descendant (rows 4, 5)
  N1: TNamer<TOther>;
  S1: TStrict<TBase>;
  // an interface type: an interface descending from it or a class implementing it (rows 6, 7)
  G1: TGreeting<TBase>;
  G2: TGreeting<IOther>;
  // record: no string, class, interface, dynamic array or pointer (row 8)
  V1: TValue<string>;
  V2: TValue<TBase>;
  V3: TValue<IGreeter>;
  V4: TValue<TArray<Integer>>;
  V5: TValue<PRec>;
  // class: class types only (row 9)
  L1: TObjList<TRec>;
  L2: TObjList<IGreeter>;
  // combined lists: one diagnostic per failed constraint, in the order class, record, constructor, types (rows 10, 11)
  B1: TBoth<TBase>;
  M1: TMake<Integer>;
  // shared and grouped lists: at the failing argument (rows 12, 13)
  P1: TPairOf<TBase, Integer>;
  D1: TMap<TBase, TBase>;
begin
  // a generic method: at the written argument; at the method name when inferred (row 14)
  TUtil.Keep<Integer>(5);
  TUtil.Keep(5);
end.
