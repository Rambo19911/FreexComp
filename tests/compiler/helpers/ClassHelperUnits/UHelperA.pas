unit UHelperA;

interface

uses
  UShape;

type
  TShapeHelperA = class helper for TShape
    function Tag: string;
    function OnlyA: string;
  end;

implementation

function TShapeHelperA.Tag: string;
begin
  Result := 'A';
end;

function TShapeHelperA.OnlyA: string;
begin
  Result := 'only A';
end;

end.