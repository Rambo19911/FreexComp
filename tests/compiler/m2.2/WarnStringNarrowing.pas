// covers: LANG-245
program WarnStringNarrowing;
// W1020 for an implicit UnicodeString → AnsiString/UTF8String/ShortString conversion; none for literals, explicit casts
// or widening conversions.
var
  S: string;
  A: AnsiString;
  H: ShortString;
  U: UTF8String;

procedure TakeAnsi(X: AnsiString);
begin
  Writeln(X);
end;

begin
  S := 'wide';
  A := S;
  H := S;
  U := S + '!';
  TakeAnsi(S);
  A := 'literal';
  H := 'literal';
  A := AnsiString(S);
  S := A;
  S := H;
  Writeln(A, ' ', H, ' ', U, ' ', S);
end.
