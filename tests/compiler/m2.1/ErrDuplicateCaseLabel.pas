// covers: LANG-355
program ErrDuplicateCaseLabel;
var
  I: Integer;
  C: Char;
begin
  I := 1;
  case I of
    1: Writeln('one');
    2, 1: Writeln('again');
    5..9: Writeln('range');
    7: Writeln('inside the range');
  end;
  C := 'a';
  case C of
    'a'..'f': Writeln('hex letter');
    'c'..'z': Writeln('overlap');
  end;
end.
