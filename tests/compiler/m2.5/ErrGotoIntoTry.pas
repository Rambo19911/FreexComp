// covers: LANG-380
program ErrGotoIntoTry;

{ A goto may not jump into or out of the try part, a handler or the finally part of a try statement, nor from outside into
  a for, with or case statement (E2072). A label is declared in the label section of the block that defines and uses it
  (E2070), defined once (E2071) and not left undefined (E2073). }

type
  TPoint = record
    X, Y: Integer;
  end;

procedure IntoAndOutOfTry;
label
  InTry, InFinally, InHandler, After;
begin
  goto InTry;
  try
  InTry:
    goto InFinally;
  finally
  InFinally:
    goto After;
  end;
  try
    goto After;
  except
    on E: TObject do
    begin
    InHandler:
      goto After;
    end;
  end;
  goto InHandler;
After:
end;

procedure IntoStatements(N: Integer);
label
  InFor, InWith, InCase, InWhile;
var
  I: Integer;
  P: TPoint;
begin
  goto InFor;
  goto InWith;
  goto InCase;
  goto InWhile;
  for I := 1 to N do
  begin
  InFor:
    Writeln(I);
  end;
  with P do
  begin
  InWith:
    X := N;
  end;
  case N of
    1:
      begin
      InCase:
        Writeln('one');
      end;
  end;
  while N < 0 do
  begin
  InWhile:
    Inc(N);
  end;
end;

procedure Labels;
label
  Twice, Unused;

  procedure Inner;
  begin
    goto Twice;
  end;

begin
Twice:
  Writeln('first');
Twice:
  Writeln('second');
Missing:
  goto Nowhere;
end;

begin
  IntoAndOutOfTry;
  IntoStatements(1);
  Labels;
end.
