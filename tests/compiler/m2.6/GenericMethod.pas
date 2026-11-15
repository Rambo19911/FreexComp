// covers: LANG-701, LANG-714
program GenericMethod;

type
  TUtil = class
    class function Max<T>(const A, B: T): T; static;
    class procedure Show<A, B>(const X: A; const Y: B); static;
    class function Zero<T>: T; static;
  end;

  TBox = class
    FCount: Integer;
    function Add<T>(const V: T): Integer;
  end;

  TCalc = record
    Base: Integer;
    function Plus<T>(const V: T): T;
  end;

class function TUtil.Max<T>(const A, B: T): T;
begin
  if A > B then
    Result := A
  else
    Result := B;
end;

class procedure TUtil.Show<A, B>(const X: A; const Y: B);
begin
  Writeln(X, ' / ', Y);
end;

class function TUtil.Zero<T>: T;
begin
  Result := Default(T);
end;

function TBox.Add<T>(const V: T): Integer;
begin
  Inc(FCount);
  Result := FCount;
end;

function TCalc.Plus<T>(const V: T): T;
begin
  Result := V + Base;
end;

var
  B: TBox;
  C: TCalc;
begin
  Writeln(TUtil.Max<Integer>(3, 7));
  Writeln(TUtil.Max(10, 4));
  Writeln(TUtil.Max('abc', 'abd'));
  Writeln(TUtil.Max<Double>(1.5, 2):0:1);
  TUtil.Show<string, Integer>('x', 1);
  TUtil.Show(True, 'y');
  Writeln(TUtil.Zero<Integer>() + 5);
  B := TBox.Create;
  B.Add(1);
  B.Add('s');
  Writeln(B.Add<Boolean>(False));
  B.Free;
  C.Base := 10;
  Writeln(C.Plus(5), ' ', C.Plus<Double>(0.5):0:1);
end.