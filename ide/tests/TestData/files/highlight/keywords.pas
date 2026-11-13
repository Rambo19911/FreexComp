program Keywords;
uses SysUtils;
var
  I: Integer;
begin
  for I := 1 to 3 do
    if (I mod 2 = 0) and not False then
      WriteLn(I)
    else
      Continue;
  WHILE nil <> nil DO ;
end.
