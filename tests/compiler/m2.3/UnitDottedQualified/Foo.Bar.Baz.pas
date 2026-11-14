unit Foo.Bar.Baz;

interface

var
  Value: Integer;

function Deep: Integer;

implementation

function Deep: Integer;
begin
  Result := Value * 10;
end;

end.
