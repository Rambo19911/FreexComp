// covers: LANG-712, LANG-713
program GenericDefault;

type
  TColor = (Red, Green, Blue);
  TPoint = record
    X, Y: Integer;
    Name: string;
  end;

  TDefaults<T> = class
    class function Get: T;
  end;

class function TDefaults<T>.Get: T;
begin
  Result := Default(T);
end;

var
  P: TPoint;
  Obj: TObject;
  S: string;
  D: Double;
  C: TColor;
  Ch: Char;
begin
  Writeln(TDefaults<Integer>.Get);
  Writeln(TDefaults<Boolean>.Get);
  D := TDefaults<Double>.Get;
  Writeln(D:0:1);
  S := TDefaults<string>.Get;
  Writeln('[', S, ']', Length(S));
  Obj := TDefaults<TObject>.Get;
  Writeln(Obj = nil);
  P.X := 5;
  P.Y := 6;
  P.Name := 'p';
  P := TDefaults<TPoint>.Get;
  Writeln(P.X, ' ', P.Y, ' [', P.Name, ']');
  C := TDefaults<TColor>.Get;
  Writeln(Ord(C));
  Ch := Default(Char);
  Writeln(Ord(Ch));
  P.X := 3;
  P := Default(TPoint);
  Writeln(P.X + P.Y);
  Writeln(Default(Integer) + 1);
end.