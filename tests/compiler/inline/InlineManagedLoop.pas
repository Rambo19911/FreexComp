// covers: LANG-562, LANG-563
program InlineManagedLoop;

{ Managed inline variables in loop bodies are empty at the start of every iteration and are released when the iteration
  ends: after the loops nothing they allocated is alive, although the routine is still running. (Function results are
  stored straight into variables: expression temporaries keep the lifetime of the routine.) }

type
  TIntArray = array of Integer;

  TNamed = record
    Name: string;
    Id: Integer;
  end;

function Text(N: Integer): string;
begin
  Result := '';
  while N > 0 do
  begin
    Result := Result + 'x';
    Dec(N);
  end;
end;

procedure Loops;
var
  Before: NativeInt;
  I: Integer;
begin
  Before := _LiveAllocCount;
  I := 0;
  while I < 3 do
  begin
    var S: string;
    Write('[', S, ']');
    S := Text(I + 1);
    var A: TIntArray;
    Write(Length(A));
    SetLength(A, I + 1);
    A[I] := I;
    Inc(I);
    Writeln(' ', S, ' ', Length(A));
  end;
  Writeln('while leaked: ', _LiveAllocCount - Before);

  for I := 1 to 3 do
  begin
    var Acc: string;
    var Piece := Text(I);
    Acc := Acc + Piece;
    var R: TNamed;
    Write('<', R.Name, '>');
    R.Name := Acc + '!';
    R.Id := I;
    if I = 2 then
      Continue;
    Writeln(' ', R.Name, ' ', R.Id);
  end;
  Writeln;
  Writeln('for leaked: ', _LiveAllocCount - Before);

  I := 0;
  repeat
    var Line: string;
    Write('{', Line, '}');
    Line := Text(3 - I);
    var Parts: TIntArray;
    SetLength(Parts, Length(Line));
    Inc(I);
    Writeln(' ', Line, ' ', Length(Parts));
  until I = 3;
  Writeln('repeat leaked: ', _LiveAllocCount - Before);

  for I := 1 to 2 do
  begin
    var Outer := Text(I);
    var J := 0;
    while J < 2 do
    begin
      var Tail := Text(J + 1);
      var Inner := Outer + Tail;
      Inc(J);
      Write(' ', Inner);
    end;
  end;
  Writeln;
  Writeln('nested leaked: ', _LiveAllocCount - Before);
end;

begin
  Loops;
end.
