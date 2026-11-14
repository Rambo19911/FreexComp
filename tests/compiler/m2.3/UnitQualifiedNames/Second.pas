unit Second;

interface

const
  Greeting = 'second';

var
  Counter: Integer;

function Twice(X: Integer): Integer;

implementation

function Twice(X: Integer): Integer;
begin
  Result := X + X + 1;
end;

end.
