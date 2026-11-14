// covers: LANG-355, LANG-356
program CaseChar;

procedure Kind(C: Char);
begin
  Write(C, ' ');
  case C of
    'a', 'e', 'i', 'o', 'u': Writeln('vowel');
    'b'..'d', 'f'..'h', 'j'..'n', 'p'..'t', 'v'..'z': Writeln('consonant');
    '0'..'9': Writeln('digit');
    'A'..'Z': Writeln('upper');
    #32: Writeln('space');
    #$263A: Writeln('smiley');
  else
    Writeln('other ', Ord(C));
  end;
end;

begin
  Kind('a');
  Kind('k');
  Kind('7');
  Kind('Q');
  Kind(' ');
  Kind('!');
  Kind('u');
  Kind('z');
  Kind(#$263A);
end.
