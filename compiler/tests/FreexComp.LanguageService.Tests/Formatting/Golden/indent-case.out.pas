program IndentCase;
var
  I: Integer;
begin
  case I of
    1: Writeln('one');
    2, 3:
      Writeln('two or three');
    4..6:
      begin
        Writeln('range');
      end;
  else
    Writeln('other');
    I := 0;
  end;
end.
