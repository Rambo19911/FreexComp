program Blocks;

var
  I: Integer;

begin
  I := 0;
  try
    repeat
      Inc(I);
    until I > 3;
    case I of
      1: Writeln('one');
    else
      Writeln('other');
    end;
    if I > 0 then
    begin
      Writeln(I);
    end;
    if I > 1 then begin Writeln; end;
    begin
    end;
  finally
    Writeln('done');
  end;
end.
