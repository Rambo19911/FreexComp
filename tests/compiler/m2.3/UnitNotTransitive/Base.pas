unit Base;

interface

type
  TPoint = record
    X, Y: Integer;
  end;

const
  Origin = 0;

function Make(X, Y: Integer): TPoint;

implementation

function Make(X, Y: Integer): TPoint;
begin
  Result.X := X;
  Result.Y := Y;
end;

end.
