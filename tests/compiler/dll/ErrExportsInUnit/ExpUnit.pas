unit ExpUnit;

interface

function Twice(A: Integer): Integer;

exports Twice;

implementation

function Twice(A: Integer): Integer;
begin
  Result := A * 2;
end;

exports
  Twice name 'twice' index 3,
  NoSuchRoutine;

end.
