// covers: LANG-380
program GotoBasic;

{ Labels are declared in the label section of their block, as identifiers or integers; goto jumps forward and backward,
  out of loops, if and case statements, and inside one part of a try statement. }

function CountTo(Limit: Integer): Integer;
label
  Again, 10;
begin
  Result := 0;
Again:
  Inc(Result);
  if Result < Limit then
    goto Again;
  goto 10;
  Result := -1;
10:
end;

procedure OutOfLoops;
label
  Found, Next;
var
  I, J: Integer;
begin
  for I := 1 to 3 do
  begin
    J := 0;
    while True do
    begin
      Inc(J);
      if I * J = 6 then
        goto Found;
      if J = 3 then
        goto Next;
    end;
  Next:
    Writeln('row ', I, ' done');
  end;
  Writeln('not found');
  Exit;
Found:
  Writeln('found ', I, ' * ', J);
end;

procedure OutOfCase(N: Integer);
label
  Small, Done;
begin
  case N of
    1, 2:
      goto Small;
    3:
      Writeln('three');
  else
    if N > 100 then
      goto Done;
    Writeln('other');
  end;
  goto Done;
Small:
  Writeln('small');
Done:
  Writeln('case ', N, ' done');
end;

procedure InsideTry;
label
  Retry, Skip, Tail;
var
  Attempts: Integer;
begin
  Attempts := 0;
  try
  Retry:
    Inc(Attempts);
    if Attempts < 3 then
      goto Retry;
    Writeln('attempts ', Attempts);
  finally
    goto Skip;
    Writeln('not reached');
  Skip:
    Writeln('finally');
  end;
  try
    Writeln('try');
  except
    goto Tail;
  Tail:
    Writeln('not reached');
  end;
end;

procedure Nested;
label
  L;

  procedure Inner;
  label
    L;
  var
    N: Integer;
  begin
    N := 0;
  L:
    Inc(N);
    if N < 2 then
      goto L;
    Writeln('inner ', N);
  end;

begin
  goto L;
  Writeln('not reached');
L:
  Inner;
end;

label
  1, Finish;
var
  Turn: Integer;
begin
  Writeln(CountTo(5));
  OutOfLoops;
  OutOfCase(1);
  OutOfCase(3);
  OutOfCase(7);
  OutOfCase(500);
  InsideTry;
  Nested;
  Turn := 0;
1:
  Inc(Turn);
  if Turn = 3 then
    goto Finish;
  goto 1;
Finish:
  Writeln('turns ', Turn);
end.
