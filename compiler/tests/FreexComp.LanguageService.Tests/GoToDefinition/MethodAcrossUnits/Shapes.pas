unit Shapes;
interface
type
  TSquare = class
    Side: Integer;
    function Area: Integer;
  end;
implementation
function TSquare.{#}Area: Integer;
begin
  Result := Side * Side;
end;
end.
