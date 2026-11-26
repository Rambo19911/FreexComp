// covers: LANG-779, LANG-780, LANG-781, LANG-782, LANG-783, LANG-784
program ErrGenericNames;

type
  TOuter = class
  public
    type
      TBox<T> = class
        V: T;
      end;
  end;

  TPair<A, B> = record
    Lo: A;
    Hi: B;
  end;

  TBox<T> = class
  end;

  TNode<T: class> = class;
  TList<T> = class;
  TDerived = class(TList<Integer>)
  end;
  TNode<T> = class
  end;
  TList<T> = class
  end;

procedure Local;
type
  TCell<T> = record
    Item: T;
  end;
begin
end;

var
  X: TBox;
  Y: TOuter.TBox<Integer, Integer>;
  Z: TOuter.TMissing<Integer>;
  O: TObject;
begin
  Local;
  WriteLn(O is TPair<Integer, Integer>);
end.
