unit PauseUnit;

interface

procedure SelfTest(Count: Integer);

implementation

procedure SelfTest(Count: Integer);
var
  I, Sum: Integer;
begin
  Sum := 0;
  for I := 1 to Count do
    Sum := Sum + I;
  WriteLn('sum ', Sum);
end;

end.
