program ClassField;
type
  TCounter = class
    {#}FCount: Integer;
  end;
var
  C: TCounter;
begin
  C := TCounter.Create;
  C.{|}FCount := 3;
  Writeln(C.FCount);
end.
