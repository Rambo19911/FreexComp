// covers: LANG-244, CMP-324, ABI-021
program StringCompare;
// all relational operators on strings: ordinal UTF-16 code unit order, a common prefix puts the shorter string first,
// tests against '' without a call, characters compared as 1-char strings, constants folded the same way.
procedure Compare(const A, B: string);
begin
  Writeln('''', A, ''' vs ''', B, ''': ', A = B, ' ', A <> B, ' ', A < B, ' ', A <= B, ' ', A > B, ' ', A >= B);
end;

var
  S, T: string;
  C: Char;
begin
  Compare('abc', 'abc');
  Compare('abc', 'abd');
  Compare('abd', 'abc');
  Compare('ab', 'abc');
  Compare('abc', 'ab');
  Compare('', 'a');
  Compare('', '');
  Compare('B', 'a');
  Compare('Z', 'z');
  S := 'abc';
  T := 'ab';
  T := T + 'c';
  Writeln(S = T, ' ', S <> T);
  S := '';
  Writeln(S = '', ' ', S <> '', ' ', '' = S);
  S := 'x';
  Writeln(S = '', ' ', S <> '');
  C := 'x';
  Writeln(S = C, ' ', C = S, ' ', S < C, ' ', 'w' < S);
  if S + 'y' > S then
    Writeln('longer is greater');
  Writeln('apple' < 'banana', ' ', 'apple' > 'Apple', ' ', 'ab' < 'abc');
end.
