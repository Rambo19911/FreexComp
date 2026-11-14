// covers: LANG-621, LANG-604, LANG-375
program ClassSelfAndWithFields;

type
  TPoint = class
    X, Y: Integer;
    procedure Move(X: Integer; DY: Integer);
    function Sum: Integer;
    function Same(Other: TPoint): Boolean;
    procedure Show;
  end;

  TPoint3 = class(TPoint)
    Z: Integer;
    procedure Show;
    procedure Bump;
  end;

{ the parameter X hides the field X; Self.X reaches the field }
procedure TPoint.Move(X: Integer; DY: Integer);
begin
  Self.X := Self.X + X;
  Y := Y + DY;
end;

function TPoint.Sum: Integer;
begin
  Result := X + Self.Y;
end;

function TPoint.Same(Other: TPoint): Boolean;
begin
  Result := Other = Self;
end;

procedure TPoint.Show;
begin
  Writeln('(', X, ',', Y, ')');
end;

procedure TPoint3.Show;
begin
  Writeln('(', X, ',', Y, ',', Z, ')');
end;

procedure TPoint3.Bump;

  procedure Twice;
  begin
    Inc(Z);
    Move(1, 1);
  end;

begin
  Twice;
  Twice;
end;

var
  P: TPoint;
  Q: TPoint3;
begin
  P := TPoint.Create;
  P.Move(3, 4);
  P.Show;
  Writeln(P.Sum, ' ', P.Same(P));
  Q := TPoint3.Create;
  Q.Bump;
  Q.Show;
  Writeln(Q.Sum, ' ', Q.Same(P), ' ', P.Same(Q));
  with Q do
  begin
    X := 10;
    Move(5, 1);
    Writeln(Sum, ' ', Z, ' ', Same(Q));
  end;
  with P, Q do
    Writeln(X, ' ', Y);
  P.Free;
  P := Q;
  P.Show;
  Q.Free;
end.
