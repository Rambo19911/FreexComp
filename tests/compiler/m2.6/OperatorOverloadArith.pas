// covers: LANG-731, LANG-732
program OperatorOverloadArith;

type
  TVec = record
    X, Y: Integer;
    constructor Create(AX, AY: Integer);
    class operator Add(const A, B: TVec): TVec;
    class operator Subtract(const A, B: TVec): TVec;
    class operator Multiply(const A: TVec; K: Integer): TVec;
    class operator Multiply(K: Integer; const A: TVec): TVec;
    class operator Negative(const A: TVec): TVec;
    class operator Equal(const A, B: TVec): Boolean;
    class operator Inc(const A: TVec): TVec;
    procedure Print;
  end;

constructor TVec.Create(AX, AY: Integer);
begin
  X := AX;
  Y := AY;
end;

class operator TVec.Add(const A, B: TVec): TVec;
begin
  Result := TVec.Create(A.X + B.X, A.Y + B.Y);
end;

class operator TVec.Subtract(const A, B: TVec): TVec;
begin
  Result.X := A.X - B.X;
  Result.Y := A.Y - B.Y;
end;

class operator TVec.Multiply(const A: TVec; K: Integer): TVec;
begin
  Result := TVec.Create(A.X * K, A.Y * K);
end;

class operator TVec.Multiply(K: Integer; const A: TVec): TVec;
begin
  Result := A * K;
end;

class operator TVec.Negative(const A: TVec): TVec;
begin
  Result := TVec.Create(-A.X, -A.Y);
end;

class operator TVec.Equal(const A, B: TVec): Boolean;
begin
  Result := (A.X = B.X) and (A.Y = B.Y);
end;

class operator TVec.Inc(const A: TVec): TVec;
begin
  Result := TVec.Create(A.X + 1, A.Y + 1);
end;

procedure TVec.Print;
begin
  Writeln('(', X, ', ', Y, ')');
end;

var
  A, B, C: TVec;
begin
  A := TVec.Create(1, 2);
  B := TVec.Create(10, 20);
  C := A + B;
  C.Print;
  C := B - A;
  C.Print;
  C := A * 3;
  C.Print;
  C := 2 * B;
  C.Print;
  C := -A;
  C.Print;
  C := A + B * 2 - A;
  C.Print;
  Inc(C);
  C.Print;
  Writeln(A = B, ' ', A = TVec.Create(1, 2), ' ', A <> B, ' ', A <> TVec.Create(1, 2));
  (A + B).Print;
  if -A = TVec.Create(-1, -2) then
    Writeln('negated');
end.
