// covers: LANG-731, LANG-732, LANG-740
program OperatorImplicitExplicit;

type
  TMoney = record
    Cents: Int64;
    class operator Implicit(Value: Integer): TMoney;
    class operator Implicit(const M: TMoney): Double;
    class operator Explicit(const M: TMoney): Integer;
    class operator Explicit(Value: Double): TMoney;
    class operator Add(const A, B: TMoney): TMoney;
  end;

class operator TMoney.Implicit(Value: Integer): TMoney;
begin
  Result.Cents := Int64(Value) * 100;
end;

class operator TMoney.Implicit(const M: TMoney): Double;
begin
  Result := M.Cents / 100;
end;

class operator TMoney.Explicit(const M: TMoney): Integer;
begin
  Result := M.Cents div 100;
end;

class operator TMoney.Explicit(Value: Double): TMoney;
begin
  Result.Cents := Round(Value * 100);
end;

class operator TMoney.Add(const A, B: TMoney): TMoney;
begin
  Result.Cents := A.Cents + B.Cents;
end;

procedure Log(D: Double); overload;
begin
  Writeln('double ', D:0:2);
end;

procedure Log(const M: TMoney); overload;
begin
  Writeln('money ', M.Cents);
end;

procedure Pay(const M: TMoney); overload;
begin
  Writeln('pay ', M.Cents);
end;

procedure Pay(const S: string); overload;
begin
  Writeln('pay text ', S);
end;

var
  M, N: TMoney;
  D: Double;
  I: Integer;
begin
  M := 12;
  Writeln(M.Cents);
  N := M + 3;
  Writeln(N.Cents);
  D := N;
  Writeln(D:0:2);
  N := TMoney(2.75);
  Writeln(N.Cents);
  I := Integer(N);
  Writeln(I);
  D := N + 0.5;
  Writeln(D:0:2);
  Log(7);
  Log(M);
  Pay(5);
  Pay('five');
end.
