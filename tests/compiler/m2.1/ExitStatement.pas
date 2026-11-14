// covers: LANG-363, LANG-413
program ExitStatement;

procedure Early(N: Integer);
begin
  Write('start ', N);
  if N > 2 then
  begin
    Writeln(' exit early');
    Exit;
  end;
  Writeln(' normal end');
end;

function FirstDivisor(N: Integer): Integer;
var
  I: Integer;
begin
  for I := 2 to N - 1 do
    if N mod I = 0 then
      Exit(I);
  Result := N;
end;

function Sign(N: Integer): Integer;
begin
  if N < 0 then
    Exit(-1);
  if N = 0 then
    Exit(0);
  Exit(1);
  Writeln('never');
end;

function KeepResult(N: Integer): Integer;
begin
  Result := N * 2;
  if N > 0 then
    Exit;
  Result := -1;
end;

function InLoop: Integer;
var
  I: Integer;
begin
  Result := 0;
  I := 0;
  while True do
  begin
    I := I + 1;
    repeat
      if I = 4 then
        Exit(I * 100);
    until True;
  end;
end;

procedure Nested;

  procedure Inner;
  begin
    Writeln('inner before');
    Exit;
    Writeln('never');
  end;

begin
  Inner;
  Writeln('outer continues');
end;

begin
  Early(1);
  Early(5);
  Writeln(FirstDivisor(91), ' ', FirstDivisor(13));
  Writeln(Sign(-5), ' ', Sign(0), ' ', Sign(7));
  Writeln(KeepResult(3), ' ', KeepResult(0));
  Writeln(InLoop);
  Nested;
  Writeln('main before exit');
  Exit;
  Writeln('never in main');
end.
