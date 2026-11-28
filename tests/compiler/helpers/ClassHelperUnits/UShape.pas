unit UShape;

interface

type
  TShape = class
    function Describe: string; virtual;
  end;

  TCircle = class(TShape)
  end;

implementation

function TShape.Describe: string;
begin
  Result := 'shape';
end;

end.