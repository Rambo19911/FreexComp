// covers: LANG-346
program ResourceStringConst;
// resourcestring constants behave exactly like const string constants: in Write, concatenation, constant expressions,
// Length, comparisons, as arguments and assigned to variables; a character value becomes a one-character string.
resourcestring
  SHello = 'Hello';
  SWorld = 'World';
  SBang = '!';
  SQuote = 'it''s';
  SEmpty = '';

const
  CGreeting = SHello + ', ' + SWorld + SBang;

procedure Show(const S: string);
begin
  Writeln('[', S, ']');
end;

var
  S: string;
begin
  Writeln(SHello);
  Writeln(CGreeting);
  Writeln(Length(CGreeting));
  Show(SQuote);
  Show(SEmpty);
  S := SHello;
  S := S + ' ' + SWorld;
  Writeln(S);
  Writeln(S = 'Hello World');
  Writeln(SBang = '!');
  Writeln(Length(SBang));
end.
