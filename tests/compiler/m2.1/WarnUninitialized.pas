// covers: LANG-390
program WarnUninitialized;

function NoResult(N: Integer): Integer;
begin
  if N > 0 then
    Result := N;
end;

procedure UseBeforeSet;
var
  X, Y: Integer;
begin
  Y := X + 1;
  Writeln(Y, ' ', X);
end;

procedure MaybeSet(Flag: Boolean);
var
  Z: Integer;
begin
  if Flag then
    Z := 1;
  Writeln(Z);
end;

procedure Fine(Flag: Boolean);
var
  A: Integer;
begin
  if Flag then
    A := 2
  else
    A := 3;
  Writeln(A);
end;

begin
  Writeln(NoResult(1));
  MaybeSet(True);
  Fine(False);
end.
