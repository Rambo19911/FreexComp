// covers: LANG-130, LANG-131, LANG-133, CMP-320
program StringCharLiterals;
// Quoted parts, doubled quotes and #n / #$n character codes concatenated into one literal, assigned to string variables.
const
  Quote = '''';
  CRLF = #13#10;
var
  S: string;
begin
  S := 'it''s';
  Writeln(S, ' ', Length(S));
  S := 'a'#9'b';
  Writeln(Length(S), ' ', Ord(S[1]), ' ', Ord(S[2]), ' ', Ord(S[3]));
  S := #72#105;
  Writeln(S);
  S := #$41#$42'C';
  Writeln(S);
  S := 'line1'#13#10'line2';
  Writeln(S);
  Writeln(Length(S));
  S := Quote;
  Writeln(S, ' ', Length(S));
  S := '''''';
  Writeln(S, ' ', Length(S));
  S := CRLF;
  Writeln(Length(S), ' ', Ord(S[1]), ' ', Ord(S[2]));
  S := '';
  Writeln('[', S, ']');
  S := #0'z';
  Writeln(Length(S), ' ', Ord(S[1]), ' ', S[2]);
  S := 'x';
  Writeln(S, Length(S));
end.
