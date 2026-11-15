// covers: LANG-712, LANG-713
program DefaultRecordWithClassField;

type
  TNode = record
    Owner: TObject;
    Value: Integer;
    Next: Pointer;
  end;

  TPair = record
    Left, Right: TNode;
    Tag: TClass;
  end;

  TDefaults<T> = class
    class function Get: T;
  end;

class function TDefaults<T>.Get: T;
begin
  Result := Default(T);
end;

var
  N: TNode;
  P: TPair;
  O: TObject;
begin
  O := TObject.Create;
  N.Owner := O;
  N.Value := 7;
  N.Next := @N;
  N := Default(TNode);
  Writeln(N.Owner = nil, ' ', N.Value, ' ', N.Next = nil);
  N.Owner := O;
  N.Value := 8;
  N := TDefaults<TNode>.Get;
  Writeln(N.Owner = nil, ' ', N.Value);
  P.Left.Owner := O;
  P.Right.Value := 9;
  P.Tag := TObject;
  P := Default(TPair);
  Writeln(P.Left.Owner = nil, ' ', P.Right.Value, ' ', P.Tag = nil);
  P.Right.Value := 4;
  P := TDefaults<TPair>.Get;
  Writeln(P.Right.Value);
  Writeln(Default(TNode).Value + 1);
  O.Free;
end.
