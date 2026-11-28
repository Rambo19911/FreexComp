unit UCircle;

interface

uses UShape;

type
  TCircle = class(TShape)
    Radius: Integer;
    function Surface: Integer; override;
  end;

implementation

function TCircle.Surface: Integer;
begin
  Result := 3 * Radius * Radius + inherited Surface;
end;

end.
