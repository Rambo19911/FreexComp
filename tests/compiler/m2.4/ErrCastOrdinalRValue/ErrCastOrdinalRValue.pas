// covers: LANG-290, C-261
program ErrCastOrdinalRValue;

type
  TFoo = class
  end;
  IThing = interface
  end;
  TPair = record
    A, B: Integer;
  end;

function Real8: Double;
begin
  Result := 1.5;
end;

function Ident(V: NativeInt): NativeInt;
begin
  Result := V;
end;

var
  O: TFoo;
  I: IThing;
  R: TPair;
begin
  O := TFoo(Real8);
  O := TFoo(Real8 + 1.0);
  I := IThing(Ident(0));
  R := TPair(Ident(0) + 0);
end.
