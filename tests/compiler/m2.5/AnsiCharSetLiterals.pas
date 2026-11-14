// covers: LANG-262, LANG-251, LANG-252
program AnsiCharSetLiterals;

{ C-200: character constants take the AnsiChar base type of a set of AnsiChar: a constructor assigned to such a set,
  a constructor argument, a constant set combined with one, and the left operand of 'in'. }

type
  TSysCharSet = set of AnsiChar;

const
  Digits: TSysCharSet = ['0'..'9'];

procedure Q(const S: TSysCharSet);
begin
  Writeln('a' in S, ' ', 'z' in S, ' ', '5' in S);
end;

var
  B: TSysCharSet;
begin
  B := ['a'..'z'];
  Q(B);
  Q(['a']);
  Q(Digits + ['z']);
  B := B - ['a'..'y'];
  Writeln('z' in B, ' ', 'a' in B);
end.
