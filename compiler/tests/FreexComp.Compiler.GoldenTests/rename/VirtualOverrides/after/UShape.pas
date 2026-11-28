unit UShape;

{ CMP-1511: a virtual method with an override in each of two other units. }

interface

type
  TShape = class
    function Surface: Integer; virtual;
  end;

procedure Report(S: TShape);

implementation

function TShape.Surface: Integer;
begin
  Result := 0;
end;

procedure Report(S: TShape);
begin
  Writeln(S.ClassName, ' ', S.Surface);
end;

end.
