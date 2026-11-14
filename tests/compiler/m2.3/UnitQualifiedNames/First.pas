unit First;

interface

const
  Greeting = 'first';

var
  Counter: Integer;

function Twice(X: Integer): Integer;

implementation

function Twice(X: Integer): Integer;
begin
  Result := X * 2;
end;

end.
