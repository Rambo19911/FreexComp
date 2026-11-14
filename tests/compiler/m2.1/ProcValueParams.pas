// covers: LANG-420
program ProcValueParams;

procedure Change(N: Integer; C: Char; B: Boolean);
begin
  N := N * 10;
  C := 'Z';
  B := not B;
  Writeln('inside: ', N, ' ', C, ' ', B);
end;

function AddAll(A, B: Integer; C: Int64): Int64;
begin
  A := A + B;
  Result := A + C;
end;

procedure Widen(B: Byte; W: Word; X: Int64);
begin
  Writeln(B, ' ', W, ' ', X);
end;

var
  N: Integer;
  C: Char;
  B: Boolean;
begin
  N := 5;
  C := 'a';
  B := False;
  Change(N, C, B);
  Writeln('outside: ', N, ' ', C, ' ', B);
  Change(N + 1, 'q', N > 0);
  Writeln(AddAll(1, 2, 3000000000), ' ', N);
  Widen(200, 60000, N);
  Widen(N, N, -N);
end.
