unit Vault;

interface

function Open: Integer;

implementation

const
  Secret = 42;

var
  Hidden: Integer;

procedure Helper;
begin
  Hidden := Secret;
end;

function Open: Integer;
begin
  Helper;
  Result := Hidden;
end;

end.
