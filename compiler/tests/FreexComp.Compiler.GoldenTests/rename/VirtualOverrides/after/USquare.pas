unit USquare;

interface

uses UShape;

type
  TSquare = class(TShape)
    Side: Integer;
    function Surface: Integer; override;
  end;

implementation

function TSquare.Surface: Integer;
begin
  Result := Side * Side;
end;

end.
