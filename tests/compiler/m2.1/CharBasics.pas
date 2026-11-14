// covers: LANG-133, LANG-240, LANG-213, LANG-922
program CharBasics;
var
  C, D: Char;
  A: AnsiChar;
begin
  C := 'A';
  Writeln(C);
  Writeln(Ord(C));
  D := Chr(98);
  Writeln(D);
  Writeln(Succ(C), Pred(D));
  Writeln(C < D, ' ', C = 'A', ' ', D > 'z', ' ', C <> D);
  C := #67;
  Writeln(C, ' ', Ord(C));
  Inc(C, 2);
  Writeln(C);
  Dec(C);
  Writeln(C);
  Writeln(Ord(High(Char)), ' ', Ord(Low(Char)));
  C := 'z';
  Writeln(Ord(C) - Ord('a'));
  Writeln(Chr(Ord('a') + 1), Chr(65), #$41, #33);
  A := 'x';
  Writeln(A, ' ', Ord(A), ' ', Succ(A));
  C := '''';
  Writeln(C, Ord(C));
end.
