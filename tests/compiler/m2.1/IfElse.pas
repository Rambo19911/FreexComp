// covers: LANG-353, LANG-354
program IfElse;

procedure Classify(N: Integer);
begin
  if N < 0 then
    Writeln(N, ' negative')
  else if N = 0 then
    Writeln(N, ' zero')
  else if N < 10 then
    Writeln(N, ' small')
  else
    Writeln(N, ' large');
end;

procedure Dangling(A, B: Boolean);
begin
  Write(A, ' ', B, ': ');
  if A then
    if B then
      Writeln('both')
    else
      Writeln('A only');
  if not A then
    Writeln('not A');
end;

procedure Nested(A, B: Integer);
begin
  if A > 0 then
  begin
    if B > 0 then
      Writeln('++')
    else
    begin
      if B = 0 then
        Writeln('+0')
      else
        Writeln('+-');
    end;
  end
  else
    Writeln('A<=0');
end;

var
  I: Integer;
begin
  Classify(-3);
  Classify(0);
  Classify(7);
  Classify(42);
  Dangling(True, True);
  Dangling(True, False);
  Dangling(False, True);
  Nested(1, 1);
  Nested(1, 0);
  Nested(1, -1);
  Nested(0, 5);
  I := 3;
  if I = 3 then ;
  if I = 3 then
  else
    Writeln('not printed');
  if (I > 1) and (I < 5) then
    Writeln('in range');
  if I = 4 then
    Writeln('four')
  else
  begin
  end;
  Writeln('done');
end.
