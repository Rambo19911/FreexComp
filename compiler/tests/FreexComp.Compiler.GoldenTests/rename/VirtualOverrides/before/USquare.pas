unit USquare;

interface

uses UShape;

type
  TSquare = class(TShape)
    Side: Integer;
    function Area: Integer; override;
  end;

implementation

function TSquare.Area: Integer;
begin
  Result := Side * Side;
end;

end.
