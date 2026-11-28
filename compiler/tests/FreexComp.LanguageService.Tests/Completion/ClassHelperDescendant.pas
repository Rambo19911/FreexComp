{ context: class-helper }
program ClassHelperDescendant;
type
  TShape = class
    Name: string;
  end;
  TCircle = class(TShape)
    Radius: Double;
  end;
  TShapeHelper = class helper for TShape
    function Describe: string;
  end;
function TShapeHelper.Describe: string; begin Result := Name end;
var
  C: TCircle;
begin
  C.{|}
end.
{#has Radius, Name, Describe, Free}
{#kind Describe=Method}
