program BreaksNextLine;
var
  I: Integer;
begin
  if I = 0 then
  begin
    I := 1;
  end
  else
  begin
    I := 2;
  end;
  for I := 1 to 2 do
  begin
    Writeln(I);
  end;
  case I of
    1:
      begin
        Writeln(1);
      end;
  end;
  try
    I := 1;
  except
    I := 2;
  end;
  repeat
    I := I + 1;
  until I > 5;
end.
