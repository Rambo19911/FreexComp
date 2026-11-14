// covers: LANG-420, LANG-421
program ProcVarParams;

procedure Swap2(var A, B: Integer);
var
  T: Integer;
begin
  T := A;
  A := B;
  B := T;
end;

procedure Twice(var N: Integer);
begin
  N := N * 2;
end;

procedure SetTo(out N: Integer; V: Integer);
begin
  N := V;
end;

procedure DivMod(A, B: Integer; out Q, R: Integer);
begin
  Q := A div B;
  R := A mod B;
end;

procedure Next(var C: Char; var Done: Boolean);
begin
  C := Succ(C);
  Done := C >= 'c';
end;

procedure Chain(var X: Int64);
begin
  X := X + 1;
  if X < 5 then
    Chain(X);
end;

procedure Outer(var N: Integer);
begin
  Twice(N);
  Twice(N);
end;

var
  X, Y, Q, R: Integer;
  C: Char;
  Done: Boolean;
  Big: Int64;
begin
  X := 1;
  Y := 2;
  Swap2(X, Y);
  Writeln(X, ' ', Y);
  Twice(X);
  Writeln(X);
  SetTo(Y, 99);
  Writeln(Y);
  DivMod(17, 5, Q, R);
  Writeln(Q, ' ', R);
  C := 'a';
  repeat
    Next(C, Done);
    Write(C);
  until Done;
  Writeln;
  Big := 0;
  Chain(Big);
  Writeln(Big);
  Outer(X);
  Writeln(X);
  Swap2(X, X);
  Writeln(X);
end.
