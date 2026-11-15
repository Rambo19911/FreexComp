// covers: LANG-702
program GenericConstraints;

type
  IGreeter = interface
    function Greet: string;
  end;

  TBase = class
    function Name: string; virtual;
  end;

  TChild = class(TBase)
    function Name: string; override;
  end;

  TGreeter = class(TInterfacedObject, IGreeter)
    function Greet: string;
  end;

  TFactory<T: class, constructor> = class
    class function Make: T;
  end;

  TNamer<T: TBase> = class
    class function NameOf(const Item: T): string;
  end;

  TGreeting<T: IGreeter> = class
    class function Run(const G: T): string;
  end;

  TSum<T: record> = class
    class function Add(const A, B: T): T;
  end;

function TBase.Name: string;
begin
  Result := 'base';
end;

function TChild.Name: string;
begin
  Result := 'child';
end;

function TGreeter.Greet: string;
begin
  Result := 'hello';
end;

class function TFactory<T>.Make: T;
begin
  Result := T.Create;
end;

class function TNamer<T>.NameOf(const Item: T): string;
begin
  Result := Item.Name;
end;

class function TGreeting<T>.Run(const G: T): string;
begin
  Result := G.Greet + '!';
end;

class function TSum<T>.Add(const A, B: T): T;
begin
  Result := A + B;
end;

var
  B: TBase;
  C: TChild;
  G: IGreeter;
begin
  B := TFactory<TBase>.Make;
  Writeln(B.Name);
  B.Free;
  C := TFactory<TChild>.Make;
  Writeln(TNamer<TChild>.NameOf(C), ' ', TNamer<TBase>.NameOf(C));
  C.Free;
  G := TGreeter.Create;
  Writeln(TGreeting<IGreeter>.Run(G));
  Writeln(TSum<Integer>.Add(2, 3), ' ', TSum<Double>.Add(0.25, 0.5):0:2);
end.