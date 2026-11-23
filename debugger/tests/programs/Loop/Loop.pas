program Loop;
var
  Counter: Int64;
begin
  Counter := 0;
  while True do
  begin
    Counter := Counter + 1;
    if Counter < 0 then
      Break;
  end;
end.