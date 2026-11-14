// covers: LANG-250, LANG-251, LANG-252, LANG-253, CMP-336
program SetOfChar;
type
  TChars = set of Char;
  TBytes = set of Byte;
  TLetters = set of 'a'..'z';
var
  S, T, U: TChars;
  B: TBytes;
  L: TLetters;
  C, Lo, Hi: Char;
  I: Integer;

procedure Show(var S: TChars);
var
  C: Char;
begin
  Write('[');
  for C := #0 to #255 do
    if C in S then
      Write(C);
  Writeln(']');
end;

procedure ShowBytes(var B: TBytes);
var
  I: Integer;
begin
  Write('{');
  for I := 0 to 255 do
    if I in B then
      Write(' ', I);
  Writeln(' }');
end;

begin
  S := [];
  Show(S);
  S := ['a', 'e', 'i', 'o', 'u'];
  Show(S);
  Writeln('a' in S, ' ', 'b' in S, ' ', #300 in S);
  Lo := 'c';
  Hi := 'g';
  T := [Lo..Hi, 'x'];
  Show(T);
  T := [Hi..Lo];
  Show(T);
  U := S + ['a'..'f'];
  Show(U);
  U := U * ['a'..'h'];
  Show(U);
  U := U - S;
  Show(U);
  Writeln(S = ['u', 'o', 'i', 'e', 'a'], ' ', S <> [], ' ', ['a'] <= S, ' ', S >= ['a', 'b'], ' ', S <= S);
  C := 'Z';
  Include(S, C);
  Exclude(S, 'a');
  Show(S);
  C := #1000;
  Include(S, C);
  Writeln(C in S);
  S := [C];
  Show(S);
  B := [0, 100..102, 255];
  ShowBytes(B);
  I := 200;
  B := B + [I];
  Writeln(I in B, ' ', 300 in B, ' ', -1 in B);
  I := 256;
  Writeln(I in B);
  L := ['a'..'c'];
  S := L;
  Show(S);
  Writeln(SizeOf(S), ' ', SizeOf(B), ' ', SizeOf(L));
end.
