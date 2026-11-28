unit UCircle;

interface

uses UShape;

type
  TCircle = class(TShape)
    Radius: Integer;
    function Area: Integer; override;
  end;

implementation

function TCircle.Area: Integer;
begin
  Result := 3 * Radius * Radius + inherited Area;
end;

end.
