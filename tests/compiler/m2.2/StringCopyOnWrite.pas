// covers: LANG-241, LANG-242, ABI-024, ABI-025, CMP-324
program StringCopyOnWrite;
// s[i] := c makes s unique first (_UStrUnique): shared copies, value parameters and static literal blocks keep their
// characters; a character passed as a var argument is made unique the same way.
procedure Modify(S: string);
begin
  S[1] := 'M';
  Writeln('in Modify: ', S);
end;

procedure ModifyVar(var S: string);
begin
  S[Length(S)] := '!';
end;

procedure Upper(var C: Char);
begin
  C := Chr(Ord(C) - 32);
end;

function Literal: string;
begin
  Result := 'const';
end;

var
  A, B, C: string;
  I: Integer;
begin
  A := 'hello';
  B := A;
  B[1] := 'j';
  Writeln(A, ' ', B);
  C := B;
  C[5] := 'y';
  Writeln(B, ' ', C);
  Modify(A);
  Writeln(A);
  ModifyVar(A);
  Writeln(A);
  B := A;
  Upper(B[2]);
  Writeln(A, ' ', B);
  Writeln(Literal);
  A := Literal;
  A[1] := 'C';
  Writeln(A, ' ', Literal);
  A := 'abc';
  B := A;
  for I := 1 to Length(B) do
    B[I] := Chr(Ord(B[I]) + 1);
  Writeln(A, ' ', B);
  Inc(B[1]);
  Writeln(B);
  A := B;
  B[1] := B[3];
  Writeln(A, ' ', B);
end.
