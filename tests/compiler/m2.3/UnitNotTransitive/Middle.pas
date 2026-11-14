unit Middle;

interface

uses Base;

var
  Home: TPoint;

function Shifted(D: Integer): TPoint;

implementation

function Shifted(D: Integer): TPoint;
begin
  Result := Make(Home.X + D, Home.Y + D);
end;

end.
