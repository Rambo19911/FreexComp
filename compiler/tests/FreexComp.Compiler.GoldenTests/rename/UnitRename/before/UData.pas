unit UData;

{ CMP-1511: a unit: heading, uses entries, qualified names; its file is renamed with it. }

interface

var
  Value: Integer;

function Twice: Integer;

implementation

function Twice: Integer;
begin
  Result := UData.Value * 2;
end;

end.
