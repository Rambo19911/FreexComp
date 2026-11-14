// covers: LANG-245, LANG-247, LANG-290, LANG-911, CMP-320, CMP-324
program PCharInterop;
// PChar(s) points to the string's data or to a static empty terminator (never nil); PChar and PAnsiChar convert to
// strings up to the terminator (nil gives ''); string literals are PChar constants; char arrays convert up to their first
// #0; Write of PChar, PAnsiChar and char arrays.
var
  S, T: string;
  P: PChar;
  A, B: AnsiString;
  PA: PAnsiChar;
  Buf: array[0..7] of Char;
  I: Integer;

function CountChars(P: PChar): Integer;
begin
  Result := 0;
  while P^ <> #0 do
  begin
    Inc(Result);
    Inc(P);
  end;
end;

begin
  S := 'pointer';
  P := PChar(S);
  Writeln(P[0], P[6], ' ', CountChars(P));
  T := P;
  Writeln(T, ' ', T = S);
  S := '';
  P := PChar(S);
  Writeln(P <> nil, ' ', CountChars(P), ' [', P, ']');
  P := 'literal';
  Writeln(P, ' ', CountChars(P));
  T := P + '!?';
  Writeln(T);
  P := nil;
  T := P;
  Writeln('[', T, ']', Length(T));
  for I := 0 to 7 do
    Buf[I] := #0;
  Buf[0] := 'a';
  Buf[1] := 'b';
  Buf[2] := 'c';
  T := Buf;
  Writeln(T, ' ', Length(T));
  for I := 0 to 7 do
    Buf[I] := Chr(Ord('A') + I);
  T := Buf;
  Writeln(T, ' ', Length(T));
  Writeln(Buf, '|', string(Buf));
  A := 'bytes';
  PA := PAnsiChar(A);
  Writeln(PA[0], PA[4]);
  B := PA;
  S := PA;
  Writeln(B, Length(B), ' ', S, Length(S), ' ', PA);
  T := 'x';
  Writeln(string(PChar(T)), ' ', NativeInt(Pointer(T)) = NativeInt(PChar(T)));
end.
