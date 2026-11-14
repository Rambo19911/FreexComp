// covers: LANG-132, LANG-240, LANG-244, CMP-320
program StringUnicode;
// non-ASCII text and characters above U+FFFF (surrogate pairs: 2 code units) in literals and variables, written as UTF-8;
// comparison is by code unit value, also when folded.
var
  S, T: string;
  I: Integer;
begin
  S := 'Grüße';
  Writeln(S, ' ', Length(S));
  S := 'Ελληνικά';
  Writeln(S, ' ', Length(S));
  S := '€ 5';
  Writeln(S, ' ', Length(S), ' ', Ord(S[1]));
  S := '𝄞';
  Writeln(S, ' ', Length(S), ' ', Ord(S[1]), ' ', Ord(S[2]));
  T := 'clef ' + S + ' ' + #$D83D#$DE00;
  Writeln(T, ' ', Length(T));
  S := '日本語';
  for I := Length(S) downto 1 do
    Write(S[I]);
  Writeln;
  Writeln('é' < 'z', ' ', 'z' < 'é');
  S := 'é';
  T := 'z';
  Writeln(S < T, ' ', S > T);
  S := 'A';
  S := S + #$00C5;
  Writeln(S, ' ', Length(S));
end.
