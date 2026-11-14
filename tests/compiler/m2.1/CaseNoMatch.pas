// covers: LANG-356
program CaseNoMatch;
var
  I: Integer;
  C: Char;
begin
  I := 42;
  case I of
    1: Writeln('one');
    2..10: Writeln('two to ten');
  end;
  Writeln('after first case');
  C := 'z';
  case C of
    'a': Writeln('a');
  end;
  case I of
    100..200: Writeln('never');
  end;
  I := -1;
  case I of
    0: Writeln('zero');
  end;
  Writeln('done');
end.
