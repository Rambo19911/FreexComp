unit My.Utils;

interface

const
  Version = 3;

function Square(X: Integer): Integer;

implementation

function Square(X: Integer): Integer;
begin
  Result := X * X;
end;

end.
