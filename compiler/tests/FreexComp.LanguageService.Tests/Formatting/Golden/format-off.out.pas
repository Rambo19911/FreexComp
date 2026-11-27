program FormatOff;
var
  I: Integer;
begin
  I := 0;
// format:off
  Matrix   :=   [ 1, 0,
                  0, 1 ];
    if I=0 then I:=1;
// format:on
  if I = 0 then I := 1;
  // FORMAT:OFF
I:=2;   I:=3;
  // Format:On
end.
