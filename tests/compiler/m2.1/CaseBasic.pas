// covers: LANG-355, LANG-356, LANG-357
program CaseBasic;

procedure Show(N: Integer);
begin
  Write(N, ': ');
  case N of
    0: Writeln('zero');
    1, 2, 3: Writeln('small');
    4..9: Writeln('digit');
    10, 20..29, 100: Writeln('special');
    -5..-1:
      begin
        Write('negative ');
        Writeln(-N);
      end;
    1000..100000: Writeln('large range');
  else
    Write('other');
    Writeln(' value');
  end;
end;

function Selector(var Calls: Integer): Integer;
begin
  Calls := Calls + 1;
  Result := 2;
end;

var
  I, Calls: Integer;
  B: Byte;
  X: Int64;
begin
  Show(0);
  Show(2);
  Show(7);
  Show(10);
  Show(25);
  Show(100);
  Show(-3);
  Show(50);
  Show(5000);
  Show(-6);
  Calls := 0;
  case Selector(Calls) of
    1: Writeln('one');
    2: Writeln('two');
    3: Writeln('three');
  end;
  Writeln('selector evaluated ', Calls, ' time(s)');
  B := 200;
  case B of
    0..127: Writeln('low byte');
    128..255: Writeln('high byte');
  end;
  X := 5000000000;
  case X of
    5000000000: Writeln('big');
  else
    Writeln('not big');
  end;
  for I := 1 to 3 do
    case I of
      1: Write('a');
      2: Write('b');
    else
      Write('c');
    end;
  Writeln;
  case True of
    False: Writeln('false');
    True: Writeln('true');
  end;
end.
