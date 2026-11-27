// covers: LANG-576
program ErrForVar;

{ A for var variable exists only inside its loop: a use after a counted loop or after a for ... in loop is E2003. Inside
  the body it is read-only: assigning it, Inc, passing it as a var argument and taking its address are E2053. }

procedure Change(var N: Integer);
begin
  N := 0;
end;

procedure AfterTheLoop;
var
  Values: array of Integer;
begin
  for var I := 1 to 3 do
    WriteLn(I);
  WriteLn(I);
  for var X in Values do
    WriteLn(X);
  WriteLn(X);
end;

procedure ReadOnly;
var
  P: Pointer;
  Values: array of Integer;
begin
  for var I := 1 to 3 do
  begin
    I := 5;
    Inc(I);
    Change(I);
    P := @I;
  end;
  for var X in Values do
    X := 1;
end;

begin
  AfterTheLoop;
  ReadOnly;
end.
