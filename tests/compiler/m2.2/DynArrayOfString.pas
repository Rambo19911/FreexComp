// covers: LANG-257, ABI-030, ABI-042, ABI-044, CMP-325, CMP-330
program DynArrayOfString;
// Managed elements: new elements are '' (zero), element assignment counts references, shrinking finalises the dropped
// strings, a shared array copied by SetLength/Copy adds a reference per string, nested string arrays.
type
  TNames = array of string;
  TAnsis = array of AnsiString;
var
  N, M: TNames;
  A: TAnsis;
  S: string;
  I: Integer;

function Joined(const X: TNames): string;
var
  K: Integer;
begin
  Result := '';
  for K := 0 to High(X) do
    Result := Result + '[' + X[K] + ']';
end;

begin
  SetLength(N, 3);
  Writeln(Joined(N), ' ', Length(N[0]));
  S := 'two';
  N[0] := 'one';
  N[1] := S;
  N[2] := S + '!';
  S := 'changed';
  Writeln(Joined(N));
  N[1][1] := 'T';
  Writeln(Joined(N), ' ', S);
  M := N;
  SetLength(M, 2);
  M[0] := 'uno';
  Writeln(Joined(N), ' ', Joined(M));
  M := Copy(N, 1, 2);
  N := nil;
  Writeln(Joined(M));
  SetLength(M, 1);
  SetLength(M, 3);
  Writeln(Joined(M), ' ', Length(M[2]));
  SetLength(A, 2);
  A[0] := 'bytes';
  A[1] := A[0] + '!';
  Writeln(A[0], ' ', A[1], ' ', Length(A[1]));
  for I := 0 to 2 do
    M[I] := Joined(Copy(M, I));
  Writeln(M[0], ' ', M[2]);
end.
