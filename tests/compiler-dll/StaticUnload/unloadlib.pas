// covers: CMP-1050, CMP-1051
library unloadlib;

uses UnloadUnit;

function Twice(A: Integer): Integer; stdcall;
begin
  Result := A * 2;
end;

exports
  Twice;

end.
