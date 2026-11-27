program PointerTypeReference;
type
  {#}PCount = ^Integer;
var
  N: Integer;
  P: {|}PCount;
begin
  N := 1;
  P := @N;
  Writeln(P^);
end.
