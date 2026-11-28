unit UHelperC;

interface

uses
  UShape, UHelperA;

type
  TShapeHelperC = class helper(TShapeHelperA) for TShape
    function Tag: string;
    function OnlyC: string;
  end;

implementation

function TShapeHelperC.Tag: string;
begin
  Result := 'C';
end;

function TShapeHelperC.OnlyC: string;
begin
  Result := 'only C';
end;

end.