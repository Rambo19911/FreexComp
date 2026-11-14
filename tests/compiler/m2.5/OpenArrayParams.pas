// covers: LANG-430, LANG-431, LANG-259
program OpenArrayParams;

type
  TFive = array[1..5] of Integer;
  TInts = array of Integer;

function Sum(const A: array of Integer): Integer;
var
  I: Integer;
begin
  Result := 0;
  for I := 0 to High(A) do
    Result := Result + A[I];
end;

procedure Describe(const A: array of Integer);
begin
  Writeln('Length=', Length(A), ' Low=', Low(A), ' High=', High(A), ' Sum=', Sum(A));
end;

procedure Firsts(const A: array of Integer);
begin
  Describe(Slice(A, 2));
end;

procedure Twice(var A: array of Integer);
var
  I: Integer;
begin
  for I := Low(A) to High(A) do
    A[I] := A[I] * 2;
end;

function Join(const Parts: array of string): string;
var
  I: Integer;
begin
  Result := '';
  for I := 0 to High(Parts) do
  begin
    if I > 0 then
      Result := Result + ',';
    Result := Result + Parts[I];
  end;
end;

function LastOf(const A: array of Integer): Integer;

  function Last: Integer;
  begin
    Result := A[High(A)];
  end;

begin
  Result := Last;
end;

function Count(const A: array of Char): Integer;
begin
  Result := Length(A);
end;

var
  F: TFive;
  D: TInts;
  I: Integer;
  S: string;
  Chars: array[0..3] of Char;
begin
  for I := 1 to 5 do
    F[I] := I;
  Describe(F);
  SetLength(D, 3);
  D[0] := 10;
  D[1] := 20;
  D[2] := 30;
  Describe(D);
  Describe([7, 8]);
  Describe([]);
  D := nil;
  Describe(D);
  Describe(Slice(F, 3));
  Twice(F);
  Writeln(F[1], ' ', F[5]);
  Firsts(F);
  SetLength(D, 2);
  D[0] := 1;
  D[1] := 2;
  Twice(D);
  Writeln(D[0], ' ', D[1]);
  S := 'b';
  Writeln(Join(['a', S, 'c' + S]));
  Writeln('[', Join([]), ']');
  Writeln(LastOf([4, 5, 6]));
  Writeln(Count(Chars), ' ', Count(['x', 'y']));
end.
