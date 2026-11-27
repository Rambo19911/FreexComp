program GlobalVariable;
var
  {#}Total: Integer;
procedure Add(N: Integer);
begin
  {|}Total := Total + N;
end;
begin
  Add(1);
  Writeln(Total);
end.
