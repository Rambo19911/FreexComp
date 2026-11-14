// covers: LANG-243, LANG-244, LANG-245, LANG-951, ABI-027, CMP-320
program AnsiStringBasics;
// AnsiString/UTF8String: UTF-8 bytes (ABI-027), conversions to and from string (LANG-245), sharing with copy before a
// character write, concatenation and bytewise comparison of byte strings (LANG-243/244), Length in bytes; Str of
// Booleans, strings and characters gives the text Write would (LANG-951).
var
  A, B: AnsiString;
  U: UTF8String;
  S: string;
  C: AnsiChar;

function Twice(const X: AnsiString): AnsiString;
begin
  Result := X + X;
end;

procedure Show(X: AnsiString);
begin
  Writeln('[', X, '] ', Length(X));
  X := 'changed';
end;

begin
  A := 'hello';
  Writeln(A, ' ', Length(A));
  S := 'größe';
  A := AnsiString(S);
  Writeln(A, ' ', Length(A), ' ', Length(S));
  S := A;
  Writeln(S, ' ', Length(S));
  B := A;
  B[1] := 'G';
  Writeln(A, ' ', B);
  A := A + '!';
  Writeln(A, ' ', Length(A));
  Writeln(Twice('ab'));
  Show(B);
  Writeln(A = 'größe!', ' ', A < B, ' ', A > B, ' ', A <> '');
  U := 'naïve';
  S := U;
  Writeln(U, ' ', Length(U), ' ', Length(S));
  C := A[2];
  Writeln(C, Ord(C));
  SetLength(A, 2);
  Writeln(A, Length(A));
  A := Copy(B, 1, 2);
  Writeln(A);
  A := '';
  Writeln('[', A, ']', Length(A), ' ', A = '');
  A := C;
  Writeln(A, Length(A));
  S := S + A;
  Writeln(S, ' ', S = 'naïver');
  A := 'x' + B + 'y' + B;
  Writeln(A);
  Str(False, S);
  Write(S, '|');
  Str(True:6, S);
  Write(S, '|');
  Str(B:7, S);
  Write(S, '|');
  Str(C, S);
  Writeln(S, '|');
end.
