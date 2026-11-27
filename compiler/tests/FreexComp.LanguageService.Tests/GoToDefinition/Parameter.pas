program Parameter;
function Twice(const {#}Value: Integer): Integer;
begin
  Result := {|}Value * 2;
end;
begin
  Writeln(Twice(2));
end.
