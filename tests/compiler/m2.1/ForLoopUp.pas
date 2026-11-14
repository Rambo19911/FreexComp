// covers: LANG-361
program ForLoopUp;
var
  I, N, Sum, Evaluations: Integer;

function Limit: Integer;
begin
  Evaluations := Evaluations + 1;
  Result := 4;
end;

function Start: Integer;
begin
  Evaluations := Evaluations + 10;
  Result := 2;
end;

begin
  Sum := 0;
  for I := 1 to 10 do
    Sum := Sum + I;
  Writeln(Sum);
  Evaluations := 0;
  for I := Start to Limit do
    Write(I, ' ');
  Writeln('| evaluations ', Evaluations);
  N := 3;
  for I := 1 to N do
  begin
    N := N + 10;
    Write(I, ' ');
  end;
  Writeln('| N=', N);
  for I := 5 to 4 do
    Writeln('never');
  Writeln('empty range done');
  for I := 7 to 7 do
    Writeln('single ', I);
  for I := -2 to 2 do
    Write(I, ' ');
  Writeln('|');
  N := 0;
  for I := 1 to 3 do
    for Sum := I to 3 do
      N := N + 1;
  Writeln('pairs ', N);
end.
