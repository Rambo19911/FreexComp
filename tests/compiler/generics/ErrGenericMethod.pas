// covers: LANG-978, LANG-979, LANG-980, LANG-981
program ErrGenericMethod;

type
  TIntFunc = function(X: Integer): Integer;

  TBase = class
    procedure O(const X: Integer); virtual;
  end;

  // what a generic method cannot be: reported where it is declared, although nothing calls these methods
  TShape = class(TBase)
  public
    procedure V<T>(const X: T); virtual;
    procedure D<T>(const X: T); dynamic;
    procedure A<T>(const X: T); virtual; abstract;
    procedure O<T>(const X: T); override;
    procedure M<T>(var Msg: T); message 1;
    constructor Create<T>(const X: T);
  published
    procedure P<T>(const X: T);
  end;

  IShape = interface
    procedure I<T>(const X: T);
  end;

  TUtil = class
    class function Make<T>: T; static;
    class function Pick<T>(const A, B: T): T; static;
    class function Twice<T>(X: T): T; static;
    class procedure Keep<T: class>(const X: T); static;
    class procedure Swap<T>(var A, B: T); static;
  end;

  TOut = class
    class procedure Show(I: Integer); overload; static;
    class procedure Show<T>(const V: T); overload; static;
  end;

  // one name, one overload set: the non-generic method needs the directive too
  TLate = class
    class procedure Show(I: Integer); static;
    class procedure Show<T>(const V: T); overload; static;
  end;

  // declared, never implemented: reported where it is declared like a non-generic method, once, whatever calls it
  TMissing = class
    class function Lost<T>(X: T): T; static;
    procedure Gone<T>(const X: T);
  end;

procedure TBase.O(const X: Integer);
begin
end;

procedure TShape.V<T>(const X: T);
begin
end;

procedure TShape.D<T>(const X: T);
begin
end;

procedure TShape.O<T>(const X: T);
begin
end;

procedure TShape.M<T>(var Msg: T);
begin
end;

constructor TShape.Create<T>(const X: T);
begin
end;

procedure TShape.P<T>(const X: T);
begin
end;

class function TUtil.Make<T>: T;
begin
  Result := Default(T);
end;

class function TUtil.Pick<T>(const A, B: T): T;
begin
  Result := A;
end;

class function TUtil.Twice<T>(X: T): T;
begin
  Result := X;
end;

class procedure TUtil.Keep<T>(const X: T);
begin
end;

class procedure TUtil.Swap<T>(var A, B: T);
begin
end;

class procedure TOut.Show(I: Integer);
begin
end;

class procedure TOut.Show<T>(const V: T);
begin
end;

class procedure TLate.Show<T>(const V: T);
begin
end;

class procedure TLate.Show(I: Integer);
begin
end;

var
  I: Integer;
  F: TIntFunc;
begin
  I := TUtil.Make;
  TUtil.Pick(1, 'a');
  TOut.Show(TObject.Create, 1);
  F := TUtil.Twice;
  TUtil.Keep(5);
  TUtil.Swap(I, Undeclared);
  I := TMissing.Lost(1) + TMissing.Lost<Integer>(2);
end.
