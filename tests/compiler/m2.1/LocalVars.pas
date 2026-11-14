// covers: LANG-310, LANG-391, LANG-402
program LocalVars;
var
  X, Y: Integer;
  Count: Integer;

procedure ShadowX;
var
  X: Integer;
begin
  X := 100;
  Y := Y + X;
  Writeln('local X=', X, ' Y=', Y);
end;

procedure Counter;
var
  Count: Integer;
begin
  Count := 1;
  Count := Count + 1;
  Writeln('local Count=', Count);
end;

function Twice(N: Integer): Integer;
var
  T: Integer;
begin
  T := N * 2;
  Twice := T;
end;

procedure Deep;
var
  Y: Char;
  Z: Boolean;
begin
  Y := 'y';
  Z := Y = 'y';
  Writeln('deep Y=', Y, ' Z=', Z, ' global X=', X);
end;

begin
  X := 1;
  Y := 2;
  Count := 50;
  ShadowX;
  Writeln('global X=', X, ' Y=', Y);
  Counter;
  Counter;
  Writeln('global Count=', Count);
  Writeln(Twice(21), ' ', Twice(Twice(3)));
  Deep;
  Writeln(Y);
end.
