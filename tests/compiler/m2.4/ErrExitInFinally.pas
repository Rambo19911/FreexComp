// covers: LANG-812
program ErrExitInFinally;

var
  I: Integer;
begin
  for I := 1 to 3 do
    try
      Writeln(I);
    finally
      while I > 5 do Break;
      if I = 2 then Break;
      Continue;
    end;
  try
  finally
    Exit;
  end;
end.