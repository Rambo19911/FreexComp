program ClassHelper;
type
  TThing = class
    N: Integer;
  end;
  TThingHelper = class helper for TThing
    function Twice: Integer;
  end;
function TThingHelper.{#}Twice: Integer;
begin
  Result := N * 2;
end;
var
  T: TThing;
begin
  T := TThing.Create;
  Writeln(T.{|}Twice);
end.
