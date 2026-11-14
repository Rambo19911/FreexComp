// covers: LANG-810, LANG-363
program TryFinallyExit;

{ Exit, Break and Continue leaving try parts run the finally parts on the way, innermost first }

function Find(Limit: Integer): Integer;
var
  I: Integer;
begin
  Result := -1;
  for I := 1 to 10 do
    try
      try
        if I mod 2 = 0 then Continue;
        if I = Limit then
        begin
          Result := I;
          Exit;
        end;
        if I > 6 then Break;
        Writeln('  body ', I);
      finally
        Writeln('  inner finally ', I);
      end;
    finally
      Writeln('  outer finally ', I);
    end;
  Writeln('  after loop');
end;

procedure Loops;
var
  N: Integer;
begin
  N := 0;
  while True do
    try
      N := N + 1;
      if N < 3 then Continue;
      Break;
    finally
      Writeln('  while finally ', N);
    end;
  repeat
    try
      N := N - 1;
      if N = 1 then Break;
    finally
      Writeln('  repeat finally ', N);
    end;
  until N = 0;
end;

var
  R: Integer;
begin
  R := Find(5);
  Writeln('Find(5) = ', R);
  R := Find(20);
  Writeln('Find(20) = ', R);
  Loops;
end.