// covers: LANG-133, LANG-290
program LiteralPCharCast;
// A string literal (or string constant) of zero or several characters cast to PAnsiChar / PChar is a pointer to its static
// zero-terminated data: ANSI bytes for PAnsiChar, UTF-16 code units for PChar, as the implicit conversions give.
const
  Greeting = 'hello';
  TypedA: PAnsiChar = PAnsiChar('typed');
  TypedW: PChar = PChar('typed');

function LenA(P: PAnsiChar): Integer;
begin
  Result := 0;
  while P[Result] <> #0 do
    Inc(Result);
end;

function LenW(P: PChar): Integer;
begin
  Result := 0;
  while P[Result] <> #0 do
    Inc(Result);
end;

procedure ShowA(P: PAnsiChar);
var
  I: Integer;
begin
  Write('A[');
  for I := 0 to LenA(P) - 1 do
    Write(P[I]);
  Writeln('] ', LenA(P), ' ', SizeOf(P[0]));
end;

procedure ShowW(P: PChar);
var
  I: Integer;
begin
  Write('W[');
  for I := 0 to LenW(P) - 1 do
    Write(P[I]);
  Writeln('] ', LenW(P), ' ', SizeOf(P[0]));
end;

var
  PA: PAnsiChar;
  PW: PChar;
begin
  PA := PAnsiChar('abc');
  ShowA(PA);
  PW := PChar('abc');
  ShowW(PW);
  ShowA(PAnsiChar(Greeting));
  ShowW(PChar(Greeting));
  ShowA(PAnsiChar(''));
  ShowW(PChar(''));
  ShowA(PAnsiChar('ab' + 'cd'));
  ShowW(PWideChar('wide'));
  ShowA(TypedA);
  ShowW(TypedW);
  Writeln(Ord(PAnsiChar('xy')[2]), ' ', Ord(PChar('xy')[2]));
end.
