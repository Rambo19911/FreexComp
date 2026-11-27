program RecordMethod;
type
  TVec = record
    X: Integer;
    function Doubled: Integer;
  end;
function TVec.{#}Doubled: Integer;
begin
  Result := X * 2;
end;
var
  V: TVec;
begin
  V.X := 3;
  Writeln(V.{|}Doubled);
end.
