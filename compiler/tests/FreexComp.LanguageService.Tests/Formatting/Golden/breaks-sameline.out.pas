program BreaksSameLine;
var
  I: Integer;
begin
  if I = 0 then begin
    I := 1;
  end else begin
    I := 2;
  end;
  while I > 0 do begin
    Dec(I);
  end;
  if I = 1 then
    I := 0
  else begin
    I := 3;
  end;
  case I of
    1: begin
      Writeln(1);
    end;
  end;
end.
