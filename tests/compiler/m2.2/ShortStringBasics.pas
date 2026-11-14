// covers: LANG-200, LANG-243, LANG-246, ABI-027
program ShortStringBasics;
// string[N] and ShortString: silent truncation to the capacity (LANG-246; at a UTF-8 sequence boundary for a converted
// UnicodeString), the length byte s[0], Length, concatenation and comparison, parameters and results by value.
type
  TName = string[10];
const
  Greeting: string[5] = 'Hi';
var
  N: TName;
  M: string[3];
  S: ShortString;
  U: string;

function Shorten(X: ShortString): ShortString;
begin
  X[0] := AnsiChar(2);
  Result := X;
end;

function Len(const X: ShortString): Integer;
begin
  Result := Length(X);
end;

begin
  N := 'Hello, World';
  Writeln(N, ' ', Length(N));
  M := N;
  Writeln(M, ' ', Length(M));
  S := N;
  S := S + '!';
  Writeln(S, ' ', Length(S));
  U := S;
  Writeln(U, ' ', Length(U));
  S := U + '?';
  Writeln(S);
  Writeln(N[1], N[5], Ord(N[0]));
  N[1] := 'J';
  Writeln(N);
  Writeln(Shorten(N), ' ', N);
  Writeln(N = 'Jello, Wor', ' ', M < N, ' ', N <> S, ' ', SizeOf(N), ' ', SizeOf(M), ' ', High(TName));
  S := '';
  Writeln('[', S, ']', Length(S));
  M := 'größe';
  Writeln(M, Length(M));
  U := 'größe';
  M := U;
  Writeln(M, Length(M));
  Writeln(Greeting, Length(Greeting), ' ', Len(N), ' ', Len('abc'), ' ', Len(M));
  N := M + N;
  Writeln(N);
end.
