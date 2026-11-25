// covers: LANG-244, LANG-245, LANG-290
program PCharOfOneCharLiteral;
// A one-character literal (or character constant) cast to PChar / PAnsiChar / PWideChar, or passed to such a parameter,
// is a pointer to a static zero-terminated string like a longer literal — never the character code as an address.
// A Char compared with a string is converted to a one-character string: it never equals ''.
const
  CX = 'c';
var
  P: PChar;
  PA: PAnsiChar;
  PW: PWideChar;
  S: string;
  C: Char;
  W: WideChar;
  A: AnsiChar;

function Len(P: PChar): Integer;
begin
  Result := 0;
  while P[Result] <> #0 do
    Inc(Result);
end;

function LenA(P: PAnsiChar): Integer;
begin
  Result := 0;
  while P[Result] <> #0 do
    Inc(Result);
end;

procedure Show(P: PChar);
begin
  Writeln('[', P, '] ', Len(P), ' ', Ord(P[0]), ' ', Ord(P[1]));
end;

begin
  P := PChar('x');
  Writeln(NativeUInt(P) <> 120, ' ', P^, ' ', Ord(P[1]), ' ', Len(P));
  PA := PAnsiChar('y');
  Writeln(NativeUInt(PA) <> 121, ' ', PA^, ' ', Ord(PA[1]), ' ', LenA(PA));
  PW := PWideChar('z');
  Writeln(NativeUInt(PW) <> 122, ' ', PW^, ' ', Ord(PW[1]), ' ', Len(PW));
  P := PChar(CX);
  Writeln(P^, ' ', Ord(P[1]));
  P := PChar(#65);
  Writeln(P^, ' ', Ord(P[1]));
  Show('a');
  Show(PChar('b'));
  Show(CX);
  Show('xy');
  Writeln(LenA('q'), ' ', LenA(PAnsiChar('r')));
  P := 'p';
  Writeln(P, ' ', Len(P));
  S := string(PChar('s'));
  Writeln(S, ' ', Length(S));
  Writeln(NativeUInt(Pointer(PChar('x'))) <> 120);

  S := 'ab';
  C := 'a';
  W := 'a';
  A := 'a';
  Writeln(S[2] = '', ' ', S[2] <> '', ' ', '' = S[2], ' ', '' <> S[2]);
  Writeln(C = '', ' ', C <> '', ' ', C > '', ' ', C < '', ' ', C >= '', ' ', '' < C);
  Writeln(W = '', ' ', W <> '', ' ', A = '', ' ', A <> '');
  Writeln(C = 'a', ' ', C = 'b', ' ', C < 'b', ' ', 'a' = C);
  Writeln(C = 'ab', ' ', C < 'ab', ' ', 'ab' > C, ' ', C <> 'ab');
  Writeln(A = 'a', ' ', A = 'ab', ' ', A < 'ab', ' ', W = 'ab', ' ', W < 'ab');
  Writeln(S[1] = S, ' ', S[1] < S, ' ', S > S[1]);
  if S[2] = '' then
    Writeln('wrong')
  else
    Writeln('ok');
end.
