unit UShape;

{ CMP-1511: a virtual method with an override in each of two other units. }

interface

type
  TShape = class
    function Area: Integer; virtual;
  end;

procedure Report(S: TShape);

implementation

function TShape.Area: Integer;
begin
  Result := 0;
end;

procedure Report(S: TShape);
begin
  Writeln(S.ClassName, ' ', S.Area);
end;

end.
