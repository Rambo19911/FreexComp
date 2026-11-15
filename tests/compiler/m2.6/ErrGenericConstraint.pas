// covers: LANG-702, LANG-703, LANG-711, LANG-712
program ErrGenericConstraint;

type
  TBase = class
  end;

  TRec = record
    X: Integer;
  end;

  TObjList<T: class> = class
    Item: T;
  end;

  TValue<T: record> = record
    Item: T;
  end;

  TSorter<T> = class
    class function Less(const A, B: T): Boolean;
  end;

class function TSorter<T>.Less(const A, B: T): Boolean;
begin
  Result := A < B;
end;

var
  A: TObjList<Integer>;
  B: TValue<TBase>;
  C: TObjList<TBase, TBase>;
  D: Boolean;
  R: TRec;
begin
  D := TSorter<Integer>.Less(1, 2);
  D := TSorter<TRec>.Less(R, R);
end.