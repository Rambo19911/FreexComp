// covers: LANG-241, LANG-242, LANG-245, CMP-320, CMP-324, CMP-325, ABI-021, ABI-023, ABI-024
program StringBasics;
// UnicodeString variables: literals, assignment, Length, reading characters, the empty string; unassigned globals and
// locals are '' (locals on every call: zero-initialised, finalised at exit).
var
  G: string;

procedure Show(const S: string);
begin
  Writeln('[', S, '] ', Length(S));
end;

procedure LocalStartsEmpty;
var
  L: string;
begin
  Write('local ');
  Show(L);
  L := 'set';
  Write('local ');
  Show(L);
end;

var
  A, B: string;
  C: Char;
  I: Integer;
begin
  Show(G);
  A := 'Hello';
  Show(A);
  B := A;
  Show(B);
  A := '';
  Show(A);
  Show(B);
  C := 'x';
  A := C;
  Show(A);
  A := 'y';
  Show(A);
  LocalStartsEmpty;
  LocalStartsEmpty;
  G := B;
  for I := 1 to Length(G) do
    Write(G[I], ' ');
  Writeln('|');
  Writeln(B[1], B[Length(B)], ' ', Ord(B[2]), ' ', High(B));
  for I := 1 to 1000 do
    A := B;
  Show(A);
  B := 'abc';
  Writeln(Length(B), ' ', Length('abc'), ' ', Length(''), ' ', Length(A));
  Writeln('[', B:6, '][', G:2, ']');
  A := G;
  G := '';
  Show(A);
  Show(G);
end.
