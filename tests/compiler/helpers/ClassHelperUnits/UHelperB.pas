unit UHelperB;

interface

uses
  UShape;

type
  TShapeHelperB = class helper for TShape
    function Tag: string;
    function OnlyB: string;
  end;

implementation

function TShapeHelperB.Tag: string;
begin
  Result := 'B';
end;

function TShapeHelperB.OnlyB: string;
begin
  Result := 'only B';
end;

end.